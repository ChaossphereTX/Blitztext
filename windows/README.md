# Blitztext für Windows (WPF / .NET 8)

Dies ist die **Windows-Portierung** der ursprünglich nur für macOS gebauten Blitztext-App.
Die macOS-App ist nativ in Swift/SwiftUI mit Apple-exklusiven Frameworks (AppKit, CoreML/WhisperKit,
Keychain, AVFoundation, CGEvent) geschrieben und lässt sich nicht „umbauen", sondern wurde hier
funktionsgleich in einem Windows-fähigen Stack neu implementiert.

Die App ist eine **Tray-/Hilfsanwendung**: Hotkey drücken, sprechen, Text wird transkribiert,
optional umgeschrieben und per simuliertem `Strg+V` in das zuvor aktive Fenster eingefügt.

## Funktionen

| Workflow | Hotkey (Windows) | Beschreibung |
|---|---|---|
| **Blitztext** | `Strg+Umschalt+D` | Sprache → Text (OpenAI Whisper oder lokal) |
| **Blitztext+** | `Strg+Umschalt+E` | Transkript → sauber umgeschriebener Text (GPT-4o-mini) |
| **Blitztext $%&!** | `Strg+Umschalt+R` | Frust-Diktat → ruhige, sachliche Nachricht (GPT-4o) |
| **Blitztext :)** | `Strg+Umschalt+J` | Transkript + passende Emojis |
| **Blitztext Lokal** | `Strg+Umschalt+L` | Transkription rein offline (whisper.cpp) |
| Abbrechen | `Esc` | Laufende Aufnahme/Verarbeitung abbrechen |

> Der macOS-`fn`-Modifier existiert auf Windows nicht – die Kürzel sind auf `Strg+Umschalt`-Chords
> abgebildet. Modus **Halten** (Push-to-talk) oder **Drücken** (Umschalten) ist in den Einstellungen wählbar.

## Architektur

```
windows/
  Blitztext.sln
  src/
    Blitztext.Core/      net8.0 – plattformunabhängige Logik (wiederverwendbar)
      OpenAi/            Whisper- & Chat-Client (gleiche Endpunkte/Payloads wie macOS)
      Workflows/         WorkflowType, PromptBuilder (identische Prompts), Quality-Heuristik
      Local/             whisper.cpp-Transkription + Modellkatalog/Download
      Models/, Settings/ Settings-Datenmodell + JSON-Persistenz
    Blitztext.App/       net8.0-windows – WPF-UI + Windows-Plattformschicht
      Platform/          CredentialStore, AudioRecorder (NAudio), GlobalHotkeys,
                         AutoPaste (SendInput), Startup (Autostart)
      UI/                Tray-Icon, Popover-Fenster, Einstellungen
```

**Plattform-Abbildung macOS → Windows**

| macOS | Windows |
|---|---|
| SwiftUI / AppKit | WPF (+ WinForms `NotifyIcon` für das Tray) |
| WhisperKit / CoreML | whisper.cpp via **Whisper.net** (GGML-Modelle) |
| macOS Keychain (`Security`) | **Windows Credential Manager** (`advapi32` Cred*) |
| `CGEvent` Cmd+V | `SendInput` Strg+V + `SetForegroundWindow` |
| `NSEvent`-Monitore | Low-Level-Keyboard-Hook (`WH_KEYBOARD_LL`) |
| `AVAudioRecorder` | **NAudio** (WASAPI) → 16 kHz Mono WAV |
| `~/Library/Application Support` | `%APPDATA%\Blitztext` |
| LaunchAtLogin (SMAppService) | HKCU `…\Run`-Eintrag |

## Voraussetzungen

- Windows 10/11 (x64)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (`dotnet --version` ≥ 8.0)
- Mikrofon (Windows-Datenschutz: Mikrofonzugriff für Desktop-Apps erlauben)
- Für Online-Workflows: eigener **OpenAI API Key** mit Zugriff auf `whisper-1`, `gpt-4o-mini`, `gpt-4o`
- Für den lokalen Modus: ein GGML-Modell – wird **in der App** per Klick heruntergeladen
  (gespeichert unter `%APPDATA%\Blitztext\models\whisper`)

Es wird **kein** Visual Studio benötigt – alles läuft über die `dotnet`-CLI.

## Development starten

```powershell
cd windows
dotnet restore
dotnet run --project src/Blitztext.App
```

Die App erscheint als Icon im Infobereich (System Tray), nicht als Fenster.
Linksklick auf das Tray-Icon öffnet das Popover, Rechtsklick das Menü.

## Build

```powershell
cd windows
dotnet build Blitztext.sln -c Release
# Ergebnis: src/Blitztext.App/bin/Release/net8.0-windows/Blitztext.exe
```

## Packaging / Distribution

Self-contained Einzelordner (kein installiertes .NET nötig):

```powershell
cd windows
dotnet publish src/Blitztext.App/Blitztext.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
# Ergebnis-Ordner: src/Blitztext.App/bin/Release/net8.0-windows/win-x64/publish/
```

Verteilt wird der **gesamte `publish/`-Ordner**: `Blitztext.exe` plus der `runtimes/`-Ordner mit
den nativen whisper.cpp-Bibliotheken (diese werden bewusst neben der Exe gehalten, damit Whisper.net
sie zur Laufzeit findet). Für eine signierte Verteilung
müsste – analog zur macOS-Notarisierung – ein Windows-Code-Signing-Zertifikat (`signtool`)
ergänzt werden; das ist hier (wie im macOS-Original) bewusst nicht enthalten.

## Erste Schritte in der App

1. Tray-Icon anklicken → **Einstellungen**.
2. Tab **Zugang**: OpenAI API Key eintragen und speichern (landet im Windows Credential Manager).
   *oder* Tab **Anpassen**: ein lokales Modell auswählen und **installieren** (Download), dann
   **Sicherer Lokaler Modus** aktivieren – dann ist kein API Key für die Transkription nötig.
3. Cursor in ein beliebiges Textfeld setzen, Hotkey halten/drücken, sprechen, loslassen → Text wird eingefügt.

## Eigennamen / Fachbegriffe

Unter *Einstellungen → Anpassen → Eigennamen* hinterlegte Begriffe (z. B. `Claude`,
`Large v3 Turbo`, Personennamen) werden als Erkennungs-Hinweis mitgegeben – **sowohl online**
(OpenAI-`prompt`) **als auch offline** (Whisper.net Initial-Prompt). Das verbessert die
Schreibweise von Produkt-/Eigennamen, die das Modell sonst phonetisch errät.

## Installer / Verteilung (BlitztextSetup.exe)

Für das interne Rollout gibt es einen **Inno-Setup-Installer** (`installer/Blitztext.iss`):
per-user (kein Admin), self-contained (kein .NET nötig), Modelle werden nicht gebündelt.

**Installer bauen:**
```powershell
# einmalig: Compiler installieren
winget install JRSoftware.InnoSetup
# bauen (publish + kompilieren)
windows\installer\build-installer.ps1
# Ergebnis: windows\installer\Output\BlitztextSetup.exe  (~52 MB)
```

**Manuelle Installation:** `BlitztextSetup.exe` doppelklicken → installiert nach
`%LOCALAPPDATA%\Programs\Blitztext`, legt Start-Menü- (und optional Desktop-)Verknüpfung an,
registriert sich in „Apps & Features" inkl. Deinstallation. Optional „mit Windows starten".

**Automatisiertes Rollout vom Firmen-Server (still):**
```powershell
BlitztextSetup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /TASKS=desktopicon
```
Eignet sich für Intune (Win32-App), GPO-Login-Skripte oder Software-Verteilung. Da per-user
installiert wird, im **Benutzerkontext** ausrollen. (Für eine maschinenweite Installation nach
`Program Files` ließe sich das Skript auf `PrivilegesRequired=admin` + `{autopf}` umstellen.)

**SmartScreen:** Die EXE ist unsigniert → bei manueller Installation erscheint einmal
„Unbekannter Herausgeber". Für signierte Pakete ein Code-Signing-Zertifikat besorgen; dann
werden Setup und `Blitztext.exe` mit `signtool` signiert.

## Updates verteilen (automatisch von GitHub)

Die App aktualisiert sich **vollautomatisch** – **die Clients konfigurieren nichts**. Die
Update-Quelle (das öffentliche GitHub-Release-Repo) ist fest eingebaut und **in der Oberfläche
nicht sichtbar**: der Nutzer sieht nur „Update auf Version X verfügbar", keine URLs/Links.

**Ablauf in der App:** Beim Start (und über Tray → „Nach Updates suchen …") liest die App das
neueste GitHub-Release (`/repos/<owner>/<repo>/releases/latest`), vergleicht die Versionen,
fragt bei einer neueren kurz nach, lädt die signierte `BlitztextSetup.exe` und installiert sie
**still** (`/VERYSILENT /RELAUNCH`); danach startet Blitztext neu.

**Update veröffentlichen:** einfach ein höheres Versions-Tag pushen → CI baut, signiert und legt
das Release an (siehe unten). Mehr ist nicht nötig – kein Anfassen der Client-Rechner.

> Das Release-Repo muss **public** sein, damit der Download ohne Token funktioniert. Quelltext
> enthält keine Secrets. Optionaler Sonderfall: eine eigene `latest.json` per
> `BLITZTEXT_UPDATE_URL`/`settings.updateFeedUrl` übersteuert die GitHub-Quelle (z. B. offline/Intranet).

## Code-Signing (intern, self-signed)

Damit auf Firmenrechnern keine „Unbekannter-Herausgeber"-Warnung erscheint, werden `Blitztext.exe`
und `BlitztextSetup.exe` mit einem **eigenen Code-Signing-Zertifikat** signiert (kostenlos, da rein intern).

```powershell
windows\signing\new-cert.ps1     # einmalig: Zertifikat erzeugen + Blitztext-CodeSigning.cer exportieren
windows\installer\build-installer.ps1   # baut + signiert exe und Setup automatisch
```

**Auf allen Firmenrechnern vertrauen (zentral per GPO/Intune):** das exportierte
`windows\signing\Blitztext-CodeSigning.cer` in diese Zertifikatspeicher verteilen:
- **Vertrauenswürdige Stammzertifizierungsstellen** (Trusted Root) und
- **Vertrauenswürdige Herausgeber** (Trusted Publishers).

GPO: *Computerkonfiguration → Richtlinien → Windows-Einstellungen → Sicherheitseinstellungen →
Richtlinien für öffentliche Schlüssel* → Zertifikat in beide Speicher importieren. (Intune: ein
„Trusted Certificate"-Profil je Speicher.) Danach gilt die Signatur fleet-weit als vertrauenswürdig
– keine SmartScreen-/Herausgeber-Warnung.

> Für Verteilung **außerhalb** der Firma bräuchtet ihr stattdessen ein Zertifikat einer
> öffentlichen CA (Sectigo/DigiCert/GlobalSign/SSL.com/Certum; Schlüssel auf Token/Cloud-HSM,
> EV = sofortiges SmartScreen-Vertrauen). Der Build signiert dann analog mit diesem Zertifikat.

## Releases über GitHub Actions (CI – nur intern)

GitHub dient ausschließlich als **interne Build-/Ablage-Maschine** für das Team. **Die App
referenziert GitHub nirgends** – Endnutzer sehen davon nichts (der Updater zeigt auf den
Firmen-Server, siehe oben).

**Einmalig:** Signatur-Zertifikat als GitHub-Secrets hinterlegen (lokal ausführen):
```powershell
windows\signing\new-cert.ps1          # falls noch nicht geschehen
windows\signing\set-ci-secrets.ps1    # legt CODESIGN_PFX_BASE64 + CODESIGN_PFX_PASSWORD an
```

**Release bauen:** ein Versions-Tag pushen – der Workflow `.github/workflows/release.yml`
baut, **signiert** und hängt `BlitztextSetup.exe` an ein GitHub-Release:
```powershell
git tag v1.6.0; git push origin v1.6.0
```
(Ohne Tag lässt sich der Workflow auch manuell starten – „Run workflow" / `gh workflow run release.yml` –
und legt das Setup als Build-Artefakt ab.)

**An die Nutzer ausliefern (ohne dass GitHub sichtbar wird):** Die signierte `BlitztextSetup.exe`
aus dem Release auf den **Firmen-Server** laden und dort `latest.json` aktualisieren. Die Clients
aktualisieren sich ausschließlich von dort – GitHub taucht in der App nie auf.

## Diagnose / Protokoll (für Admins)

Die App schreibt ein dauerhaftes Protokoll für die Fehlersuche:

- **Pfad:** `%APPDATA%\Blitztext\blitztext.log` (pro Benutzer, `…\AppData\Roaming\Blitztext`)
- **Schnellzugriff:** Tray-Icon → Rechtsklick → **„Protokoll anzeigen"**
- **Inhalt:** App-Start + Version, gewählter Transkriptionspfad (lokal/online + Modell),
  Fehler und unbehandelte Ausnahmen (mit Stacktrace), Auto-Paste-Diagnose.
- **Rotation:** ab ~1 MB wird automatisch nach `blitztext.log.old` rotiert (kein unbegrenztes Wachstum).

Bei einem Fehlerbericht aus dem internen Rollout genügt es, `blitztext.log` (und ggf. `.old`)
aus dem Roaming-Profil des betroffenen Nutzers einzusammeln.

## Bekannte Einschränkungen

- **Auto-Paste** erfordert, dass das Zielfenster den Fokus annehmen kann. Bei als Administrator
  laufenden Zielprogrammen kann das Einfügen blockiert sein (UIPI); der Text liegt dann als
  Fallback in der Zwischenablage (`Strg+V` manuell).
- Globale Hotkeys belegen die `Strg+Umschalt`-Chords systemweit, solange Blitztext läuft.
- Lokale Transkription läuft per Whisper.net auf der **CPU**. Größere Modelle (Large) sind
  entsprechend langsamer; **Small** ist als schneller Standard voreingestellt.
- Kein automatischer Update-Feed (wie im macOS-Original): neue Versionen selbst bauen.
