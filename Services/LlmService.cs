using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Speaky.Services;

/// <summary>
/// Lokaler LLM-Client für den Diplomatie-Modus.
///
/// Spricht die <b>OpenAI-kompatible Chat-Completions-API</b> von Ollama
/// (<c>http://localhost:11434/v1/chat/completions</c>). Dieselbe Schnittstelle
/// funktioniert auch mit LM Studio auf Port 1234 – für den MVP ist der Pfad aber
/// fest auf Ollama, weil Speaky den Dienst auch lifecyclen will (siehe
/// <see cref="OllamaLifecycle"/>).
///
/// VERHALTEN:
/// - Wenn Ollama nicht erreichbar ist oder ein Fehler auftritt, wirft die Methode
///   eine Exception. Der Aufrufer (ModeManager) fängt das ab und fällt auf den
///   rohen Transkripttext zurück, damit Speaky auch ohne laufendes LLM nutzbar bleibt.
/// - <c>&lt;think&gt;...&lt;/think&gt;</c>-Blöcke von Qwen3 werden gestrippt.
/// - Der System-Prompt ist bewusst knapp und explizit ("nur umformulieren, keine
///   Einleitung"), damit das Modell keinen Erklärtext vor die Antwort schreibt.
/// </summary>
public sealed class LlmService
{
    private const string ChatUrl = "http://localhost:11434/v1/chat/completions";

    private const string DiplomatieSystemPrompt =
        "Du bist ein höflicher Kommunikations-Assistent. Der Benutzer diktiert dir " +
        "einen Text, der wütend, beleidigend, hektisch oder unsachlich sein kann. " +
        "Formuliere diesen Text in eine ruhige, sachliche, höfliche Business-" +
        "Sprache um. WICHTIG: Inhalt und Anliegen des Benutzers bleiben erhalten – " +
        "du entfernst Beleidigungen, Kraftausdrücke und emotionale Ausbrüche, aber " +
        "nicht die eigentliche Botschaft. Antworte AUSSCHLIESSLICH mit dem " +
        "umformulierten deutschen Text, ohne Einleitung, ohne Erklärung, ohne " +
        "Anführungszeichen, ohne Markdown.";

    private static readonly Regex ThinkBlockRegex = new(
        @"<think>.*?</think>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly string _logPath;

    public LlmService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        _logPath = Path.Combine(AppContext.BaseDirectory, "whisper-debug.log");
    }

    /// <summary>
    /// Formuliert einen Text mit dem Diplomatie-Prompt um. Wirft bei Fehler –
    /// der Aufrufer muss entscheiden, wie er den Rückfall handhabt.
    /// </summary>
    public async Task<string> DiplomatieRewriteAsync(string rawText, string model, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return rawText;

        Log($"--- LLM diplomatie start model={model} len={rawText.Length} ---");

        var payload = new ChatRequest
        {
            Model = model,
            Temperature = 0.3,
            Stream = false,
            Messages = new[]
            {
                new ChatMessage { Role = "system", Content = DiplomatieSystemPrompt },
                new ChatMessage { Role = "user", Content = rawText },
            },
        };

        using var resp = await _http.PostAsJsonAsync(ChatUrl, payload, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await SafeReadAsync(resp).ConfigureAwait(false);
            Log($"  LLM http {(int)resp.StatusCode}: {body}");
            throw new InvalidOperationException($"Ollama antwortete mit HTTP {(int)resp.StatusCode}");
        }

        var parsed = await resp.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: ct).ConfigureAwait(false);
        var content = parsed?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;

        // Qwen3 schreibt gerne "<think>…</think>" vor die eigentliche Antwort.
        content = ThinkBlockRegex.Replace(content, string.Empty);
        content = content.Trim().Trim('"');

        Log($"--- LLM diplomatie done: \"{content}\" ---");

        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("Ollama lieferte leeren Content zurück");

        return content;
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage resp)
    {
        try { return await resp.Content.ReadAsStringAsync().ConfigureAwait(false); }
        catch { return "(body unreadable)"; }
    }

    private void Log(string line)
    {
        try
        {
            File.AppendAllText(_logPath, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
        }
        catch { /* Log-Fehler niemals propagieren */ }
    }

    // ---------- DTOs für OpenAI-kompatible Chat-Completions API ----------

    private sealed class ChatRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("messages")] public ChatMessage[] Messages { get; set; } = Array.Empty<ChatMessage>();
        [JsonPropertyName("temperature")] public double Temperature { get; set; } = 0.3;
        [JsonPropertyName("stream")] public bool Stream { get; set; } = false;
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
    }

    private sealed class ChatResponse
    {
        [JsonPropertyName("choices")] public ChatChoice[]? Choices { get; set; }
    }

    private sealed class ChatChoice
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
    }
}
