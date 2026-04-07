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

    // Auto-refresh
    private DispatcherTimer? _autoRefreshTimer;
    private bool _isCapturing;

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

        Loaded += (_, _) => Dispatcher.BeginInvoke(CaptureAndProcess, DispatcherPriority.Background);
        IsVisibleChanged += OnIsVisibleChanged;
        SizeChanged += OnSizeChanged;
    }

    // ── Recapture when window becomes visible again ───────────
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            Dispatcher.BeginInvoke(CaptureAndProcess, DispatcherPriority.Background);
            StartAutoRefreshIfEnabled();
        }
        else
        {
            StopAutoRefresh();
        }
    }

    // ── Auto-refresh timer ────────────────────────────────────
    public void StartAutoRefreshIfEnabled()
    {
        StopAutoRefresh();
        if (!_settingsService.Settings.AutoRefreshEnabled) return;

        _autoRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(_settingsService.Settings.UpdateIntervalMs)
        };
        _autoRefreshTimer.Tick += (_, _) =>
        {
            if (!_isCapturing) CaptureAndProcess();
        };
        _autoRefreshTimer.Start();
    }

    private void StopAutoRefresh()
    {
        _autoRefreshTimer?.Stop();
        _autoRefreshTimer = null;
    }

    // ── Single-shot capture & process (no flickering) ──────────

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(nint hWnd, uint dwAffinity);
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private bool _affinitySet;

    /// <summary>
    /// Set WDA_EXCLUDEFROMCAPTURE so CopyFromScreen ignores this window.
    /// No more hide/show flicker. Requires Win10 2004+.
    /// </summary>
    private void EnsureExcludeFromCapture()
    {
        if (_affinitySet) return;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != nint.Zero)
        {
            _affinitySet = SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
        }
    }

    private async void CaptureAndProcess()
    {
        if (_isCapturing) return;
        _isCapturing = true;
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            GetWindowRect(hwnd, out RECT wr);

            double dpi;
            try { dpi = GetDpiForSystem() / 96.0; } catch { dpi = 1.0; }
            int barTop = (int)(24 * dpi);
            int barBottom = (int)(32 * dpi);

            int x = wr.Left;
            int y = wr.Top;
            int w = wr.Right - wr.Left;
            int h = wr.Bottom - wr.Top;
            if (w <= 0 || h <= 0) return;

            EnsureExcludeFromCapture();

            Bitmap captured;
            if (_affinitySet)
            {
                // Window is excluded from capture — no need to hide
                captured = _captureService.CaptureRegion(x, y, w, h);
            }
            else
            {
                // Fallback: hide briefly (older Windows)
                Opacity = 0;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                await Task.Delay(16);
                captured = _captureService.CaptureRegion(x, y, w, h);
                Opacity = 1;
            }

            // Process on background thread to avoid UI freeze
            var settings = _settingsService.Settings;
            var isErase = _viewModel.IsEraseMode;
            var targetColor = settings.TargetColor;
            var overlayRgb = settings.OverlayColor.Rgb;

            var result = await Task.Run(() =>
            {
                Bitmap processed;
                if (isErase)
                    processed = _detectorService.EraseColor(captured, targetColor);
                else
                    processed = _detectorService.MultiplyBlend(captured, overlayRgb);
                var source = ConvertBitmap(processed);
                processed.Dispose();
                captured.Dispose();
                return source;
            });

            _viewModel.ProcessedImageSource = result;
        }
        catch
        {
            if (!_affinitySet) Opacity = 1;
        }
        finally
        {
            _isCapturing = false;
        }
    }

    // ── Triggers ───────────────────────────────────────────────

    private void DragBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
            // DragMove blocks until mouse up → capture at new position
            CaptureAndProcess();
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Debounce: wait until resize stops
        _resizeDebounce?.Stop();
        _resizeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _resizeDebounce.Tick += (_, _) =>
        {
            _resizeDebounce.Stop();
            CaptureAndProcess();
        };
        _resizeDebounce.Start();
    }

    private void BtnMode_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleMode();
        CaptureAndProcess();
    }

    private void BtnFullScreen_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleFullScreen(this);
        CaptureAndProcess();
    }

    // ── Export ─────────────────────────────────────────────────

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Export: use Win32 physical pixel coordinates
            var expHwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            GetWindowRect(expHwnd, out RECT ewr);
            double edpi;
            try { edpi = GetDpiForSystem() / 96.0; } catch { edpi = 1.0; }
            int x = ewr.Left;
            int y = ewr.Top + (int)(24 * edpi);
            int w = ewr.Right - ewr.Left;
            int h = (ewr.Bottom - ewr.Top) - (int)(24 * edpi) - (int)(32 * edpi);
            if (w <= 0 || h <= 0) return;

            EnsureExcludeFromCapture();
            if (!_affinitySet)
            {
                Opacity = 0;
                UpdateLayout();
                System.Threading.Thread.Sleep(16);
            }

            using var captured = _captureService.CaptureRegion(x, y, w, h);
            if (!_affinitySet) Opacity = 1;

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
        catch { Opacity = 1; }
    }

    // ── Other handlers ────────────────────────────────────────

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        StopAutoRefresh();
        var settingsWindow = new SettingsWindow(_settingsService);
        settingsWindow.Owner = this;
        settingsWindow.Topmost = true;
        settingsWindow.ShowDialog();
        _viewModel.RefreshFromSettings();
        CaptureAndProcess();
        StartAutoRefreshIfEnabled();
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
            CaptureAndProcess();
            e.Handled = true;
        }
        // F5 or Space = manual refresh
        if (e.Key == Key.F5 || e.Key == Key.Space)
        {
            CaptureAndProcess();
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
        SavePosition();
        base.OnClosed(e);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetDpiForSystem();

    private static BitmapSource ConvertBitmap(Bitmap bitmap)
    {
        double dpi;
        try { dpi = GetDpiForSystem(); } catch { dpi = 96; }

        var bitmapData = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var source = BitmapSource.Create(
            bitmapData.Width, bitmapData.Height, dpi, dpi,
            PixelFormats.Bgra32, null,
            bitmapData.Scan0, bitmapData.Stride * bitmapData.Height, bitmapData.Stride);
        bitmap.UnlockBits(bitmapData);
        source.Freeze();
        return source;
    }
}
