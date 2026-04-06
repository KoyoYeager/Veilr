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
        SizeChanged += OnSizeChanged;
    }

    // ── Single-shot capture & process (no flickering) ──────────

    private async void CaptureAndProcess()
    {
        try
        {
            int x, y, w, h;
            if (_viewModel.IsEraseMode)
            {
                // Erase mode: body area only (exclude bars)
                x = (int)Left;
                y = (int)Top + 24;
                w = (int)ActualWidth;
                h = (int)ActualHeight - 24 - 32;
            }
            else
            {
                // Sheet mode: full window
                x = (int)Left;
                y = (int)Top;
                w = (int)ActualWidth;
                h = (int)ActualHeight;
            }
            if (w <= 0 || h <= 0) return;

            Opacity = 0;
            await Task.Delay(50);

            using var captured = _captureService.CaptureRegion(x, y, w, h);

            Opacity = 1;

            Bitmap processed;
            if (_viewModel.IsEraseMode)
            {
                processed = _detectorService.EraseColor(captured, _settingsService.Settings.TargetColor);
            }
            else
            {
                processed = _detectorService.MultiplyBlend(
                    captured,
                    _settingsService.Settings.OverlayColor.Rgb);
            }

            using (processed)
                _viewModel.ProcessedImageSource = ConvertBitmap(processed);
        }
        catch
        {
            Opacity = 1;
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
            // Export always captures body area only
            int x = (int)Left;
            int y = (int)Top + 24;
            int w = (int)ActualWidth;
            int h = (int)ActualHeight - 24 - 32;
            if (w <= 0 || h <= 0) return;

            Opacity = 0;
            UpdateLayout();
            System.Threading.Thread.Sleep(50);

            using var captured = _captureService.CaptureRegion(x, y, w, h);
            Opacity = 1;

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
        var settingsWindow = new SettingsWindow(_settingsService);
        settingsWindow.Owner = this;
        settingsWindow.Topmost = true;
        settingsWindow.ShowDialog();
        _viewModel.RefreshFromSettings();
        CaptureAndProcess();
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
