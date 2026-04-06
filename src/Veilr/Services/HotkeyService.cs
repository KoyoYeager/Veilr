using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Veilr.Services;

public class HotkeyService : IDisposable
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hWnd, int id);

    private const int HOTKEY_ID = 9001;
    private const uint MOD_CTRL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint VK_E = 0x45;

    private HwndSource? _source;
    private readonly SettingsService _settings;

    public event Action? SheetToggleRequested;

    public HotkeyService(SettingsService settings)
    {
        _settings = settings;

        var helper = new WindowInteropHelper(System.Windows.Application.Current.MainWindow);
        _source = HwndSource.FromHwnd(helper.Handle);
        _source?.AddHook(WndProc);

        // TODO: parse hotkey string from settings
        RegisterHotKey(helper.Handle, HOTKEY_ID, MOD_CTRL | MOD_SHIFT, VK_E);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            SheetToggleRequested?.Invoke();
            handled = true;
        }
        return nint.Zero;
    }

    public void Dispose()
    {
        if (_source != null)
        {
            var hwnd = _source.Handle;
            UnregisterHotKey(hwnd, HOTKEY_ID);
            _source.RemoveHook(WndProc);
            _source = null;
        }
    }
}
