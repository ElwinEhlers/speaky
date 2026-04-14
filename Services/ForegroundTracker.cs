using System.Windows.Threading;
using Speaky.Native;

namespace Speaky.Services;

/// <summary>
/// Verfolgt kontinuierlich, welches Fenster zuletzt den Vordergrund hatte –
/// und zwar NUR Fenster die NICHT zu Speaky selbst gehören.
///
/// Damit können wir nach einem GUI-Button-Klick (der Speaky den Fokus gibt)
/// das ursprüngliche Zielfenster wieder in den Vordergrund holen, bevor wir
/// den transkribierten Text per SendInput einfügen.
/// </summary>
public sealed class ForegroundTracker : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly uint _ourPid;
    private IntPtr _lastForeign = IntPtr.Zero;

    public ForegroundTracker()
    {
        _ourPid = Win32.GetCurrentProcessId();
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(120),
        };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var fg = Win32.GetForegroundWindow();
        if (fg == IntPtr.Zero) return;

        Win32.GetWindowThreadProcessId(fg, out var pid);
        if (pid != _ourPid)
        {
            _lastForeign = fg;
        }
    }

    /// <summary>HWND des zuletzt fokussierten Nicht-Speaky-Fensters, oder Zero.</summary>
    public IntPtr LastForeignWindow => _lastForeign;

    /// <summary>Holt das zuletzt gemerkte Fremdfenster wieder in den Vordergrund.</summary>
    public bool RestoreForeground()
    {
        if (_lastForeign == IntPtr.Zero) return false;
        return Win32.SetForegroundWindow(_lastForeign);
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
