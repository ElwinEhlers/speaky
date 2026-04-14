using System.IO;
using Whisper.net;

namespace Speaky.Services;

/// <summary>
/// Lokale Transkription via Whisper.net.
///
/// STRATEGIE: Minimal. Frische <see cref="WhisperFactory"/> pro Aufruf. Caching
/// hatte in Kombination mit dem WPF-SynchronizationContext zu kaputten Ergebnissen
/// geführt (Halluzinationen wie "Hallo, ]]]]]"). Die eigentliche Whisper-Arbeit
/// läuft bewusst auf einem Pool-Thread via Task.Run, damit der
/// SynchronizationContext der WPF-UI nicht in die native Schleife hineinwirkt.
///
/// Schreibt ein whisper-debug.log neben der .exe – nützlich, um bei merkwürdigen
/// Ausgaben sofort sehen zu können, was Whisper segmentweise geliefert hat,
/// unabhängig vom Post-Processing in ModeManager/TextInsertion.
///
/// VERSIONSABHÄNGIG: Whisper.net 1.7.x.
/// </summary>
public sealed class TranscriptionService : IDisposable
{
    private readonly string _modelPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _logPath;

    public TranscriptionService(string modelPath)
    {
        _modelPath = modelPath;
        _logPath = Path.Combine(AppContext.BaseDirectory, "whisper-debug.log");
    }

    public bool ModelExists => File.Exists(_modelPath);

    public async Task<string> TranscribeFileAsync(string wavPath, CancellationToken ct = default)
    {
        if (!ModelExists)
        {
            throw new FileNotFoundException(
                $"Whisper-Modell nicht gefunden: {_modelPath}\n" +
                "Lege z.B. ggml-small.bin nach 'whisper-models/' im App-Verzeichnis. Siehe README.");
        }

        if (!File.Exists(wavPath))
        {
            throw new FileNotFoundException($"WAV-Datei nicht gefunden: {wavPath}");
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Log($"=== Transcribe start: {wavPath} ({new FileInfo(wavPath).Length} bytes) ===");

            // KEIN Caching. Frische Factory pro Aufruf – exakt wie der
            // Standalone-Diagnose-Test, der sauber funktioniert.
            // Die Arbeit findet bewusst auf einem Pool-Thread statt, damit
            // Whisper.net nicht mit dem WPF-Dispatcher-SynchronizationContext
            // interagiert.
            var result = await Task.Run(async () =>
            {
                using var factory = WhisperFactory.FromPath(_modelPath);
                using var processor = factory.CreateBuilder()
                    .WithLanguage("de")
                    .Build();

                var sb = new System.Text.StringBuilder();
                int segCount = 0;
                using var fs = File.OpenRead(wavPath);
                await foreach (var segment in processor.ProcessAsync(fs, ct).ConfigureAwait(false))
                {
                    segCount++;
                    Log($"  seg[{segCount}] [{segment.Start} → {segment.End}] \"{segment.Text}\"");
                    sb.Append(segment.Text);
                }
                Log($"  total segments: {segCount}");
                return sb.ToString().Trim();
            }, ct).ConfigureAwait(false);

            Log($"=== Transcribe done: \"{result}\" ===");
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Log(string line)
    {
        try
        {
            File.AppendAllText(_logPath, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
        }
        catch { /* Log-Fehler niemals propagieren */ }
    }

    public void Dispose()
    {
        _gate.Dispose();
    }
}
