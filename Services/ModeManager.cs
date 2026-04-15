using Speaky.Models;

namespace Speaky.Services;

/// <summary>
/// Wendet pro Modus ein Post-Processing auf das rohe Whisper-Transkript an.
///
/// - Wörtlich / Ausschreib / Emoji: rein deterministisch, kein externer Service.
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
    private readonly EmojiDictionary _emojiDict;

    public ModeManager(OllamaLifecycle ollama, LlmService llm, EmojiDictionary emojiDict)
    {
        _ollama = ollama;
        _llm = llm;
        _emojiDict = emojiDict;
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
            case RecordingMode.Woertlich:
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
    /// Emoji-Modus:
    /// 1) Jedes Wort, das im <see cref="EmojiDictionary"/> steht, wird inline
    ///    durch sein Emoji ersetzt ("Die Kuh steht in der Sonne" → "Die 🐄
    ///    steht in der ☀️").
    /// 2) Danach werden optional noch N zufällige Emojis aus dem Pool angehängt
    ///    (Slider 0–5 in der GUI). Bei 0 gibt es keine Random-Anhänge, der Text
    ///    kommt nur inline-ersetzt raus.
    /// </summary>
    private string WithEmojis(string text, int count)
    {
        // 1) Inline-Ersetzung
        var replaced = _emojiDict.ReplaceWords(text);

        // 2) Optional zufällige Emojis am Ende anhängen
        count = Math.Clamp(count, 0, 5);
        if (count == 0) return replaced;

        var rnd = Random.Shared;
        var emojis = new System.Text.StringBuilder(" ");
        for (int i = 0; i < count; i++)
        {
            emojis.Append(EmojiPool[rnd.Next(EmojiPool.Length)]);
        }
        return replaced + emojis;
    }
}
