using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Veilr.Models;

namespace Veilr.Services;

public class ColorDetectorService
{
    /// <summary>
    /// Dispatch to the selected erase algorithm.
    /// </summary>
    public Bitmap EraseColor(Bitmap source, ColorSettings target)
    {
        return target.EraseAlgorithm == "labmask"
            ? EraseColorLabMask(source, target)
            : EraseColorChromaKey(source, target);
    }

    // ══════════════════════════════════════════════════════════
    //  Algorithm 1: Chroma Key (soft alpha blending)
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Chroma-key style: continuous alpha (0.0-1.0) with smooth blending.
    /// Best for: gradual color boundaries, printed text with heavy anti-aliasing.
    /// </summary>
    private Bitmap EraseColorChromaKey(Bitmap source, ColorSettings target)
    {
        int w = source.Width, h = source.Height;
        var srcData = source.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int stride = srcData.Stride;
        int bytes = Math.Abs(stride) * h;
        byte[] src = new byte[bytes];
        Marshal.Copy(srcData.Scan0, src, 0, bytes);
        source.UnlockBits(srcData);

        // Target color in Lab
        RgbToLab(target.Rgb[0], target.Rgb[1], target.Rgb[2],
            out double tL, out double tA, out double tB);
        double targetChroma = Math.Sqrt(tA * tA + tB * tB);
        double targetAngle = Math.Atan2(tB, tA);

        // Thresholds from settings (derived from tolerance slider)
        double similarity = target.Threshold.H * 1.35;       // inner radius: fully remove
        double smoothness = similarity * 0.8;                  // transition zone width
        double outerRadius = similarity + smoothness;          // beyond this: fully keep

        // Pass 1: compute alpha for every pixel
        double[] alpha = new double[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * stride + x * 4;
                RgbToLab(src[i + 2], src[i + 1], src[i], out double pL, out double pA, out double pB);

                double chromaDist = Math.Sqrt((pA - tA) * (pA - tA) + (pB - tB) * (pB - tB));
                double pixelChroma = Math.Sqrt(pA * pA + pB * pB);

                // Compute key distance (combined chrominance + hue angle)
                double keyDist = chromaDist;

                // Also consider hue angle for anti-aliased edges
                if (pixelChroma > 2 && targetChroma > 5)
                {
                    double pixelAngle = Math.Atan2(pB, pA);
                    double angleDiff = Math.Abs(targetAngle - pixelAngle);
                    if (angleDiff > Math.PI) angleDiff = 2 * Math.PI - angleDiff;
                    double angleDeg = angleDiff * 180.0 / Math.PI;

                    // If hue angle is close, reduce the effective distance
                    // (helps detect anti-aliased edges that have low chroma but correct hue)
                    if (angleDeg <= outerRadius * 1.3)
                    {
                        double angleBonus = (1.0 - angleDeg / (outerRadius * 1.3)) * similarity * 0.5;
                        keyDist = Math.Max(0, chromaDist - angleBonus);
                    }
                }

                // Map distance to alpha: 0=remove, 1=keep
                if (keyDist <= similarity)
                    alpha[y * w + x] = 0.0;
                else if (keyDist >= outerRadius)
                    alpha[y * w + x] = 1.0;
                else
                    alpha[y * w + x] = (keyDist - similarity) / smoothness;
            }

        // Pass 2: compute background color map (averaged from fully-opaque nearby pixels)
        // For each pixel with alpha < 1, find replacement color from nearby alpha=1 pixels
        byte[] bgR = new byte[w * h], bgG = new byte[w * h], bgB = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (alpha[y * w + x] >= 1.0)
                {
                    int i = y * stride + x * 4;
                    bgR[y * w + x] = src[i + 2];
                    bgG[y * w + x] = src[i + 1];
                    bgB[y * w + x] = src[i];
                }
                else
                {
                    var (nr, ng, nb) = FindBackground(src, alpha, stride, x, y, w, h);
                    bgR[y * w + x] = nr;
                    bgG[y * w + x] = ng;
                    bgB[y * w + x] = nb;
                }
            }

        // Pass 3: blend original with background using alpha
        var result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var dstData = result.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        byte[] dst = new byte[bytes];

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * stride + x * 4;
                int idx = y * w + x;
                double a = alpha[idx];

                if (a >= 1.0)
                {
                    // Fully keep original
                    dst[i] = src[i]; dst[i + 1] = src[i + 1];
                    dst[i + 2] = src[i + 2]; dst[i + 3] = src[i + 3];
                }
                else
                {
                    // Blend: result = original * alpha + background * (1 - alpha)
                    dst[i]     = (byte)(src[i]     * a + bgB[idx] * (1 - a));
                    dst[i + 1] = (byte)(src[i + 1] * a + bgG[idx] * (1 - a));
                    dst[i + 2] = (byte)(src[i + 2] * a + bgR[idx] * (1 - a));
                    dst[i + 3] = 255;
                }
            }

        Marshal.Copy(dst, 0, dstData.Scan0, bytes);
        result.UnlockBits(dstData);
        return result;
    }

    /// <summary>
    /// Find background color by averaging nearby fully-opaque pixels (alpha=1),
    /// skipping pixels near the edge of the keyed region.
    /// </summary>
    private static (byte R, byte G, byte B) FindBackground(
        byte[] src, double[] alpha, int stride, int cx, int cy, int w, int h)
    {
        int sumR = 0, sumG = 0, sumB = 0, count = 0;
        const int samplesNeeded = 12;

        for (int r = 1; r <= 50 && count < samplesNeeded; r++)
        {
            for (int d = -r; d <= r && count < samplesNeeded; d++)
            {
                TryAccum(src, alpha, stride, cx + d, cy - r, w, h, ref sumR, ref sumG, ref sumB, ref count);
                TryAccum(src, alpha, stride, cx + d, cy + r, w, h, ref sumR, ref sumG, ref sumB, ref count);
                if (d != -r && d != r)
                {
                    TryAccum(src, alpha, stride, cx - r, cy + d, w, h, ref sumR, ref sumG, ref sumB, ref count);
                    TryAccum(src, alpha, stride, cx + r, cy + d, w, h, ref sumR, ref sumG, ref sumB, ref count);
                }
            }
        }

        if (count == 0) return (255, 255, 255);
        return ((byte)(sumR / count), (byte)(sumG / count), (byte)(sumB / count));
    }

    private static void TryAccum(byte[] src, double[] alpha, int stride,
        int x, int y, int w, int h, ref int sumR, ref int sumG, ref int sumB, ref int count)
    {
        if (x < 0 || x >= w || y < 0 || y >= h) return;
        if (alpha[y * w + x] < 1.0) return; // only sample fully-opaque pixels

        // Skip if any neighbor has alpha < 1 (avoid edge contamination)
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && nx < w && ny >= 0 && ny < h && alpha[ny * w + nx] < 1.0)
                    return;
            }

        int i = y * stride + x * 4;
        sumR += src[i + 2]; sumG += src[i + 1]; sumB += src[i];
        count++;
    }

    // ══════════════════════════════════════════════════════════
    //  Algorithm 2: Lab Mask (binary mask + averaged replacement)
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Binary mask with 3-layer detection + conditional dilation + averaged replacement.
    /// Best for: sharp text, solid color blocks, when chroma key over-blends.
    /// </summary>
    private Bitmap EraseColorLabMask(Bitmap source, ColorSettings target)
    {
        int w = source.Width, h = source.Height;
        var srcData = source.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int stride = srcData.Stride;
        int bytes = Math.Abs(stride) * h;
        byte[] src = new byte[bytes];
        Marshal.Copy(srcData.Scan0, src, 0, bytes);
        source.UnlockBits(srcData);

        RgbToLab(target.Rgb[0], target.Rgb[1], target.Rgb[2],
            out double tL, out double tA, out double tB);
        double targetChroma = Math.Sqrt(tA * tA + tB * tB);
        double targetAngle = Math.Atan2(tB, tA);
        double maxDist = target.Threshold.H * 1.35;

        // Pass 1: mark target pixels
        bool[] mask = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * stride + x * 4;
                RgbToLab(src[i + 2], src[i + 1], src[i], out double pL, out double pA, out double pB);
                double chromaDist = Math.Sqrt((pA - tA) * (pA - tA) + (pB - tB) * (pB - tB));
                double pixelChroma = Math.Sqrt(pA * pA + pB * pB);

                bool directMatch = chromaDist <= maxDist;
                bool edgeMatch = false;
                if (pixelChroma > 2 && targetChroma > 5)
                {
                    double pixelAngle = Math.Atan2(pB, pA);
                    double angleDiff = Math.Abs(targetAngle - pixelAngle);
                    if (angleDiff > Math.PI) angleDiff = 2 * Math.PI - angleDiff;
                    edgeMatch = angleDiff * 180.0 / Math.PI <= maxDist * 1.3;
                }
                if (directMatch || edgeMatch)
                    mask[y * w + x] = true;
            }

        // Pass 1.5: conditional dilation
        for (int pass = 0; pass < 3; pass++)
        {
            bool[] next = new bool[w * h];
            Array.Copy(mask, next, mask.Length);
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    if (next[y * w + x]) continue;
                    int neighbors = 0;
                    if (mask[(y - 1) * w + x]) neighbors++;
                    if (mask[(y + 1) * w + x]) neighbors++;
                    if (mask[y * w + x - 1]) neighbors++;
                    if (mask[y * w + x + 1]) neighbors++;
                    if (neighbors < 3) continue;

                    int i = y * stride + x * 4;
                    RgbToLab(src[i + 2], src[i + 1], src[i], out _, out double eA, out double eB);
                    double eChroma = Math.Sqrt(eA * eA + eB * eB);
                    if (eChroma < 1.5) continue;
                    double eAngle = Math.Atan2(eB, eA);
                    double aDiff = Math.Abs(targetAngle - eAngle);
                    if (aDiff > Math.PI) aDiff = 2 * Math.PI - aDiff;
                    if (aDiff * 180 / Math.PI <= 28)
                        next[y * w + x] = true;
                }
            mask = next;
        }

        // Pass 2: replace with averaged background
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
                    var (nr, ng, nb) = FindBackgroundMask(src, mask, stride, x, y, w, h);
                    dst[i] = nb; dst[i + 1] = ng; dst[i + 2] = nr; dst[i + 3] = 255;
                }
            }

        Marshal.Copy(dst, 0, dstData.Scan0, bytes);
        result.UnlockBits(dstData);
        return result;
    }

    private static (byte R, byte G, byte B) FindBackgroundMask(
        byte[] src, bool[] mask, int stride, int cx, int cy, int w, int h)
    {
        int sumR = 0, sumG = 0, sumB = 0, count = 0;
        for (int r = 1; r <= 50 && count < 12; r++)
            for (int d = -r; d <= r && count < 12; d++)
            {
                AccumMask(src, mask, stride, cx + d, cy - r, w, h, ref sumR, ref sumG, ref sumB, ref count);
                AccumMask(src, mask, stride, cx + d, cy + r, w, h, ref sumR, ref sumG, ref sumB, ref count);
                if (d != -r && d != r)
                {
                    AccumMask(src, mask, stride, cx - r, cy + d, w, h, ref sumR, ref sumG, ref sumB, ref count);
                    AccumMask(src, mask, stride, cx + r, cy + d, w, h, ref sumR, ref sumG, ref sumB, ref count);
                }
            }
        if (count == 0) return (255, 255, 255);
        return ((byte)(sumR / count), (byte)(sumG / count), (byte)(sumB / count));
    }

    private static void AccumMask(byte[] src, bool[] mask, int stride,
        int x, int y, int w, int h, ref int sumR, ref int sumG, ref int sumB, ref int count)
    {
        if (x < 0 || x >= w || y < 0 || y >= h) return;
        if (mask[y * w + x]) return;
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && nx < w && ny >= 0 && ny < h && mask[ny * w + nx]) return;
            }
        int i = y * stride + x * 4;
        sumR += src[i + 2]; sumG += src[i + 1]; sumB += src[i];
        count++;
    }

    // ── Lab conversion ────────────────────────────────────────

    private static void RgbToLab(int r, int g, int b,
        out double L, out double labA, out double labB)
    {
        double lr = Linearize(r / 255.0);
        double lg = Linearize(g / 255.0);
        double lb = Linearize(b / 255.0);

        double x = lr * 0.4124564 + lg * 0.3575761 + lb * 0.1804375;
        double y = lr * 0.2126729 + lg * 0.7151522 + lb * 0.0721750;
        double z = lr * 0.0193339 + lg * 0.1191920 + lb * 0.9503041;

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
