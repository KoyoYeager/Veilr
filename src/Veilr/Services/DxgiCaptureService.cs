using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;
using MapMode = Vortice.Direct3D11.MapMode;

namespace Veilr.Services;

/// <summary>
/// GPU-accelerated screen capture via DXGI Desktop Duplication.
/// Uses primary output; falls back to GDI for other monitors.
/// </summary>
public class DxgiCaptureService : IDisposable
{
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Texture2D? _staging;
    private int _screenLeft, _screenTop, _screenW, _screenH;
    private int _stagingW, _stagingH;
    private bool _initialized, _failed;
    private bool _lastFrameValid;

    private IDXGIAdapter? _adapter;
    private int _currentOutputIndex;
    private readonly ScreenCaptureService _gdiFallback = new();

    public bool IsUsingDxgi => _initialized && !_failed;
    public int CurrentOutputIndex => _currentOutputIndex;
    public int OutputCount { get; private set; }
    internal ID3D11Device? Device => _device;
    internal ID3D11DeviceContext? Context => _context;

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
            _adapter = dxgiDevice.GetAdapter();

            // Count outputs
            for (uint i = 0; ; i++)
            {
                var ehr = _adapter.EnumOutputs(i, out var eo);
                if (ehr.Failure || eo == null) break;
                eo.Dispose();
                OutputCount = (int)i + 1;
            }

            SwitchToOutput(0);
            return _duplication != null;
        }
        catch
        {
            _failed = true;
            Cleanup();
            return false;
        }
    }

    /// <summary>
    /// Capture directly to a GPU texture (no CPU copy).
    /// Coordinates are converted to current output-local.
    /// </summary>
    internal bool TryCaptureToGpuTexture(int x, int y, int w, int h, ID3D11Texture2D gpuTarget)
    {
        if (_failed || _duplication == null || _context == null) return false;

        try
        {
            // Convert to output-local coordinates
            int localX = x - _screenLeft;
            int localY = y - _screenTop;
            if (localX < 0) { w += localX; localX = 0; }
            if (localY < 0) { h += localY; localY = 0; }
            if (localX + w > _screenW) w = _screenW - localX;
            if (localY + h > _screenH) h = _screenH - localY;
            if (w <= 0 || h <= 0) return false;

            var hr = _duplication.AcquireNextFrame(0, out _, out var resource);
            if (hr.Failure || resource == null) return _lastFrameValid;

            try
            {
                using var srcTex = resource.QueryInterface<ID3D11Texture2D>();
                _context.CopySubresourceRegion(
                    gpuTarget, 0, 0, 0, 0,
                    srcTex, 0,
                    new Box(localX, localY, 0, localX + w, localY + h, 1));
                _lastFrameValid = true;
            }
            finally
            {
                resource.Dispose();
                _duplication.ReleaseFrame();
            }
            return true;
        }
        catch { return false; }
    }

    /// <summary>Switch to a specific output (monitor). Called by UI button.</summary>
    public void SwitchToOutput(int outputIndex)
    {
        _duplication?.Dispose();
        _duplication = null;
        _lastFrameValid = false;

        try
        {
            if (_adapter == null) return;
            var hr = _adapter.EnumOutputs((uint)outputIndex, out var output);
            if (hr.Failure || output == null) return;

            using (output)
            {
                var bounds = output.Description.DesktopCoordinates;
                _screenLeft = bounds.Left;
                _screenTop = bounds.Top;
                _screenW = bounds.Right - bounds.Left;
                _screenH = bounds.Bottom - bounds.Top;
                _currentOutputIndex = outputIndex;

                using var output1 = output.QueryInterface<IDXGIOutput1>();
                _duplication = output1.DuplicateOutput(_device!);
            }
        }
        catch
        {
            _duplication = null;
        }
    }

    /// <summary>Cycle to next output. Returns new output index.</summary>
    public int CycleOutput()
    {
        if (OutputCount <= 1) return 0;
        int next = (_currentOutputIndex + 1) % OutputCount;
        SwitchToOutput(next);
        return next;
    }

    private void EnsureStaging(int w, int h)
    {
        if (w == _stagingW && h == _stagingH && _staging != null) return;
        _staging?.Dispose();
        _staging = _device!.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)w, Height = (uint)h, MipLevels = 1, ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm, SampleDescription = new(1, 0),
            Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read
        });
        _stagingW = w; _stagingH = h;
    }

    /// <summary>
    /// Capture via DXGI (primary monitor only). Returns false → GDI fallback.
    /// </summary>
    public bool TryCaptureRegion(int x, int y, int w, int h, byte[] dst, int stride)
    {
        if (_failed || _duplication == null || _context == null) return false;

        try
        {
            int localX = x - _screenLeft;
            int localY = y - _screenTop;
            if (localX < 0) { w += localX; localX = 0; }
            if (localY < 0) { h += localY; localY = 0; }
            if (localX + w > _screenW) w = _screenW - localX;
            if (localY + h > _screenH) h = _screenH - localY;
            if (w <= 0 || h <= 0) return false;

            var hr = _duplication.AcquireNextFrame(0, out _, out var resource);
            if (hr.Failure || resource == null) return _lastFrameValid;

            try
            {
                using var srcTex = resource.QueryInterface<ID3D11Texture2D>();
                EnsureStaging(w, h);
                _context.CopySubresourceRegion(
                    _staging!, 0, 0, 0, 0,
                    srcTex, 0,
                    new Box(localX, localY, 0, localX + w, localY + h, 1));

                var mapped = _context.Map(_staging!, 0, MapMode.Read);
                try
                {
                    int srcPitch = (int)mapped.RowPitch;
                    int copyBytes = w * 4;
                    if (srcPitch == stride)
                        Marshal.Copy(mapped.DataPointer, dst, 0, Math.Min(stride * h, dst.Length));
                    else
                        for (int row = 0; row < h; row++)
                            Marshal.Copy(mapped.DataPointer + row * srcPitch, dst, row * stride, copyBytes);
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

        // GDI fallback (works on any monitor)
        buf.CaptureAndCopyPixels(_gdiFallback, x, y);
    }

    private void Cleanup()
    {
        _duplication?.Dispose(); _duplication = null;
        _staging?.Dispose(); _staging = null;
        _adapter?.Dispose(); _adapter = null;
        _context?.Dispose(); _context = null;
        _device?.Dispose(); _device = null;
    }

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }
}
