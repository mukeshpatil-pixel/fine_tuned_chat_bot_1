namespace SensorReportBot.Application.Steps;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SensorReportBot.Application.DTOs;
using SensorReportBot.Application.Interfaces;
using SensorReportBot.Domain.Entities;

public class IntentExtractionStep : IChatStep
{
    private const string OffTopicReply = "Sorry, I can only assist with industrial sensor and asset reports.";

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
            // 1. Fetch available database catalog (Assets)
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

            // 4. Pure LLM Call for every single message
            var result = await _llmService.GenerateJsonResponseAsync<ConversationalChatResultDto>(systemPrompt, userContextMessage, ct);

            if (result != null)
            {
                state.IsOnTopic = result.IsOnTopic;
                state.IsComplete = result.IsComplete;
                state.ExtractedParameters = result.ExtractedParameters ?? new ExtractedReportParametersDto();
                state.Reply = result.ReplyMessage;
                state.SuggestedAction = result.SuggestedAction;
                state.SuggestedOptions = result.SuggestedOptions ?? new List<string>();

                // If off-topic, ensure reply is set and exit early
                if (!result.IsOnTopic)
                {
                    if (string.IsNullOrWhiteSpace(state.Reply))
                        state.Reply = OffTopicReply;
                    state.SuggestedAction = "none";
                    state.SuggestedOptions = new List<string>();
                    return;
                }

                // 1. Resolve AssetId from database if LLM extracted AssetName
                if (!string.IsNullOrWhiteSpace(state.ExtractedParameters.AssetName) && (!state.ExtractedParameters.AssetId.HasValue || state.ExtractedParameters.AssetId == 0))
                {
                    var matchedAsset = assets.FirstOrDefault(a => 
                        a.Name.Equals(state.ExtractedParameters.AssetName, StringComparison.OrdinalIgnoreCase) ||
                        a.Name.Contains(state.ExtractedParameters.AssetName, StringComparison.OrdinalIgnoreCase) ||
                        state.ExtractedParameters.AssetName.Contains(a.Name, StringComparison.OrdinalIgnoreCase));

                    if (matchedAsset != null)
                    {
                        state.ExtractedParameters.AssetId = matchedAsset.AssetId;
                        state.ExtractedParameters.AssetName = matchedAsset.Name;
                    }
                }

                // 2. Dynamic options enrichment from database for asset selection
                if (state.SuggestedAction == "select_asset")
                {
                    state.SuggestedOptions = assets.Select(a => a.Name).ToList();
                }

                // 3. Ensure isComplete is synchronized when configuration is ready or action is confirm_queue
                if (state.SuggestedAction == "confirm_queue" || 
                    (state.ExtractedParameters.AssetId.HasValue && state.ExtractedParameters.AssetId > 0 &&
                     (!string.IsNullOrWhiteSpace(state.ExtractedParameters.TimeRange) || !string.IsNullOrWhiteSpace(state.ExtractedParameters.FromDate))))
                {
                    state.IsComplete = true;
                    if (string.IsNullOrWhiteSpace(state.SuggestedAction) || state.SuggestedAction == "none")
                    {
                        state.SuggestedAction = "confirm_queue";
                        state.SuggestedOptions = new List<string> { "Yes, Queue PDF Report", "Change Options" };
                    }
                }
            }
            else
            {
                // Graceful fallback only if LLM API request fails
                state.IsOnTopic = true;
                state.IsComplete = false;
                state.Reply = "Hi there! Which machine or asset would you like a report for?";
                state.SuggestedAction = "select_asset";
                state.SuggestedOptions = assets.Select(a => a.Name).ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute LLM intent extraction call in IntentExtractionStep");
            state.IsOnTopic = true;
            state.Reply = "I'm having trouble processing your request right now. Could you please try again?";
        }
    }
}
