namespace SensorReportBot.Infrastructure.Services;

using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SensorReportBot.Application.Interfaces;
using SensorReportBot.Infrastructure.Configuration;

/// <summary>
/// Infrastructure service for executing OpenAI-compatible HTTP inference queries against
/// Ollama or external LLM gateways, parsing JSON outputs, and handling edge model fallbacks.
/// </summary>
public class LlmService : ILlmService
{
    private readonly HttpClient _httpClient;
    private readonly LlmOptions _options;
    private readonly ILogger<LlmService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LlmService"/> class.
    /// </summary>
    /// <param name="httpClient">Configured HTTP client instance.</param>
    /// <param name="options">Inference options specifying endpoint, model, and hyperparameters.</param>
    /// <param name="logger">Logger instance.</param>
    public LlmService(HttpClient httpClient, IOptions<LlmOptions> options, ILogger<LlmService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Sends a structured chat completion request to the LLM backend and deserializes the JSON response.
    /// </summary>
    /// <typeparam name="TResponse">Expected target response type.</typeparam>
    /// <param name="systemPrompt">Grounding instructions defining taxonomy, schema, and behavioral boundaries.</param>
    /// <param name="userMessage">Context string containing state, user query, and temporal anchors.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Deserialized response object, or default/null if generation was unparseable.</returns>
    public async Task<TResponse?> GenerateJsonResponseAsync<TResponse>(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        _logger.LogDebug("\n==================== [LLM REQUEST LOG] ====================\n[SYSTEM PROMPT]:\n{SystemPrompt}\n-----------------------------------------------------------\n[USER PROMPT]:\n{UserMessage}\n===========================================================", systemPrompt, userMessage);

        var payload = new
        {
            model = _options.Model,
            temperature = _options.Temperature,
            max_tokens = _options.MaxTokens,
            response_format = new { type = "json_object" },
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            }
        };

        string endpointUrl = _options.Endpoint;
        if (!endpointUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            endpointUrl = endpointUrl.TrimEnd('/') + "/chat/completions";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpointUrl);
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }
        request.Content = JsonContent.Create(payload);

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            string errorBody = await response.Content.ReadAsStringAsync(ct);

            // Groq or local Ollama may return HTTP 400 when empty generation occurs.
            if ((int)response.StatusCode == 400 && errorBody.Contains("json_validate_failed"))
            {
                _logger.LogWarning("LLM returned json_validate_failed (empty generation). Returning null for graceful fallback. Body: {Body}", errorBody);
                return default;
            }

            _logger.LogError("LLM request failed with status {StatusCode}: {ResponseBody}", response.StatusCode, errorBody);
            response.EnsureSuccessStatusCode();
        }

        var chatResponse = await response.Content.ReadFromJsonAsync<ChatCompletionsResponse>(cancellationToken: ct);
        string? rawContent = chatResponse?.Choices?.FirstOrDefault()?.Message?.Content;

        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return default;
        }

        string cleanJson = CleanJson(rawContent);
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };
            return JsonSerializer.Deserialize<TResponse>(cleanJson, options);
        }
        catch (JsonException jsonEx)
        {
            _logger.LogWarning(jsonEx, "Failed to deserialize JSON response from LLM. Raw content: {RawContent}", rawContent);
            // Attempt emergency repair: extract object between first '{' and last '}'
            int firstBrace = rawContent.IndexOf('{');
            int lastBrace = rawContent.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                try
                {
                    string repairedJson = rawContent.Substring(firstBrace, lastBrace - firstBrace + 1);
                    return JsonSerializer.Deserialize<TResponse>(repairedJson, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        AllowTrailingCommas = true
                    });
                }
                catch
                {
                    return default;
                }
            }
            return default;
        }
    }

    /// <summary>
    /// Strips markdown code blocks, backticks, and whitespace wrapping from LLM generated JSON strings.
    /// </summary>
    /// <param name="raw">Raw string output from language model.</param>
    /// <returns>Cleaned JSON substring.</returns>
    private static string CleanJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "{}";
        }

        string trimmed = raw.Trim();
        if (trimmed.StartsWith("```"))
        {
            int firstLineEnd = trimmed.IndexOf('\n');
            if (firstLineEnd >= 0)
            {
                trimmed = trimmed[(firstLineEnd + 1)..];
            }

            if (trimmed.EndsWith("```"))
            {
                trimmed = trimmed[..^3];
            }
        }

        int start = trimmed.IndexOf('{');
        int end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            trimmed = trimmed.Substring(start, end - start + 1);
        }

        return trimmed.Trim();
    }
}

/// <summary>
/// OpenAI-compatible chat completion response payload model.
/// </summary>
public class ChatCompletionsResponse
{
    /// <summary>
    /// Gets or sets the list of completion choices returned by the model.
    /// </summary>
    [JsonPropertyName("choices")]
    public ChatChoice[]? Choices { get; set; }
}

/// <summary>
/// Choice item in a completion response.
/// </summary>
public class ChatChoice
{
    /// <summary>
    /// Gets or sets the message generated by the model.
    /// </summary>
    [JsonPropertyName("message")]
    public ChatMessageContent? Message { get; set; }
}

/// <summary>
/// Content payload for a chat completion message.
/// </summary>
public class ChatMessageContent
{
    /// <summary>
    /// Gets or sets the text content.
    /// </summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}
