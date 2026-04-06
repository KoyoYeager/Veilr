using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Veilr.Services;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using Rectangle = System.Drawing.Rectangle;

namespace Veilr.Views;

/// <summary>
/// Reliable eyedropper: captures the entire screen as a frozen snapshot,
/// displays it full-screen, and lets the user click any pixel to pick its color.
/// A zoom preview follows the cursor with a crosshair marking the target pixel.
/// </summary>
public sealed class EyedropperOverlay : Window
{
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT pt);
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    private readonly Bitmap _snapshot;
    private readonly int _offsetX, _offsetY;
    private readonly double _dpiScale;
    private readonly Window _preview;
    private readonly System.Windows.Controls.Image _previewImage;
    private readonly DispatcherTimer _timer;

    // Last cursor position in physical pixels (updated every tick)
    private int _lastPixelX, _lastPixelY;

    public bool Confirmed { get; private set; }
    public byte PickedR { get; private set; }
    public byte PickedG { get; private set; }
    public byte PickedB { get; private set; }

    private EyedropperOverlay()
    {
        // Use WinForms Screen for PHYSICAL pixel bounds (DPI-aware)
        var allScreens = System.Windows.Forms.Screen.AllScreens;
        int vl = allScreens.Min(s => s.Bounds.Left);
        int vt = allScreens.Min(s => s.Bounds.Top);
        int vr = allScreens.Max(s => s.Bounds.Right);
        int vb = allScreens.Max(s => s.Bounds.Bottom);
        int vw = vr - vl;
        int vh = vb - vt;
        _offsetX = vl;
        _offsetY = vt;

        _snapshot = new Bitmap(vw, vh, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(_snapshot))
            g.CopyFromScreen(vl, vt, 0, 0, new System.Drawing.Size(vw, vh));

        // Convert snapshot to WPF image
        var bgSource = BitmapToSource(_snapshot);

        // Cache DPI scale once
        _dpiScale = GetDpiScale();

        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        Topmost = true;
        ShowInTaskbar = false;
        Left = vl / _dpiScale;
        Top = vt / _dpiScale;
        Width = vw / _dpiScale;
        Height = vh / _dpiScale;
        Cursor = System.Windows.Input.Cursors.Cross;
        Content = new System.Windows.Controls.Image
        {
            Source = bgSource,
            Stretch = Stretch.Fill,  // Fill to cover DPI-scaled window
        };

        // Floating zoom preview
        _previewImage = new System.Windows.Controls.Image { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(_previewImage, BitmapScalingMode.NearestNeighbor);

        _preview = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            Width = 168,
            Height = 168,
            IsHitTestVisible = false,
            Content = new Border
            {
                BorderBrush = System.Windows.Media.Brushes.DimGray,
                BorderThickness = new Thickness(2),
                Child = _previewImage,
            },
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;

        MouseLeftButtonDown += (_, _) =>
        {
            // Use the physical pixel coords from the last timer tick
            int sx = _lastPixelX;
            int sy = _lastPixelY;
            if (sx >= 0 && sy >= 0 && sx < _snapshot.Width && sy < _snapshot.Height)
            {
                var c = _snapshot.GetPixel(sx, sy);
                PickedR = c.R;
                PickedG = c.G;
                PickedB = c.B;
            }
            Finish(true);
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape) Finish(false);
        };
        Loaded += (_, _) => { _preview.Show(); _timer.Start(); };
        Closed += (_, _) => { _snapshot.Dispose(); };
    }

    /// <summary>
    /// Run the eyedropper. Blocks until the user clicks or presses ESC.
    /// </summary>
    public static EyedropperOverlay Pick()
    {
        var overlay = new EyedropperOverlay();
        overlay.ShowDialog();
        return overlay;
    }

    private void OnTick(object? s, EventArgs a)
    {
        GetCursorPos(out POINT pt);

        // Always move preview first (even if image update fails)
        _preview.Left = pt.X / _dpiScale + 24;
        _preview.Top = pt.Y / _dpiScale + 24;

        try
        {
            // Physical pixel position clamped to snapshot bounds
            int sx = Math.Clamp(pt.X - _offsetX, 0, _snapshot.Width - 1);
            int sy = Math.Clamp(pt.Y - _offsetY, 0, _snapshot.Height - 1);
            _lastPixelX = sx;
            _lastPixelY = sy;

            const int size = 21;
            int half = size / 2;

            // Compute crop rect fully within snapshot
            int rx = Math.Clamp(sx - half, 0, _snapshot.Width - size);
            int ry = Math.Clamp(sy - half, 0, _snapshot.Height - size);

            using var region = _snapshot.Clone(
                new Rectangle(rx, ry, size, size), _snapshot.PixelFormat);

            int cx = sx - rx;
            int cy = sy - ry;
            DrawCrosshair(region, cx, cy);

            _previewImage.Source = BitmapToSource(region);
        }
        catch
        {
            // Never let an exception kill the preview
        }
    }

    private void Finish(bool ok)
    {
        Confirmed = ok;
        _timer.Stop();
        _preview.Close();
        Close();
    }

    [DllImport("user32.dll")]
    private static extern int GetDpiForSystem();

    private static double GetDpiScale()
    {
        try { return GetDpiForSystem() / 96.0; }
        catch { return 1.0; }
    }

    private static BitmapSource BitmapToSource(Bitmap bmp)
    {
        var bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var src = BitmapSource.Create(bd.Width, bd.Height, 96, 96,
            PixelFormats.Bgra32, null, bd.Scan0, bd.Stride * bd.Height, bd.Stride);
        bmp.UnlockBits(bd);
        src.Freeze();
        return src;
    }

    private static void DrawCrosshair(Bitmap bmp, int cx, int cy)
    {
        var c = bmp.GetPixel(cx, cy);
        double lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
        var border = lum > 140
            ? System.Drawing.Color.FromArgb(240, 0, 0, 0)
            : System.Drawing.Color.FromArgb(240, 255, 255, 255);
        var line = lum > 140
            ? System.Drawing.Color.FromArgb(100, 0, 0, 0)
            : System.Drawing.Color.FromArgb(100, 255, 255, 255);

        // Border around center pixel
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                int nx = cx + dx, ny = cy + dy;
                if ((dx != 0 || dy != 0) && nx >= 0 && ny >= 0 && nx < bmp.Width && ny < bmp.Height)
                    bmp.SetPixel(nx, ny, border);
            }

        // Crosshair lines
        for (int i = 0; i < bmp.Width; i++)
            if (Math.Abs(i - cx) > 2) bmp.SetPixel(i, cy, line);
        for (int j = 0; j < bmp.Height; j++)
            if (Math.Abs(j - cy) > 2) bmp.SetPixel(cx, j, line);
    }
}
