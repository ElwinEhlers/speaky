namespace Speaky.Models;

/// <summary>
/// Aufnahme-Modi. Jeder Modus verändert nur das Post-Processing des Textes –
/// die Audio-Aufnahme selbst ist in allen Modi identisch.
///
/// Die Reihenfolge der Enum-Werte ist bewusst stabil gehalten, weil sie direkt
/// auf die <c>ModeCombo.SelectedIndex</c> in der GUI gemappt wird.
/// </summary>
public enum RecordingMode
{
    /// <summary>Text so direkt wie gesprochen einfügen.</summary>
    Blitz = 0,

    /// <summary>Pausen und Satzstruktur sauber ausformulieren (deterministisch).</summary>
    Ausschreib = 1,

    /// <summary>
    /// Wütendes Diktat → höfliche Business-Sprache.
    /// Nutzt ein lokales LLM via Ollama (siehe <see cref="Speaky.Services.LlmService"/>).
    /// Fällt auf den Rohtext zurück, wenn Ollama nicht verfügbar ist.
    /// </summary>
    Diplomatie = 2,

    /// <summary>Text wie gesprochen plus eingestreute Emojis.</summary>
    Emoji = 3,
}

/// <summary>
/// Unterstützte Ollama-Modelle für den Diplomatie-Modus.
/// Tags müssen exakt so in der lokalen Ollama-Installation vorhanden sein
/// (Prüfen mit <c>ollama list</c>).
/// </summary>
public static class LlmModels
{
    public const string Qwen3_8B = "qwen3:8b";
    public const string Gemma4_E4B = "gemma4:e4b";
    public const string Gemma4_26B = "gemma4:26b";

    /// <summary>Default: kleinstes/schnellstes Modell.</summary>
    public const string Default = Qwen3_8B;

    public static readonly string[] All =
    {
        Qwen3_8B, Gemma4_E4B, Gemma4_26B,
    };
}
