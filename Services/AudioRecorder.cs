using System.IO;
using NAudio.Wave;

namespace Speaky.Services;

/// <summary>
/// Nimmt das Mikrofon-Signal auf und schreibt es als korrektes WAV in eine Temp-Datei.
///
/// WICHTIG: Wir schreiben bewusst in eine echte Datei (nicht in einen MemoryStream),
/// damit NAudio's WaveFileWriter den RIFF-Header beim Dispose sauber finalisieren kann.
/// Der vorherige Ansatz mit gewrapptem MemoryStream hat zu subtil kaputten WAV-Headern
/// geführt – Whisper hat dann nur Rauschen/Halluzinationen ausgegeben.
///
/// Zusätzlich wird nach jeder Aufnahme eine Debug-Kopie unter
/// &lt;AppDir&gt;/last-recording.wav abgelegt. Nützlich um bei Qualitätsproblemen
/// manuell reinzuhören.
/// </summary>
public sealed class AudioRecorder : IDisposable
{
    // Whisper arbeitet nativ mit 16 kHz Mono 16-bit PCM.
    private static readonly WaveFormat TargetFormat = new(16_000, 16, 1);

    private readonly string _debugCopyPath;

    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _tempPath;

    public AudioRecorder()
    {
        _debugCopyPath = Path.Combine(AppContext.BaseDirectory, "last-recording.wav");
    }

    public event Action<float>? LevelChanged;

    public bool IsRecording { get; private set; }

    public void Start()
    {
        if (IsRecording) return;

        _tempPath = Path.Combine(Path.GetTempPath(), $"speaky_{Guid.NewGuid():N}.wav");

        try
        {
            _writer = new WaveFileWriter(_tempPath, TargetFormat);

            _waveIn = new WaveInEvent
            {
                WaveFormat = TargetFormat,
                BufferMilliseconds = 50,
                // Explizit default device:
                DeviceNumber = 0,
            };
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;

            _waveIn.StartRecording();
            IsRecording = true;
        }
        catch (Exception ex)
        {
            // Aufräumen bei Fehler
            _writer?.Dispose();
            _writer = null;
            _waveIn?.Dispose();
            _waveIn = null;
            try { if (_tempPath is not null && File.Exists(_tempPath)) File.Delete(_tempPath); } catch { }
            _tempPath = null;

            throw new InvalidOperationException(
                "Mikrofon konnte nicht gestartet werden. Prüfe: Einstellungen → Datenschutz & Sicherheit → Mikrofon.\n" +
                "Details: " + ex.Message, ex);
        }
    }

    /// <summary>
    /// Stoppt die Aufnahme, finalisiert die WAV-Datei und gibt den Pfad zurück.
    /// Der Aufrufer ist verantwortlich, die Datei nach Gebrauch zu löschen
    /// (oder via <see cref="CleanupFile"/>).
    /// </summary>
    public string? StopAndGetFilePath()
    {
        if (!IsRecording || _waveIn is null || _writer is null) return null;

        _waveIn.StopRecording();
        _waveIn.DataAvailable -= OnDataAvailable;
        _waveIn.RecordingStopped -= OnRecordingStopped;
        _waveIn.Dispose();
        _waveIn = null;

        // WICHTIG: Dispose finalisiert den RIFF-Header (Chunk-Größen)
        _writer.Dispose();
        _writer = null;

        IsRecording = false;
        LevelChanged?.Invoke(0f);

        var path = _tempPath;
        _tempPath = null;

        // Debug-Kopie ablegen (Fehler hier sind nicht kritisch)
        try
        {
            if (path is not null && File.Exists(path))
            {
                File.Copy(path, _debugCopyPath, overwrite: true);
            }
        }
        catch { /* ignore */ }

        return path;
    }

    public void CleanupFile(string? path)
    {
        if (path is null) return;
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _writer?.Write(e.Buffer, 0, e.BytesRecorded);

        // Peak-Level für VU-Meter (16-bit signed samples, little-endian)
        float peak = 0f;
        for (int i = 0; i < e.BytesRecorded - 1; i += 2)
        {
            short sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
            float abs = Math.Abs(sample / 32768f);
            if (abs > peak) peak = abs;
        }
        LevelChanged?.Invoke(peak);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e) { /* no-op */ }

    public void Dispose()
    {
        if (IsRecording)
        {
            var p = StopAndGetFilePath();
            CleanupFile(p);
        }
    }
}
