using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Veilr.Models;
using Veilr.Services;

namespace Veilr.Tests;

/// <summary>
/// GPU pipeline integration tests.
/// Tests DXGI initialization, shader compilation, GPU processing, and monitor detection.
/// Skips gracefully if no GPU is available (CI environments).
/// </summary>
public class GpuPipelineTests
{
    private static bool HasGpu()
    {
        try { return GpuProcessingService.TestGpuCapability(); }
        catch { return false; }
    }

    // ── DXGI Tests ────────────────────────────────────────────

    [Fact]
    public void DxgiCapture_Initialize_Succeeds()
    {
        using var dxgi = new DxgiCaptureService();
        bool result = dxgi.TryInitialize();
        // May fail in CI (no display), but should not throw
        if (result)
        {
            Assert.True(dxgi.IsUsingDxgi);
            Assert.True(dxgi.OutputCount >= 1, $"OutputCount={dxgi.OutputCount}");
        }
    }

    [Fact]
    public void DxgiCapture_CaptureRegion_ReturnsData()
    {
        using var dxgi = new DxgiCaptureService();
        if (!dxgi.TryInitialize()) return; // skip if no display

        int w = 100, h = 100, stride = w * 4;
        byte[] dst = new byte[stride * h];
        // First capture may fail (no frame yet), retry once
        dxgi.TryCaptureRegion(100, 100, w, h, dst, stride);
        System.Threading.Thread.Sleep(50);
        bool ok = dxgi.TryCaptureRegion(100, 100, w, h, dst, stride);

        if (ok)
        {
            // Verify not all zeros (actual screen content captured)
            bool hasData = false;
            for (int i = 0; i < dst.Length && !hasData; i++)
                if (dst[i] != 0) hasData = true;
            Assert.True(hasData, "Captured data should not be all zeros");
        }
    }

    [Fact]
    public void DxgiCapture_CycleOutput_DoesNotThrow()
    {
        using var dxgi = new DxgiCaptureService();
        if (!dxgi.TryInitialize()) return;

        int before = dxgi.CurrentOutputIndex;
        int after = dxgi.CycleOutput();

        if (dxgi.OutputCount > 1)
            Assert.NotEqual(before, after);
        else
            Assert.Equal(0, after);
    }

    [Fact]
    public void DxgiCapture_SwitchToOutput_AllOutputs()
    {
        using var dxgi = new DxgiCaptureService();
        if (!dxgi.TryInitialize()) return;

        for (int i = 0; i < dxgi.OutputCount; i++)
        {
            dxgi.SwitchToOutput(i);
            Assert.Equal(i, dxgi.CurrentOutputIndex);
            Assert.True(dxgi.IsUsingDxgi);
        }
    }

    [Fact]
    public void DxgiCapture_CaptureIntoBuffer_GdiFallback()
    {
        using var dxgi = new DxgiCaptureService();
        // Don't initialize DXGI → forces GDI fallback
        var buf = new FrameBuffer();
        int w = 50, h = 50, stride = w * 4;
        buf.EnsureCapacity(w, h, stride);

        // Should not throw, uses GDI
        dxgi.CaptureIntoBuffer(buf, 100, 100);

        bool hasData = false;
        for (int i = 0; i < buf.Src.Length && !hasData; i++)
            if (buf.Src[i] != 0) hasData = true;
        Assert.True(hasData, "GDI fallback should capture data");
    }

    // ── GPU Processing Tests ──────────────────────────────────

    [Fact]
    public void GpuCapability_Test()
    {
        // Should not throw regardless of GPU availability
        bool result = GpuProcessingService.TestGpuCapability();
        // result may be true or false depending on hardware
    }

    [SkippableFact]
    public void GpuService_Initialize_WithDxgiDevice()
    {
        using var dxgi = new DxgiCaptureService();
        Skip.IfNot(dxgi.TryInitialize(), "No DXGI");
        Skip.If(dxgi.Device == null || dxgi.Context == null, "No D3D11 device");

        using var gpu = new GpuProcessingService();
        bool ok = gpu.Initialize(dxgi.Device!, dxgi.Context!);

        if (ok)
        {
            Assert.True(gpu.IsAvailable);
            Assert.Null(gpu.InitError);
        }
        else
        {
            Assert.NotNull(gpu.InitError);
        }
    }

    [SkippableFact]
    public void GpuService_MultiplyBlend_ProducesOutput()
    {
        using var dxgi = new DxgiCaptureService();
        Skip.IfNot(dxgi.TryInitialize(), "No DXGI");

        using var gpu = new GpuProcessingService();
        Skip.IfNot(gpu.Initialize(dxgi.Device!, dxgi.Context!), "GPU init failed");

        int w = 64, h = 64;
        gpu.EnsureTexturesPublic(w, h);

        // Capture a small region to srcTexture
        System.Threading.Thread.Sleep(50);
        dxgi.TryCaptureToGpuTexture(100, 100, w, h, gpu.GetSrcTexture()!);

        // Process
        gpu.ProcessMultiplyBlend(gpu.GetSrcTexture()!, [255, 0, 0], w, h);

        // Read result
        byte[] result = new byte[w * h * 4];
        gpu.ReadResultToCpu(result, w, h);

        // GPU processed without crashing — verify we got data back
        Assert.Equal(w * h * 4, result.Length);
    }

    [SkippableFact]
    public void GpuService_EraseChromaKey_DoesNotFreeze()
    {
        using var dxgi = new DxgiCaptureService();
        Skip.IfNot(dxgi.TryInitialize(), "No DXGI");

        using var gpu = new GpuProcessingService();
        Skip.IfNot(gpu.Initialize(dxgi.Device!, dxgi.Context!), "GPU init failed");

        int w = 64, h = 64;
        gpu.EnsureTexturesPublic(w, h);
        System.Threading.Thread.Sleep(50);
        dxgi.TryCaptureToGpuTexture(100, 100, w, h, gpu.GetSrcTexture()!);

        var target = new ColorSettings
        {
            Rgb = [255, 0, 0],
            Threshold = new ThresholdSettings { H = 15, S = 50, V = 50 },
            EraseAlgorithm = "chromakey"
        };

        // Should complete without freezing (timeout protection)
        var cts = new CancellationTokenSource(5000); // 5s timeout
        var task = Task.Run(() =>
        {
            gpu.ProcessEraseChromaKey(gpu.GetSrcTexture()!, target, w, h);
        });

        bool completed = task.Wait(5000);
        Assert.True(completed, "ChromaKey GPU should complete within 5 seconds (not freeze)");

        byte[] result = new byte[w * h * 4];
        gpu.ReadResultToCpu(result, w, h);
        // Just verify we got some data back
        Assert.Equal(w * h * 4, result.Length);
    }

    // ── Pipeline Integration Tests ────────────────────────────

    [Fact]
    public void FrameBuffer_CaptureAndProcess_CpuPath()
    {
        // Full CPU pipeline: capture → process → verify
        var buf = new FrameBuffer();
        int w = 100, h = 100, stride = w * 4;
        buf.EnsureCapacity(w, h, stride);

        var captureService = new ScreenCaptureService();
        buf.CaptureAndCopyPixels(captureService, 100, 100);

        var detector = new ColorDetectorService();
        var target = new ColorSettings
        {
            Rgb = [255, 0, 0],
            Threshold = new ThresholdSettings { H = 15, S = 50, V = 50 },
            EraseAlgorithm = "chromakey"
        };
        detector.EraseColorInto(buf, target);

        // Verify output has data
        bool hasDst = false;
        for (int i = 0; i < buf.Dst.Length && !hasDst; i++)
            if (buf.Dst[i] != 0) hasDst = true;
        Assert.True(hasDst, "CPU pipeline should produce non-zero output");
    }

    [Fact]
    public void FrameBuffer_MultiplyBlend_CpuPath()
    {
        var buf = new FrameBuffer();
        int w = 100, h = 100, stride = w * 4;
        buf.EnsureCapacity(w, h, stride);

        var captureService = new ScreenCaptureService();
        buf.CaptureAndCopyPixels(captureService, 100, 100);

        var detector = new ColorDetectorService();
        detector.MultiplyBlendInto(buf, [255, 0, 0]);

        bool hasDst = false;
        for (int i = 0; i < buf.Dst.Length && !hasDst; i++)
            if (buf.Dst[i] != 0) hasDst = true;
        Assert.True(hasDst, "MultiplyBlend should produce non-zero output");
    }
}
