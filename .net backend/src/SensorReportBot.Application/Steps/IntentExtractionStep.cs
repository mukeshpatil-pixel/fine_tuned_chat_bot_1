namespace SensorReportBot.Application.Steps;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SensorReportBot.Application.Chat;
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
        if (state.IsHandled) return; // Fast-path already resolved the turn

        var assets = await _telemetryRepo.GetAssetsAsync(ct);
        var current = (state.PreviousParameters ?? ConversationStateReader.ReadPrevious(state.History) ?? new ExtractedReportParametersDto()).Clone();

        try
        {
            string availableAssetsJson = string.Join(" | ", assets.Select(a => $"{a.AssetId}: {a.Name}"));
            string historyContext = state.History != null && state.History.Any()
                ? string.Join("\n", state.History.Select(h => $"{h.Role.ToUpper()}: {h.Content}"))
                : "No prior history.";

            string currentTimeStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
            string systemPromptTemplate = await _promptProvider.GetPromptAsync("ExtractorPrompt", ct);
            string systemPrompt = systemPromptTemplate.Replace("{AvailableAssetsJson}", availableAssetsJson);

            string userContextMessage = $"[CURRENT UTC TIME]: {currentTimeStr}\n\n[CONVERSATION HISTORY]:\n{historyContext}\n\n[LATEST USER INPUT]:\n{state.UserMessage}";

            var result = await _llmService.GenerateJsonResponseAsync<ConversationalChatResultDto>(systemPrompt, userContextMessage, ct);

            if (result != null)
            {
                if (!result.IsOnTopic)
                {
                    state.IsOnTopic = false;
                    state.IsComplete = false;
                    state.Reply = !string.IsNullOrWhiteSpace(result.ReplyMessage) ? result.ReplyMessage : OffTopicReply;
                    state.SuggestedAction = "none";
                    state.SuggestedOptions = new();
                    state.ExtractedParameters = current;
                    return;
                }

                // If LLM recognized asset / time in complex text, apply it
                if (RuleBasedResolver.TryResolve(state, assets, requireReportLike: false)) return;

                // Apply parameters from LLM response
                state.IsOnTopic = true;
                state.IsComplete = result.IsComplete;
                state.Reply = result.ReplyMessage;
                state.SuggestedAction = result.SuggestedAction;
                state.SuggestedOptions = result.SuggestedOptions ?? new();
                state.ExtractedParameters = result.ExtractedParameters ?? current;

                if (state.SuggestedAction == "select_asset")
                {
                    state.SuggestedOptions = assets.Select(a => a.Name).ToList();
                }

                if (state.SuggestedAction == "confirm_queue" || 
                    (state.ExtractedParameters.AssetId.HasValue && state.ExtractedParameters.AssetId > 0 &&
                     (!string.IsNullOrWhiteSpace(state.ExtractedParameters.TimeRange) || !string.IsNullOrWhiteSpace(state.ExtractedParameters.FromDate))))
                {
                    state.IsComplete = true;
                    if (string.IsNullOrWhiteSpace(state.SuggestedAction) || state.SuggestedAction == "none")
                    {
                        state.SuggestedAction = "confirm_queue";
                        state.SuggestedOptions = new System.Collections.Generic.List<string> { "Yes, Queue PDF Report", "Change Options" };
                    }
                }
            }
            else
            {
                ChatReplyBuilder.ApplyAuto(state, current, assets);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM processing in IntentExtractionStep failed; applying graceful state recovery");
            ChatReplyBuilder.ApplyAuto(state, current, assets, "I'm having a little trouble understanding that. ");
        }
    }
}
