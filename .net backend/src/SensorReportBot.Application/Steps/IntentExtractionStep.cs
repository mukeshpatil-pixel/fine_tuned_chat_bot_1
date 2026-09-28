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
        try
        {
            // 1. Fetch available database catalog (Assets) - format compactly to save tokens
            var assets = await _telemetryRepo.GetAssetsAsync(ct);
            string availableAssetsJson = string.Join(" | ", assets.Select(a => $"{a.AssetId}: {a.Name}"));

            // 2. Build history context
            string historyContext = state.History != null && state.History.Any()
                ? string.Join("\n", state.History.Select(h => $"{h.Role.ToUpper()}: {h.Content}"))
                : "No prior history.";

            // 3. Load & format Extractor System Prompt (Static - cached in Ollama KV memory)
            string currentTimeStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
            string systemPromptTemplate = await _promptProvider.GetPromptAsync("ExtractorPrompt", ct);
            string systemPrompt = systemPromptTemplate
                .Replace("{AvailableAssetsJson}", availableAssetsJson);

            string userContextMessage = $"[CURRENT UTC TIME]: {currentTimeStr}\n\n[CONVERSATION HISTORY]:\n{historyContext}\n\n[LATEST USER INPUT]:\n{state.UserMessage}";

            // 4. Execute LLM Intent & Reply Generation
            var result = await _llmService.GenerateJsonResponseAsync<ConversationalChatResultDto>(systemPrompt, userContextMessage, ct);

            if (result != null)
            {
                state.IsOnTopic = result.IsOnTopic;
                state.IsComplete = result.IsComplete;
                state.ExtractedParameters = result.ExtractedParameters ?? new ExtractedReportParametersDto();
                state.Reply = result.ReplyMessage;
                state.SuggestedAction = result.SuggestedAction;
                state.SuggestedOptions = result.SuggestedOptions;

                // If off-topic, ensure reply is set and exit early
                if (!result.IsOnTopic)
                {
                    if (string.IsNullOrWhiteSpace(state.Reply))
                        state.Reply = "Sorry, I can only help with industrial sensor and asset reports.";
                    return;
                }

                // Resolve AssetId and official catalog Name from Database if LLM extracted AssetName
                if (!string.IsNullOrWhiteSpace(state.ExtractedParameters.AssetName) && (!state.ExtractedParameters.AssetId.HasValue || state.ExtractedParameters.AssetId == 0))
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

                // Safety guard: Ensure isComplete is only true when both Asset and Timeframe are actually resolved
                if (state.IsComplete)
                {
                    bool hasAsset = state.ExtractedParameters.AssetId.HasValue && state.ExtractedParameters.AssetId.Value > 0;
                    bool hasTime = !string.IsNullOrWhiteSpace(state.ExtractedParameters.TimeRange) || !string.IsNullOrWhiteSpace(state.ExtractedParameters.FromDate);
                    if (!hasAsset || !hasTime)
                    {
                        state.IsComplete = false;
                    }
                }

                // Dynamic UI Action & Options Enrichment directly from live database catalog
                if (state.IsOnTopic && !state.IsComplete)
                {
                    bool hasAsset = state.ExtractedParameters.AssetId.HasValue && state.ExtractedParameters.AssetId.Value > 0;
                    if (state.SuggestedAction == "select_asset" || !hasAsset)
                    {
                        state.SuggestedAction = "select_asset";
                        state.SuggestedOptions = assets.Select(a => a.Name).ToList();
                    }
                    else if (state.SuggestedAction == "select_timeframe" || (string.IsNullOrWhiteSpace(state.ExtractedParameters.TimeRange) && string.IsNullOrWhiteSpace(state.ExtractedParameters.FromDate)))
                    {
                        state.SuggestedAction = "select_timeframe";
                        if (state.SuggestedOptions == null || !state.SuggestedOptions.Any())
                        {
                            state.SuggestedOptions = new List<string> { "24h", "5d", "14d", "30d" };
                        }
                    }
                }
                else if (state.IsComplete)
                {
                    state.SuggestedAction = "confirm_queue";
                    state.SuggestedOptions = new List<string> { "Yes, Queue PDF Report", "Change Options" };
                }
            }
            else
            {
                // Fallback if LLM API call fails
                state.IsOnTopic = true;
                state.IsComplete = false;
                state.Reply = "Hi there! I'm your Sensor Report Assistant. Which machine or asset would you like a report for?";
                state.SuggestedAction = "select_asset";
                state.SuggestedOptions = assets.Select(a => a.Name).ToList();
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
