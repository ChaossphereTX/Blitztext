# Blitztext (Windows)

Blitztext ist eine **Windows-App für Sprache-zu-Text**: Hotkey drücken, sprechen, der Text wird
transkribiert, optional umgeschrieben und automatisch in das gerade aktive Fenster eingefügt.

Die App lebt im **System-Tray** und arbeitet wahlweise **lokal/offline** (whisper.cpp) oder
**online** über die OpenAI-API.

> Diese Version ist **Windows-only** (WPF / .NET 8). Eine frühere macOS-Variante wurde entfernt.

## Workflows

| Workflow | Hotkey | Beschreibung |
|---|---|---|
| **Blitztext** | `Strg+Umschalt+D` | Sprache → Text (lokal oder OpenAI Whisper) |
| **Blitztext+** | `Strg+Umschalt+E` | Transkript → sauber umgeschrieben (GPT-4o-mini) |
| **Blitztext $%&!** | `Strg+Umschalt+R` | Frust-Diktat → ruhige Nachricht (GPT-4o) |
| **Blitztext :)** | `Strg+Umschalt+J` | Transkript + passende Emojis |
| **Blitztext Lokal** | `Strg+Umschalt+L` | Transkription rein offline |
| Abbrechen | `Esc` | laufende Aufnahme/Verarbeitung stoppen |

## Schnellstart (Entwicklung)

```powershell
cd windows
dotnet run --project src/Blitztext.App
```

## Build, Installer & Verteilung

Alles Weitere – Voraussetzungen, Build, Packaging, **Installer (`BlitztextSetup.exe`)**,
Rollout auf Firmenrechner/-server, Architektur, Diagnose-Logging und bekannte Einschränkungen –
steht in **[windows/README.md](windows/README.md)**.

## Lizenz

Code unter MIT-Lizenz, siehe [LICENSE](LICENSE).
