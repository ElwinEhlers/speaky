using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Speaky.Models;

/// <summary>
/// Zentraler Zustand der Aufnahme. GUI und Hotkey lesen und ändern denselben State,
/// dadurch sind Button-Beschriftung, Tray-Icon und Hotkey-Verhalten immer synchron.
/// </summary>
public sealed class RecordingState : INotifyPropertyChanged
{
    public enum Phase
    {
        Idle,
        Recording,
        Transcribing,
    }

    private Phase _currentPhase = Phase.Idle;
    private RecordingMode _mode = RecordingMode.Blitz;
    private int _emojiCount = 2;
    private float _inputLevel;
    private string _statusText = "Bereit";

    public Phase CurrentPhase
    {
        get => _currentPhase;
        set { if (_currentPhase != value) { _currentPhase = value; OnChanged(); OnChanged(nameof(IsRecording)); OnChanged(nameof(IsBusy)); } }
    }

    public RecordingMode Mode
    {
        get => _mode;
        set { if (_mode != value) { _mode = value; OnChanged(); } }
    }

    /// <summary>Anzahl Emojis im Emoji-Modus (1–5).</summary>
    public int EmojiCount
    {
        get => _emojiCount;
        set { var clamped = Math.Clamp(value, 1, 5); if (_emojiCount != clamped) { _emojiCount = clamped; OnChanged(); } }
    }

    /// <summary>Aktueller Mikrofon-Pegel 0.0 – 1.0 für VU-Meter.</summary>
    public float InputLevel
    {
        get => _inputLevel;
        set { if (Math.Abs(_inputLevel - value) > 0.001f) { _inputLevel = value; OnChanged(); } }
    }

    public string StatusText
    {
        get => _statusText;
        set { if (_statusText != value) { _statusText = value; OnChanged(); } }
    }

    public bool IsRecording => _currentPhase == Phase.Recording;
    public bool IsBusy => _currentPhase != Phase.Idle;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
