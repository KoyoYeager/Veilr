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
        return target.EraseAlgorithm switch
        {
            "labmask" => EraseColorLabMask(source, target),
            "ycbcr" => EraseColorYCbCr(source, target),
            _ => EraseColorChromaKey(source, target),
        };
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

        // Color family: hue angle tolerance (degrees)
        // Must be tight enough to exclude orange (例題 header at ~55°)
        // while including red variants (pure red at ~40° vs target at ~39°)
        double hueToleranceDeg = similarity * 0.5;

        // Pass 1: compute alpha for every pixel
        double[] alpha = new double[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * stride + x * 4;
                RgbToLab(src[i + 2], src[i + 1], src[i], out double pL, out double pA, out double pB);

                double chromaDist = Math.Sqrt((pA - tA) * (pA - tA) + (pB - tB) * (pB - tB));
                double pixelChroma = Math.Sqrt(pA * pA + pB * pB);

                double keyDist = chromaDist;

                if (pixelChroma > 2 && targetChroma > 5)
                {
                    double pixelAngle = Math.Atan2(pB, pA);
                    double angleDiff = Math.Abs(targetAngle - pixelAngle);
                    if (angleDiff > Math.PI) angleDiff = 2 * Math.PI - angleDiff;
                    double angleDeg = angleDiff * 180.0 / Math.PI;

                    // Color family match: same hue direction → treat as same color
                    // regardless of saturation/lightness difference
                    if (angleDeg <= hueToleranceDeg && pixelChroma > 10)
                    {
                        // Effective distance based on hue angle alone
                        keyDist = Math.Min(keyDist, angleDeg / hueToleranceDeg * similarity);
                    }
                    else if (angleDeg <= outerRadius * 1.3)
                    {
                        double angleBonus = (1.0 - angleDeg / (outerRadius * 1.3)) * similarity * 0.5;
                        keyDist = Math.Max(0, keyDist - angleBonus);
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

        // Pass 4: Despill — remove target color cast from edge pixels
        double tNormR = target.Rgb[0] / 255.0;
        double tNormG = target.Rgb[1] / 255.0;
        double tNormB = target.Rgb[2] / 255.0;

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                if (alpha[idx] < 0.95) continue;

                // Check if near a keyed pixel (within 4px)
                bool nearKeyed = false;
                for (int dy = -4; dy <= 4 && !nearKeyed; dy++)
                    for (int dx = -4; dx <= 4 && !nearKeyed; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && nx < w && ny >= 0 && ny < h && alpha[ny * w + nx] < 0.5)
                            nearKeyed = true;
                    }
                if (!nearKeyed) continue;

                int i = y * stride + x * 4;
                double pR = dst[i + 2] / 255.0;
                double pG = dst[i + 1] / 255.0;
                double pB = dst[i] / 255.0;

                // Despill: limit the dominant target channel
                double spillLimit;
                if (tNormR >= tNormG && tNormR >= tNormB)
                {
                    spillLimit = Math.Max(pG, pB);
                    if (pR > spillLimit) pR = pR * 0.4 + spillLimit * 0.6;
                }
                else if (tNormG >= tNormR && tNormG >= tNormB)
                {
                    spillLimit = Math.Max(pR, pB);
                    if (pG > spillLimit) pG = pG * 0.4 + spillLimit * 0.6;
                }
                else
                {
                    spillLimit = Math.Max(pR, pG);
                    if (pB > spillLimit) pB = pB * 0.4 + spillLimit * 0.6;
                }

                dst[i + 2] = (byte)(Math.Clamp(pR, 0, 1) * 255);
                dst[i + 1] = (byte)(Math.Clamp(pG, 0, 1) * 255);
                dst[i]     = (byte)(Math.Clamp(pB, 0, 1) * 255);
            }

        Marshal.Copy(dst, 0, dstData.Scan0, bytes);
        result.UnlockBits(dstData);
        return result;
    }

    /// <summary>
    /// Find background color by averaging nearby fully-opaque pixels (alpha=1),
    /// skipping pixels near the edge of the keyed region.
    /// </summary>
    /// <summary>
    /// Find background color using MEDIAN of nearby clean pixels.
    /// Median is more robust against outliers than mean.
    /// </summary>
    private static (byte R, byte G, byte B) FindBackground(
        byte[] src, double[] alpha, int stride, int cx, int cy, int w, int h)
    {
        var samplesR = new List<byte>(16);
        var samplesG = new List<byte>(16);
        var samplesB = new List<byte>(16);

        for (int r = 1; r <= 50 && samplesR.Count < 16; r++)
        {
            for (int d = -r; d <= r && samplesR.Count < 16; d++)
            {
                TryCollect(src, alpha, stride, cx + d, cy - r, w, h, samplesR, samplesG, samplesB);
                TryCollect(src, alpha, stride, cx + d, cy + r, w, h, samplesR, samplesG, samplesB);
                if (d != -r && d != r)
                {
                    TryCollect(src, alpha, stride, cx - r, cy + d, w, h, samplesR, samplesG, samplesB);
                    TryCollect(src, alpha, stride, cx + r, cy + d, w, h, samplesR, samplesG, samplesB);
                }
            }
        }

        if (samplesR.Count == 0) return (255, 255, 255);

        samplesR.Sort(); samplesG.Sort(); samplesB.Sort();
        int mid = samplesR.Count / 2;
        return (samplesR[mid], samplesG[mid], samplesB[mid]);
    }

    private static void TryCollect(byte[] src, double[] alpha, int stride,
        int x, int y, int w, int h, List<byte> rList, List<byte> gList, List<byte> bList)
    {
        if (x < 0 || x >= w || y < 0 || y >= h) return;
        if (alpha[y * w + x] < 1.0) return;

        // Skip if any neighbor has alpha < 1 (edge contamination)
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && nx < w && ny >= 0 && ny < h && alpha[ny * w + nx] < 1.0)
                    return;
            }

        int i = y * stride + x * 4;
        rList.Add(src[i + 2]); gList.Add(src[i + 1]); bList.Add(src[i]);
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
                bool familyMatch = false;
                if (pixelChroma > 2 && targetChroma > 5)
                {
                    double pixelAngle = Math.Atan2(pB, pA);
                    double angleDiff = Math.Abs(targetAngle - pixelAngle);
                    if (angleDiff > Math.PI) angleDiff = 2 * Math.PI - angleDiff;
                    double angleDeg = angleDiff * 180.0 / Math.PI;
                    edgeMatch = angleDeg <= maxDist * 1.3;
                    // Color family: same hue direction with sufficient chroma
                    familyMatch = angleDeg <= maxDist * 0.6 && pixelChroma > 10;
                }
                if (directMatch || edgeMatch || familyMatch)
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
        var sR = new List<byte>(16); var sG = new List<byte>(16); var sB = new List<byte>(16);
        for (int r = 1; r <= 50 && sR.Count < 16; r++)
            for (int d = -r; d <= r && sR.Count < 16; d++)
            {
                CollectMask(src, mask, stride, cx + d, cy - r, w, h, sR, sG, sB);
                CollectMask(src, mask, stride, cx + d, cy + r, w, h, sR, sG, sB);
                if (d != -r && d != r)
                {
                    CollectMask(src, mask, stride, cx - r, cy + d, w, h, sR, sG, sB);
                    CollectMask(src, mask, stride, cx + r, cy + d, w, h, sR, sG, sB);
                }
            }
        if (sR.Count == 0) return (255, 255, 255);
        sR.Sort(); sG.Sort(); sB.Sort();
        int mid = sR.Count / 2;
        return (sR[mid], sG[mid], sB[mid]);
    }

    private static void CollectMask(byte[] src, bool[] mask, int stride,
        int x, int y, int w, int h, List<byte> rL, List<byte> gL, List<byte> bL)
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
        rL.Add(src[i + 2]); gL.Add(src[i + 1]); bL.Add(src[i]);
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

    // ══════════════════════════════════════════════════════════
    //  Algorithm 3: YCbCr Chroma Key (broadcast industry standard)
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// YCbCr color space keying — the broadcast/film industry standard.
    /// Separates luminance (Y) from chrominance (Cb,Cr) completely.
    /// Distance is computed on CbCr plane only, ignoring brightness.
    /// </summary>
    private Bitmap EraseColorYCbCr(Bitmap source, ColorSettings target)
    {
        int w = source.Width, h = source.Height;
        var srcData = source.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int stride = srcData.Stride;
        int bytes = Math.Abs(stride) * h;
        byte[] src = new byte[bytes];
        Marshal.Copy(srcData.Scan0, src, 0, bytes);
        source.UnlockBits(srcData);

        // Target in YCbCr
        RgbToYCbCr(target.Rgb[0], target.Rgb[1], target.Rgb[2],
            out double tY, out double tCb, out double tCr);

        double similarity = target.Threshold.H * 1.8;
        double smoothness = similarity * 0.7;
        double outerRadius = similarity + smoothness;

        // Pass 1: alpha from CbCr distance
        double[] alpha = new double[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * stride + x * 4;
                RgbToYCbCr(src[i + 2], src[i + 1], src[i],
                    out double pY, out double pCb, out double pCr);

                double dist = Math.Sqrt((pCb - tCb) * (pCb - tCb) + (pCr - tCr) * (pCr - tCr));

                if (dist <= similarity)
                    alpha[y * w + x] = 0.0;
                else if (dist >= outerRadius)
                    alpha[y * w + x] = 1.0;
                else
                    alpha[y * w + x] = (dist - similarity) / smoothness;
            }

        // Pass 2: background color
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

        // Pass 3: blend
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
                    dst[i] = src[i]; dst[i + 1] = src[i + 1];
                    dst[i + 2] = src[i + 2]; dst[i + 3] = src[i + 3];
                }
                else
                {
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

    private static void RgbToYCbCr(int r, int g, int b,
        out double y, out double cb, out double cr)
    {
        y  =  0.299 * r + 0.587 * g + 0.114 * b;
        cb = -0.169 * r - 0.331 * g + 0.500 * b + 128;
        cr =  0.500 * r - 0.419 * g - 0.081 * b + 128;
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
