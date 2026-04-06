using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Veilr.Models;

namespace Veilr.Services;

public class ColorDetectorService
{
    /// <summary>
    /// Erase target-colored pixels using CIE Lab color distance.
    /// Lab space is perceptually uniform: black/white/gray are naturally far from any saturated color.
    /// </summary>
    public Bitmap EraseColor(Bitmap source, ColorSettings target)
    {
        int w = source.Width, h = source.Height;
        var srcData = source.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int stride = srcData.Stride;
        int bytes = Math.Abs(stride) * h;
        byte[] src = new byte[bytes];
        Marshal.Copy(srcData.Scan0, src, 0, bytes);
        source.UnlockBits(srcData);

        // Convert target color to Lab (once)
        RgbToLab(target.Rgb[0], target.Rgb[1], target.Rgb[2],
            out double tL, out double tA, out double tB);

        // Chrominance distance threshold from settings
        // Use H threshold as the primary control (mapped from tolerance slider)
        // tolerance 0→H=0→threshold=0, tolerance 50→H=22→threshold=30, tolerance 100→H=45→threshold=60
        double maxDist = target.Threshold.H * 1.35;

        // Target hue angle in Lab a-b plane
        double targetChroma = Math.Sqrt(tA * tA + tB * tB);
        double targetAngle = Math.Atan2(tB, tA);

        // Pass 1: mark target pixels using Lab chrominance
        bool[] mask = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * stride + x * 4;
                RgbToLab(src[i + 2], src[i + 1], src[i], out double pL, out double pA, out double pB);

                double chromaDist = Math.Sqrt((pA - tA) * (pA - tA) + (pB - tB) * (pB - tB));
                double pixelChroma = Math.Sqrt(pA * pA + pB * pB);

                // Criterion 1: chrominance distance within threshold (core color match)
                bool directMatch = chromaDist <= maxDist;

                // Criterion 2: same hue direction with some chroma (anti-aliased edges)
                bool edgeMatch = false;
                if (pixelChroma > 2 && targetChroma > 5)
                {
                    double pixelAngle = Math.Atan2(pB, pA);
                    double angleDiff = Math.Abs(targetAngle - pixelAngle);
                    if (angleDiff > Math.PI) angleDiff = 2 * Math.PI - angleDiff;
                    double angleDiffDeg = angleDiff * 180.0 / Math.PI;
                    edgeMatch = angleDiffDeg <= maxDist * 1.3;
                }

                if (directMatch || edgeMatch)
                    mask[y * w + x] = true;
            }

        // Pass 1.5: conditional dilation for remaining anti-aliased edge pixels
        // Only expand into pixels surrounded by 2+ marked neighbors
        // AND having some chroma in the target direction
        for (int pass = 0; pass < 3; pass++)
        {
            bool[] next = new bool[w * h];
            Array.Copy(mask, next, mask.Length);
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    if (next[y * w + x]) continue;

                    // Count marked neighbors (4-connected)
                    int neighbors = 0;
                    if (mask[(y - 1) * w + x]) neighbors++;
                    if (mask[(y + 1) * w + x]) neighbors++;
                    if (mask[y * w + x - 1]) neighbors++;
                    if (mask[y * w + x + 1]) neighbors++;
                    if (neighbors < 3) continue;

                    int i = y * stride + x * 4;
                    RgbToLab(src[i + 2], src[i + 1], src[i], out _, out double eA, out double eB);
                    double eChroma = Math.Sqrt(eA * eA + eB * eB);
                    if (eChroma < 1.5) continue; // truly achromatic

                    double eAngle = Math.Atan2(eB, eA);
                    double aDiff = Math.Abs(targetAngle - eAngle);
                    if (aDiff > Math.PI) aDiff = 2 * Math.PI - aDiff;
                    if (aDiff * 180 / Math.PI <= 28)
                        next[y * w + x] = true;
                }
            mask = next;
        }

        // Pass 2: replace marked pixels
        var result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var dstData = result.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        byte[] dst = new byte[bytes];

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * stride + x * 4;
                if (!mask[y * w + x])
                {
                    dst[i] = src[i]; dst[i + 1] = src[i + 1];
                    dst[i + 2] = src[i + 2]; dst[i + 3] = src[i + 3];
                }
                else
                {
                    var (nr, ng, nb) = FindNearestClean(src, mask, stride, x, y, w, h);
                    dst[i] = nb; dst[i + 1] = ng; dst[i + 2] = nr; dst[i + 3] = 255;
                }
            }

        Marshal.Copy(dst, 0, dstData.Scan0, bytes);
        result.UnlockBits(dstData);
        return result;
    }

    // ── Lab conversion (RGB → XYZ → Lab) ──────────────────────

    private static void RgbToLab(int r, int g, int b,
        out double L, out double labA, out double labB)
    {
        // 1. RGB to linear (sRGB gamma correction)
        double lr = Linearize(r / 255.0);
        double lg = Linearize(g / 255.0);
        double lb = Linearize(b / 255.0);

        // 2. Linear RGB to XYZ (sRGB D65)
        double x = lr * 0.4124564 + lg * 0.3575761 + lb * 0.1804375;
        double y = lr * 0.2126729 + lg * 0.7151522 + lb * 0.0721750;
        double z = lr * 0.0193339 + lg * 0.1191920 + lb * 0.9503041;

        // 3. XYZ to Lab (D65 reference white)
        x /= 0.95047; y /= 1.00000; z /= 1.08883;
        x = LabF(x); y = LabF(y); z = LabF(z);

        L = 116.0 * y - 16.0;
        labA = 500.0 * (x - y);
        labB = 200.0 * (y - z);
    }

    private static double Linearize(double v) =>
        v > 0.04045 ? Math.Pow((v + 0.055) / 1.055, 2.4) : v / 12.92;

    private static double LabF(double t) =>
        t > 0.008856 ? Math.Cbrt(t) : (7.787 * t + 16.0 / 116.0);

    // ── Pixel search ──────────────────────────────────────────

    /// <summary>
    /// Find replacement color by averaging multiple nearby non-marked pixels.
    /// This produces smoother results than using a single nearest pixel.
    /// </summary>
    private static (byte R, byte G, byte B) FindNearestClean(
        byte[] src, bool[] mask, int stride, int cx, int cy, int w, int h)
    {
        int sumR = 0, sumG = 0, sumB = 0, count = 0;
        const int samplesNeeded = 12;

        for (int r = 1; r <= 50 && count < samplesNeeded; r++)
        {
            for (int d = -r; d <= r && count < samplesNeeded; d++)
            {
                Accumulate(src, mask, stride, cx + d, cy - r, w, h, ref sumR, ref sumG, ref sumB, ref count);
                Accumulate(src, mask, stride, cx + d, cy + r, w, h, ref sumR, ref sumG, ref sumB, ref count);
                if (d != -r && d != r)
                {
                    Accumulate(src, mask, stride, cx - r, cy + d, w, h, ref sumR, ref sumG, ref sumB, ref count);
                    Accumulate(src, mask, stride, cx + r, cy + d, w, h, ref sumR, ref sumG, ref sumB, ref count);
                }
            }
        }

        if (count == 0) return (255, 255, 255);
        return ((byte)(sumR / count), (byte)(sumG / count), (byte)(sumB / count));
    }

    private static void Accumulate(byte[] src, bool[] mask, int stride,
        int x, int y, int w, int h, ref int sumR, ref int sumG, ref int sumB, ref int count)
    {
        if (x < 0 || x >= w || y < 0 || y >= h) return;
        if (mask[y * w + x]) return;

        // Skip pixels near marked pixels (avoid anti-aliased edge contamination)
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && nx < w && ny >= 0 && ny < h && mask[ny * w + nx])
                    return;
            }

        int i = y * stride + x * 4;
        sumR += src[i + 2]; sumG += src[i + 1]; sumB += src[i];
        count++;
    }

    // ── Multiply blend ────────────────────────────────────────

    public Bitmap MultiplyBlend(Bitmap source, int[] sheetRgb)
    {
        int w = source.Width, h = source.Height;
        var result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var srcData = source.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var dstData = result.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        int bytes = Math.Abs(srcData.Stride) * h;
        byte[] srcPx = new byte[bytes];
        byte[] dstPx = new byte[bytes];
        Marshal.Copy(srcData.Scan0, srcPx, 0, bytes);

        double sr = sheetRgb[0] / 255.0, sg = sheetRgb[1] / 255.0, sb = sheetRgb[2] / 255.0;

        for (int i = 0; i < bytes; i += 4)
        {
            dstPx[i]     = (byte)(srcPx[i]     * sb);
            dstPx[i + 1] = (byte)(srcPx[i + 1] * sg);
            dstPx[i + 2] = (byte)(srcPx[i + 2] * sr);
            dstPx[i + 3] = srcPx[i + 3];
        }

        Marshal.Copy(dstPx, 0, dstData.Scan0, bytes);
        source.UnlockBits(srcData);
        result.UnlockBits(dstData);
        return result;
    }
}
