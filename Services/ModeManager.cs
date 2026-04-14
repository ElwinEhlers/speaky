using Speaky.Models;

namespace Speaky.Services;

/// <summary>
/// Wendet pro Modus ein Post-Processing auf das rohe Whisper-Transkript an.
///
/// HINWEIS: Das MVP arbeitet rein deterministisch (Regex, Mapping).
/// Für wirklich "natürliche" Umformulierungen (insbesondere Rage-Modus und
/// Ausschreib-Modus) ist der nächste Ausbau ein LLM-Call (lokales Ollama / LM Studio).
/// Dafür ist die Architektur schon vorbereitet – es reicht, diese Klasse hinter
/// ein Interface zu ziehen und eine LLM-Variante zu implementieren.
/// </summary>
public sealed class ModeManager
{
    private static readonly string[] EmojiPool =
    {
        "🔥", "✨", "🚀", "💡", "🎯", "👀", "💪", "🙌", "😄", "🤔",
        "😎", "💯", "👌", "🎉", "⚡", "🌟", "❤️", "🙏",
    };

    public string Process(string rawText, RecordingMode mode, int emojiCount)
    {
        var text = rawText.Trim();
        if (text.Length == 0) return text;

        return mode switch
        {
            RecordingMode.Blitz => text,
            RecordingMode.Ausschreib => Cleanup(text),
            RecordingMode.Rage => Rage(Cleanup(text)),
            RecordingMode.Emoji => WithEmojis(text, emojiCount),
            _ => text,
        };
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

    /// <summary>Rage-Modus: alles groß + Ausrufezeichen. Deterministisch, keine Magie.</summary>
    private static string Rage(string text)
    {
        text = text.TrimEnd('.', '!', '?', ' ');
        return text.ToUpperInvariant() + "!!!";
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
