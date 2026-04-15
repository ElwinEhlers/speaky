using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace Speaky.Services;


/// <summary>
/// Startet und stoppt den lokalen Ollama-Server bedarfsgerecht.
///
/// STRATEGIE:
/// - Speaky selbst startet Ollama NUR wenn der Diplomatie-Modus tatsächlich benutzt
///   wird. Wörtlich/Ausschreib/Emoji triggern das nie.
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
    private readonly string _logPath;
    private Process? _ownedProcess;
    private bool _weStartedIt;

    public OllamaLifecycle()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(ReadyProbeTimeoutMs) };
        _logPath = Path.Combine(AppContext.BaseDirectory, "whisper-debug.log");
    }

    private void Log(string line)
    {
        try
        {
            File.AppendAllText(_logPath, $"{DateTime.Now:HH:mm:ss.fff} [ollama] {line}{Environment.NewLine}");
        }
        catch { /* never propagate log errors */ }
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
            // 1) Schon erreichbar? Dann nichts tun – wir fassen einen vorhandenen
            //    Ollama-Server (Autostart, Tray-App, manueller Start) NICHT an.
            if (await IsReachableAsync(ct).ConfigureAwait(false))
            {
                Log("reachable on first probe – using existing server, _weStartedIt=false");
                _weStartedIt = false;
                return true;
            }

            // 2) Ollama binary suchen
            var exe = FindOllamaExecutable();
            if (exe is null)
            {
                Log("ollama.exe not found in PATH or default install dirs");
                return false;
            }
            Log($"starting child: {exe} serve");

            // 3) Child-Prozess starten
            Process? proc;
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
                proc = Process.Start(psi);
            }
            catch (Exception ex)
            {
                Log($"Process.Start failed: {ex.Message}");
                return false;
            }

            if (proc is null)
            {
                Log("Process.Start returned null");
                return false;
            }

            // 4) Kurz warten und prüfen ob der Child noch lebt.
            //    Wenn der Port bereits besetzt ist (z.B. durch Ollama-Autostart-Tray
            //    oder einen parallel laufenden Speaky), beendet sich "ollama serve"
            //    nach <1 Sekunde mit "bind: address already in use". Dann dürfen wir
            //    den Prozess NICHT als unseren eigenen markieren.
            await Task.Delay(700, ct).ConfigureAwait(false);
            if (proc.HasExited)
            {
                Log($"child exited immediately (code {proc.ExitCode}) – port probably already held by another ollama");
                try { proc.Dispose(); } catch { }

                // Re-probe: wenn ein anderer Ollama schon auf 11434 hört, ist das OK
                // – wir benutzen ihn einfach mit und räumen später nichts auf.
                if (await IsReachableAsync(ct).ConfigureAwait(false))
                {
                    Log("existing ollama reachable after failed spawn – adopting, _weStartedIt=false");
                    _weStartedIt = false;
                    return true;
                }

                Log("no ollama reachable after failed spawn");
                return false;
            }

            // 5) Child lebt. Jetzt als unseren markieren und auf API-Bereitschaft warten.
            _ownedProcess = proc;
            _weStartedIt = true;
            Log($"child alive (pid {proc.Id}) – polling /api/tags");

            var deadline = DateTime.UtcNow.AddMilliseconds(StartupWaitTotalMs);
            while (DateTime.UtcNow < deadline)
            {
                if (ct.IsCancellationRequested) return false;
                if (await IsReachableAsync(ct).ConfigureAwait(false))
                {
                    Log($"child is serving after {StartupWaitTotalMs - (int)(deadline - DateTime.UtcNow).TotalMilliseconds}ms");
                    return true;
                }
                try { await Task.Delay(StartupPollIntervalMs, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return false; }
            }

            Log($"child did not start serving within {StartupWaitTotalMs}ms");
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
                    Log($"killing owned child (pid {_ownedProcess.Id})");
                    _ownedProcess.Kill(entireProcessTree: true);
                    _ownedProcess.WaitForExit(2000);
                }
                else
                {
                    Log("owned child already exited at dispose time");
                }
            }
            catch (Exception ex)
            {
                Log($"kill failed: {ex.Message}");
            }
            finally
            {
                try { _ownedProcess.Dispose(); } catch { }
            }
        }
        else if (_ownedProcess is null)
        {
            Log("dispose: no owned process to clean up");
        }

        _http.Dispose();
        _gate.Dispose();
    }
}
