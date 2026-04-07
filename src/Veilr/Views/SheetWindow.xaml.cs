using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Veilr.Models;
using Veilr.Services;
using Veilr.ViewModels;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using Rectangle = System.Drawing.Rectangle;

namespace Veilr.Views;

public partial class SheetWindow : Window
{
    private readonly SheetViewModel _viewModel;
    private readonly SettingsService _settingsService;
    private readonly ScreenCaptureService _captureService = new();
    private readonly ColorDetectorService _detectorService = new();

    // Debounce timer for resize
    private DispatcherTimer? _resizeDebounce;

    // ── Background capture pipeline ──────────────────────────
    private readonly FrameBuffer _front = new();   // UI reads
    private readonly FrameBuffer _back = new();    // Capture thread writes
    private readonly object _swapLock = new();
    private Thread? _captureThread;
    private volatile bool _captureRunning;
    private volatile bool _oneshotRequested;
    private volatile bool _frameReady;
    private WriteableBitmap? _writeableBitmap;
    private nint _hwndCache;

    public SheetWindow(SettingsService settingsService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _viewModel = new SheetViewModel(settingsService);
        DataContext = _viewModel;

        var last = settingsService.Settings.LastSession;
        if (last.Sheets.Count > 0)
        {
            var pos = last.Sheets[0];
            Left = pos.X;
            Top = pos.Y;
            Width = pos.W;
            Height = pos.H;
        }

        Loaded += OnLoaded;
        IsVisibleChanged += OnIsVisibleChanged;
        SizeChanged += OnSizeChanged;

        // Register CompositionTarget.Rendering for smooth UI updates
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hwndCache = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        EnsureExcludeFromCapture();
        RequestCapture();
        StartCaptureThreadIfEnabled();
    }

    // ── CompositionTarget.Rendering: UI update (memcpy only, ~0.3ms) ──
    private void OnRendering(object? sender, EventArgs e)
    {
        if (!_frameReady) return;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        lock (_swapLock)
        {
            int w = _front.Width, h = _front.Height;
            if (w <= 0 || h <= 0) return;

            double dpi;
            try { dpi = GetDpiForSystem(); } catch { dpi = 96; }

            if (_writeableBitmap == null
                || _writeableBitmap.PixelWidth != w
                || _writeableBitmap.PixelHeight != h)
            {
                _writeableBitmap = new WriteableBitmap(w, h, dpi, dpi, PixelFormats.Bgra32, null);
                _viewModel.ProcessedImageSource = _writeableBitmap;
            }

            _writeableBitmap.Lock();
            Marshal.Copy(_front.Dst, 0, _writeableBitmap.BackBuffer, _front.ByteCount);
            _writeableBitmap.AddDirtyRect(new Int32Rect(0, 0, w, h));
            _writeableBitmap.Unlock();
        }
        sw.Stop();
        _frameReady = false;

        _renderCount++;
        _renderTotalMs += sw.Elapsed.TotalMilliseconds;
    }

    // ── Background capture thread ─────────────────────────────
    private void StartCaptureThreadIfEnabled()
    {
        if (_captureThread?.IsAlive == true) return;
        _captureRunning = true;
        _captureThread = new Thread(CaptureLoop)
        {
            IsBackground = true,
            Name = "VeilrCapture",
            Priority = ThreadPriority.BelowNormal
        };
        _captureThread.Start();
    }

    private void StopCaptureThread()
    {
        _captureRunning = false;
        // Thread will exit on its own
    }

    // ── Profiling ──────────────────────────────────────────────
    private readonly System.Diagnostics.Stopwatch _profSw = new();
    private int _profFrameCount;
    private const int PROF_MAX_FRAMES = 50;
    private readonly List<string> _profLog = new();
    private long _renderCount;
    private double _renderTotalMs;

    private void CaptureLoop()
    {
        // Save first captured frame for WDA verification
        bool savedDebugFrame = false;

        while (_captureRunning)
        {
            bool autoEnabled = _settingsService.Settings.AutoRefreshEnabled;
            bool shouldCapture = _oneshotRequested || autoEnabled;

            if (!shouldCapture || _frameReady)
            {
                Thread.Sleep(1);
                continue;
            }

            _oneshotRequested = false;

            try
            {
                _profSw.Restart();

                GetWindowRect(_hwndCache, out RECT wr);
                int x = wr.Left, y = wr.Top;
                int w = wr.Right - wr.Left, h = wr.Bottom - wr.Top;
                if (w <= 0 || h <= 0) continue;

                int stride = w * 4;
                _back.EnsureCapacity(w, h, stride);

                // --- Capture ---
                long t0 = _profSw.ElapsedTicks;
                _back.CaptureAndCopyPixels(_captureService, x, y);
                long t1 = _profSw.ElapsedTicks;

                // Verify actual stride matches expected
                if (!savedDebugFrame && _back.CaptureBitmap != null)
                {
                    var dbgData = _back.CaptureBitmap.LockBits(
                        new Rectangle(0, 0, w, h),
                        System.Drawing.Imaging.ImageLockMode.ReadOnly,
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    int actualStride = dbgData.Stride;
                    _back.CaptureBitmap.UnlockBits(dbgData);

                    // Save first frame as PNG for visual verification
                    try
                    {
                        string dbgPath = Path.Combine(
                            AppDomain.CurrentDomain.BaseDirectory, "debug-capture.png");
                        _back.CaptureBitmap.Save(dbgPath, System.Drawing.Imaging.ImageFormat.Png);
                        _profLog.Add($"DEBUG: First frame saved to {dbgPath}");
                        _profLog.Add($"DEBUG: Expected stride={stride}, Actual stride={actualStride}");
                        _profLog.Add($"DEBUG: WDA_EXCLUDEFROMCAPTURE = {_affinitySet}");
                        // Check if first pixel is black (WDA failure indicator)
                        byte b0 = _back.Src[0], g0 = _back.Src[1], r0 = _back.Src[2];
                        _profLog.Add($"DEBUG: First pixel RGB=({r0},{g0},{b0})");
                    }
                    catch { /* ignore */ }
                    savedDebugFrame = true;
                }

                // --- Process ---
                var settings = _settingsService.Settings;
                bool isErase;
                try { isErase = _viewModel.IsEraseMode; } catch { isErase = false; }

                long t2 = _profSw.ElapsedTicks;
                if (isErase)
                    _detectorService.EraseColorInto(_back, settings.TargetColor);
                else
                    _detectorService.MultiplyBlendInto(_back, settings.OverlayColor.Rgb);
                long t3 = _profSw.ElapsedTicks;

                // --- Swap ---
                lock (_swapLock)
                {
                    _front.EnsureCapacity(w, h, stride);
                    Buffer.BlockCopy(_back.Dst, 0, _front.Dst, 0, _back.ByteCount);
                }
                long t4 = _profSw.ElapsedTicks;
                _frameReady = true;

                // --- Log ---
                double freq = System.Diagnostics.Stopwatch.Frequency;
                if (_profFrameCount < PROF_MAX_FRAMES)
                {
                    double capMs = (t1 - t0) / freq * 1000;
                    double procMs = (t3 - t2) / freq * 1000;
                    double swapMs = (t4 - t3) / freq * 1000;
                    double totalMs = (t4 - t0) / freq * 1000;
                    _profLog.Add($"Frame {_profFrameCount:D3}  " +
                        $"Capture:{capMs,6:F1}ms  Process:{procMs,6:F1}ms  " +
                        $"Swap:{swapMs,5:F1}ms  Total:{totalMs,6:F1}ms  " +
                        $"Size:{w}x{h}  Mode:{(isErase ? "erase" : "sheet")}");
                    _profFrameCount++;

                    if (_profFrameCount == PROF_MAX_FRAMES)
                        FlushProfileLog(w, h);
                }
            }
            catch (Exception ex)
            {
                if (_profFrameCount < PROF_MAX_FRAMES)
                    _profLog.Add($"ERROR: {ex.Message}");
            }

            if (!_settingsService.Settings.AutoRefreshEnabled)
                continue;

            int interval = _settingsService.Settings.UpdateIntervalMs;
            Thread.Sleep(interval);
        }
    }

    private void FlushProfileLog(int w, int h)
    {
        try
        {
            var lines = new List<string>
            {
                "=== Veilr Capture Profile ===",
                $"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                $"Window: {w}x{h}",
                $"WDA_EXCLUDEFROMCAPTURE: {_affinitySet}",
                $"AutoRefresh: {_settingsService.Settings.AutoRefreshEnabled}",
                $"Interval: {_settingsService.Settings.UpdateIntervalMs}ms",
                $"Render frames: {_renderCount}, avg: {(_renderCount > 0 ? _renderTotalMs / _renderCount : 0):F2}ms",
                ""
            };
            lines.AddRange(_profLog);

            string logPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "capture-profile.log");
            File.WriteAllLines(logPath, lines);
        }
        catch { /* ignore */ }
    }

    /// <summary>Request a single frame capture (non-blocking).</summary>
    private void RequestCapture()
    {
        _oneshotRequested = true;
        // Ensure capture thread is running
        if (_captureThread?.IsAlive != true)
            StartCaptureThreadIfEnabled();
    }

    // ── Visibility ────────────────────────────────────────────
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            RequestCapture();
            StartCaptureThreadIfEnabled();
        }
        else
        {
            StopCaptureThread();
        }
    }

    // ── WDA_EXCLUDEFROMCAPTURE ────────────────────────────────

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(nint hWnd, uint dwAffinity);
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private bool _affinitySet;

    private void EnsureExcludeFromCapture()
    {
        if (_affinitySet || _hwndCache == nint.Zero) return;
        _affinitySet = SetWindowDisplayAffinity(_hwndCache, WDA_EXCLUDEFROMCAPTURE);
    }

    // ── Triggers ───────────────────────────────────────────────

    private void DragBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
            RequestCapture();
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _resizeDebounce?.Stop();
        _resizeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _resizeDebounce.Tick += (_, _) =>
        {
            _resizeDebounce.Stop();
            RequestCapture();
        };
        _resizeDebounce.Start();
    }

    private void BtnMode_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleMode();
        RequestCapture();
    }

    private void BtnFullScreen_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleFullScreen(this);
        RequestCapture();
    }

    // ── Export ─────────────────────────────────────────────────

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            GetWindowRect(_hwndCache, out RECT ewr);
            double edpi;
            try { edpi = GetDpiForSystem() / 96.0; } catch { edpi = 1.0; }
            int x = ewr.Left;
            int y = ewr.Top + (int)(24 * edpi);
            int w = ewr.Right - ewr.Left;
            int h = (ewr.Bottom - ewr.Top) - (int)(24 * edpi) - (int)(32 * edpi);
            if (w <= 0 || h <= 0) return;

            using var captured = _captureService.CaptureRegion(x, y, w, h);
            using var processed = _detectorService.EraseColor(captured, _settingsService.Settings.TargetColor);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var dlg = new SaveFileDialog
            {
                FileName = $"Veilr_{timestamp}",
                Filter = "PNG|*.png|JPEG|*.jpg|BMP|*.bmp",
                FilterIndex = _settingsService.Settings.Export.Format switch
                {
                    "jpeg" => 2, "bmp" => 3, _ => 1
                },
                InitialDirectory = string.IsNullOrEmpty(_settingsService.Settings.Export.SavePath)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                    : _settingsService.Settings.Export.SavePath
            };

            if (dlg.ShowDialog() == true)
            {
                var format = Path.GetExtension(dlg.FileName).ToLower() switch
                {
                    ".jpg" or ".jpeg" => ImageFormat.Jpeg,
                    ".bmp" => ImageFormat.Bmp,
                    _ => ImageFormat.Png
                };
                processed.Save(dlg.FileName, format);
                _settingsService.Settings.Export.SavePath = Path.GetDirectoryName(dlg.FileName) ?? "";
                _settingsService.Save();
            }
        }
        catch { /* swallow */ }
    }

    // ── Other handlers ────────────────────────────────────────

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        StopCaptureThread();
        var settingsWindow = new SettingsWindow(_settingsService);
        settingsWindow.Owner = this;
        settingsWindow.Topmost = true;
        settingsWindow.ShowDialog();
        _viewModel.RefreshFromSettings();
        RequestCapture();
        StartCaptureThreadIfEnabled();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        SavePosition();
        Hide();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _viewModel.IsFullScreen)
        {
            _viewModel.ToggleFullScreen(this);
            RequestCapture();
            e.Handled = true;
        }
        if (e.Key == Key.F5 || e.Key == Key.Space)
        {
            RequestCapture();
            e.Handled = true;
        }
    }

    private void SavePosition()
    {
        _settingsService.Settings.LastSession.Sheets =
        [
            new() { X = Left, Y = Top, W = Width, H = Height }
        ];
        _settingsService.Save();
    }

    protected override void OnClosed(EventArgs e)
    {
        StopCaptureThread();
        CompositionTarget.Rendering -= OnRendering;
        SavePosition();
        base.OnClosed(e);
    }

    [DllImport("user32.dll")]
    private static extern int GetDpiForSystem();
}
