using System.IO;
using System.Windows;
using Speaky.Models;
using Speaky.Services;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace Speaky;

/// <summary>
/// Composition Root. Alle Services werden hier einmal instanziiert und verdrahtet.
/// Kein DI-Container – für den MVP bewusst manuell, damit die Abhängigkeiten offensichtlich sind.
/// </summary>
public partial class App : Application
{
    public static bool IsShuttingDown { get; private set; }

    private MainWindow? _window;
    private RecordingState? _state;
    private HotkeyService? _hotkey;
    private AudioRecorder? _recorder;
    private TranscriptionService? _transcriber;
    private TextInsertion? _textInsert;
    private ModeManager? _modeManager;
    private TrayIconService? _tray;
    private ForegroundTracker? _foreground;
    private OllamaLifecycle? _ollama;
    private LlmService? _llm;
    private EmojiDictionary? _emojiDict;

    // Serialisiert Aufnahme-Toggles, damit ein schneller Doppel-Druck keinen
    // Race zwischen "Start" und "Stop" erzeugt.
    private readonly SemaphoreSlim _toggleLock = new(1, 1);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _state = new RecordingState();
        _recorder = new AudioRecorder();
        _textInsert = new TextInsertion();
        _foreground = new ForegroundTracker();

        // LLM-Stack nur instanziiert – NICHT gestartet. Ollama läuft erst dann hoch,
        // wenn der User tatsächlich den Diplomatie-Modus benutzt. Wörtlich/Ausschreib/
        // Emoji triggern nie einen Ollama-Start.
        _ollama = new OllamaLifecycle();
        _llm = new LlmService();

        // Emoji-Wörterbuch einmalig laden. Wenn die JSON fehlt, bleibt das
        // Wörterbuch leer und der Emoji-Modus fällt auf reines Anhängen zurück.
        _emojiDict = EmojiDictionary.LoadFromDefaultPath();

        _modeManager = new ModeManager(_ollama, _llm, _emojiDict);

        // Modell-Pfad: whisper-models/ggml-small.bin neben der .exe.
        // (base war nur für Diagnose aktiv – small liefert spürbar bessere deutsche Transkripte)
        var modelPath = Path.Combine(AppContext.BaseDirectory, "whisper-models", "ggml-small.bin");
        _transcriber = new TranscriptionService(modelPath);

        _recorder.LevelChanged += level =>
        {
            if (_state is not null)
                _state.InputLevel = level;
        };

        _window = new MainWindow();
        _window.Wire(_state, ToggleRecording);
        _window.Show();

        // Hotkey braucht ein existierendes HWND, daher NACH Show().
        _hotkey = new HotkeyService();
        _hotkey.Register(_window);
        _hotkey.HotkeyPressed += ToggleRecording;

        _tray = new TrayIconService(_state);
        _tray.ShowWindowRequested += () =>
        {
            _window.Show();
            _window.WindowState = WindowState.Normal;
            _window.Activate();
        };
        _tray.ToggleRecordingRequested += ToggleRecording;
        _tray.ExitRequested += Shutdown;

        // Startup-Check: Whisper-Modell vorhanden?
        if (!_transcriber.ModelExists)
        {
            _state.StatusText = "Modell fehlt – siehe README";
        }
    }

    private async void ToggleRecording()
    {
        if (_state is null || _recorder is null || _transcriber is null
            || _modeManager is null || _textInsert is null || _foreground is null) return;

        if (!await _toggleLock.WaitAsync(0))
            return; // gerade schon im Toggle – ignorieren.

        try
        {
            if (_state.CurrentPhase == RecordingState.Phase.Idle)
            {
                // Start
                try
                {
                    _recorder.Start();
                    _state.CurrentPhase = RecordingState.Phase.Recording;
                    _state.StatusText = "Aufnahme läuft…";
                }
                catch (Exception ex)
                {
                    _state.StatusText = "Mikrofon-Fehler";
                    MessageBox.Show(ex.Message, "Speaky", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else if (_state.CurrentPhase == RecordingState.Phase.Recording)
            {
                // Stop → Transcribe → Insert
                _state.CurrentPhase = RecordingState.Phase.Transcribing;
                _state.StatusText = "Transkribiert…";

                var wavPath = _recorder.StopAndGetFilePath();
                if (string.IsNullOrEmpty(wavPath))
                {
                    _state.StatusText = "Aufnahme leer";
                    _state.CurrentPhase = RecordingState.Phase.Idle;
                    return;
                }

                try
                {
                    var raw = await _transcriber.TranscribeFileAsync(wavPath);

                    // Für den Diplomatie-Modus kann das Post-Processing mehrere
                    // Sekunden dauern (Ollama-Start + Modell-Load in VRAM + Inferenz).
                    // Der Status wird hier sichtbar umgesetzt, damit der User weiß,
                    // dass die App nicht hängt. "Modell lädt" beim Kaltstart (bis ~90s),
                    // "Diplomatie" bei warmem Modell (10–20s).
                    if (_state.Mode == RecordingMode.Diplomatie)
                        _state.StatusText = $"Modell lädt… ({_state.LlmModel})";

                    var result = await _modeManager.ProcessAsync(
                        raw, _state.Mode, _state.EmojiCount, _state.LlmModel);

                    if (!string.IsNullOrWhiteSpace(result.Text))
                    {
                        // Fokus zurück auf das ursprüngliche Zielfenster holen
                        // (wichtig wenn der User per GUI-Button gestartet hat).
                        _foreground.RestoreForeground();
                        await Task.Delay(80);

                        _textInsert.Insert(result.Text);
                        _state.StatusText = result.Status;
                    }
                    else
                    {
                        _state.StatusText = "Nichts erkannt";
                    }
                }
                catch (FileNotFoundException fnf)
                {
                    _state.StatusText = "Modell fehlt";
                    MessageBox.Show(fnf.Message, "Speaky", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch (Exception ex)
                {
                    _state.StatusText = "Fehler: " + ex.Message;
                }
                finally
                {
                    _recorder.CleanupFile(wavPath);
                    _state.CurrentPhase = RecordingState.Phase.Idle;
                }
            }
        }
        finally
        {
            _toggleLock.Release();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsShuttingDown = true;
        _hotkey?.Dispose();
        _recorder?.Dispose();
        _transcriber?.Dispose();
        _tray?.Dispose();
        _foreground?.Dispose();
        // OllamaLifecycle killt den Ollama-Child-Prozess genau dann, wenn Speaky
        // ihn selbst gestartet hat. Einen bereits laufenden Ollama-Service rührt
        // Dispose nicht an.
        _ollama?.Dispose();
        _toggleLock.Dispose();
        base.OnExit(e);
    }
}
