using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Veilr.Helpers;
using Veilr.Services;
using static Veilr.Helpers.Loc;

namespace Veilr.ViewModels;

public class SheetViewModel : INotifyPropertyChanged
{
    private readonly SettingsService _settings;
    private bool _isEraseMode;
    private bool _isFullScreen;
    private double _savedLeft, _savedTop, _savedWidth, _savedHeight;
    private BitmapSource? _processedImageSource;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SheetViewModel(SettingsService settings)
    {
        _settings = settings;
        _isEraseMode = settings.Settings.Mode == "erase";
        RefreshFromSettings();
    }

    public bool IsEraseMode
    {
        get => _isEraseMode;
        private set
        {
            _isEraseMode = value;
            OnPropertyChanged(nameof(IsEraseMode));
        }
    }

    public bool IsFullScreen => _isFullScreen;

    public string ModeLabel => _isEraseMode
        ? (_isFullScreen ? Loc.EraseModeFullScreen : Loc.EraseMode)
        : (_isFullScreen ? Loc.SheetModeFullScreen : Loc.SheetMode);

    public string ModeSwitchLabel => _isEraseMode
        ? Loc.SwitchToSheetMode : Loc.SwitchToEraseMode;

    public string FullScreenLabel => _isFullScreen ? Loc.ExitFullScreen : Loc.FullScreen;
    public string ExportLabel => Helpers.Loc.Export;
    public string SettingsLabel => Helpers.Loc.Settings;

    public SolidColorBrush SheetBrush
    {
        get
        {
            if (_isEraseMode)
                return new SolidColorBrush(System.Windows.Media.Colors.White);
            var rgb = _settings.Settings.OverlayColor.Rgb;
            return new SolidColorBrush(Color.FromRgb((byte)rgb[0], (byte)rgb[1], (byte)rgb[2]));
        }
    }

    public double SheetOpacity => _settings.Settings.OverlayColor.Opacity;

    public SolidColorBrush TextForeground
    {
        get
        {
            var rgb = _settings.Settings.OverlayColor.Rgb;
            double luminance = 0.299 * rgb[0] + 0.587 * rgb[1] + 0.114 * rgb[2];
            return luminance > 140
                ? new SolidColorBrush(System.Windows.Media.Colors.Black)
                : new SolidColorBrush(System.Windows.Media.Colors.White);
        }
    }

    // Both modes use full-window image now (no separate visibility needed)

    // Erase mode: window frame + white bars + black text
    // No border thickness to avoid content offset. Bars provide visual frame.
    public Thickness WindowBorderThickness => new Thickness(1);
    public SolidColorBrush WindowBorderBrush
    {
        get
        {
            var rgb = _settings.Settings.OverlayColor.Rgb;
            return new SolidColorBrush(Color.FromRgb((byte)rgb[0], (byte)rgb[1], (byte)rgb[2]));
        }
    }
    public SolidColorBrush BarBackground => _isEraseMode
        ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(
            (byte)(_settings.Settings.BarOpacityPercent * 255 / 100), 255, 255, 255))
        : new SolidColorBrush(System.Windows.Media.Colors.Transparent);
    public SolidColorBrush BarForeground =>
        new SolidColorBrush(System.Windows.Media.Colors.Black);

    public BitmapSource? ProcessedImageSource
    {
        get => _processedImageSource;
        set
        {
            _processedImageSource = value;
            OnPropertyChanged(nameof(ProcessedImageSource));
        }
    }

    public void ToggleMode()
    {
        IsEraseMode = !_isEraseMode;
        _settings.Settings.Mode = _isEraseMode ? "erase" : "sheet";
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(ModeSwitchLabel));
        NotifyModeVisuals();
    }

    private void NotifyModeVisuals()
    {
        OnPropertyChanged(nameof(SheetBrush));
        OnPropertyChanged(nameof(WindowBorderThickness));
        OnPropertyChanged(nameof(WindowBorderBrush));
        OnPropertyChanged(nameof(BarBackground));
        OnPropertyChanged(nameof(BarForeground));
    }

    public void ToggleFullScreen(Window window)
    {
        if (_isFullScreen)
        {
            window.Left = _savedLeft;
            window.Top = _savedTop;
            window.Width = _savedWidth;
            window.Height = _savedHeight;
            window.WindowState = WindowState.Normal;
            _isFullScreen = false;
        }
        else
        {
            _savedLeft = window.Left;
            _savedTop = window.Top;
            _savedWidth = window.Width;
            _savedHeight = window.Height;

            var (sw, sh) = Win32Interop.GetScreenSize();
            window.Left = 0;
            window.Top = 0;
            window.Width = sw;
            window.Height = sh;
            _isFullScreen = true;
        }
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(FullScreenLabel));
        OnPropertyChanged(nameof(IsFullScreen));
    }

    public void RefreshFromSettings()
    {
        OnPropertyChanged(nameof(SheetBrush));
        OnPropertyChanged(nameof(SheetOpacity));
        OnPropertyChanged(nameof(TextForeground));
        OnPropertyChanged(nameof(WindowBorderBrush));
    }

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
