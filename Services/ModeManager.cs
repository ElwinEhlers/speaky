using Speaky.Models;

namespace Speaky.Services;

/// <summary>
/// Wendet pro Modus ein Post-Processing auf das rohe Whisper-Transkript an.
///
/// - Blitz / Ausschreib / Emoji: rein deterministisch, kein externer Service.
/// - Diplomatie: ruft <see cref="LlmService"/> (Ollama) auf und fällt auf den
///   deterministischen Cleanup zurück, wenn Ollama nicht erreichbar ist.
///   Ollama wird dabei <b>on demand</b> via <see cref="OllamaLifecycle"/>
///   gestartet – die anderen Modi starten nie ein LLM.
/// </summary>
public sealed class ModeManager
{
    private static readonly string[] EmojiPool =
    {
        "🔥", "✨", "🚀", "💡", "🎯", "👀", "💪", "🙌", "😄", "🤔",
        "😎", "💯", "👌", "🎉", "⚡", "🌟", "❤️", "🙏",
    };

    private readonly OllamaLifecycle _ollama;
    private readonly LlmService _llm;

    public ModeManager(OllamaLifecycle ollama, LlmService llm)
    {
        _ollama = ollama;
        _llm = llm;
    }

    /// <summary>Ergebnis eines Mode-Processing-Laufs inklusive Statusmeldung für die GUI.</summary>
    public readonly record struct Result(string Text, string Status);

    public async Task<Result> ProcessAsync(
        string rawText,
        RecordingMode mode,
        int emojiCount,
        string llmModel,
        CancellationToken ct = default)
    {
        var text = rawText.Trim();
        if (text.Length == 0) return new Result(text, "Nichts erkannt");

        switch (mode)
        {
            case RecordingMode.Blitz:
                return new Result(text, "Eingefügt – bereit");

            case RecordingMode.Ausschreib:
                return new Result(Cleanup(text), "Eingefügt – bereit");

            case RecordingMode.Emoji:
                return new Result(WithEmojis(text, emojiCount), "Eingefügt – bereit");

            case RecordingMode.Diplomatie:
                return await DiplomatieAsync(text, llmModel, ct).ConfigureAwait(false);

            default:
                return new Result(text, "Eingefügt – bereit");
        }
    }

    private async Task<Result> DiplomatieAsync(string text, string llmModel, CancellationToken ct)
    {
        // 1) Ollama hochfahren (oder feststellen, dass er schon läuft).
        var up = await _ollama.EnsureRunningAsync(ct).ConfigureAwait(false);
        if (!up)
        {
            // Fallback: Rohtext nur aufgeräumt zurückgeben. So funktioniert Speaky
            // auch dann weiter, wenn Ollama nicht installiert ist.
            return new Result(Cleanup(text), "Ollama nicht erreichbar – Rohtext eingefügt");
        }

        // 2) LLM-Rewrite versuchen. Bei Fehler ebenfalls Cleanup-Fallback.
        try
        {
            var rewritten = await _llm.DiplomatieRewriteAsync(text, llmModel, ct).ConfigureAwait(false);
            return new Result(rewritten, $"Diplomatie ({llmModel}) – eingefügt");
        }
        catch (Exception ex)
        {
            return new Result(Cleanup(text), "LLM-Fehler: " + ex.Message);
        }
    }

    /// <summary>
    /// Einfaches Aufräumen: doppelte Leerzeichen weg, erster Buchstabe groß,
    /// Satzzeichen am Ende wenn keines da ist.
    /// </summary>
    private static string Cleanup(string text)
    {
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length == 0) return text;
        text = char.ToUpper(text[0]) + text[1..];
        if (!".!?".Contains(text[^1])) text += ".";
        return text;
    }

    /// <summary>
    /// Emoji-Modus: Text bleibt wie gesprochen, am Ende werden N Emojis angehängt.
    /// Die Auswahl ist zufällig aus dem Pool – genug Abwechslung für Social Media.
    /// </summary>
    private static string WithEmojis(string text, int count)
    {
        count = Math.Clamp(count, 1, 5);
        var rnd = Random.Shared;
        var emojis = new System.Text.StringBuilder(" ");
        for (int i = 0; i < count; i++)
        {
            emojis.Append(EmojiPool[rnd.Next(EmojiPool.Length)]);
        }
        return text + emojis;
    }
}
