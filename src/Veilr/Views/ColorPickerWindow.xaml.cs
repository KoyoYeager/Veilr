using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Veilr.Helpers;
using Veilr.Services;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using Rectangle = System.Drawing.Rectangle;

namespace Veilr.Views;

/// <summary>
/// Color picker with screen eyedropper.
/// During eyedrop mode a full-screen transparent overlay captures all mouse/keyboard input.
/// A small floating preview follows the cursor showing an 8x-zoomed view with a crosshair
/// on the center pixel.
/// </summary>
public partial class ColorPickerWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    private readonly ScreenCaptureService _captureService = new();
    private byte _pickedR = 255, _pickedG = 0, _pickedB = 0;

    // Eyedrop mode overlay
    private Window? _overlay;
    private Window? _preview;
    private System.Windows.Controls.Image? _previewImage;
    private DispatcherTimer? _timer;

    public bool ColorSelected { get; private set; }
    public byte ResultR => _pickedR;
    public byte ResultG => _pickedG;
    public byte ResultB => _pickedB;

    public ColorPickerWindow(byte initialR, byte initialG, byte initialB)
    {
        InitializeComponent();
        _pickedR = initialR;
        _pickedG = initialG;
        _pickedB = initialB;

        ApplyLocalization();
        UpdateColorDisplay();
        TxtHexInput.Text = $"#{_pickedR:X2}{_pickedG:X2}{_pickedB:X2}";
    }

    private void ApplyLocalization()
    {
        Title = Loc.ColorPickerTitle;
        LblEyedropper.Text = Loc.EyedropperMode;
        BtnEyedrop.Content = Loc.EyedropperButton;
        LblPickedColor.Text = Loc.PickedColor;
        LblManualInput.Text = Loc.ManualInput;
        BtnApplyHex.Content = Loc.Apply;
        BtnUseColor.Content = Loc.UseThisColor;
        BtnCancelPicker.Content = Loc.Cancel;
    }

    // ── Eyedropper ──────────────────────────────────────────

    private void BtnEyedrop_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        StartEyedrop();
    }

    private void StartEyedrop()
    {
        // Full-screen transparent overlay covering ALL monitors (VirtualScreen)
        double vl = SystemParameters.VirtualScreenLeft;
        double vt = SystemParameters.VirtualScreenTop;
        double vw = SystemParameters.VirtualScreenWidth;
        double vh = SystemParameters.VirtualScreenHeight;

        _overlay = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            Left = vl,
            Top = vt,
            Width = vw,
            Height = vh,
            Cursor = System.Windows.Input.Cursors.Cross,
        };
        _overlay.MouseLeftButtonDown += Overlay_MouseDown;
        _overlay.KeyDown += Overlay_KeyDown;
        _overlay.Show();
        _overlay.Activate();

        // Floating zoom-preview window (no chrome, always near cursor)
        _previewImage = new System.Windows.Controls.Image
        {
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(_previewImage, BitmapScalingMode.NearestNeighbor);

        _preview = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            Width = 160,
            Height = 160,
            IsHitTestVisible = false,   // clicks pass through to overlay
            Content = new System.Windows.Controls.Border
            {
                BorderBrush = System.Windows.Media.Brushes.Gray,
                BorderThickness = new Thickness(1),
                Child = _previewImage,
            },
        };
        _preview.Show();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _timer.Tick += EyedropTick;
        _timer.Start();
    }

    private void EyedropTick(object? sender, EventArgs e)
    {
        GetCursorPos(out POINT pt);

        const int captureSize = 21; // odd number so center pixel is exact
        int half = captureSize / 2;
        int cx = pt.X - half;
        int cy = pt.Y - half;

        try
        {
            using var bmp = _captureService.CaptureRegion(cx, cy, captureSize, captureSize);

            // Read center pixel
            var centerColor = bmp.GetPixel(half, half);
            _pickedR = centerColor.R;
            _pickedG = centerColor.G;
            _pickedB = centerColor.B;

            // Draw crosshair on center pixel
            DrawCrosshair(bmp, half, half);

            // Convert to WPF BitmapSource
            var bmpData = bmp.LockBits(
                new Rectangle(0, 0, bmp.Width, bmp.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var source = BitmapSource.Create(
                bmpData.Width, bmpData.Height, 96, 96,
                PixelFormats.Bgra32, null,
                bmpData.Scan0, bmpData.Stride * bmpData.Height, bmpData.Stride);
            bmp.UnlockBits(bmpData);
            source.Freeze();

            if (_previewImage != null)
                _previewImage.Source = source;

            // Position preview near cursor (offset to avoid covering the target pixel)
            if (_preview != null)
            {
                _preview.Left = pt.X + 20;
                _preview.Top = pt.Y + 20;
            }
        }
        catch
        {
            // screen edge
        }
    }

    private static void DrawCrosshair(Bitmap bmp, int cx, int cy)
    {
        // Determine contrast color for crosshair
        var c = bmp.GetPixel(cx, cy);
        double lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
        var lineColor = lum > 140
            ? System.Drawing.Color.FromArgb(200, 0, 0, 0)
            : System.Drawing.Color.FromArgb(200, 255, 255, 255);
        var fillColor = lum > 140
            ? System.Drawing.Color.FromArgb(80, 0, 0, 0)
            : System.Drawing.Color.FromArgb(80, 255, 255, 255);

        // Highlight center pixel with a border
        bmp.SetPixel(cx - 1, cy - 1, lineColor);
        bmp.SetPixel(cx,     cy - 1, lineColor);
        bmp.SetPixel(cx + 1, cy - 1, lineColor);
        bmp.SetPixel(cx - 1, cy,     lineColor);
        // keep center pixel as-is
        bmp.SetPixel(cx + 1, cy,     lineColor);
        bmp.SetPixel(cx - 1, cy + 1, lineColor);
        bmp.SetPixel(cx,     cy + 1, lineColor);
        bmp.SetPixel(cx + 1, cy + 1, lineColor);

        // Draw thin crosshair lines (skip near center to keep border visible)
        for (int i = 0; i < bmp.Width; i++)
        {
            if (Math.Abs(i - cx) > 2)
                bmp.SetPixel(i, cy, fillColor);
        }
        for (int j = 0; j < bmp.Height; j++)
        {
            if (Math.Abs(j - cy) > 2)
                bmp.SetPixel(cx, j, fillColor);
        }
    }

    private void Overlay_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Color is already captured in the last tick
        StopEyedrop(confirm: true);
    }

    private void Overlay_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
            StopEyedrop(confirm: false);
    }

    private void StopEyedrop(bool confirm)
    {
        _timer?.Stop();
        _timer = null;
        _preview?.Close();
        _preview = null;
        _previewImage = null;
        _overlay?.Close();
        _overlay = null;

        // Restore picker window
        Show();
        Activate();
        Topmost = true;

        if (confirm)
        {
            UpdateColorDisplay();
            TxtHexInput.Text = $"#{_pickedR:X2}{_pickedG:X2}{_pickedB:X2}";
            ZoomPreview.Source = null; // clear stale preview
        }
    }

    // ── Manual input / buttons ───────────────────────────────

    private void BtnApplyHex_Click(object sender, RoutedEventArgs e)
    {
        var hex = TxtHexInput.Text.Trim().TrimStart('#');
        if (hex.Length == 6)
        {
            try
            {
                _pickedR = (byte)Convert.ToInt32(hex[..2], 16);
                _pickedG = (byte)Convert.ToInt32(hex[2..4], 16);
                _pickedB = (byte)Convert.ToInt32(hex[4..6], 16);
                UpdateColorDisplay();
            }
            catch { }
        }
    }

    private void BtnUseColor_Click(object sender, RoutedEventArgs e)
    {
        ColorSelected = true;
        Close();
    }

    private void BtnCancelPicker_Click(object sender, RoutedEventArgs e)
    {
        ColorSelected = false;
        Close();
    }

    private void UpdateColorDisplay()
    {
        PickedColorBrush.Color = System.Windows.Media.Color.FromRgb(_pickedR, _pickedG, _pickedB);
        TxtHex.Text = $"#{_pickedR:X2}{_pickedG:X2}{_pickedB:X2}";
        TxtR.Text = _pickedR.ToString();
        TxtG.Text = _pickedG.ToString();
        TxtB.Text = _pickedB.ToString();

        var hsv = HsvConverter.FromRgb(_pickedR, _pickedG, _pickedB);
        TxtH.Text = ((int)hsv.H).ToString();
        TxtS.Text = ((int)hsv.S).ToString();
        TxtV.Text = ((int)hsv.V).ToString();
    }
}
