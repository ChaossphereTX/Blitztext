using System.Net.Http.Headers;

namespace Blitztext.Core.OpenAi;

/// <summary>
/// Calls the OpenAI Audio Transcriptions API (whisper-1). Faithful port of the macOS
/// TranscriptionService: same endpoint, multipart fields (model, response_format=text,
/// optional prompt and language) and error handling.
/// </summary>
public sealed class OpenAiTranscriptionClient
{
    private const string RemoteModel = "whisper-1";
    private const string TranscriptionsUrl = "https://api.openai.com/v1/audio/transcriptions";

    private readonly HttpClient _http;

    public OpenAiTranscriptionClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task<string> TranscribeAsync(
        string apiKey,
        string audioFilePath,
        IReadOnlyList<string>? customTerms = null,
        string? language = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw OpenAiException.NotConfigured();
        }

        if (!File.Exists(audioFilePath))
        {
            throw OpenAiException.NoFile();
        }

        byte[] audioData = await File.ReadAllBytesAsync(audioFilePath, ct).ConfigureAwait(false);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(audioData);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "file", "audio.wav");
        content.Add(new StringContent(RemoteModel), "model");
        content.Add(new StringContent("text"), "response_format");

        if (customTerms is { Count: > 0 })
        {
            content.Add(new StringContent("Eigennamen und Begriffe: " + string.Join(", ", customTerms)), "prompt");
        }

        string? trimmedLanguage = language?.Trim();
        if (!string.IsNullOrEmpty(trimmedLanguage))
        {
            content.Add(new StringContent(trimmedLanguage), "language");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, TranscriptionsUrl) { Content = content };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
        request.Headers.TryAddWithoutValidation("Accept", "text/plain, application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw OpenAiException.Network(ex.Message);
        }

        string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw OpenAiException.Api(OpenAiChatClient.ExtractError(body) ?? $"Status {(int)response.StatusCode}");
        }

        string text = body.Trim();
        if (string.IsNullOrEmpty(text))
        {
            throw OpenAiException.Api("Transkription fehlgeschlagen");
        }

        return text;
    }
}
