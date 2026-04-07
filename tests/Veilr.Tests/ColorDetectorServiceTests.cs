using System.Drawing;
using System.Drawing.Imaging;
using Veilr.Models;
using Veilr.Services;

namespace Veilr.Tests;

/// <summary>
/// ColorDetectorService のアルゴリズム正当性テスト。
/// 最適化（Parallel.For, Lab LUT, BFS距離変換）後も出力が正しいことを確認。
/// </summary>
public class ColorDetectorServiceTests
{
    private readonly ColorDetectorService _service = new();

    // ── Helper ────────────────────────────────────────────────

    private static Bitmap CreateSolidBitmap(int w, int h, Color color)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(color);
        return bmp;
    }

    private static Bitmap CreateTwoColorBitmap(int w, int h, Color bg, Color fg, Rectangle fgRect)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(bg);
        using var brush = new SolidBrush(fg);
        g.FillRectangle(brush, fgRect);
        return bmp;
    }

    private static Color GetPixelAt(Bitmap bmp, int x, int y)
    {
        return bmp.GetPixel(x, y);
    }

    private static ColorSettings DefaultRedTarget(int tolerance = 50) => new()
    {
        Rgb = [255, 0, 0],
        Threshold = new ThresholdSettings
        {
            H = (int)(tolerance * 0.45),
            S = tolerance,
            V = tolerance
        },
        EraseAlgorithm = "chromakey"
    };

    // ══════════════════════════════════════════════════════════
    //  MultiplyBlend テスト
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void MultiplyBlend_WhitePixel_BecomesSheetColor()
    {
        using var src = CreateSolidBitmap(10, 10, Color.White);
        using var result = _service.MultiplyBlend(src, [255, 0, 0]);

        var pixel = GetPixelAt(result, 5, 5);
        // White(255,255,255) × Red(255,0,0) / 255 = (255, 0, 0)
        Assert.Equal(255, pixel.R);
        Assert.Equal(0, pixel.G);
        Assert.Equal(0, pixel.B);
    }

    [Fact]
    public void MultiplyBlend_BlackPixel_StaysBlack()
    {
        using var src = CreateSolidBitmap(10, 10, Color.Black);
        using var result = _service.MultiplyBlend(src, [255, 0, 0]);

        var pixel = GetPixelAt(result, 5, 5);
        Assert.Equal(0, pixel.R);
        Assert.Equal(0, pixel.G);
        Assert.Equal(0, pixel.B);
    }

    [Fact]
    public void MultiplyBlend_RedOnRed_StaysRed()
    {
        // Red text on red sheet → same color as white-on-red → text disappears
        using var src = CreateSolidBitmap(10, 10, Color.Red);
        using var result = _service.MultiplyBlend(src, [255, 0, 0]);

        var pixel = GetPixelAt(result, 5, 5);
        Assert.Equal(255, pixel.R);
        Assert.Equal(0, pixel.G);
        Assert.Equal(0, pixel.B);
    }

    [Fact]
    public void MultiplyBlend_PreservesSize()
    {
        using var src = CreateSolidBitmap(100, 50, Color.White);
        using var result = _service.MultiplyBlend(src, [128, 64, 32]);

        Assert.Equal(100, result.Width);
        Assert.Equal(50, result.Height);
    }

    // ══════════════════════════════════════════════════════════
    //  ChromaKey テスト
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void ChromaKey_RedOnWhite_RedIsErased()
    {
        // White background with red rectangle in the center
        using var src = CreateTwoColorBitmap(40, 40, Color.White, Color.Red,
            new Rectangle(10, 10, 20, 20));
        var target = DefaultRedTarget();

        using var result = _service.EraseColor(src, target);

        // Center (was red) should now be close to white (background)
        var center = GetPixelAt(result, 20, 20);
        Assert.True(center.R > 200, $"Center R={center.R}, expected > 200 (background white)");
        Assert.True(center.G > 200, $"Center G={center.G}, expected > 200");
        Assert.True(center.B > 200, $"Center B={center.B}, expected > 200");

        // Corner (was white) should remain white
        var corner = GetPixelAt(result, 2, 2);
        Assert.Equal(255, corner.R);
        Assert.Equal(255, corner.G);
        Assert.Equal(255, corner.B);
    }

    [Fact]
    public void ChromaKey_BlackText_Preserved()
    {
        // White background with black rectangle
        using var src = CreateTwoColorBitmap(40, 40, Color.White, Color.Black,
            new Rectangle(10, 10, 20, 20));
        var target = DefaultRedTarget();

        using var result = _service.EraseColor(src, target);

        // Black pixels should be preserved (not erased)
        var center = GetPixelAt(result, 20, 20);
        Assert.True(center.R < 30, $"Black text R={center.R}, expected < 30");
        Assert.True(center.G < 30, $"Black text G={center.G}, expected < 30");
        Assert.True(center.B < 30, $"Black text B={center.B}, expected < 30");
    }

    [Fact]
    public void ChromaKey_NoRedPixels_OutputMatchesInput()
    {
        // All white image — nothing to erase
        using var src = CreateSolidBitmap(20, 20, Color.White);
        var target = DefaultRedTarget();

        using var result = _service.EraseColor(src, target);

        var pixel = GetPixelAt(result, 10, 10);
        Assert.Equal(255, pixel.R);
        Assert.Equal(255, pixel.G);
        Assert.Equal(255, pixel.B);
    }

    // ══════════════════════════════════════════════════════════
    //  LabMask テスト
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void LabMask_RedOnWhite_RedIsErased()
    {
        using var src = CreateTwoColorBitmap(40, 40, Color.White, Color.Red,
            new Rectangle(10, 10, 20, 20));
        var target = DefaultRedTarget();
        target.EraseAlgorithm = "labmask";

        using var result = _service.EraseColor(src, target);

        var center = GetPixelAt(result, 20, 20);
        Assert.True(center.R > 200, $"LabMask center R={center.R}, expected > 200");
        Assert.True(center.G > 200, $"LabMask center G={center.G}, expected > 200");
    }

    [Fact]
    public void LabMask_BlackPreserved()
    {
        using var src = CreateTwoColorBitmap(40, 40, Color.White, Color.Black,
            new Rectangle(10, 10, 20, 20));
        var target = DefaultRedTarget();
        target.EraseAlgorithm = "labmask";

        using var result = _service.EraseColor(src, target);

        var center = GetPixelAt(result, 20, 20);
        Assert.True(center.R < 30, $"Black R={center.R}");
    }

    // ══════════════════════════════════════════════════════════
    //  YCbCr テスト
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void YCbCr_RedOnWhite_RedIsErased()
    {
        using var src = CreateTwoColorBitmap(40, 40, Color.White, Color.Red,
            new Rectangle(10, 10, 20, 20));
        var target = DefaultRedTarget();
        target.EraseAlgorithm = "ycbcr";

        using var result = _service.EraseColor(src, target);

        var center = GetPixelAt(result, 20, 20);
        Assert.True(center.R > 200, $"YCbCr center R={center.R}, expected > 200");
        Assert.True(center.G > 200, $"YCbCr center G={center.G}, expected > 200");
    }

    [Fact]
    public void YCbCr_BlackPreserved()
    {
        using var src = CreateTwoColorBitmap(40, 40, Color.White, Color.Black,
            new Rectangle(10, 10, 20, 20));
        var target = DefaultRedTarget();
        target.EraseAlgorithm = "ycbcr";

        using var result = _service.EraseColor(src, target);

        var center = GetPixelAt(result, 20, 20);
        Assert.True(center.R < 30, $"Black R={center.R}");
    }

    // ══════════════════════════════════════════════════════════
    //  色ファミリー検出テスト（濃淡の赤が消えるか）
    // ══════════════════════════════════════════════════════════

    [Theory]
    [InlineData(255, 0, 0)]     // 純赤
    [InlineData(230, 1, 0)]     // 暗い赤
    [InlineData(200, 30, 30)]   // ダークレッド
    public void ChromaKey_RedVariants_AllErased(int r, int g, int b)
    {
        using var src = CreateTwoColorBitmap(40, 40, Color.White,
            Color.FromArgb(r, g, b), new Rectangle(10, 10, 20, 20));
        var target = DefaultRedTarget(70); // 高めのtolerance

        using var result = _service.EraseColor(src, target);

        var center = GetPixelAt(result, 20, 20);
        // Erased → should be close to white background
        Assert.True(center.R > 180,
            $"Red variant ({r},{g},{b}) → result R={center.R}, expected > 180");
    }

    // ══════════════════════════════════════════════════════════
    //  パフォーマンステスト（処理が妥当な時間で完了するか）
    // ══════════════════════════════════════════════════════════

    [Fact]
    public void ChromaKey_Performance_420x200_Under200ms()
    {
        // 一般的なウィンドウサイズ
        using var src = CreateTwoColorBitmap(420, 200, Color.White, Color.Red,
            new Rectangle(50, 50, 320, 100));
        var target = DefaultRedTarget();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var result = _service.EraseColor(src, target);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 200,
            $"420x200 ChromaKey took {sw.ElapsedMilliseconds}ms, expected < 200ms");
    }

    [Fact]
    public void MultiplyBlend_Performance_FullHD_Under50ms()
    {
        using var src = CreateSolidBitmap(1920, 1080, Color.White);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var result = _service.MultiplyBlend(src, [255, 0, 0]);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 50,
            $"1920x1080 MultiplyBlend took {sw.ElapsedMilliseconds}ms, expected < 50ms");
    }

    // ══════════════════════════════════════════════════════════
    //  出力サイズ整合性
    // ══════════════════════════════════════════════════════════

    [Theory]
    [InlineData("chromakey")]
    [InlineData("labmask")]
    [InlineData("ycbcr")]
    public void EraseColor_OutputSizeMatchesInput(string algorithm)
    {
        using var src = CreateSolidBitmap(123, 67, Color.White);
        var target = DefaultRedTarget();
        target.EraseAlgorithm = algorithm;

        using var result = _service.EraseColor(src, target);

        Assert.Equal(src.Width, result.Width);
        Assert.Equal(src.Height, result.Height);
    }
}
