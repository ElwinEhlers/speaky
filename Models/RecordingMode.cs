namespace Speaky.Models;

/// <summary>
/// Aufnahme-Modi. Jeder Modus verändert nur das Post-Processing des Textes –
/// die Audio-Aufnahme selbst ist in allen Modi identisch.
/// </summary>
public enum RecordingMode
{
    /// <summary>Text so direkt wie gesprochen einfügen.</summary>
    Blitz,

    /// <summary>Pausen und Satzstruktur sauber ausformulieren.</summary>
    Ausschreib,

    /// <summary>Emotional, prägnant, direkter Ton.</summary>
    Rage,

    /// <summary>Text wie gesprochen plus eingestreute Emojis.</summary>
    Emoji,
}
