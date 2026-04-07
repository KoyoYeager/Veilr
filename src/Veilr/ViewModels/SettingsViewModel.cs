using System.ComponentModel;
using Veilr.Services;

namespace Veilr.ViewModels;

public class SettingsViewModel : INotifyPropertyChanged
{
    private readonly SettingsService _settings;

    // Color (shared for both modes)
    private int _colorR, _colorG, _colorB;
    private double _opacity;

    // Erase mode tolerance (0-100)
    private int _tolerance;

    // Erase algorithm
    private int _eraseAlgorithmIndex;

    // Behavior
    private int _updateIntervalMs;
    private bool _autoRefreshEnabled;

    // Appearance
    private string _language = "ja";

    // Snapshot of initial values for change detection
    private int _initR, _initG, _initB;
    private double _initOpacity;
    private int _initTolerance, _initInterval, _initAlgorithm;
    private bool _initAutoRefresh;
    private string _initLanguage = "ja", _initHotkey = "ctrl+shift+e";

    // Hotkey
    private string _hotkeyToggleSheet = "ctrl+shift+e";

    public event PropertyChangedEventHandler? PropertyChanged;

    public SettingsViewModel(SettingsService settings)
    {
        _settings = settings;
        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        var s = _settings.Settings;

        _colorR = s.TargetColor.Rgb[0];
        _colorG = s.TargetColor.Rgb[1];
        _colorB = s.TargetColor.Rgb[2];
        _opacity = s.OverlayColor.Opacity;

        // Derive tolerance from H threshold
        var th = s.TargetColor.Threshold;
        _tolerance = Math.Clamp((int)(th.H / 0.45), 0, 100);

        _eraseAlgorithmIndex = s.TargetColor.EraseAlgorithm switch
        {
            "labmask" => 1,
            "ycbcr" => 2,
            _ => 0
        };

        _updateIntervalMs = s.UpdateIntervalMs;
        _autoRefreshEnabled = s.AutoRefreshEnabled;

        _language = s.UiTheme.Language;
        _hotkeyToggleSheet = s.Hotkeys.ToggleSheet;

        // Save initial snapshot
        _initR = _colorR; _initG = _colorG; _initB = _colorB;
        _initOpacity = _opacity; _initTolerance = _tolerance;
        _initInterval = _updateIntervalMs;
        _initAutoRefresh = _autoRefreshEnabled;
        _initAlgorithm = _eraseAlgorithmIndex;
        _initLanguage = _language; _initHotkey = _hotkeyToggleSheet;
    }

    // --- Color ---
    public int ColorR { get => _colorR; set { _colorR = value; NotifyColorChanged(); } }
    public int ColorG { get => _colorG; set { _colorG = value; NotifyColorChanged(); } }
    public int ColorB { get => _colorB; set { _colorB = value; NotifyColorChanged(); } }

    private void NotifyColorChanged()
    {
        OnPropertyChanged(nameof(ColorR));
        OnPropertyChanged(nameof(ColorG));
        OnPropertyChanged(nameof(ColorB));
        OnPropertyChanged(nameof(ColorPreview));
        OnPropertyChanged(nameof(HexColor));
    }

    public string HexColor
    {
        get => $"#{_colorR:X2}{_colorG:X2}{_colorB:X2}";
        set
        {
            if (TryParseHex(value, out int r, out int g, out int b))
            {
                _colorR = r; _colorG = g; _colorB = b;
                NotifyColorChanged();
            }
        }
    }

    public System.Windows.Media.SolidColorBrush ColorPreview =>
        new(System.Windows.Media.Color.FromRgb((byte)_colorR, (byte)_colorG, (byte)_colorB));

    public double Opacity
    {
        get => _opacity;
        set { _opacity = Math.Clamp(value, 0.1, 1.0); OnPropertyChanged(nameof(Opacity)); OnPropertyChanged(nameof(OpacityPercent)); }
    }
    public int OpacityPercent => (int)(_opacity * 100);

    // --- Erase mode tolerance ---
    public int Tolerance
    {
        get => _tolerance;
        set { _tolerance = Math.Clamp(value, 0, 100); OnPropertyChanged(nameof(Tolerance)); }
    }

    // --- Erase algorithm ---
    public bool IsChromaKey
    {
        get => _eraseAlgorithmIndex == 0;
        set { if (value) { _eraseAlgorithmIndex = 0; NotifyAlgorithm(); } }
    }
    public bool IsLabMask
    {
        get => _eraseAlgorithmIndex == 1;
        set { if (value) { _eraseAlgorithmIndex = 1; NotifyAlgorithm(); } }
    }
    public bool IsYCbCr
    {
        get => _eraseAlgorithmIndex == 2;
        set { if (value) { _eraseAlgorithmIndex = 2; NotifyAlgorithm(); } }
    }
    private void NotifyAlgorithm()
    {
        OnPropertyChanged(nameof(IsChromaKey));
        OnPropertyChanged(nameof(IsLabMask));
        OnPropertyChanged(nameof(IsYCbCr));
    }

    // --- Behavior ---
    public bool AutoRefreshEnabled
    {
        get => _autoRefreshEnabled;
        set { _autoRefreshEnabled = value; OnPropertyChanged(nameof(AutoRefreshEnabled)); }
    }
    public int UpdateIntervalMs
    {
        get => _updateIntervalMs;
        set { _updateIntervalMs = Math.Clamp(value, 8, 500); OnPropertyChanged(nameof(UpdateIntervalMs)); OnPropertyChanged(nameof(UpdateIntervalDisplay)); }
    }
    public string UpdateIntervalDisplay => $"{_updateIntervalMs}ms ({1000 / _updateIntervalMs}fps)";

    // --- Appearance ---
    public string Language { get => _language; set { _language = value; OnPropertyChanged(nameof(Language)); OnPropertyChanged(nameof(LanguageIndex)); } }
    public int LanguageIndex
    {
        get => _language == "en" ? 1 : 0;
        set { Language = value == 1 ? "en" : "ja"; }
    }
    public bool HasChanges =>
        _colorR != _initR || _colorG != _initG || _colorB != _initB
        || _opacity != _initOpacity || _tolerance != _initTolerance
        || _updateIntervalMs != _initInterval
        || _autoRefreshEnabled != _initAutoRefresh
        || _eraseAlgorithmIndex != _initAlgorithm
        || _language != _initLanguage || _hotkeyToggleSheet != _initHotkey;

    // --- Hotkey ---
    public string HotkeyToggleSheet { get => _hotkeyToggleSheet; set { _hotkeyToggleSheet = value; OnPropertyChanged(nameof(HotkeyToggleSheet)); } }

    public void SetColorFromDialog(System.Drawing.Color c)
    {
        _colorR = c.R; _colorG = c.G; _colorB = c.B;
        NotifyColorChanged();
    }

    public void Apply()
    {
        var s = _settings.Settings;

        // Both modes use same color
        s.TargetColor.Rgb = [_colorR, _colorG, _colorB];
        s.OverlayColor.Rgb = [_colorR, _colorG, _colorB];
        s.OverlayColor.Opacity = _opacity;

        // Tolerance → H/S/V thresholds (conservative S/V to avoid matching black/white)
        s.TargetColor.Threshold.H = (int)(_tolerance * 0.45);  // 0-45
        s.TargetColor.Threshold.S = (int)(_tolerance * 1.0);   // 0-100
        s.TargetColor.Threshold.V = (int)(_tolerance * 1.0);   // 0-100
        s.TargetColor.ThresholdMode = _tolerance > 30 ? "flexible" : "strict";
        s.TargetColor.EraseAlgorithm = _eraseAlgorithmIndex switch
        {
            1 => "labmask",
            2 => "ycbcr",
            _ => "chromakey"
        };

        s.UpdateIntervalMs = _updateIntervalMs;
        s.AutoRefreshEnabled = _autoRefreshEnabled;
        s.UiTheme.Language = _language;
        s.Hotkeys.ToggleSheet = _hotkeyToggleSheet;

        _settings.AddColorHistory([_colorR, _colorG, _colorB]);
        _settings.Save();
    }

    private static bool TryParseHex(string hex, out int r, out int g, out int b)
    {
        r = g = b = 0;
        hex = hex.TrimStart('#');
        if (hex.Length != 6) return false;
        try
        {
            r = Convert.ToInt32(hex[..2], 16);
            g = Convert.ToInt32(hex[2..4], 16);
            b = Convert.ToInt32(hex[4..6], 16);
            return true;
        }
        catch { return false; }
    }

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
