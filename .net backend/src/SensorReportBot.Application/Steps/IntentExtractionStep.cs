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

            // 2. Build history context (filter out off-topic exchanges so they don't bias or confuse the LLM)
            var cleanHistory = state.History?
                .Where(h => {
                    if (h.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) && 
                        h.Content.Contains("I can only assist with industrial", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                    if (!string.IsNullOrWhiteSpace(h.Metadata))
                    {
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(h.Metadata);
                            if (doc.RootElement.TryGetProperty("isOnTopic", out var prop) && !prop.GetBoolean())
                                return false;
                        }
                        catch { }
                    }
                    return true;
                })
                .ToList();

            string historyContext = cleanHistory != null && cleanHistory.Any()
                ? string.Join("\n", cleanHistory.Select(h => $"{h.Role.ToUpper()}: {h.Content}"))
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

                // Guard against false off-topic refusals if user asked for assets or mentioned a catalog asset
                bool mentionsCatalogAsset = assets.Any(a => 
                    state.UserMessage.Contains(a.Name, StringComparison.OrdinalIgnoreCase) ||
                    a.Name.Contains(state.UserMessage.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    state.UserMessage.Contains("boiler", StringComparison.OrdinalIgnoreCase) ||
                    state.UserMessage.Contains("compressor", StringComparison.OrdinalIgnoreCase) ||
                    state.UserMessage.Contains("conveyor", StringComparison.OrdinalIgnoreCase) ||
                    state.UserMessage.Contains("crusher", StringComparison.OrdinalIgnoreCase) ||
                    state.UserMessage.Contains("cooling", StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(result.ExtractedParameters?.AssetName)));

                bool asksAboutAssets = state.UserMessage.Contains("machine", StringComparison.OrdinalIgnoreCase) ||
                                       state.UserMessage.Contains("asset", StringComparison.OrdinalIgnoreCase) ||
                                       state.UserMessage.Contains("report", StringComparison.OrdinalIgnoreCase) ||
                                       state.UserMessage.Contains("suggest", StringComparison.OrdinalIgnoreCase);

                if (!state.IsOnTopic && (mentionsCatalogAsset || asksAboutAssets))
                {
                    state.IsOnTopic = true;
                }

                // If off-topic, ensure reply is set and exit early
                if (!state.IsOnTopic)
                {
                    if (string.IsNullOrWhiteSpace(state.Reply))
                        state.Reply = OffTopicReply;
                    state.SuggestedAction = "none";
                    state.SuggestedOptions = new List<string>();
                    return;
                }

                // Handle general catalog queries directly if LLM missed action
                if (asksAboutAssets && (!state.ExtractedParameters.AssetId.HasValue || state.ExtractedParameters.AssetId == 0) && !mentionsCatalogAsset)
                {
                    state.SuggestedAction = "select_asset";
                    state.SuggestedOptions = assets.Select(a => a.Name).ToList();
                    state.Reply = "We monitor 5 machines: Boiler Feed Pump Motor, Air Compressor Motor, Main Conveyor Drive Motor, Cooling Tower Fan Motor, and Primary Crusher Motor. Which one would you like a report for?";
                }

                // 1. Resolve AssetId from database if LLM extracted AssetName or user mentioned asset
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
                else if (mentionsCatalogAsset && (!state.ExtractedParameters.AssetId.HasValue || state.ExtractedParameters.AssetId == 0))
                {
                    var matchedAsset = assets.FirstOrDefault(a => 
                        state.UserMessage.Contains(a.Name, StringComparison.OrdinalIgnoreCase) ||
                        a.Name.Contains(state.UserMessage.Trim(), StringComparison.OrdinalIgnoreCase) ||
                        (state.UserMessage.Contains("boiler", StringComparison.OrdinalIgnoreCase) && a.Name.Contains("Boiler", StringComparison.OrdinalIgnoreCase)) ||
                        (state.UserMessage.Contains("compressor", StringComparison.OrdinalIgnoreCase) && a.Name.Contains("Compressor", StringComparison.OrdinalIgnoreCase)) ||
                        (state.UserMessage.Contains("conveyor", StringComparison.OrdinalIgnoreCase) && a.Name.Contains("Conveyor", StringComparison.OrdinalIgnoreCase)) ||
                        (state.UserMessage.Contains("crusher", StringComparison.OrdinalIgnoreCase) && a.Name.Contains("Crusher", StringComparison.OrdinalIgnoreCase)) ||
                        (state.UserMessage.Contains("cooling", StringComparison.OrdinalIgnoreCase) && a.Name.Contains("Cooling", StringComparison.OrdinalIgnoreCase)));

                    if (matchedAsset != null)
                    {
                        state.ExtractedParameters.AssetId = matchedAsset.AssetId;
                        state.ExtractedParameters.AssetName = matchedAsset.Name;
                        if (string.IsNullOrWhiteSpace(state.SuggestedAction) || state.SuggestedAction == "none")
                        {
                            state.SuggestedAction = "select_timeframe";
                            state.Reply = $"Got it — {matchedAsset.Name}. What timeframe would you like to inspect?";
                        }
                    }
                }

                // 1b. Normalize TimeRange or DateRange from UserMessage if LLM left it null
                if (string.IsNullOrWhiteSpace(state.ExtractedParameters.TimeRange) && string.IsNullOrWhiteSpace(state.ExtractedParameters.FromDate))
                {
                    string? parsedRange = TryExtractTimeRange(state.UserMessage);
                    if (!string.IsNullOrWhiteSpace(parsedRange))
                    {
                        state.ExtractedParameters.TimeRange = parsedRange;
                    }
                    else
                    {
                        var (parsedFrom, parsedTo) = TryExtractDateRange(state.UserMessage);
                        if (!string.IsNullOrWhiteSpace(parsedFrom))
                        {
                            state.ExtractedParameters.FromDate = parsedFrom;
                            state.ExtractedParameters.ToDate = parsedTo;
                        }
                    }
                }

                // 1c. Carry forward previously selected Asset from history if user is now specifying timeframe
                if ((!state.ExtractedParameters.AssetId.HasValue || state.ExtractedParameters.AssetId == 0) &&
                    state.PreviousParameters?.AssetId.HasValue == true && state.PreviousParameters.AssetId > 0 &&
                    (!string.IsNullOrWhiteSpace(state.ExtractedParameters.TimeRange) || !string.IsNullOrWhiteSpace(state.ExtractedParameters.FromDate)))
                {
                    state.ExtractedParameters.AssetId = state.PreviousParameters.AssetId;
                    state.ExtractedParameters.AssetName = state.PreviousParameters.AssetName;
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
                    state.SuggestedAction = "confirm_queue";
                    state.SuggestedOptions = new List<string> { "Yes, Queue PDF Report", "Change Options" };

                    if (string.IsNullOrWhiteSpace(state.Reply) || state.Reply.Contains("What timeframe", StringComparison.OrdinalIgnoreCase))
                    {
                        string rangeDisplay;
                        if (!string.IsNullOrWhiteSpace(state.ExtractedParameters.TimeRange))
                        {
                            rangeDisplay = state.ExtractedParameters.TimeRange;
                        }
                        else
                        {
                            string f = state.ExtractedParameters.FromDate?.Split('T')[0] ?? state.ExtractedParameters.FromDate ?? "";
                            string t = state.ExtractedParameters.ToDate?.Split('T')[0] ?? state.ExtractedParameters.ToDate ?? "";
                            rangeDisplay = !string.IsNullOrWhiteSpace(t) ? $"{f} to {t}" : f;
                        }
                        state.Reply = $"I have configured your report for {state.ExtractedParameters.AssetName} covering {rangeDisplay}. Would you like me to queue and generate this PDF report now?";
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

    private static string? TryExtractTimeRange(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string t = text.Trim().ToLowerInvariant();

        // Hours: e.g. "last 24 hours", "24h", "12 hours"
        var hourMatch = System.Text.RegularExpressions.Regex.Match(t, @"(?:last\s+|past\s+)?(\d+)\s*(?:hours?|h\b)");
        if (hourMatch.Success) return $"{hourMatch.Groups[1].Value}h";

        // Days: e.g. "for last 3 days", "3 days", "i said 3 days", "5d", "last 2 days"
        var dayMatch = System.Text.RegularExpressions.Regex.Match(t, @"(?:for\s+|last\s+|past\s+|said\s+)?(\d+)\s*(?:days?|d\b)");
        if (dayMatch.Success) return $"{dayMatch.Groups[1].Value}d";

        // Weeks: e.g. "last 2 weeks", "2w", "1 week"
        var weekMatch = System.Text.RegularExpressions.Regex.Match(t, @"(?:last\s+|past\s+)?(\d+)\s*(?:weeks?|w\b)");
        if (weekMatch.Success)
        {
            if (int.TryParse(weekMatch.Groups[1].Value, out int w))
                return $"{w * 7}d";
        }

        // Months: e.g. "last 1 month", "1m"
        var monthMatch = System.Text.RegularExpressions.Regex.Match(t, @"(?:last\s+|past\s+)?(\d+)\s*(?:months?|m\b)");
        if (monthMatch.Success)
        {
            if (int.TryParse(monthMatch.Groups[1].Value, out int m))
                return $"{m * 30}d";
        }

        if (t.Contains("today") || t.Contains("yesterday") || t.Contains("1 day")) return "24h";
        if (t.Contains("a week") || t.Contains("one week")) return "7d";
        if (t.Contains("a month") || t.Contains("one month")) return "30d";

        // Bare number in context e.g. "3" or "2"
        if (int.TryParse(t, out int bareNum) && bareNum > 0 && bareNum <= 365)
            return $"{bareNum}d";

        return null;
    }

    private static (string? fromDate, string? toDate) TryExtractDateRange(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null);

        var matchRange = System.Text.RegularExpressions.Regex.Match(text, @"(\d{4}-\d{2}-\d{2})(?:\s*(?:to|until|-|and)\s*(\d{4}-\d{2}-\d{2}))?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (matchRange.Success)
        {
            string d1 = matchRange.Groups[1].Value;
            string? d2 = matchRange.Groups[2].Success ? matchRange.Groups[2].Value : null;

            string fromIso = DateTime.TryParse(d1, out var dt1) ? dt1.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") : $"{d1}T00:00:00Z";
            string toIso = !string.IsNullOrWhiteSpace(d2) && DateTime.TryParse(d2, out var dt2) 
                ? dt2.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") 
                : DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

            return (fromIso, toIso);
        }

        return (null, null);
    }
}
