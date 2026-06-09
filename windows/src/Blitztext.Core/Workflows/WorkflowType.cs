namespace Blitztext.Core.Workflows;

/// <summary>The available Blitztext workflows. Mirrors the macOS WorkflowType.</summary>
public enum WorkflowType
{
    Transcription,
    LocalTranscription,
    TextImprover,
    DampfAblassen,
    EmojiText,
}

public static class WorkflowTypeInfo
{
    /// <summary>Workflows shown in the main menu (local transcription is reachable via hotkey only).</summary>
    public static readonly WorkflowType[] MainMenuCases =
    {
        WorkflowType.Transcription,
        WorkflowType.TextImprover,
        WorkflowType.DampfAblassen,
        WorkflowType.EmojiText,
    };

    public static string DisplayName(this WorkflowType type) => type switch
    {
        WorkflowType.Transcription => "Blitztext",
        WorkflowType.LocalTranscription => "Blitztext Lokal",
        WorkflowType.TextImprover => "Blitztext+",
        WorkflowType.DampfAblassen => "Blitztext $%&!",
        WorkflowType.EmojiText => "Blitztext :)",
        _ => "Blitztext",
    };

    public static string Subtitle(this WorkflowType type) => type switch
    {
        WorkflowType.Transcription => "Sprache rein. Text raus.",
        WorkflowType.LocalTranscription => "Nur lokal. Kein Server.",
        WorkflowType.TextImprover => "Geschrieben sprechen.",
        WorkflowType.DampfAblassen => "Frust rein. Entspannt raus.",
        WorkflowType.EmojiText => "Text rein. Emojis dazu.",
        _ => string.Empty,
    };

    /// <summary>
    /// Windows hotkey labels. Only the transcription workflow has a global hotkey
    /// (Ctrl+Shift held alone); the other workflows are started from the tray popover.
    /// </summary>
    public static string HotkeyLabel(this WorkflowType type) => type switch
    {
        WorkflowType.Transcription => "Strg + Umschalt",
        _ => string.Empty,
    };
}
