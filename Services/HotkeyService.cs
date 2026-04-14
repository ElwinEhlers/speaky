using System.Windows;
using System.Windows.Interop;
using Speaky.Native;

namespace Speaky.Services;

/// <summary>
/// Registriert einen globalen Hotkey. Der Hotkey ist aktiv, sobald ein WPF-Window
/// existiert (auch wenn es minimiert/versteckt ist), weil wir dessen HWND als Ziel
/// für <c>RegisterHotKey</c> benutzen.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int HOTKEY_ID = 0xBEEF;

    // Virtual-Key Codes
    private const uint VK_S = 0x53;

    private HwndSource? _source;
    private IntPtr _hwnd;
    private bool _registered;

    public event Action? HotkeyPressed;

    /// <summary>
    /// Muss NACH dem ersten Show() des Windows aufgerufen werden, damit ein HWND existiert.
    /// </summary>
    public void Register(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _hwnd = helper.EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);

        var mods = (uint)(Win32.HotkeyModifiers.Control | Win32.HotkeyModifiers.Alt | Win32.HotkeyModifiers.NoRepeat);
        _registered = Win32.RegisterHotKey(_hwnd, HOTKEY_ID, mods, VK_S);

        if (!_registered)
        {
            // Hotkey-Konflikt mit einer anderen App – nicht fatal, GUI bleibt nutzbar.
            System.Diagnostics.Debug.WriteLine("[Speaky] Hotkey Ctrl+Alt+S konnte nicht registriert werden.");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered && _hwnd != IntPtr.Zero)
        {
            Win32.UnregisterHotKey(_hwnd, HOTKEY_ID);
            _registered = false;
        }
        _source?.RemoveHook(WndProc);
        _source = null;
    }
}
