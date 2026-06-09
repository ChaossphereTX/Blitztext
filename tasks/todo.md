# Blitztext – Windows-Portierung (WPF / .NET 8)

## Ausgangslage
Original ist eine native **macOS**-App (Swift/SwiftUI/AppKit, CoreML/WhisperKit, Keychain,
Accessibility, CGEvent, AVFoundation, Xcode-Build). Keine plattformneutrale Schicht vorhanden →
echte Windows-Lauffähigkeit erfordert Neuimplementierung. Entscheidung des Nutzers:
**WPF (.NET 8)**, **Online + Lokal (Whisper.net) sofort**.

## Architektur
- `windows/src/Blitztext.Core` (net8.0, OS-unabhängig): OpenAI-Clients, Prompts, Settings, Quality, lokale Transkription (Whisper.net)
- `windows/src/Blitztext.App` (net8.0-windows, WPF + WinForms-Tray): Audio (NAudio), Hotkeys, Auto-Paste, Credential-Store, UI

## Aufgaben
- [x] Codebase vollständig analysiert
- [x] Toolchain geprüft (.NET 8 SDK vorhanden, kein VS/Workload)
- [x] Stack-/Scope-Entscheidung mit Nutzer geklärt
- [x] Core: Models / Settings
- [x] Core: WorkflowType + PromptBuilder + TranscriptionQuality
- [x] Core: OpenAI Transcription + Chat Clients
- [x] Core: Lokale Transkription (Whisper.net) + Modellkatalog/Download
- [x] Core: AppPaths + SettingsStore
- [x] App: Platform-Services (Credential, Audio, Hotkeys, AutoPaste, Startup)
- [x] App: AppController (Orchestrierung ~ AppState)
- [x] App: UI (Tray, Popover, Settings, Onboarding)
- [x] Build (dotnet restore/build) – Debug + Release, 0 Fehler/0 Warnungen
- [x] Smoke-Test (Start/Tray/Hook stabil, Responding=True)
- [x] Packaging-Test (self-contained Single-File publish win-x64)
- [x] README-Windows + Doku + .gitignore
- [x] Abschlussbericht

## Review
- Neuimplementierung statt Port: macOS-App ist 100 % native (Swift/SwiftUI/CoreML/Keychain/CGEvent),
  keine portierbare Schicht. Saubere Lösung: gemeinsame OS-unabhängige Core-Lib + Windows-Platform-Layer.
- Geschäftslogik (OpenAI-Endpunkte, Payloads, Prompts, Quality-Heuristik) 1:1 portiert → gleiches Verhalten.
- Build/Run/Publish lokal verifiziert. E2E (Mikro→OpenAI/lokal→Auto-Paste) erfordert API-Key bzw.
  Modell-Download und interaktive Nutzung; manuell durchzuführen (siehe windows/README.md).
- Offen/Risiko: Auto-Paste in elevated Zielfenstern (UIPI); CPU-only lokale Transkription;
  globale Strg+Umschalt-Hotkeys systemweit belegt.
