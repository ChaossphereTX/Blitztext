using System.Text.Json.Serialization;

namespace Blitztext.Core.Models;

/// <summary>How the global hotkeys behave. Mirrors the macOS HotkeyMode.</summary>
public enum HotkeyMode
{
    /// <summary>Keys held = recording, release = stop (push to talk).</summary>
    Hold,

    /// <summary>Press once = start, press again / Escape = stop.</summary>
    Toggle,
}

public enum TextTone
{
    Formal,
    Neutral,
    Casual,
}

public enum EmojiDensity
{
    Wenig,
    Mittel,
    Viel,
}

/// <summary>General application settings (1:1 port of the macOS AppSettings).</summary>
public sealed class AppSettings
{
    public HotkeyMode HotkeyMode { get; set; } = HotkeyMode.Hold;
    public bool HasSeenOnboarding { get; set; }
    public bool SecureLocalModeEnabled { get; set; }
    public string SelectedLocalTranscriptionModelName { get; set; } = string.Empty;
    public bool HasAutoSelectedFastLocalModel { get; set; }

    /// <summary>Run Blitztext automatically at Windows login. (macOS: LaunchAtLogin)</summary>
    public bool LaunchAtLogin { get; set; }

    /// <summary>
    /// URL of the update manifest (latest.json) on the company server. When set, the app
    /// checks for newer versions on startup. Empty = updater disabled.
    /// </summary>
    public string UpdateFeedUrl { get; set; } = string.Empty;
}

public sealed class TranscriptionSettings
{
    public string Language { get; set; } = "de";
}

public sealed class TextImprovementSettings
{
    public string SystemPrompt { get; set; } = string.Empty;
    public List<string> CustomTerms { get; set; } = new();
    public string Context { get; set; } = string.Empty;
    public TextTone Tone { get; set; } = TextTone.Neutral;
    public string CustomName { get; set; } = string.Empty;
}

public sealed class DampfAblassenSettings
{
    public string SystemPrompt { get; set; } =
        "Du erhältst ein emotional gesprochenes Transkript. Erkenne zuerst das eigentliche Ziel, " +
        "Anliegen und den wahren Frust der Person. Formuliere daraus eine klare, respektvolle und " +
        "wirksame Nachricht, mit der die Person ihr Ziel eher erreicht. Bewahre relevante Fakten, " +
        "konkrete Probleme, Grenzen, Erwartungen und die nötige Dringlichkeit. Entferne Beleidigungen, " +
        "Drohungen, Sarkasmus, Unterstellungen und unnötige Eskalation. Wenn mehrere Vorwürfe genannt " +
        "werden, verdichte sie auf die entscheidenden Kernpunkte. Der Ton soll ruhig, menschlich, " +
        "bestimmt und lösungsorientiert sein. Gib NUR die fertige Nachricht zurück.";

    public string CustomName { get; set; } = string.Empty;
}

public sealed class EmojiTextSettings
{
    public EmojiDensity EmojiDensity { get; set; } = EmojiDensity.Mittel;
    public string CustomName { get; set; } = string.Empty;
}

/// <summary>The on-disk settings container (settings.json).</summary>
public sealed class SettingsContainer
{
    public AppSettings App { get; set; } = new();
    public TranscriptionSettings Transcription { get; set; } = new();
    public TextImprovementSettings TextImprovement { get; set; } = new();
    public DampfAblassenSettings DampfAblassen { get; set; } = new();
    public EmojiTextSettings EmojiText { get; set; } = new();
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SettingsContainer))]
public partial class SettingsJsonContext : JsonSerializerContext
{
}
