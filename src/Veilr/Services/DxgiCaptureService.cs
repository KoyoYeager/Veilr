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
/// Supports multi-monitor: auto-switches Output when window moves between monitors.
/// </summary>
public class DxgiCaptureService : IDisposable
{
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGIAdapter? _adapter;
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Texture2D? _staging;
    private int _stagingW, _stagingH;
    private bool _initialized, _failed;
    private bool _lastFrameValid;

    // Current output info
    private int _currentOutputIndex = -1;
    private int _outputLeft, _outputTop, _outputW, _outputH;

    // All outputs (monitors) info
    private readonly List<OutputInfo> _outputs = new();
    private record OutputInfo(int Index, int Left, int Top, int Right, int Bottom);

    private readonly ScreenCaptureService _gdiFallback = new();

    public bool IsUsingDxgi => _initialized && !_failed;
    internal ID3D11Device? Device => _device;
    internal ID3D11DeviceContext? Context => _context;

    internal bool TryCaptureToGpuTexture(int x, int y, int w, int h, ID3D11Texture2D gpuTarget)
    {
        if (_failed || _context == null) return false;
        EnsureCorrectOutput(x, y, w, h);
        if (_duplication == null) return false;
        uint timeout = _lastFrameValid ? 0u : 16u; // Wait longer after monitor switch

        try
        {
            // Convert to output-local coordinates
            int localX = x - _outputLeft;
            int localY = y - _outputTop;
            if (localX < 0) { w += localX; localX = 0; }
            if (localY < 0) { h += localY; localY = 0; }
            if (localX + w > _outputW) w = _outputW - localX;
            if (localY + h > _outputH) h = _outputH - localY;
            if (w <= 0 || h <= 0) return false;

            var hr = _duplication.AcquireNextFrame(timeout, out _, out var resource);
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

            // Enumerate all outputs (monitors)
            _outputs.Clear();
            for (uint i = 0; ; i++)
            {
                var hr = _adapter.EnumOutputs(i, out var output);
                if (hr.Failure || output == null) break;
                var bounds = output.Description.DesktopCoordinates;
                _outputs.Add(new OutputInfo((int)i, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom));
                output.Dispose();
            }

            if (_outputs.Count == 0) { _failed = true; return false; }

            // Initialize with primary monitor (Output 0)
            SwitchToOutput(0);
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
    /// Check if window center is on a different monitor, switch if needed.
    /// </summary>
    private void EnsureCorrectOutput(int winX, int winY, int winW, int winH)
    {
        if (_outputs.Count <= 1 && _duplication != null) return;

        int cx = winX + winW / 2;
        int cy = winY + winH / 2;

        // Check if center is still on current output AND duplication is active
        if (_currentOutputIndex >= 0 && _duplication != null)
        {
            var cur = _outputs[_currentOutputIndex];
            if (cx >= cur.Left && cx < cur.Right && cy >= cur.Top && cy < cur.Bottom)
                return; // still on same monitor, duplication working
        }

        // Find which output contains the center
        for (int i = 0; i < _outputs.Count; i++)
        {
            var o = _outputs[i];
            if (cx >= o.Left && cx < o.Right && cy >= o.Top && cy < o.Bottom)
            {
                SwitchToOutput(i);
                return;
            }
        }
    }

    private void SwitchToOutput(int outputIndex)
    {
        _duplication?.Dispose();
        _duplication = null;
        _lastFrameValid = false;

        try
        {
            var hr = _adapter!.EnumOutputs((uint)outputIndex, out var output);
            if (hr.Failure || output == null)
            {
                _currentOutputIndex = -1; // Allow retry next frame
                return;
            }

            using (output)
            {
                var bounds = output.Description.DesktopCoordinates;
                _outputLeft = bounds.Left;
                _outputTop = bounds.Top;
                _outputW = bounds.Right - bounds.Left;
                _outputH = bounds.Bottom - bounds.Top;

                using var output1 = output.QueryInterface<IDXGIOutput1>();
                _duplication = output1.DuplicateOutput(_device!);
                _currentOutputIndex = outputIndex; // Only update on success
            }
        }
        catch
        {
            _duplication = null;
            _currentOutputIndex = -1; // Allow retry next frame
        }
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

    public bool TryCaptureRegion(int x, int y, int w, int h, byte[] dst, int stride)
    {
        if (_failed || _context == null) return false;
        EnsureCorrectOutput(x, y, w, h);
        if (_duplication == null) return false;

        try
        {
            int localX = x - _outputLeft;
            int localY = y - _outputTop;
            if (localX < 0) { w += localX; localX = 0; }
            if (localY < 0) { h += localY; localY = 0; }
            if (localX + w > _outputW) w = _outputW - localX;
            if (localY + h > _outputH) h = _outputH - localY;
            if (w <= 0 || h <= 0) return false;

            uint acquireTimeout = _lastFrameValid ? 0u : 16u;
            var hr = _duplication.AcquireNextFrame(acquireTimeout, out _, out var resource);
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
        catch { return false; }
    }

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
