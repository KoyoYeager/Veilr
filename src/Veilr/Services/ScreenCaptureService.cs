using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Veilr.Services;

public class ScreenCaptureService
{
    [DllImport("user32.dll")] private static extern nint GetDC(nint hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hWnd, nint hDC);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint hdc, int nXPos, int nYPos);

    public Bitmap CaptureRegion(int x, int y, int width, int height)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        return bmp;
    }

    /// <summary>
    /// Capture into a pre-allocated Bitmap (zero allocation).
    /// </summary>
    public void CaptureInto(Bitmap target, int x, int y)
    {
        using var g = Graphics.FromImage(target);
        g.CopyFromScreen(x, y, 0, 0, target.Size, CopyPixelOperation.SourceCopy);
    }

    public System.Drawing.Color GetPixelColor(int x, int y)
    {
        var hdc = GetDC(nint.Zero);
        uint pixel = GetPixel(hdc, x, y);
        ReleaseDC(nint.Zero, hdc);
        return System.Drawing.Color.FromArgb(
            (int)(pixel & 0x000000FF),
            (int)((pixel & 0x0000FF00) >> 8),
            (int)((pixel & 0x00FF0000) >> 16));
    }
}
