using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Veilr.Models;

namespace Veilr.Services;

/// <summary>
/// Pre-allocated frame buffers for zero-allocation per-frame processing.
/// Call EnsureCapacity once when dimensions change; reuse across frames.
/// </summary>
public class FrameBuffer
{
    public int Width, Height, Stride, PixelCount, ByteCount;
    public byte[] Src = [], Dst = [];
    public double[] Alpha = [];
    public byte[] BgR = [], BgG = [], BgB = [];
    public int[] NearestClean = [];
    public Queue<int> BfsQueue = new();

    /// <summary>Pre-allocated capture Bitmap (reused across frames).</summary>
    public Bitmap? CaptureBitmap;

    public void EnsureCapacity(int w, int h, int stride)
    {
        if (w == Width && h == Height && stride == Stride) return;
        Width = w; Height = h; Stride = stride;
        PixelCount = w * h;
        ByteCount = Math.Abs(stride) * h;
        Src = new byte[ByteCount];
        Dst = new byte[ByteCount];
        Alpha = new double[PixelCount];
        BgR = new byte[PixelCount];
        BgG = new byte[PixelCount];
        BgB = new byte[PixelCount];
        NearestClean = new int[PixelCount];
        CaptureBitmap?.Dispose();
        CaptureBitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
    }

    /// <summary>Capture screen into this buffer's Bitmap, then copy pixels into Src.</summary>
    public void CaptureAndCopyPixels(ScreenCaptureService captureService, int x, int y)
    {
        if (CaptureBitmap == null) return;
        captureService.CaptureInto(CaptureBitmap, x, y);
        var data = CaptureBitmap.LockBits(
            new Rectangle(0, 0, Width, Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(data.Scan0, Src, 0, ByteCount);
        CaptureBitmap.UnlockBits(data);
    }
}

public class ColorDetectorService
{
    // ── Pre-computed Lab LUT (6-bit per channel → 64×64×64 = 262,144 entries) ──
    private static readonly double[] LabLutL = new double[64 * 64 * 64];
    private static readonly double[] LabLutA = new double[64 * 64 * 64];
    private static readonly double[] LabLutB = new double[64 * 64 * 64];
    private static readonly double[] LinearizeLut = new double[256];

    static ColorDetectorService()
    {
        // Linearize LUT
        for (int i = 0; i < 256; i++)
        {
            double v = i / 255.0;
            LinearizeLut[i] = v > 0.04045 ? Math.Pow((v + 0.055) / 1.055, 2.4) : v / 12.92;
        }

        // Lab LUT (6-bit quantized: step=4, covers 0-252)
        for (int ri = 0; ri < 64; ri++)
            for (int gi = 0; gi < 64; gi++)
                for (int bi = 0; bi < 64; bi++)
                {
                    int idx = (ri << 12) | (gi << 6) | bi;
                    RgbToLabFull(ri * 4, gi * 4, bi * 4,
                        out LabLutL[idx], out LabLutA[idx], out LabLutB[idx]);
                }
    }

    /// <summary>
    /// Fast Lab lookup using 6-bit quantized table.
    /// </summary>
    private static void RgbToLabFast(int r, int g, int b,
        out double L, out double labA, out double labB)
    {
        int idx = ((r >> 2) << 12) | ((g >> 2) << 6) | (b >> 2);
        L = LabLutL[idx];
        labA = LabLutA[idx];
        labB = LabLutB[idx];
    }

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
    //  Algorithm 1: Chroma Key (soft alpha blending) — parallelized
    // ══════════════════════════════════════════════════════════

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

        RgbToLabFull(target.Rgb[0], target.Rgb[1], target.Rgb[2],
            out double tL, out double tA, out double tB);
        double targetChroma = Math.Sqrt(tA * tA + tB * tB);
        double targetAngle = Math.Atan2(tB, tA);

        double similarity = target.Threshold.H * 1.5;
        double smoothness = similarity * 0.8;
        double outerRadius = similarity + smoothness;
        double hueToleranceDeg = similarity * 0.5;

        // Pass 1: compute alpha (parallelized)
        double[] alpha = new double[w * h];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * stride + x * 4;
                RgbToLabFast(src[i + 2], src[i + 1], src[i], out double pL, out double pA, out double pB);

                double dA = pA - tA, dB = pB - tB;
                double chromaDist = Math.Sqrt(dA * dA + dB * dB);
                double pixelChroma = Math.Sqrt(pA * pA + pB * pB);

                double keyDist = chromaDist;

                if (pixelChroma > 2 && targetChroma > 5)
                {
                    double pixelAngle = Math.Atan2(pB, pA);
                    double angleDiff = Math.Abs(targetAngle - pixelAngle);
                    if (angleDiff > Math.PI) angleDiff = 2 * Math.PI - angleDiff;
                    double angleDeg = angleDiff * 180.0 / Math.PI;

                    if (angleDeg <= hueToleranceDeg && pixelChroma > 10)
                        keyDist = Math.Min(keyDist, angleDeg / hueToleranceDeg * similarity);
                    else if (angleDeg <= outerRadius * 1.3)
                    {
                        double angleBonus = (1.0 - angleDeg / (outerRadius * 1.3)) * similarity * 0.5;
                        keyDist = Math.Max(0, keyDist - angleBonus);
                    }
                }

                if (keyDist <= similarity)
                    alpha[y * w + x] = 0.0;
                else if (keyDist >= outerRadius)
                    alpha[y * w + x] = 1.0;
                else
                    alpha[y * w + x] = (keyDist - similarity) / smoothness;
            }
        });

        // Pass 2: BFS distance transform for background color
        var (bgR, bgG, bgB) = BuildBackgroundMapBfs(src, alpha, stride, w, h);

        // Pass 3: blend (parallelized)
        var result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var dstData = result.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        byte[] dst = new byte[bytes];

        Parallel.For(0, h, y =>
        {
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
        });

        // Pass 4: Graduated Despill (parallelized)
        double tNormR = target.Rgb[0] / 255.0;
        double tNormG = target.Rgb[1] / 255.0;
        double tNormB = target.Rgb[2] / 255.0;

        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                if (alpha[idx] < 0.9) continue;

                int minDist = int.MaxValue;
                for (int dy = -6; dy <= 6; dy++)
                    for (int dx = -6; dx <= 6; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && nx < w && ny >= 0 && ny < h && alpha[ny * w + nx] < 0.5)
                        {
                            int d = Math.Abs(dx) + Math.Abs(dy);
                            if (d < minDist) minDist = d;
                        }
                    }
                if (minDist > 6) continue;

                double strength = Math.Clamp(1.0 - (minDist - 1) / 6.0, 0, 1) * 0.7;

                int i = y * stride + x * 4;
                double pR = dst[i + 2] / 255.0;
                double pG = dst[i + 1] / 255.0;
                double pB = dst[i] / 255.0;

                if (tNormR >= tNormG && tNormR >= tNormB)
                {
                    double limit = Math.Max(pG, pB);
                    if (pR > limit) pR = pR * (1 - strength) + limit * strength;
                }
                else if (tNormG >= tNormR && tNormG >= tNormB)
                {
                    double limit = Math.Max(pR, pB);
                    if (pG > limit) pG = pG * (1 - strength) + limit * strength;
                }
                else
                {
                    double limit = Math.Max(pR, pG);
                    if (pB > limit) pB = pB * (1 - strength) + limit * strength;
                }

                dst[i + 2] = (byte)(Math.Clamp(pR, 0, 1) * 255);
                dst[i + 1] = (byte)(Math.Clamp(pG, 0, 1) * 255);
                dst[i]     = (byte)(Math.Clamp(pB, 0, 1) * 255);
            }
        });

        Marshal.Copy(dst, 0, dstData.Scan0, bytes);
        result.UnlockBits(dstData);
        return result;
    }

    // ══════════════════════════════════════════════════════════
    //  BFS distance transform: O(w×h) background color map
    //  Replaces per-pixel spiral search O(keyed × r²)
    // ══════════════════════════════════════════════════════════

    private static (byte[] R, byte[] G, byte[] B) BuildBackgroundMapBfs(
        byte[] src, double[] alpha, int stride, int w, int h)
    {
        int total = w * h;
        byte[] bgR = new byte[total], bgG = new byte[total], bgB = new byte[total];
        int[] nearestClean = new int[total]; // index of nearest clean pixel
        Array.Fill(nearestClean, -1);

        // Initialize queue with clean pixels (alpha >= 1.0, not adjacent to keyed)
        var queue = new Queue<int>(total / 2);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                if (alpha[idx] >= 1.0)
                {
                    // Check if surrounded by clean pixels (skip edge-contaminated)
                    bool clean = true;
                    for (int dy = -1; dy <= 1 && clean; dy++)
                        for (int dx = -1; dx <= 1 && clean; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx >= 0 && nx < w && ny >= 0 && ny < h && alpha[ny * w + nx] < 1.0)
                                clean = false;
                        }
                    if (clean)
                    {
                        int i = y * stride + x * 4;
                        bgR[idx] = src[i + 2];
                        bgG[idx] = src[i + 1];
                        bgB[idx] = src[i];
                        nearestClean[idx] = idx;
                        queue.Enqueue(idx);
                    }
                }
            }

        // BFS: propagate background color to keyed pixels
        int[] dx4 = { -1, 1, 0, 0 };
        int[] dy4 = { 0, 0, -1, 1 };
        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            int cx = cur % w, cy = cur / w;
            for (int d = 0; d < 4; d++)
            {
                int nx = cx + dx4[d], ny = cy + dy4[d];
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                int ni = ny * w + nx;
                if (nearestClean[ni] >= 0) continue;
                nearestClean[ni] = nearestClean[cur];
                bgR[ni] = bgR[cur];
                bgG[ni] = bgG[cur];
                bgB[ni] = bgB[cur];
                queue.Enqueue(ni);
            }
        }

        // Fallback: any remaining unset → white
        for (int i = 0; i < total; i++)
        {
            if (nearestClean[i] < 0)
            {
                bgR[i] = 255; bgG[i] = 255; bgB[i] = 255;
            }
        }

        return (bgR, bgG, bgB);
    }

    // ══════════════════════════════════════════════════════════
    //  Algorithm 2: Lab Mask (binary mask) — parallelized
    // ══════════════════════════════════════════════════════════

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

        RgbToLabFull(target.Rgb[0], target.Rgb[1], target.Rgb[2],
            out double tL, out double tA, out double tB);
        double targetChroma = Math.Sqrt(tA * tA + tB * tB);
        double targetAngle = Math.Atan2(tB, tA);
        double maxDist = target.Threshold.H * 1.35;

        // Pass 1: mark target pixels (parallelized)
        bool[] mask = new bool[w * h];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * stride + x * 4;
                RgbToLabFast(src[i + 2], src[i + 1], src[i], out double pL, out double pA, out double pB);
                double dA = pA - tA, dB = pB - tB;
                double chromaDist = Math.Sqrt(dA * dA + dB * dB);
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
        });

        // Pass 1.5: conditional dilation (sequential — depends on previous state)
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
                    RgbToLabFast(src[i + 2], src[i + 1], src[i], out _, out double eA, out double eB);
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

        // Pass 2: BFS background map
        double[] maskAlpha = new double[w * h];
        for (int i = 0; i < maskAlpha.Length; i++)
            maskAlpha[i] = mask[i] ? 0.0 : 1.0;
        var (bgR, bgG, bgB) = BuildBackgroundMapBfs(src, maskAlpha, stride, w, h);

        // Pass 3: replace (parallelized)
        var result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var dstData = result.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        byte[] dst = new byte[bytes];

        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * stride + x * 4;
                int idx = y * w + x;
                if (!mask[idx])
                {
                    dst[i] = src[i]; dst[i + 1] = src[i + 1];
                    dst[i + 2] = src[i + 2]; dst[i + 3] = src[i + 3];
                }
                else
                {
                    dst[i] = bgB[idx]; dst[i + 1] = bgG[idx];
                    dst[i + 2] = bgR[idx]; dst[i + 3] = 255;
                }
            }
        });

        Marshal.Copy(dst, 0, dstData.Scan0, bytes);
        result.UnlockBits(dstData);
        return result;
    }

    // ══════════════════════════════════════════════════════════
    //  Algorithm 3: YCbCr Chroma Key — parallelized
    // ══════════════════════════════════════════════════════════

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

        RgbToYCbCr(target.Rgb[0], target.Rgb[1], target.Rgb[2],
            out double tY, out double tCb, out double tCr);

        double similarity = target.Threshold.H * 2.0;
        double smoothness = similarity * 0.7;
        double outerRadius = similarity + smoothness;

        double tCbCentered = tCb - 128, tCrCentered = tCr - 128;
        double targetAngleCbCr = Math.Atan2(tCrCentered, tCbCentered);
        double targetChromaCbCr = Math.Sqrt(tCbCentered * tCbCentered + tCrCentered * tCrCentered);
        double hueToleranceCbCr = similarity * 0.5;

        // Pass 1: alpha (parallelized)
        double[] alpha = new double[w * h];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * stride + x * 4;
                RgbToYCbCr(src[i + 2], src[i + 1], src[i],
                    out double pY, out double pCb, out double pCr);

                double dist = Math.Sqrt((pCb - tCb) * (pCb - tCb) + (pCr - tCr) * (pCr - tCr));

                double pCbC = pCb - 128, pCrC = pCr - 128;
                double pChromaCbCr = Math.Sqrt(pCbC * pCbC + pCrC * pCrC);
                if (pChromaCbCr > 5 && targetChromaCbCr > 5)
                {
                    double pAngle = Math.Atan2(pCrC, pCbC);
                    double aDiff = Math.Abs(targetAngleCbCr - pAngle);
                    if (aDiff > Math.PI) aDiff = 2 * Math.PI - aDiff;
                    double aDeg = aDiff * 180.0 / Math.PI;
                    if (aDeg <= hueToleranceCbCr && pChromaCbCr > 8)
                        dist = Math.Min(dist, aDeg / hueToleranceCbCr * similarity);
                }

                if (dist <= similarity)
                    alpha[y * w + x] = 0.0;
                else if (dist >= outerRadius)
                    alpha[y * w + x] = 1.0;
                else
                    alpha[y * w + x] = (dist - similarity) / smoothness;
            }
        });

        // Pass 2: BFS background
        var (bgR, bgG, bgB) = BuildBackgroundMapBfs(src, alpha, stride, w, h);

        // Pass 3: blend (parallelized)
        var result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var dstData = result.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        byte[] dst = new byte[bytes];

        Parallel.For(0, h, y =>
        {
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
        });

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

    // ── Multiply blend (parallelized) ─────────────────────────

    public Bitmap MultiplyBlend(Bitmap source, int[] sheetRgb)
    {
        int w = source.Width, h = source.Height;
        var result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var srcData = source.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var dstData = result.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        int stride = srcData.Stride;
        int bytes = Math.Abs(stride) * h;
        byte[] srcPx = new byte[bytes];
        byte[] dstPx = new byte[bytes];
        Marshal.Copy(srcData.Scan0, srcPx, 0, bytes);

        double sr = sheetRgb[0] / 255.0, sg = sheetRgb[1] / 255.0, sb = sheetRgb[2] / 255.0;

        Parallel.For(0, h, y =>
        {
            int rowStart = y * stride;
            int rowEnd = rowStart + w * 4;
            for (int i = rowStart; i < rowEnd; i += 4)
            {
                dstPx[i]     = (byte)(srcPx[i]     * sb);
                dstPx[i + 1] = (byte)(srcPx[i + 1] * sg);
                dstPx[i + 2] = (byte)(srcPx[i + 2] * sr);
                dstPx[i + 3] = srcPx[i + 3];
            }
        });

        Marshal.Copy(dstPx, 0, dstData.Scan0, bytes);
        source.UnlockBits(srcData);
        result.UnlockBits(dstData);
        return result;
    }

    // ── Lab conversion (full precision for target color) ──────

    private static void RgbToLabFull(int r, int g, int b,
        out double L, out double labA, out double labB)
    {
        double lr = LinearizeLut[r];
        double lg = LinearizeLut[g];
        double lb = LinearizeLut[b];

        double x = lr * 0.4124564 + lg * 0.3575761 + lb * 0.1804375;
        double y = lr * 0.2126729 + lg * 0.7151522 + lb * 0.0721750;
        double z = lr * 0.0193339 + lg * 0.1191920 + lb * 0.9503041;

        x /= 0.95047; y /= 1.00000; z /= 1.08883;
        x = LabF(x); y = LabF(y); z = LabF(z);

        L = 116.0 * y - 16.0;
        labA = 500.0 * (x - y);
        labB = 200.0 * (y - z);
    }

    private static double LabF(double t) =>
        t > 0.008856 ? Math.Cbrt(t) : (7.787 * t + 16.0 / 116.0);

    // ══════════════════════════════════════════════════════════
    //  Zero-allocation API: works with pre-allocated FrameBuffer
    //  buf.Src must be populated before calling.
    //  Results are written into buf.Dst.
    // ══════════════════════════════════════════════════════════

    public void EraseColorInto(FrameBuffer buf, ColorSettings target)
    {
        switch (target.EraseAlgorithm)
        {
            case "labmask": EraseLabMaskInto(buf, target); break;
            case "ycbcr": EraseYCbCrInto(buf, target); break;
            default: EraseChromaKeyInto(buf, target); break;
        }
    }

    public void MultiplyBlendInto(FrameBuffer buf, int[] sheetRgb)
    {
        int w = buf.Width, h = buf.Height, stride = buf.Stride;
        byte[] src = buf.Src, dst = buf.Dst;
        double sr = sheetRgb[0] / 255.0, sg = sheetRgb[1] / 255.0, sb = sheetRgb[2] / 255.0;

        Parallel.For(0, h, y =>
        {
            int rowStart = y * stride;
            int rowEnd = rowStart + w * 4;
            for (int i = rowStart; i < rowEnd; i += 4)
            {
                dst[i]     = (byte)(src[i]     * sb);
                dst[i + 1] = (byte)(src[i + 1] * sg);
                dst[i + 2] = (byte)(src[i + 2] * sr);
                dst[i + 3] = src[i + 3];
            }
        });
    }

    private void EraseChromaKeyInto(FrameBuffer buf, ColorSettings target)
    {
        int w = buf.Width, h = buf.Height, stride = buf.Stride;
        byte[] src = buf.Src, dst = buf.Dst;
        double[] alpha = buf.Alpha;

        RgbToLabFull(target.Rgb[0], target.Rgb[1], target.Rgb[2],
            out double tL, out double tA, out double tB);
        double targetChroma = Math.Sqrt(tA * tA + tB * tB);
        double targetAngle = Math.Atan2(tB, tA);
        double similarity = target.Threshold.H * 1.5;
        double smoothness = similarity * 0.8;
        double outerRadius = similarity + smoothness;
        double hueToleranceDeg = similarity * 0.5;

        // Pass 1: alpha
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * stride + x * 4;
                RgbToLabFast(src[i + 2], src[i + 1], src[i], out double pL, out double pA, out double pB);
                double dA = pA - tA, dB = pB - tB;
                double chromaDist = Math.Sqrt(dA * dA + dB * dB);
                double pixelChroma = Math.Sqrt(pA * pA + pB * pB);
                double keyDist = chromaDist;

                if (pixelChroma > 2 && targetChroma > 5)
                {
                    double pixelAngle = Math.Atan2(pB, pA);
                    double angleDiff = Math.Abs(targetAngle - pixelAngle);
                    if (angleDiff > Math.PI) angleDiff = 2 * Math.PI - angleDiff;
                    double angleDeg = angleDiff * 180.0 / Math.PI;
                    if (angleDeg <= hueToleranceDeg && pixelChroma > 10)
                        keyDist = Math.Min(keyDist, angleDeg / hueToleranceDeg * similarity);
                    else if (angleDeg <= outerRadius * 1.3)
                    {
                        double angleBonus = (1.0 - angleDeg / (outerRadius * 1.3)) * similarity * 0.5;
                        keyDist = Math.Max(0, keyDist - angleBonus);
                    }
                }

                if (keyDist <= similarity) alpha[y * w + x] = 0.0;
                else if (keyDist >= outerRadius) alpha[y * w + x] = 1.0;
                else alpha[y * w + x] = (keyDist - similarity) / smoothness;
            }
        });

        // Pass 2: BFS background (reuses buf arrays)
        BuildBackgroundBfsInto(buf);

        // Pass 3: blend
        byte[] bgR = buf.BgR, bgG = buf.BgG, bgB = buf.BgB;
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * stride + x * 4;
                int idx = y * w + x;
                double a = alpha[idx];
                if (a >= 1.0)
                { dst[i] = src[i]; dst[i+1] = src[i+1]; dst[i+2] = src[i+2]; dst[i+3] = src[i+3]; }
                else
                {
                    dst[i]   = (byte)(src[i]   * a + bgB[idx] * (1-a));
                    dst[i+1] = (byte)(src[i+1] * a + bgG[idx] * (1-a));
                    dst[i+2] = (byte)(src[i+2] * a + bgR[idx] * (1-a));
                    dst[i+3] = 255;
                }
            }
        });

        // Pass 4: despill
        double tNormR = target.Rgb[0] / 255.0, tNormG = target.Rgb[1] / 255.0, tNormB = target.Rgb[2] / 255.0;
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                if (alpha[idx] < 0.9) continue;
                int minDist = int.MaxValue;
                for (int dy = -6; dy <= 6; dy++)
                    for (int dx = -6; dx <= 6; dx++)
                    {
                        int nx = x+dx, ny = y+dy;
                        if (nx >= 0 && nx < w && ny >= 0 && ny < h && alpha[ny*w+nx] < 0.5)
                        { int d = Math.Abs(dx)+Math.Abs(dy); if (d < minDist) minDist = d; }
                    }
                if (minDist > 6) continue;
                double strength = Math.Clamp(1.0-(minDist-1)/6.0, 0, 1) * 0.7;
                int i = y * stride + x * 4;
                double pR = dst[i+2]/255.0, pG = dst[i+1]/255.0, pB = dst[i]/255.0;
                if (tNormR >= tNormG && tNormR >= tNormB) { double l = Math.Max(pG,pB); if (pR>l) pR = pR*(1-strength)+l*strength; }
                else if (tNormG >= tNormR && tNormG >= tNormB) { double l = Math.Max(pR,pB); if (pG>l) pG = pG*(1-strength)+l*strength; }
                else { double l = Math.Max(pR,pG); if (pB>l) pB = pB*(1-strength)+l*strength; }
                dst[i+2] = (byte)(Math.Clamp(pR,0,1)*255);
                dst[i+1] = (byte)(Math.Clamp(pG,0,1)*255);
                dst[i]   = (byte)(Math.Clamp(pB,0,1)*255);
            }
        });
    }

    private void EraseYCbCrInto(FrameBuffer buf, ColorSettings target)
    {
        int w = buf.Width, h = buf.Height, stride = buf.Stride;
        byte[] src = buf.Src, dst = buf.Dst;
        double[] alpha = buf.Alpha;

        RgbToYCbCr(target.Rgb[0], target.Rgb[1], target.Rgb[2], out _, out double tCb, out double tCr);
        double similarity = target.Threshold.H * 2.0;
        double smoothness = similarity * 0.7;
        double outerRadius = similarity + smoothness;
        double tCbC = tCb-128, tCrC = tCr-128;
        double targetAngle = Math.Atan2(tCrC, tCbC);
        double targetChroma = Math.Sqrt(tCbC*tCbC + tCrC*tCrC);
        double hueTol = similarity * 0.5;

        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = y*stride + x*4;
                RgbToYCbCr(src[i+2], src[i+1], src[i], out _, out double pCb, out double pCr);
                double dist = Math.Sqrt((pCb-tCb)*(pCb-tCb) + (pCr-tCr)*(pCr-tCr));
                double pCbC2 = pCb-128, pCrC2 = pCr-128;
                double pChroma = Math.Sqrt(pCbC2*pCbC2 + pCrC2*pCrC2);
                if (pChroma > 5 && targetChroma > 5)
                {
                    double pAng = Math.Atan2(pCrC2, pCbC2);
                    double ad = Math.Abs(targetAngle - pAng); if (ad > Math.PI) ad = 2*Math.PI - ad;
                    double adeg = ad * 180.0 / Math.PI;
                    if (adeg <= hueTol && pChroma > 8) dist = Math.Min(dist, adeg/hueTol*similarity);
                }
                if (dist <= similarity) alpha[y*w+x] = 0.0;
                else if (dist >= outerRadius) alpha[y*w+x] = 1.0;
                else alpha[y*w+x] = (dist - similarity) / smoothness;
            }
        });

        BuildBackgroundBfsInto(buf);

        byte[] bgR = buf.BgR, bgG = buf.BgG, bgB = buf.BgB;
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = y*stride+x*4; int idx = y*w+x; double a = alpha[idx];
                if (a >= 1.0) { dst[i]=src[i]; dst[i+1]=src[i+1]; dst[i+2]=src[i+2]; dst[i+3]=src[i+3]; }
                else { dst[i]=(byte)(src[i]*a+bgB[idx]*(1-a)); dst[i+1]=(byte)(src[i+1]*a+bgG[idx]*(1-a)); dst[i+2]=(byte)(src[i+2]*a+bgR[idx]*(1-a)); dst[i+3]=255; }
            }
        });
    }

    private void EraseLabMaskInto(FrameBuffer buf, ColorSettings target)
    {
        int w = buf.Width, h = buf.Height, stride = buf.Stride;
        byte[] src = buf.Src, dst = buf.Dst;
        double[] alpha = buf.Alpha; // reuse as mask (0.0 = keyed, 1.0 = keep)

        RgbToLabFull(target.Rgb[0], target.Rgb[1], target.Rgb[2], out _, out double tA, out double tB);
        double targetChroma = Math.Sqrt(tA*tA + tB*tB);
        double targetAngle = Math.Atan2(tB, tA);
        double maxDist = target.Threshold.H * 1.35;

        // Pass 1: mark
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = y*stride+x*4;
                RgbToLabFast(src[i+2], src[i+1], src[i], out _, out double pA, out double pB);
                double dA2 = pA-tA, dB2 = pB-tB;
                double cd = Math.Sqrt(dA2*dA2+dB2*dB2);
                double pc = Math.Sqrt(pA*pA+pB*pB);
                bool dm = cd <= maxDist, em = false;
                if (pc > 2 && targetChroma > 5) { double pa = Math.Atan2(pB,pA); double ad = Math.Abs(targetAngle-pa); if (ad>Math.PI) ad=2*Math.PI-ad; em = ad*180.0/Math.PI <= maxDist*1.3; }
                alpha[y*w+x] = (dm || em) ? 0.0 : 1.0;
            }
        });

        // Pass 1.5: dilation (sequential)
        for (int pass = 0; pass < 3; pass++)
        {
            // Use BgR as temp mask storage (avoids allocation)
            for (int i = 0; i < buf.PixelCount; i++) buf.BgR[i] = alpha[i] < 0.5 ? (byte)1 : (byte)0;
            for (int y = 1; y < h-1; y++)
                for (int x = 1; x < w-1; x++)
                {
                    if (alpha[y*w+x] < 0.5) continue;
                    int nb = 0;
                    if (alpha[(y-1)*w+x] < 0.5) nb++; if (alpha[(y+1)*w+x] < 0.5) nb++;
                    if (alpha[y*w+x-1] < 0.5) nb++; if (alpha[y*w+x+1] < 0.5) nb++;
                    if (nb < 3) continue;
                    int ii = y*stride+x*4;
                    RgbToLabFast(src[ii+2], src[ii+1], src[ii], out _, out double eA, out double eB);
                    double ec = Math.Sqrt(eA*eA+eB*eB); if (ec < 1.5) continue;
                    double ea = Math.Atan2(eB,eA); double ad2 = Math.Abs(targetAngle-ea); if(ad2>Math.PI) ad2=2*Math.PI-ad2;
                    if (ad2*180/Math.PI <= 28) buf.BgR[y*w+x] = 1;
                }
            for (int i = 0; i < buf.PixelCount; i++) alpha[i] = buf.BgR[i] == 1 ? 0.0 : 1.0;
        }

        BuildBackgroundBfsInto(buf);

        byte[] bgR = buf.BgR, bgG = buf.BgG, bgB = buf.BgB;
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = y*stride+x*4; int idx = y*w+x;
                if (alpha[idx] >= 1.0) { dst[i]=src[i]; dst[i+1]=src[i+1]; dst[i+2]=src[i+2]; dst[i+3]=src[i+3]; }
                else { dst[i]=bgB[idx]; dst[i+1]=bgG[idx]; dst[i+2]=bgR[idx]; dst[i+3]=255; }
            }
        });
    }

    /// <summary>
    /// BFS background using pre-allocated FrameBuffer arrays.
    /// Reads: buf.Src, buf.Alpha. Writes: buf.BgR, buf.BgG, buf.BgB, buf.NearestClean.
    /// </summary>
    private static void BuildBackgroundBfsInto(FrameBuffer buf)
    {
        int w = buf.Width, h = buf.Height, stride = buf.Stride, total = buf.PixelCount;
        byte[] src = buf.Src;
        double[] alpha = buf.Alpha;
        byte[] bgR = buf.BgR, bgG = buf.BgG, bgB = buf.BgB;
        int[] nearest = buf.NearestClean;
        var queue = buf.BfsQueue;

        Array.Fill(nearest, -1);
        queue.Clear();

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                if (alpha[idx] < 1.0) continue;
                bool clean = true;
                for (int dy = -1; dy <= 1 && clean; dy++)
                    for (int dx = -1; dx <= 1 && clean; dx++)
                    {
                        int nx = x+dx, ny = y+dy;
                        if (nx >= 0 && nx < w && ny >= 0 && ny < h && alpha[ny*w+nx] < 1.0)
                            clean = false;
                    }
                if (clean)
                {
                    int i = y * stride + x * 4;
                    bgR[idx] = src[i+2]; bgG[idx] = src[i+1]; bgB[idx] = src[i];
                    nearest[idx] = idx;
                    queue.Enqueue(idx);
                }
            }

        int[] dx4 = {-1,1,0,0}, dy4 = {0,0,-1,1};
        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            int cx = cur % w, cy = cur / w;
            for (int d = 0; d < 4; d++)
            {
                int nx = cx+dx4[d], ny = cy+dy4[d];
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                int ni = ny*w+nx;
                if (nearest[ni] >= 0) continue;
                nearest[ni] = nearest[cur];
                bgR[ni] = bgR[cur]; bgG[ni] = bgG[cur]; bgB[ni] = bgB[cur];
                queue.Enqueue(ni);
            }
        }

        for (int i = 0; i < total; i++)
            if (nearest[i] < 0) { bgR[i] = 255; bgG[i] = 255; bgB[i] = 255; }
    }
}
