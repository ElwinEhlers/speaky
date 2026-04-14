# Speaky

Kleine Windows-11-Hintergrund-App: Hotkey drücken → sprechen → loslassen → Text landet im gerade aktiven Eingabefeld. Deutsch. Lokal. Kein Cloud-Service nötig.

## Was das MVP kann

- **System-Tray-App** mit kleiner WPF-GUI (Start/Stop, Status, Modus-Auswahl, VU-Meter)
- **Globaler Hotkey** `Ctrl+Alt+S` — startet/stoppt die Aufnahme überall in Windows
- **Mikrofon-Aufnahme** in 16 kHz Mono via NAudio
- **Lokale Transkription** via Whisper.net (deutsches Sprachmodell, offline)
- **Automatisches Einfügen** in das fokussierte Eingabefeld via Clipboard + Ctrl+V (Win32 `SendInput`)
- **4 Modi** mit unterschiedlichem Post-Processing:
  1. **Blitz** – Text 1:1 wie gesprochen
  2. **Ausschreib** – Satzzeichen/Groß-Klein aufgeräumt
  3. **Rage** – GROSSBUCHSTABEN + Ausrufezeichen
  4. **Emoji** – Text + 1–5 zufällige Emojis (per Slider)

## Setup

### 1. Abhängigkeiten wiederherstellen

```bash
dotnet restore
```

### 2. Whisper-Modell herunterladen

Die App erwartet ein GGML-Whisper-Modell unter `whisper-models/ggml-small.bin` neben der EXE.

**Empfohlen für Deutsch:** `ggml-small.bin` (~488 MB, deutlich besser als base, immer noch schnell).
**Noch besser aber langsamer:** `ggml-medium.bin` (~1.5 GB).

Download:

```
https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin
```

Ablegen unter:

```
<Projektordner>/whisper-models/ggml-small.bin
```

Die Datei wird beim Build automatisch in den Output-Ordner kopiert (siehe `Speaky.csproj`).

Wenn du ein anderes Modell verwenden willst, passe den Pfad in `App.xaml.cs` an (`modelPath`).

### 3. Bauen und starten

```bash
dotnet build -c Release
dotnet run -c Release
```

Oder direkt die Debug-Version:

```bash
dotnet run
```

### 4. Mikrofon-Berechtigung

Beim ersten Start muss Windows die Mikrofon-Nutzung für Desktop-Apps erlauben:

**Einstellungen → Datenschutz & Sicherheit → Mikrofon → "Desktop-Apps Zugriff erlauben" = EIN**

Falls das Mikrofon blockiert ist, zeigt Speaky eine entsprechende Meldung.

## Bedienung

1. Cursor in das Zieleingabefeld setzen (Chat, Texteditor, Suche, …)
2. `Ctrl+Alt+S` drücken **oder** Start-Button klicken
3. Sprechen
4. `Ctrl+Alt+S` nochmal drücken **oder** Stop-Button klicken
5. Warten (1–3 s beim ersten Mal, <1 s danach) — Text wird eingefügt

## Architektur (Kurzfassung)

```
App.xaml.cs                 ← Composition Root, verdrahtet alle Services
├── MainWindow              ← kompakte GUI
├── TrayIconService         ← System-Tray
├── HotkeyService           ← Win32 RegisterHotKey
├── AudioRecorder           ← NAudio WaveInEvent
├── TranscriptionService    ← Whisper.net
├── TextInsertion           ← Win32 SendInput
├── ModeManager             ← Post-Processing pro Modus
└── Models/
    ├── RecordingState      ← Shared State GUI ↔ Hotkey
    └── RecordingMode
```

GUI-Button und Hotkey ändern denselben `RecordingState`. Dadurch sind Button-Label, Tray-Icon und Hotkey-Verhalten immer synchron — egal womit gestartet wurde.

## Bekannte Grenzen / Edge Cases

- **Clipboard-Einfügen verändert kurz die Zwischenablage**: Speaky sichert den bisherigen Inhalt und stellt ihn nach ~150 ms wieder her, d.h. in dem kurzen Fenster kann ein anderer Prozess, der genau dann aus der Clipboard liest, unseren Text sehen. In der Praxis unkritisch.
- **Clipboard-Einfügen setzt voraus**, dass das Zielfeld `Ctrl+V` unterstützt. Klassische Win32-/WPF-/Electron-/Browser-Eingabefelder: ja. Manche Spiele/Custom-Controls: nein.
- **Erstes Transkript dauert länger**, weil Whisper das Modell erst in den RAM lädt. Danach ist es schnell.
- **Hotkey-Konflikt**: Wenn eine andere App bereits `Ctrl+Alt+S` belegt, schlägt das `RegisterHotKey` fehl. GUI-Button bleibt nutzbar. Späterer Ausbau: Hotkey in Settings änderbar machen.
- **UAC-erhöhte Prozesse**: Wenn Speaky nicht-erhöht läuft, blockt UIPI das `SendInput` von Ctrl+V in erhöhte Fenster. Workaround: Speaky als Administrator starten.
- **Remote Desktop / Citrix**: Clipboard-Paste kommt je nach Konfiguration nicht durch.
- **Zu kurze Aufnahmen (< 0.5 s)** liefern oft leeren Text — Whisper braucht etwas Kontext.

## Hart erkaufte Lessons Learned

Damit das nicht noch einmal stundenlang debuggt werden muss, wenn jemand das Ding neu aufsetzt:

1. **Whisper.net + WPF = `WhisperFactory` nicht cachen + in `Task.Run` wrappen.**
   Frühere Version cachte die Factory als Feld (`_factory ??= ...`) und lief die `ProcessAsync`-Schleife direkt auf dem UI-Thread-SynchronizationContext. Resultat: Halluzinationen wie `"Hallo, ]]]]]]]]]]"` obwohl der exakt gleiche Code+Modell+WAV in einer Console-App sauber transkribiert. Behoben durch: frische Factory pro Aufruf + `Task.Run(...)` drum herum. Siehe `Services/TranscriptionService.cs`.

2. **Text-Einfügen darf NICHT zeichenweise per `SendInput` mit `KEYEVENTF_UNICODE` gemacht werden.**
   Die MVP-Variante hat jedes Zeichen einzeln getippt. Resultat: `"Hallo ppeake,...……………."` obwohl Whisper sauber `"Hallo Speake, dies ist ein Test."` geliefert hat. Ursachen: Modifier-Leaks vom gerade losgelassenen `Ctrl+Alt+S`, Win11-Notepad-Autocorrect wandelt `...` in `…`, Target-App verschluckt Zeichen bei 60+ Events in einem Rutsch. Behoben durch: Clipboard + Ctrl+V (mit Backup/Restore der Original-Clipboard). Siehe `Services/TextInsertion.cs`.

3. **Modifier-Drain vor dem `Ctrl+V`.**
   Bevor Speaky `Ctrl+V` schickt, wartet es per `GetAsyncKeyState` bis zu 400 ms, bis physisch kein `Ctrl`/`Alt`/`Shift`/`Win` mehr gedrückt ist. Sonst mischt sich das gerade losgelassene `Ctrl+Alt+S` mit unserem eigenen `Ctrl+V` zu einer unerwünschten Tastenkombination.

4. **Diagnose-Log.** `TranscriptionService` schreibt bei jedem Aufruf `whisper-debug.log` neben der `.exe`, inklusive jedem Whisper-Segment mit Zeitstempel. Unbezahlbar, um zu sehen, ob ein Bug in Whisper, im Mode-Processing oder in der Text-Insertion steckt. Ist in der .gitignore.

## Nächste Ausbau-Ideen nach dem MVP

1. **LLM-Post-Processing** für Ausschreib-Modus und einen zukünftigen "Diplomatie-Modus" (wütendes Diktat → höfliche Business-Sprache) via lokalem Ollama/LM Studio mit OpenAI-kompatibler REST-API. Aktuell geplant: Ollama + `Qwen3-8B-GGUF`. Soll als optional zuschaltbares Add-on gebaut werden, damit Speaky auch ohne laufendes LLM weiter funktioniert.
2. **Alternative Transkription** (Azure Speech, OpenAI Whisper API) — `TranscriptionService` hinter ein Interface legen.
3. **Konfigurierbarer Hotkey** in einem Settings-Fenster.
4. **Autostart mit Windows** (Registry-Eintrag oder Scheduled Task).
5. **Sprach-Feedback** (kurzer Ton beim Start/Stop) statt nur visuell.
6. **Transcript-Historie** (letzte 10 Einfügungen, Undo).
7. **Custom Wörterbuch** für Namen/Fachbegriffe, die Whisper falsch versteht (z.B. "Speaky" wird oft als "Spiky"/"Speakey"/"Speake" transkribiert).
8. **Whisper-Artefakt-Filter** für `[MUSIK]`/`[Applaus]`/`(Musik)` direkt nach der Transkription.

## Risiken (Ampel)

| Baustein | Risiko | Status |
|---|---|---|
| Audio-Aufnahme via NAudio | Mikrofon blockiert durch Windows-Datenschutz | 🟢 lösbar mit Einstellung |
| Whisper.net (lokal) | Modell-Download, erster Load dauert | 🟢 einmaliger Setup-Schritt |
| Whisper.net + WPF Kombination | SynchronizationContext kann Ausgabe korrumpieren | 🟢 gefixt via Task.Run + frische Factory |
| Globaler Hotkey | Konflikt mit anderer App | 🟡 Konfigurierbarkeit nötig |
| Clipboard-basiertes Text-Einfügen | UAC, Games, Remote Desktop, kurz verändertes Clipboard | 🟡 Edge-Cases bekannt |
| Topmost-Fenster ohne Fokus-Klau | WPF `Topmost=True` ist nicht 100% "non-activating" | 🟡 ggf. WS_EX_NOACTIVATE später |
| Ausschreib/Rage/Emoji via Deterministik | Wirkt noch nicht "natürlich" | 🟡 LLM-Ausbau geplant (Ollama + Qwen3-8B) |
