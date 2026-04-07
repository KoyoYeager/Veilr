using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;

namespace Veilr.Services;

/// <summary>
/// GPU-accelerated screen capture via DXGI Desktop Duplication.
/// Uses CopySubresourceRegion to transfer only the needed window region.
/// Falls back to GDI if DXGI init fails.
/// </summary>
public class DxgiCaptureService : IDisposable
{
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Texture2D? _staging;
    private int _screenW, _screenH;
    private int _stagingW, _stagingH;
    private bool _initialized, _failed;
    private bool _lastFrameValid; // true if previous AcquireNextFrame succeeded

    private readonly ScreenCaptureService _gdiFallback = new();

    public bool IsUsingDxgi => _initialized && !_failed;

    public bool TryInitialize()
    {
        if (_initialized) return !_failed;
        _initialized = true;

        try
        {
            D3D11CreateDevice(
                null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                null, out _device, out _context);

            if (_device == null) { _failed = true; return false; }

            using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();

            var hr = adapter.EnumOutputs(0, out var output);
            if (hr.Failure || output == null) { _failed = true; return false; }

            using (output)
            {
                var bounds = output.Description.DesktopCoordinates;
                _screenW = bounds.Right - bounds.Left;
                _screenH = bounds.Bottom - bounds.Top;

                using var output1 = output.QueryInterface<IDXGIOutput1>();
                _duplication = output1.DuplicateOutput(_device);
            }
            return true;
        }
        catch
        {
            _failed = true;
            Cleanup();
            return false;
        }
    }

    /// <summary>
    /// Ensure staging texture matches the requested region size.
    /// Only recreate when dimensions change.
    /// </summary>
    private void EnsureStaging(int w, int h)
    {
        if (w == _stagingW && h == _stagingH && _staging != null) return;
        _staging?.Dispose();
        _staging = _device!.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)w,
            Height = (uint)h,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            CPUAccessFlags = CpuAccessFlags.Read,
            BindFlags = BindFlags.None
        });
        _stagingW = w;
        _stagingH = h;
    }

    /// <summary>
    /// Capture a screen region into byte array via DXGI.
    /// Uses CopySubresourceRegion for minimal GPU→CPU transfer.
    /// Returns true on success.
    /// </summary>
    public bool TryCaptureRegion(int x, int y, int w, int h, byte[] dst, int stride)
    {
        if (_failed || _duplication == null || _context == null || _device == null)
            return false;

        try
        {
            // Clamp to screen bounds
            if (x < 0) { w += x; x = 0; }
            if (y < 0) { h += y; y = 0; }
            if (x + w > _screenW) w = _screenW - x;
            if (y + h > _screenH) h = _screenH - y;
            if (w <= 0 || h <= 0) return false;

            // AcquireNextFrame: timeout 0ms = non-blocking
            var hr = _duplication.AcquireNextFrame(0, out _, out var resource);

            if (hr.Failure || resource == null)
            {
                // No new frame — if we have a valid last frame, skip capture
                // (caller reuses previous buffer content)
                return _lastFrameValid;
            }

            try
            {
                using var srcTex = resource.QueryInterface<ID3D11Texture2D>();
                EnsureStaging(w, h);

                // Copy ONLY the window region (not the whole screen)
                _context.CopySubresourceRegion(
                    _staging!, 0, 0, 0, 0,   // dst: staging texture at (0,0)
                    srcTex, 0,                 // src: desktop texture
                    new Box(x, y, 0, x + w, y + h, 1));  // src region

                // Map staging → CPU read
                var mapped = _context.Map(_staging!, 0, MapMode.Read);
                try
                {
                    int srcPitch = (int)mapped.RowPitch;
                    int copyBytes = w * 4;

                    if (srcPitch == stride)
                    {
                        // Pitch matches stride — single bulk copy
                        Marshal.Copy(mapped.DataPointer, dst, 0, Math.Min(stride * h, dst.Length));
                    }
                    else
                    {
                        // Row-by-row copy (different alignment)
                        for (int row = 0; row < h; row++)
                        {
                            Marshal.Copy(
                                mapped.DataPointer + row * srcPitch,
                                dst, row * stride, copyBytes);
                        }
                    }
                }
                finally { _context.Unmap(_staging!, 0); }

                _lastFrameValid = true;
            }
            finally
            {
                resource.Dispose();
                _duplication.ReleaseFrame();
            }
            return true;
        }
        catch
        {
            _lastFrameValid = false;
            return false;
        }
    }

    /// <summary>DXGI capture → FrameBuffer.Src, with GDI fallback.</summary>
    public void CaptureIntoBuffer(FrameBuffer buf, int x, int y)
    {
        if (!_failed && _initialized
            && TryCaptureRegion(x, y, buf.Width, buf.Height, buf.Src, buf.Stride))
            return;

        buf.CaptureAndCopyPixels(_gdiFallback, x, y);
    }

    private void Cleanup()
    {
        _duplication?.Dispose(); _duplication = null;
        _staging?.Dispose(); _staging = null;
        _context?.Dispose(); _context = null;
        _device?.Dispose(); _device = null;
    }

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }
}
