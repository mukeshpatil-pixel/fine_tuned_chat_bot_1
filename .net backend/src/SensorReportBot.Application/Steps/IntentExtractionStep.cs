namespace SensorReportBot.Application.Steps;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SensorReportBot.Application.DTOs;
using SensorReportBot.Application.Interfaces;
using SensorReportBot.Domain.Entities;

public class IntentExtractionStep : IChatStep
{
    private readonly ILlmService _llmService;
    private readonly IPromptProvider _promptProvider;
    private readonly ITelemetryRepository _telemetryRepo;
    private readonly ILogger<IntentExtractionStep> _logger;

    public IntentExtractionStep(
        ILlmService llmService,
        IPromptProvider promptProvider,
        ITelemetryRepository telemetryRepo,
        ILogger<IntentExtractionStep> logger)
    {
        _llmService = llmService;
        _promptProvider = promptProvider;
        _telemetryRepo = telemetryRepo;
        _logger = logger;
    }

    public async Task RunAsync(ChatRequestState state, CancellationToken ct)
    {
        // Skip only if a previous step (e.g. SaveUserMessageStep) already set a reply.
        // We do NOT check IsOnTopic here — this step is responsible for determining it.
        if (!string.IsNullOrEmpty(state.Reply)) return;

        try
        {
            // 1. Fetch available database catalog (Assets)
            var assets = await _telemetryRepo.GetAssetsAsync(ct);
            var assetsSummaryList = assets.Select(a => new
            {
                assetId = a.AssetId,
                name = a.Name,
                location = a.Location ?? a.AssetType
            }).ToList();

            string availableAssetsJson = JsonSerializer.Serialize(assetsSummaryList);

            // 2. Build history context
            string historyContext = state.History != null && state.History.Any()
                ? string.Join("\n", state.History.Select(h => $"{h.Role.ToUpper()}: {h.Content}"))
                : "No prior history.";

            // 3. Load & format Extractor System Prompt
            string currentTimeStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
            string systemPromptTemplate = await _promptProvider.GetPromptAsync("ExtractorPrompt", ct);
            string systemPrompt = systemPromptTemplate
                .Replace("{AvailableAssetsJson}", availableAssetsJson)
                .Replace("{CurrentTime}", currentTimeStr);

            string userContextMessage = $"[CONVERSATION HISTORY FROM POSTGRESQL]:\n{historyContext}\n\n[LATEST USER INPUT]:\n{state.UserMessage}";

            // 4. Execute LLM Intent & Reply Generation
            var result = await _llmService.GenerateJsonResponseAsync<ConversationalChatResultDto>(systemPrompt, userContextMessage, ct);

            if (result != null)
            {
                state.IsOnTopic = result.IsOnTopic;
                state.IsComplete = result.IsComplete;
                state.ExtractedParameters = result.ExtractedParameters ?? new ExtractedReportParametersDto();
                state.Reply = result.ReplyMessage;

                // If off-topic, ensure reply is set and exit early
                if (!result.IsOnTopic)
                {
                    if (string.IsNullOrWhiteSpace(state.Reply))
                        state.Reply = "Sorry, I can only help with industrial sensor and asset reports.";
                    return;
                }

                // Resolve AssetId from Database Catalog if LLM extracted AssetName
                if (state.ExtractedParameters != null && !string.IsNullOrWhiteSpace(state.ExtractedParameters.AssetName) && (!state.ExtractedParameters.AssetId.HasValue || state.ExtractedParameters.AssetId == 0))
                {
                    var matchedAsset = assets.FirstOrDefault(a => 
                        a.Name.Equals(state.ExtractedParameters.AssetName, StringComparison.OrdinalIgnoreCase) ||
                        a.Name.Contains(state.ExtractedParameters.AssetName, StringComparison.OrdinalIgnoreCase) ||
                        state.ExtractedParameters.AssetName.Contains(a.Name, StringComparison.OrdinalIgnoreCase) ||
                        a.Name.Split(' ').Any(w => w.Length > 3 && state.ExtractedParameters.AssetName.Contains(w, StringComparison.OrdinalIgnoreCase)));

                    if (matchedAsset != null)
                    {
                        state.ExtractedParameters.AssetId = matchedAsset.AssetId;
                        state.ExtractedParameters.AssetName = matchedAsset.Name;
                    }
                }
            }
            else
            {
                // LLM returned null (e.g. Groq json_validate_failed on casual messages).
                // Keep conversation alive with a natural prompt rather than an error.
                state.IsOnTopic = true;
                state.IsComplete = false;
                state.Reply = "Hi there! I'm your Sensor Report Assistant. Which machine or asset would you like a report for?";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute LLM intent extraction call in IntentExtractionStep");
            // Keep IsOnTopic = true so the conversation isn't killed; surface a neutral retry message
            state.IsOnTopic = true;
            state.Reply = "I'm having trouble processing your request right now. Could you please try again?";
        }
    }
}
