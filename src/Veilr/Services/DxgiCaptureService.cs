using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;

namespace Veilr.Services;

/// <summary>
/// GPU-accelerated screen capture via DXGI Desktop Duplication.
/// Falls back to GDI CopyFromScreen if DXGI init fails.
/// </summary>
public class DxgiCaptureService : IDisposable
{
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Texture2D? _staging;
    private int _screenW, _screenH;
    private bool _initialized, _failed;

    private readonly ScreenCaptureService _gdiFallback = new();

    public bool IsUsingDxgi => _initialized && !_failed;

    public bool TryInitialize()
    {
        if (_initialized) return !_failed;
        _initialized = true;

        try
        {
            // Create D3D11 device with default adapter
            D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                null,
                out _device,
                out _context);

            if (_device == null) { _failed = true; return false; }

            // Walk: Device → DXGIDevice → Adapter → Output → Output1
            using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();

            // EnumOutputs: try out-parameter pattern
            IDXGIOutput? output = null;
            var hr = adapter.EnumOutputs(0, out output);
            if (hr.Failure || output == null) { _failed = true; return false; }

            using (output)
            {
                // Get screen dimensions from output description
                var outDesc = output.Description;
                var bounds = outDesc.DesktopCoordinates;
                _screenW = bounds.Right - bounds.Left;
                _screenH = bounds.Bottom - bounds.Top;

                using var output1 = output.QueryInterface<IDXGIOutput1>();
                _duplication = output1.DuplicateOutput(_device);
            }

            // Staging texture for CPU readback
            _staging = _device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)_screenW,
                Height = (uint)_screenH,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                CPUAccessFlags = CpuAccessFlags.Read,
                BindFlags = BindFlags.None
            });

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
    /// Capture a screen region directly into a byte array via DXGI.
    /// Returns true on success, false → caller should use GDI fallback.
    /// </summary>
    public bool TryCaptureRegion(int x, int y, int w, int h, byte[] dst, int stride)
    {
        if (_failed || _duplication == null || _context == null || _staging == null)
            return false;

        try
        {
            // AcquireNextFrame: timeout 0 = return immediately
            var hr = _duplication.AcquireNextFrame(0,
                out _, out var resource);

            if (hr.Failure || resource == null)
                return false; // no new frame — screen hasn't changed

            try
            {
                using var tex = resource.QueryInterface<ID3D11Texture2D>();
                _context.CopyResource(_staging, tex);

                var mapped = _context.Map(_staging, 0, MapMode.Read);
                try
                {
                    int srcPitch = (int)mapped.RowPitch;
                    int copyBytes = w * 4;

                    for (int row = 0; row < h; row++)
                    {
                        int srcOff = (y + row) * srcPitch + x * 4;
                        int dstOff = row * stride;
                        if (srcOff + copyBytes <= srcPitch * _screenH
                            && dstOff + copyBytes <= dst.Length)
                        {
                            Marshal.Copy(mapped.DataPointer + srcOff, dst, dstOff, copyBytes);
                        }
                    }
                }
                finally { _context.Unmap(_staging, 0); }
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
            return false;
        }
    }

    /// <summary>DXGI capture → FrameBuffer.Src, with GDI fallback.</summary>
    public void CaptureIntoBuffer(FrameBuffer buf, int x, int y)
    {
        if (!_failed && _initialized
            && TryCaptureRegion(x, y, buf.Width, buf.Height, buf.Src, buf.Stride))
            return;

        // GDI fallback
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
