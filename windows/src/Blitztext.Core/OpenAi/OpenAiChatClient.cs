using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Blitztext.Core.Models;

namespace Blitztext.Core.OpenAi;

/// <summary>The two rewrite models, mirroring the macOS RewriteModel enum.</summary>
public enum RewriteModel
{
    FastEdit,
    RageMode,
}

internal static class RewriteModelExtensions
{
    public static string ApiName(this RewriteModel model) => model switch
    {
        RewriteModel.FastEdit => "gpt-4o-mini",
        RewriteModel.RageMode => "gpt-4o",
        _ => "gpt-4o-mini",
    };
}

/// <summary>
/// Calls the OpenAI Chat Completions API. Faithful port of the macOS LLMService:
/// same endpoint, payload shape, temperatures and prompt wiring.
/// </summary>
public sealed class OpenAiChatClient
{
    private const string ChatCompletionsUrl = "https://api.openai.com/v1/chat/completions";

    private readonly HttpClient _http;

    public OpenAiChatClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
    }

    public Task<string> ImproveAsync(
        string apiKey, string text, TextImprovementSettings settings,
        RewriteModel model = RewriteModel.FastEdit, CancellationToken ct = default)
        => CompleteAsync(apiKey, text, PromptBuilderProxy.Improve(settings), model, 0.3, ct);

    public Task<string> DampfAblassenAsync(
        string apiKey, string text, string systemPrompt,
        RewriteModel model = RewriteModel.RageMode, CancellationToken ct = default)
        => CompleteAsync(apiKey, text, systemPrompt, model, 0.4, ct);

    public Task<string> AddEmojisAsync(
        string apiKey, string text, EmojiTextSettings settings,
        RewriteModel model = RewriteModel.FastEdit, CancellationToken ct = default)
        => CompleteAsync(apiKey, text, PromptBuilderProxy.Emoji(settings), model, 0.3, ct);

    private async Task<string> CompleteAsync(
        string apiKey, string text, string systemPrompt, RewriteModel model, double temperature, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw OpenAiException.NotConfigured();
        }

        var payload = new ChatRequest
        {
            Model = model.ApiName(),
            Messages = new[]
            {
                new ChatMessage { Role = "system", Content = systemPrompt },
                new ChatMessage { Role = "user", Content = text },
            },
            Temperature = temperature,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, ChatCompletionsUrl);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
        request.Content = JsonContent.Create(payload, options: SerializerOptions);

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
            throw OpenAiException.Api(ExtractError(body) ?? $"Status {(int)response.StatusCode}");
        }

        ChatResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ChatResponse>(body, SerializerOptions);
        }
        catch
        {
            throw OpenAiException.NoContent();
        }

        string? content = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw OpenAiException.NoContent();
        }

        return content.Trim();
    }

    internal static string? ExtractError(string body)
    {
        try
        {
            var err = JsonSerializer.Deserialize<OpenAiErrorEnvelope>(body, SerializerOptions);
            return err?.Error?.Message;
        }
        catch
        {
            return null;
        }
    }

    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed class ChatRequest
    {
        public string Model { get; set; } = string.Empty;
        public ChatMessage[] Messages { get; set; } = Array.Empty<ChatMessage>();
        public double Temperature { get; set; }
    }

    private sealed class ChatMessage
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    private sealed class ChatResponse
    {
        [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; }

        public sealed class Choice
        {
            [JsonPropertyName("message")] public Msg? Message { get; set; }
        }

        public sealed class Msg
        {
            [JsonPropertyName("content")] public string? Content { get; set; }
        }
    }
}

internal sealed class OpenAiErrorEnvelope
{
    [JsonPropertyName("error")] public ApiError? Error { get; set; }

    internal sealed class ApiError
    {
        [JsonPropertyName("message")] public string? Message { get; set; }
    }
}

/// <summary>Bridges the OpenAi namespace to the Workflows.PromptBuilder without a circular using.</summary>
internal static class PromptBuilderProxy
{
    public static string Improve(TextImprovementSettings s) => Workflows.PromptBuilder.BuildImprovementSystemPrompt(s);
    public static string Emoji(EmojiTextSettings s) => Workflows.PromptBuilder.BuildEmojiSystemPrompt(s.EmojiDensity);
}
