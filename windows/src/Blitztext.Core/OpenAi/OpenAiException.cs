namespace Blitztext.Core.OpenAi;

public enum OpenAiErrorKind
{
    NotConfigured,
    Network,
    Api,
    NoContent,
    NoFile,
}

/// <summary>
/// Unified error type for the OpenAI clients. The German messages mirror the macOS
/// LLMError / TranscriptionError descriptions shown to the user.
/// </summary>
public sealed class OpenAiException : Exception
{
    public OpenAiErrorKind Kind { get; }

    public OpenAiException(OpenAiErrorKind kind, string message) : base(message)
    {
        Kind = kind;
    }

    public static OpenAiException NotConfigured() =>
        new(OpenAiErrorKind.NotConfigured, "OpenAI API Key fehlt. Bitte in den Einstellungen hinterlegen.");

    public static OpenAiException Network(string detail) =>
        new(OpenAiErrorKind.Network, $"Verbindungsproblem: {detail}");

    public static OpenAiException Api(string detail) =>
        new(OpenAiErrorKind.Api, $"Fehler von OpenAI: {detail}");

    public static OpenAiException NoContent() =>
        new(OpenAiErrorKind.NoContent, "Keine Antwort erhalten. Bitte nochmal versuchen.");

    public static OpenAiException NoFile() =>
        new(OpenAiErrorKind.NoFile, "Keine Audio-Datei gefunden");
}
