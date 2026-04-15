using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Speaky.Services;

/// <summary>
/// Wort-zu-Emoji-Nachschlag für den Emoji-Modus.
///
/// Lädt <c>emoji-dictionary.de.json</c> neben der EXE einmalig beim Start.
/// Schlüssel sind kleingeschrieben, der Lookup ist case-insensitiv. Ersetzt
/// wird <b>nur als ganzes Wort</b> (Regex-<c>\b</c>-Wortgrenze), damit "Ei"
/// nicht in "ein" oder "Eimer" hineinwirkt.
///
/// Die JSON darf vom Benutzer direkt editiert werden. Ein "_comment"-Feld
/// wird beim Laden ignoriert.
/// </summary>
public sealed class EmojiDictionary
{
    // Erfasst Wörter aus Buchstaben + Umlauten + ß (Unicode-Kategorie L).
    // Zahlen und Satzzeichen bleiben unangetastet.
    private static readonly Regex WordRegex = new(
        @"[\p{L}]+",
        RegexOptions.Compiled);

    private readonly Dictionary<string, string> _map;

    public EmojiDictionary(Dictionary<string, string> map)
    {
        _map = map;
    }

    public int Count => _map.Count;

    /// <summary>
    /// Ersetzt jedes Vorkommen eines Wortes, das im Wörterbuch steht, durch
    /// sein Emoji. Wörter ohne Treffer bleiben unverändert. Die Groß-/
    /// Kleinschreibung des Originals spielt keine Rolle.
    /// </summary>
    public string ReplaceWords(string text)
    {
        if (string.IsNullOrEmpty(text) || _map.Count == 0)
            return text;

        return WordRegex.Replace(text, match =>
        {
            var word = match.Value.ToLowerInvariant();
            return _map.TryGetValue(word, out var emoji) ? emoji : match.Value;
        });
    }

    /// <summary>
    /// Lädt das Wörterbuch aus der JSON-Datei neben der EXE. Wenn die Datei
    /// fehlt oder kaputt ist, wird ein leeres Wörterbuch zurückgegeben –
    /// Speaky bleibt funktionsfähig, nur ohne Inline-Ersetzung.
    /// </summary>
    public static EmojiDictionary LoadFromDefaultPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "emoji-dictionary.de.json");
        return LoadFromFile(path);
    }

    public static EmojiDictionary LoadFromFile(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(path))
                return new EmojiDictionary(map);

            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return new EmojiDictionary(map);

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                // "_comment" und sonstige Nicht-String-Werte überspringen
                if (prop.Name.StartsWith('_')) continue;
                if (prop.Value.ValueKind != JsonValueKind.String) continue;

                var key = prop.Name.Trim().ToLowerInvariant();
                var value = prop.Value.GetString();
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
                    continue;

                map[key] = value;
            }
        }
        catch
        {
            // JSON kaputt? Leeres Wörterbuch zurückgeben – der Emoji-Modus
            // fällt dann auf das reine Anhängen zufälliger Emojis zurück.
        }

        return new EmojiDictionary(map);
    }
}
