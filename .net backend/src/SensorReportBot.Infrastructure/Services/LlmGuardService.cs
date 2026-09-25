namespace SensorReportBot.Infrastructure.Services;

using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SensorReportBot.Application.Interfaces;
using SensorReportBot.Domain.Enums;

public class LlmGuardService : IGuardService
{
    private readonly ILlmService _llmService;
    private readonly IPromptProvider _promptProvider;
    private readonly ILogger<LlmGuardService> _logger;

    public LlmGuardService(
        ILlmService llmService,
        IPromptProvider promptProvider,
        ILogger<LlmGuardService> logger)
    {
        _llmService = llmService;
        _promptProvider = promptProvider;
        _logger = logger;
    }

    public async Task<GuardDecision> CheckAsync(string message, string? historyContext = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return GuardDecision.OffTopic;
        }

        try
        {
            string systemPrompt = await _promptProvider.GetPromptAsync("GuardPrompt", ct);
            string userContext = !string.IsNullOrWhiteSpace(historyContext)
                ? $"[RECENT DIALOGUE CONTEXT]:\n{historyContext}\n\n[USER INPUT TO CLASSIFY]:\n{message}"
                : message;

            var result = await _llmService.GenerateJsonResponseAsync<GuardLlmResult>(systemPrompt, userContext, ct);

            if (result != null)
            {
                _logger.LogInformation("LLM Guard Decision: IsOnTopic={IsOnTopic}, Reason='{Reason}'", result.IsOnTopic, result.Reason);
                return result.IsOnTopic ? GuardDecision.OnTopic : GuardDecision.OffTopic;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM Guard call failed.");
            return GuardDecision.OnTopic; // Default to OnTopic on guard failure so extraction step can process parameter
        }

        return GuardDecision.OnTopic;
    }
}

public class GuardLlmResult
{
    [JsonPropertyName("isOnTopic")]
    public bool IsOnTopic { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}
