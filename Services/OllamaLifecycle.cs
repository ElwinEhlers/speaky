using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace Speaky.Services;

/// <summary>
/// Startet und stoppt den lokalen Ollama-Server bedarfsgerecht.
///
/// STRATEGIE:
/// - Speaky selbst startet Ollama NUR wenn der Diplomatie-Modus tatsächlich benutzt
///   wird. Blitz/Ausschreib/Emoji triggern das nie.
/// - Wenn Ollama bereits läuft (weil der User ihn in einem anderen Kontext gestartet
///   hat), lassen wir ihn in Ruhe und killen ihn beim Speaky-Shutdown NICHT.
/// - Wenn Speaky Ollama selbst gestartet hat, wird der Prozess beim Dispose
///   (und damit beim App-Exit) wieder beendet. So schleppt Speaky kein Ollama als
///   Dauerläufer mit.
/// - Die Erkennung "läuft schon?" erfolgt über einen HTTP-Probe auf /api/tags –
///   der einzige verlässliche Check, weil "ollama serve" auch dann noch laufen kann,
///   wenn er in einem anderen Terminal gestartet wurde.
///
/// Windows-Details: Ollama für Windows installiert standardmäßig einen Autostart-
/// Service und ein Tray-Icon. Wenn der User den nicht will, kann er ihn über
/// "Einstellungen → Apps → Autostart" deaktivieren. Speaky erkennt einen bereits
/// laufenden Ollama-Dienst und stört ihn nicht.
/// </summary>
public sealed class OllamaLifecycle : IDisposable
{
    private const string BaseUrl = "http://localhost:11434";
    private const int ReadyProbeTimeoutMs = 1500;
    private const int StartupWaitTotalMs = 15000;
    private const int StartupPollIntervalMs = 400;

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _ownedProcess;
    private bool _weStartedIt;

    public OllamaLifecycle()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(ReadyProbeTimeoutMs) };
    }

    /// <summary>
    /// Stellt sicher, dass Ollama erreichbar ist. Startet ihn notfalls selbst.
    /// Gibt <c>true</c> zurück, wenn Ollama am Ende erreichbar ist.
    /// </summary>
    public async Task<bool> EnsureRunningAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (await IsReachableAsync(ct).ConfigureAwait(false))
                return true;

            // Ollama binary suchen und starten
            var exe = FindOllamaExecutable();
            if (exe is null)
                return false;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "serve",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                _ownedProcess = Process.Start(psi);
                _weStartedIt = _ownedProcess is not null;
            }
            catch
            {
                return false;
            }

            // Pollen bis die API antwortet
            var deadline = DateTime.UtcNow.AddMilliseconds(StartupWaitTotalMs);
            while (DateTime.UtcNow < deadline)
            {
                if (ct.IsCancellationRequested) return false;
                if (await IsReachableAsync(ct).ConfigureAwait(false))
                    return true;
                try { await Task.Delay(StartupPollIntervalMs, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return false; }
            }

            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> IsReachableAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(BaseUrl + "/api/tags", ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sucht <c>ollama.exe</c> erst über PATH, dann an den üblichen
    /// Windows-Installations-Orten.
    /// </summary>
    private static string? FindOllamaExecutable()
    {
        // 1) PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    var candidate = Path.Combine(dir.Trim(), "ollama.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch { /* invalid path segment, skip */ }
            }
        }

        // 2) Default-Installationspfade für Windows
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string[] candidates =
        {
            Path.Combine(localAppData, "Programs", "Ollama", "ollama.exe"),
            Path.Combine(programFiles, "Ollama", "ollama.exe"),
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        return null;
    }

    public void Dispose()
    {
        // Nur killen, wenn WIR ihn gestartet haben. Einen bereits laufenden Ollama
        // (z.B. als Autostart-Service) lassen wir unangetastet.
        if (_weStartedIt && _ownedProcess is not null)
        {
            try
            {
                if (!_ownedProcess.HasExited)
                {
                    _ownedProcess.Kill(entireProcessTree: true);
                    _ownedProcess.WaitForExit(2000);
                }
            }
            catch { /* best effort */ }
            finally
            {
                try { _ownedProcess.Dispose(); } catch { }
            }
        }

        _http.Dispose();
        _gate.Dispose();
    }
}
