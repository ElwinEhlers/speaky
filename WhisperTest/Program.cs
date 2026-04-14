// Standalone-Diagnose: Umgeht komplett unsere Speaky-App-Infrastruktur.
// Lädt direkt das Modell, öffnet last-recording.wav als Stream,
// und gibt alle Whisper-Segmente mit Zeitstempel aus.
//
// Wenn dieser Test sauber "Hallo Speaky dies ist ein Test" ausspuckt,
// liegt der Bug in unserer App. Wenn es auch hier kaputt ist,
// liegt es an Whisper.net/Modell/Audio.

using Whisper.net;

if (args.Length < 2)
{
    Console.WriteLine("Usage: WhisperTest <model-path> <wav-path>");
    return 1;
}

var modelPath = args[0];
var wavPath = args[1];

Console.WriteLine($"Model: {modelPath}");
Console.WriteLine($"WAV:   {wavPath}");
Console.WriteLine($"Model exists: {File.Exists(modelPath)}");
Console.WriteLine($"WAV exists:   {File.Exists(wavPath)}");
Console.WriteLine();

// TEST 1: Language = "de"
Console.WriteLine("=== TEST 1: WithLanguage(\"de\") ===");
await RunTest(modelPath, wavPath, "de");

// TEST 2: Language = "auto"
Console.WriteLine();
Console.WriteLine("=== TEST 2: WithLanguage(\"auto\") ===");
await RunTest(modelPath, wavPath, "auto");

return 0;

static async Task RunTest(string modelPath, string wavPath, string language)
{
    try
    {
        using var factory = WhisperFactory.FromPath(modelPath);
        using var processor = factory.CreateBuilder()
            .WithLanguage(language)
            .Build();

        using var fs = File.OpenRead(wavPath);
        int segmentCount = 0;
        await foreach (var segment in processor.ProcessAsync(fs))
        {
            segmentCount++;
            Console.WriteLine($"[{segment.Start} → {segment.End}] \"{segment.Text}\"");
        }
        if (segmentCount == 0)
        {
            Console.WriteLine("(keine Segmente zurückgegeben)");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FEHLER: {ex.GetType().Name}: {ex.Message}");
    }
}
