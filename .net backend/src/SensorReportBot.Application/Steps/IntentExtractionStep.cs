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
    private readonly IPdfJobQueue _pdfJobQueue;
    private readonly ILogger<IntentExtractionStep> _logger;

    public IntentExtractionStep(
        ILlmService llmService,
        IPromptProvider promptProvider,
        ITelemetryRepository telemetryRepo,
        IPdfJobQueue pdfJobQueue,
        ILogger<IntentExtractionStep> logger)
    {
        _llmService = llmService;
        _promptProvider = promptProvider;
        _telemetryRepo = telemetryRepo;
        _pdfJobQueue = pdfJobQueue;
        _logger = logger;
    }

    public async Task RunAsync(ChatRequestState state, CancellationToken ct)
    {
        try
        {
            // 1. Fetch available database catalog (Assets)
            var assets = await _telemetryRepo.GetAssetsAsync(ct);
            string availableAssetsJson = string.Join(" | ", assets.Select(a => $"{a.AssetId}: {a.Name}"));

            string trimmed = (state.UserMessage ?? "").Trim();
            string lower = trimmed.ToLowerInvariant();

            // ========================================================
            // FAST-PATH: Sub-millisecond instant execution for edge UI
            // ========================================================
            // 1. Greetings & Catalog inquiries
            if (lower == "hi" || lower == "hello" || lower == "hey" || lower == "help" || lower == "start" || 
                lower == "i want report" || lower == "i want a report" || lower == "what machines are there?" || lower == "machines" || lower == "assets")
            {
                state.IsOnTopic = true;
                state.IsComplete = false;
                state.SuggestedAction = "select_asset";
                state.SuggestedOptions = assets.Select(a => a.Name).ToList();
                state.Reply = "Hello! Which machine or asset would you like a report for?";
                return;
            }

            // 1b. Change Asset / Switch Machine
            if (lower.Contains("change asset") || lower.Contains("change machine") || lower.Contains("switch asset") || 
                lower.Contains("switch machine") || lower.Contains("different asset") || lower.Contains("different machine") || 
                lower.Contains("another asset") || lower.Contains("another machine") || lower.Contains("choose another") ||
                lower.Contains("change the settings") || lower.Contains("change settings") || lower.Contains("change options") || 
                lower == "reset" || lower == "start over" || lower == "clear")
            {
                state.IsOnTopic = true;
                state.IsComplete = false;
                state.ExtractedParameters = new ExtractedReportParametersDto();
                state.SuggestedAction = "select_asset";
                state.SuggestedOptions = assets.Select(a => a.Name).ToList();
                state.Reply = "Sure! Which machine or asset would you like to configure instead?";
                return;
            }

            // 1c. Change Timeframe / Dates
            if (lower.Contains("change timeframe") || lower.Contains("change time") || lower.Contains("change date") || 
                lower.Contains("different timeframe") || lower.Contains("different time") || lower.Contains("new timeframe"))
            {
                state.IsOnTopic = true;
                state.IsComplete = false;
                state.ExtractedParameters = new ExtractedReportParametersDto
                {
                    AssetId = state.PreviousParameters?.AssetId,
                    AssetName = state.PreviousParameters?.AssetName,
                    Mode = "raw"
                };
                state.SuggestedAction = "select_timeframe";
                state.SuggestedOptions = new List<string> { "24h", "5d", "14d", "30d" };
                string name = state.PreviousParameters?.AssetName ?? "this machine";
                state.Reply = $"Got it. What timeframe would you like for {name} instead?";
                return;
            }

            // 2. Direct click on an Asset Name chip
            var directAsset = assets.FirstOrDefault(a => a.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
            if (directAsset != null)
            {
                state.IsOnTopic = true;
                state.IsComplete = false;
                state.ExtractedParameters = new ExtractedReportParametersDto
                {
                    AssetId = directAsset.AssetId,
                    AssetName = directAsset.Name,
                    Mode = "raw"
                };
                state.SuggestedAction = "select_timeframe";
                state.SuggestedOptions = new List<string> { "24h", "5d", "14d", "30d" };
                state.Reply = $"Got it — {directAsset.Name}. What timeframe would you like to inspect?";
                return;
            }

            // 3. Direct click or natural language Timeframe when Asset is already selected
            string? fastTimeRange = TryExtractTimeRange(lower);
            if (string.IsNullOrWhiteSpace(fastTimeRange))
            {
                var (fastFrom, fastTo) = TryExtractDateRange(state.UserMessage);
                if (!string.IsNullOrWhiteSpace(fastFrom))
                {
                    fastTimeRange = $"{fastFrom.Split('T')[0]} to {fastTo?.Split('T')[0] ?? "now"}";
                }
            }

            if (state.PreviousParameters?.AssetId.HasValue == true && state.PreviousParameters.AssetId > 0 && !string.IsNullOrWhiteSpace(fastTimeRange))
            {
                state.IsOnTopic = true;
                state.IsComplete = true;
                state.ExtractedParameters = new ExtractedReportParametersDto
                {
                    AssetId = state.PreviousParameters.AssetId,
                    AssetName = state.PreviousParameters.AssetName,
                    TimeRange = fastTimeRange,
                    Mode = "raw"
                };
                state.SuggestedAction = "confirm_queue";
                state.SuggestedOptions = new List<string> { "Yes, Queue PDF Report", "Change Options" };
                state.Reply = $"I have configured your report for {state.PreviousParameters.AssetName} covering {fastTimeRange}. Would you like me to queue and generate this PDF report now?";
                return;
            }

            // 3b. One-shot Natural Language matching for known catalog assets + timeframes
            var oneShotAsset = assets.FirstOrDefault(a => 
                lower.Contains(a.Name.ToLowerInvariant()) ||
                (lower.Contains("crusher") && a.Name.Contains("Crusher", StringComparison.OrdinalIgnoreCase)) ||
                (lower.Contains("boiler") && a.Name.Contains("Boiler", StringComparison.OrdinalIgnoreCase)) ||
                (lower.Contains("compressor") && a.Name.Contains("Compressor", StringComparison.OrdinalIgnoreCase)) ||
                (lower.Contains("conveyor") && a.Name.Contains("Conveyor", StringComparison.OrdinalIgnoreCase)) ||
                (lower.Contains("cooling") && a.Name.Contains("Cooling", StringComparison.OrdinalIgnoreCase)));

            if (oneShotAsset != null && !string.IsNullOrWhiteSpace(fastTimeRange))
            {
                state.IsOnTopic = true;
                state.IsComplete = true;
                state.ExtractedParameters = new ExtractedReportParametersDto
                {
                    AssetId = oneShotAsset.AssetId,
                    AssetName = oneShotAsset.Name,
                    TimeRange = fastTimeRange,
                    Mode = "raw"
                };
                state.SuggestedAction = "confirm_queue";
                state.SuggestedOptions = new List<string> { "Yes, Queue PDF Report", "Change Options" };
                state.Reply = $"I have configured your report for {oneShotAsset.Name} covering {fastTimeRange}. Would you like me to queue and generate this PDF report now?";
                return;
            }

            // 4. Confirmation action ("Yes, Queue PDF Report", "ok lets do it", "generate report", etc.)
            bool isConfirm = lower.Contains("yes, queue") || lower.Contains("yes please") || lower == "yes" || 
                             lower.Contains("queue") || lower.Contains("generate") || lower.Contains("lets do it") || 
                             lower.Contains("let's do it") || lower.Contains("do it") || lower.Contains("proceed") || 
                             lower.Contains("go ahead") || lower.Contains("confirm") || lower == "sure" || 
                             lower == "ok" || lower == "okay";

            if (isConfirm && state.PreviousParameters?.AssetId.HasValue == true && state.PreviousParameters.AssetId > 0)
            {
                var assetId = state.PreviousParameters.AssetId.Value;
                var assetName = state.PreviousParameters.AssetName ?? $"Asset-{assetId}";
                var timeRange = state.PreviousParameters.TimeRange ?? "24h";

                DateTime to = DateTime.UtcNow;
                DateTime from = to.AddDays(-1);
                if (!string.IsNullOrWhiteSpace(state.PreviousParameters.FromDate))
                {
                    if (DateTime.TryParse(state.PreviousParameters.FromDate, out var parsedFrom))
                        from = parsedFrom;
                    if (!string.IsNullOrWhiteSpace(state.PreviousParameters.ToDate) && DateTime.TryParse(state.PreviousParameters.ToDate, out var parsedTo))
                        to = parsedTo;
                }
                else
                {
                    var matchHour = System.Text.RegularExpressions.Regex.Match(timeRange, @"(\d+)\s*h");
                    var matchDay = System.Text.RegularExpressions.Regex.Match(timeRange, @"(\d+)\s*d");
                    if (matchHour.Success && int.TryParse(matchHour.Groups[1].Value, out int h))
                        from = to.AddHours(-h);
                    else if (matchDay.Success && int.TryParse(matchDay.Groups[1].Value, out int d))
                        from = to.AddDays(-d);
                    else
                        from = to.AddDays(-7);
                }

                var req = new ReportRequestDto
                {
                    AssetId = assetId,
                    From = from,
                    To = to,
                    IncludeEvents = true,
                    IncludeAlerts = true,
                    IncludeInsights = true,
                    IncludeCharts = true,
                    IncludeFullRawData = true
                };

                var job = await _pdfJobQueue.EnqueueAsync(req, assetName, ct);

                state.IsOnTopic = true;
                state.IsComplete = true;
                state.JobId = job.Id;
                state.ExtractedParameters = state.PreviousParameters;
                state.SuggestedAction = "confirm_queue";
                state.SuggestedOptions = new List<string> { "Change Options" };
                string shortJobId = job.Id.ToString()[..8].ToUpper();
                state.Reply = $"🚀 Report for **{assetName}** covering **{timeRange}** has been queued for background generation (Job **REP-{shortJobId}**)! You can track live progress and download it below.";
                return;
            }

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
            string systemPromptTemplate = await _promptProvider.GetPromptAsync("ExtractorPrompt", ct);
            string systemPrompt = systemPromptTemplate
                .Replace("{AvailableAssetsJson}", availableAssetsJson);

            string userContextMessage = $"[CONVERSATION HISTORY]:\n{historyContext}\n\n[LATEST USER INPUT]:\n{state.UserMessage}\n\n[DATE UTC]: {DateTime.UtcNow:yyyy-MM-dd}";

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
