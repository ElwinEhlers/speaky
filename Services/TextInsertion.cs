using System.Windows;
using Speaky.Native;
using Clipboard = System.Windows.Clipboard;

namespace Speaky.Services;

/// <summary>
/// Fügt den transkribierten Text ins aktive Ziel-Fenster ein, indem er über die
/// Zwischenablage + Ctrl+V geht.
///
/// WARUM CLIPBOARD STATT SENDINPUT-PRO-ZEICHEN?
/// Die frühere Variante hat jedes Zeichen einzeln per KEYEVENTF_UNICODE getippt.
/// Resultat: verstümmelte Ausgaben wie "Hallo ppeake,...……………" obwohl Whisper
/// sauber "Hallo Speake, dies ist ein Test." geliefert hat (verifiziert via
/// whisper-debug.log). Ursachen:
///   • Modifier-Keys (Ctrl/Alt) vom gerade losgelassenen Hotkey wirken nach
///     und wandeln einzelne Unicode-Events zu Tastenkombinationen um.
///   • Win11-Notepad Autocorrect verwandelt "..." in "…".
///   • Bei 60+ SendInput-Events in einem Rutsch verschluckt die Ziel-App Zeichen.
///
/// Clipboard+Paste umgeht alle drei Probleme: ein einziger Ctrl+V, der Text
/// landet atomar, Unicode/Emojis inklusive.
///
/// Die Original-Zwischenablage wird nach dem Paste wiederhergestellt.
/// </summary>
public sealed class TextInsertion
{
    public void Insert(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Sicherstellen, dass Ctrl/Alt/Shift/Win physisch losgelassen sind,
        // bevor wir Ctrl+V schicken. Sonst würde unser Ctrl+V sich mit einem
        // evtl. noch gedrückten Alt zu Ctrl+Alt+V mischen.
        WaitForModifiersReleased(timeoutMs: 400);

        // Clipboard-API ist STA-gebunden → auf den WPF-Dispatcher-Thread marshallen.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) return;

        string? backup = null;
        dispatcher.Invoke(() =>
        {
            try
            {
                if (Clipboard.ContainsText())
                    backup = Clipboard.GetText();
            }
            catch { /* Clipboard kurz gesperrt – kein Drama, wir restaurieren dann halt nicht */ }

            if (!TrySetClipboard(text))
            {
                // Kurz warten, dann einmal retryen – Clipboard kann kurz von anderen Prozessen belegt sein.
                System.Threading.Thread.Sleep(40);
                TrySetClipboard(text);
            }
        });

        SendCtrlV();

        // Der Ziel-Prozess braucht einen Moment, um WM_PASTE zu verarbeiten,
        // bevor wir die Zwischenablage zurückschreiben dürfen.
        System.Threading.Thread.Sleep(150);

        if (backup is not null)
        {
            dispatcher.Invoke(() =>
            {
                try { Clipboard.SetDataObject(backup, copy: true); } catch { }
            });
        }
    }

    private static bool TrySetClipboard(string text)
    {
        try
        {
            Clipboard.SetDataObject(text, copy: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void WaitForModifiersReleased(int timeoutMs)
    {
        var deadline = Environment.TickCount + timeoutMs;
        while (Environment.TickCount < deadline)
        {
            bool anyDown =
                (Win32.GetAsyncKeyState(Win32.VK_CONTROL) & 0x8000) != 0 ||
                (Win32.GetAsyncKeyState(Win32.VK_MENU)    & 0x8000) != 0 ||
                (Win32.GetAsyncKeyState(Win32.VK_SHIFT)   & 0x8000) != 0 ||
                (Win32.GetAsyncKeyState(Win32.VK_LWIN)    & 0x8000) != 0 ||
                (Win32.GetAsyncKeyState(Win32.VK_RWIN)    & 0x8000) != 0;
            if (!anyDown) return;
            System.Threading.Thread.Sleep(15);
        }
    }

    private static void SendCtrlV()
    {
        var inputs = new[]
        {
            MakeKey(Win32.VK_CONTROL, down: true),
            MakeKey(Win32.VK_V,       down: true),
            MakeKey(Win32.VK_V,       down: false),
            MakeKey(Win32.VK_CONTROL, down: false),
        };
        Win32.SendInput((uint)inputs.Length, inputs, Win32.INPUT.Size);
    }

    private static Win32.INPUT MakeKey(ushort vk, bool down) => new()
    {
        type = Win32.INPUT_KEYBOARD,
        U = new Win32.InputUnion
        {
            ki = new Win32.KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = down ? 0 : Win32.KEYEVENTF_KEYUP,
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            },
        },
    };
}
