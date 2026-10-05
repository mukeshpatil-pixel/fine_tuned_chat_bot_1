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
        AssetDto? explicitAsset = null;
        try
        {
            // 1. Fetch available database catalog (Assets)
            var assets = await _telemetryRepo.GetAssetsAsync(ct);
            string availableAssetsJson = string.Join(" | ", assets.Select(a => $"{a.AssetId}: {a.Name}"));

            string trimmed = (state.UserMessage ?? "").Trim();
            string lower = trimmed.ToLowerInvariant();

            // An explicitly named asset takes precedence over the previous selection.
            explicitAsset = FindAssetMention(trimmed, assets);

            // ========================================================
            // FAST-PATH: Sub-millisecond instant execution for edge UI
            // ========================================================
            // 1. Greetings & Catalog inquiries
            if (lower == "hi" || lower == "hello" || lower == "hey" || lower == "help" || lower == "start" || 
                lower.StartsWith("hi ") || lower.StartsWith("hello ") || lower.StartsWith("hey ") || 
                lower.StartsWith("good morning") || lower.StartsWith("good afternoon") || lower.StartsWith("good evening") ||
                lower == "i want report" || lower == "i want a report" || lower == "what machines are there?" || lower == "machines" || lower == "assets")
            {
                state.IsOnTopic = true;
                state.IsComplete = false;
                state.SuggestedAction = "select_asset";
                state.SuggestedOptions = assets.Select(a => a.Name).ToList();
                state.Reply = "Hello! Which machine or asset would you like a report for?";
                return;
            }

            // 1b. Change Asset / Switch Machine / Rejections & Another Report
            if (lower.Contains("change asset") || lower.Contains("change machine") || lower.Contains("switch asset") || 
                lower.Contains("switch machine") || lower.Contains("different asset") || lower.Contains("different machine") || 
                lower.Contains("another asset") || lower.Contains("another machine") || lower.Contains("choose another") ||
                lower.Contains("change to") || lower.Contains("switch to") ||
                lower.Contains("another report") || lower.Contains("different report") || lower.Contains("new report") ||
                lower.Contains("change report") || lower.Contains("not this") || lower.Contains("not now") ||
                lower.Contains("dont queue") || lower.Contains("do not queue") || lower.Contains("no thanks") ||
                lower.Contains("no not this") || lower.Contains("no, not this") ||
                lower == "no" || lower.StartsWith("no,") || lower.StartsWith("no ") || lower == "nope" ||
                lower == "cancel" || lower == "stop" ||
                lower.Contains("change the settings") || lower.Contains("change settings") || lower.Contains("change options") || 
                lower == "reset" || lower == "start over" || lower == "clear")
            {
                state.IsOnTopic = true;
                if (explicitAsset != null)
                {
                    string? carryTime = state.PreviousParameters?.TimeRange;
                    string? carryFrom = state.PreviousParameters?.FromDate;
                    string? carryTo = state.PreviousParameters?.ToDate;

                    if (!string.IsNullOrWhiteSpace(carryTime) || !string.IsNullOrWhiteSpace(carryFrom))
                    {
                        state.IsComplete = true;
                        state.ExtractedParameters = new ExtractedReportParametersDto
                        {
                            AssetId = explicitAsset.AssetId,
                            AssetName = explicitAsset.Name,
                            TimeRange = carryTime,
                            FromDate = carryFrom,
                            ToDate = carryTo,
                            Mode = "raw"
                        };
                        state.SuggestedAction = "confirm_queue";
                        state.SuggestedOptions = new List<string> { "Yes, Queue PDF Report", "Change Options" };
                        string display = carryTime ?? $"{carryFrom} to {carryTo}";
                        state.Reply = $"Got it — switched machine to {explicitAsset.Name} covering {display}. Would you like me to queue and generate this PDF report now?";
                        return;
                    }

                    state.IsComplete = false;
                    state.ExtractedParameters = new ExtractedReportParametersDto
                    {
                        AssetId = explicitAsset.AssetId,
                        AssetName = explicitAsset.Name,
                        Mode = "raw"
                    };
                    AskForTimeframe(state);
                }
                else
                {
                    state.IsComplete = false;
                    state.ExtractedParameters = new ExtractedReportParametersDto();
                    state.SuggestedAction = "select_asset";
                    state.SuggestedOptions = assets.Select(a => a.Name).ToList();
                    state.Reply = "Sure! Which machine or asset would you like to configure instead?";
                }
                return;
            }

            // 1c. Change Timeframe / Dates
            if (lower.Contains("change timeframe") || lower.Contains("change time") || lower.Contains("change date") || 
                lower.Contains("different timeframe") || lower.Contains("different time") || lower.Contains("new timeframe"))
            {
                string? inlineTime = TryExtractTimeRange(lower);
                var (inlineFrom, inlineTo) = TryExtractDateRange(trimmed);
                if (string.IsNullOrWhiteSpace(inlineTime) && !string.IsNullOrWhiteSpace(inlineFrom))
                {
                    inlineTime = $"{inlineFrom.Split('T')[0]} to {inlineTo?.Split('T')[0] ?? "now"}";
                }

                if (!string.IsNullOrWhiteSpace(inlineTime) && state.PreviousParameters?.AssetId.HasValue == true)
                {
                    state.IsOnTopic = true;
                    state.IsComplete = true;
                    state.ExtractedParameters = new ExtractedReportParametersDto
                    {
                        AssetId = state.PreviousParameters.AssetId,
                        AssetName = state.PreviousParameters.AssetName,
                        TimeRange = inlineTime,
                        FromDate = inlineFrom,
                        ToDate = inlineTo,
                        Mode = "raw"
                    };
                    state.SuggestedAction = "confirm_queue";
                    state.SuggestedOptions = new List<string> { "Yes, Queue PDF Report", "Change Options" };
                    state.Reply = $"I have updated your report for {state.PreviousParameters.AssetName} covering {inlineTime}. Would you like me to queue and generate this PDF report now?";
                    return;
                }

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
                if (!string.IsNullOrWhiteSpace(state.PreviousParameters?.TimeRange) || !string.IsNullOrWhiteSpace(state.PreviousParameters?.FromDate))
                {
                    state.IsComplete = true;
                    state.ExtractedParameters = new ExtractedReportParametersDto
                    {
                        AssetId = directAsset.AssetId,
                        AssetName = directAsset.Name,
                        TimeRange = state.PreviousParameters.TimeRange,
                        FromDate = state.PreviousParameters.FromDate,
                        ToDate = state.PreviousParameters.ToDate,
                        Mode = "raw"
                    };
                    state.SuggestedAction = "confirm_queue";
                    state.SuggestedOptions = new List<string> { "Yes, Queue PDF Report", "Change Options" };
                    string displayRange = state.PreviousParameters.TimeRange ?? $"{state.PreviousParameters.FromDate} to {state.PreviousParameters.ToDate}";
                    state.Reply = $"I have configured your report for {directAsset.Name} covering {displayRange}. Would you like me to queue and generate this PDF report now?";
                    return;
                }

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
            var (fastFrom, fastTo) = TryExtractDateRange(trimmed);
            if (string.IsNullOrWhiteSpace(fastTimeRange))
            {
                if (!string.IsNullOrWhiteSpace(fastFrom))
                {
                    fastTimeRange = $"{fastFrom.Split('T')[0]} to {fastTo?.Split('T')[0] ?? "now"}";
                }
            }

            if (explicitAsset == null && state.PreviousParameters?.AssetId.HasValue == true && state.PreviousParameters.AssetId > 0 && !string.IsNullOrWhiteSpace(fastTimeRange))
            {
                state.IsOnTopic = true;
                state.IsComplete = true;
                state.ExtractedParameters = new ExtractedReportParametersDto
                {
                    AssetId = state.PreviousParameters.AssetId,
                    AssetName = state.PreviousParameters.AssetName,
                    TimeRange = fastTimeRange,
                    FromDate = fastFrom,
                    ToDate = fastTo,
                    Mode = "raw"
                };
                state.SuggestedAction = "confirm_queue";
                state.SuggestedOptions = new List<string> { "Yes, Queue PDF Report", "Change Options" };
                state.Reply = $"I have configured your report for {state.PreviousParameters.AssetName} covering {fastTimeRange}. Would you like me to queue and generate this PDF report now?";
                return;
            }

            // 3a. User specified a Timeframe FIRST (before selecting an asset) e.g. "Last week data", "24h"
            int wordCount = trimmed.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
            bool isShortTimeframePhrase = wordCount <= 5;
            if (explicitAsset == null && 
                (state.PreviousParameters?.AssetId == null || state.PreviousParameters.AssetId <= 0) && 
                !string.IsNullOrWhiteSpace(fastTimeRange) &&
                !HasOffTopicHint(lower) &&
                (isShortTimeframePhrase || HasDomainHint(lower)))
            {
                state.IsOnTopic = true;
                state.IsComplete = false;
                state.ExtractedParameters = new ExtractedReportParametersDto
                {
                    TimeRange = fastTimeRange,
                    FromDate = fastFrom,
                    ToDate = fastTo,
                    Mode = "raw"
                };
                state.SuggestedAction = "select_asset";
                state.SuggestedOptions = assets.Select(a => a.Name).ToList();
                state.Reply = $"Got it — timeframe set to {fastTimeRange}. Which machine or asset would you like a report for?";
                return;
            }

            // 3b. One-shot Natural Language matching for known catalog assets + timeframes
            var oneShotAsset = explicitAsset;

            if (oneShotAsset != null && !string.IsNullOrWhiteSpace(fastTimeRange))
            {
                state.IsOnTopic = true;
                state.IsComplete = true;
                state.ExtractedParameters = new ExtractedReportParametersDto
                {
                    AssetId = oneShotAsset.AssetId,
                    AssetName = oneShotAsset.Name,
                    TimeRange = fastTimeRange,
                    FromDate = fastFrom,
                    ToDate = fastTo,
                    Mode = "raw"
                };
                state.SuggestedAction = "confirm_queue";
                state.SuggestedOptions = new List<string> { "Yes, Queue PDF Report", "Change Options" };
                state.Reply = $"I have configured your report for {oneShotAsset.Name} covering {fastTimeRange}. Would you like me to queue and generate this PDF report now?";
                return;
            }

            if (oneShotAsset != null)
            {
                state.ExtractedParameters = new ExtractedReportParametersDto
                {
                    AssetId = oneShotAsset.AssetId,
                    AssetName = oneShotAsset.Name,
                    Mode = "raw"
                };
                AskForTimeframe(state);
                return;
            }

            // 4. Confirmation action ("Yes, Queue PDF Report", "ok lets do it", "generate report", etc.)
            bool isNegative = lower == "no" || lower == "nope" || lower == "cancel" || lower == "stop" ||
                              lower.Contains("do not") || lower.Contains("don't") || lower.Contains("not now") ||
                              lower.Contains("dont");
            bool isConfirm = lower.Contains("yes, queue") || lower.Contains("yes please") || lower == "yes" || 
                             lower.Contains("queue") || lower.Contains("generate") || lower.Contains("lets do it") || 
                             lower.Contains("let's do it") || lower.Contains("do it") || lower.Contains("proceed") || 
                             lower.Contains("go ahead") || lower.Contains("confirm") || lower == "sure" || 
                             lower == "ok" || lower == "okay";

            if (isNegative)
            {
                state.IsOnTopic = true;
                state.IsComplete = false;
                state.SuggestedAction = "select_asset";
                state.SuggestedOptions = assets.Select(a => a.Name).ToList();
                state.ExtractedParameters = new ExtractedReportParametersDto();
                state.Reply = "Okay, I will not queue a report. Which machine would you like to configure next?";
                return;
            }

            if (isConfirm && state.PreviousParameters?.AssetId.HasValue == true && state.PreviousParameters.AssetId > 0)
            {
                if (explicitAsset != null && explicitAsset.AssetId != state.PreviousParameters.AssetId)
                {
                    state.ExtractedParameters = new ExtractedReportParametersDto
                    {
                        AssetId = explicitAsset.AssetId, AssetName = explicitAsset.Name, Mode = "raw"
                    };
                    AskForTimeframe(state);
                    return;
                }
                if (string.IsNullOrWhiteSpace(state.PreviousParameters.TimeRange) &&
                    string.IsNullOrWhiteSpace(state.PreviousParameters.FromDate))
                {
                    state.ExtractedParameters = state.PreviousParameters;
                    AskForTimeframe(state);
                    return;
                }
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

                var assetSignals = await _telemetryRepo.GetSignalsByAssetAsync(assetId, ct);
                var targetSignalIds = (state.PreviousParameters.SignalIds != null && state.PreviousParameters.SignalIds.Count > 0)
                    ? state.PreviousParameters.SignalIds
                    : assetSignals.Take(6).Select(s => s.SignalId).ToList();

                var req = new ReportRequestDto
                {
                    AssetId = assetId,
                    SignalIds = targetSignalIds,
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

            bool hasDomainHint = HasDomainHint(lower);
            bool hasOffTopicHint = HasOffTopicHint(lower);
            bool canUseTimeOnlyWithPreviousAsset = state.PreviousParameters?.AssetId.HasValue == true &&
                state.PreviousParameters.AssetId > 0 &&
                (!string.IsNullOrWhiteSpace(fastTimeRange) || !string.IsNullOrWhiteSpace(fastFrom));
            if (hasOffTopicHint || (!hasDomainHint && !canUseTimeOnlyWithPreviousAsset))
            {
                state.IsOnTopic = false;
                state.IsComplete = false;
                state.SuggestedAction = "none";
                state.SuggestedOptions = new List<string>();
                state.ExtractedParameters = new ExtractedReportParametersDto { Mode = "raw" };
                state.Reply = OffTopicReply;
                return;
            }

            // 5. Small-model fallback: send only current state and latest message.
            string systemPromptTemplate = await _promptProvider.GetPromptAsync("ExtractorPrompt", ct);
            string systemPrompt = systemPromptTemplate
                .Replace("{AvailableAssetsJson}", availableAssetsJson);

            string currentAsset = state.PreviousParameters?.AssetName ?? "null";
            string currentTime = state.PreviousParameters?.TimeRange
                ?? (!string.IsNullOrWhiteSpace(state.PreviousParameters?.FromDate)
                    ? $"{state.PreviousParameters.FromDate} to {state.PreviousParameters.ToDate ?? "now"}"
                    : "null");
            string userContextMessage = $"CURRENT_STATE: asset={currentAsset},time={currentTime}\nINPUT: {trimmed}\nDATE_UTC: {DateTime.UtcNow:yyyy-MM-dd}";

            var result = await _llmService.GenerateJsonResponseAsync<ConversationalChatResultDto>(systemPrompt, userContextMessage, ct);

            if (result != null)
            {
                state.IsOnTopic = result.IsOnTopic;
                state.IsComplete = result.IsComplete;
                state.ExtractedParameters = result.ExtractedParameters ?? new ExtractedReportParametersDto();
                state.SuggestedAction = result.SuggestedAction;
                state.SuggestedOptions = result.SuggestedOptions ?? new List<string>();

                // Guard against false off-topic refusals if user asked for assets or mentioned a catalog asset
                bool mentionsCatalogAsset = assets.Any(a => 
                    trimmed.Contains(a.Name, StringComparison.OrdinalIgnoreCase) ||
                    a.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Contains("boiler", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Contains("compressor", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Contains("conveyor", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Contains("crusher", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Contains("cooling", StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(result.ExtractedParameters?.AssetName)));

                bool asksAboutAssets = trimmed.Contains("machine", StringComparison.OrdinalIgnoreCase) ||
                                       trimmed.Contains("asset", StringComparison.OrdinalIgnoreCase) ||
                                       trimmed.Contains("report", StringComparison.OrdinalIgnoreCase) ||
                                       trimmed.Contains("suggest", StringComparison.OrdinalIgnoreCase);

                bool providesTimeOrData = !string.IsNullOrWhiteSpace(fastTimeRange) ||
                                          !string.IsNullOrWhiteSpace(fastFrom) ||
                                          hasDomainHint ||
                                          !string.IsNullOrWhiteSpace(result.ExtractedParameters?.TimeRange) ||
                                          !string.IsNullOrWhiteSpace(result.ExtractedParameters?.FromDate);

                if (!state.IsOnTopic && (mentionsCatalogAsset || asksAboutAssets || providesTimeOrData))
                {
                    state.IsOnTopic = true;
                }

                // If off-topic, ensure reply is set and exit early
                if (!state.IsOnTopic)
                {
                    state.IsComplete = false;
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
                        trimmed.Contains(a.Name, StringComparison.OrdinalIgnoreCase) ||
                        a.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                        (trimmed.Contains("boiler", StringComparison.OrdinalIgnoreCase) && a.Name.Contains("Boiler", StringComparison.OrdinalIgnoreCase)) ||
                        (trimmed.Contains("compressor", StringComparison.OrdinalIgnoreCase) && a.Name.Contains("Compressor", StringComparison.OrdinalIgnoreCase)) ||
                        (trimmed.Contains("conveyor", StringComparison.OrdinalIgnoreCase) && a.Name.Contains("Conveyor", StringComparison.OrdinalIgnoreCase)) ||
                        (trimmed.Contains("crusher", StringComparison.OrdinalIgnoreCase) && a.Name.Contains("Crusher", StringComparison.OrdinalIgnoreCase)) ||
                        (trimmed.Contains("cooling", StringComparison.OrdinalIgnoreCase) && a.Name.Contains("Cooling", StringComparison.OrdinalIgnoreCase)));

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
                    string? parsedRange = TryExtractTimeRange(trimmed);
                    if (!string.IsNullOrWhiteSpace(parsedRange))
                    {
                        state.ExtractedParameters.TimeRange = parsedRange;
                    }
                    else
                    {
                        var (parsedFrom, parsedTo) = TryExtractDateRange(trimmed);
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

                // Do not trust the model's completion flag without actual required parameters.
                if (explicitAsset != null)
                {
                    bool changedAsset = state.ExtractedParameters.AssetId != explicitAsset.AssetId;
                    state.ExtractedParameters.AssetId = explicitAsset.AssetId;
                    state.ExtractedParameters.AssetName = explicitAsset.Name;
                    if (changedAsset) state.ExtractedParameters.SignalIds = null;
                }
                bool hasAsset = assets.Any(a => a.AssetId == state.ExtractedParameters.AssetId);
                if (!string.IsNullOrWhiteSpace(state.ExtractedParameters.TimeRange) &&
                    !IsSupportedTimeRange(state.ExtractedParameters.TimeRange))
                {
                    state.ExtractedParameters.TimeRange = null;
                }

                bool userProvidedTimeframe = !string.IsNullOrWhiteSpace(fastTimeRange) ||
                    !string.IsNullOrWhiteSpace(fastFrom);
                bool previousHadTimeframe = !string.IsNullOrWhiteSpace(state.PreviousParameters?.TimeRange) ||
                    !string.IsNullOrWhiteSpace(state.PreviousParameters?.FromDate);
                if (!userProvidedTimeframe && !previousHadTimeframe)
                {
                    state.ExtractedParameters.TimeRange = null;
                    state.ExtractedParameters.FromDate = null;
                    state.ExtractedParameters.ToDate = null;
                }

                bool hasTimeframe = !string.IsNullOrWhiteSpace(state.ExtractedParameters.TimeRange) ||
                    !string.IsNullOrWhiteSpace(state.ExtractedParameters.FromDate);
                state.IsComplete = hasAsset && hasTimeframe;
                if (hasAsset && !hasTimeframe)
                {
                    AskForTimeframe(state);
                }
                else if (!hasAsset)
                {
                    state.SuggestedAction = "select_asset";
                    state.SuggestedOptions = assets.Select(a => a.Name).ToList();
                    state.Reply = "Which machine or asset would you like a report for?";
                }
                else
                {
                    state.IsComplete = true;
                    state.SuggestedAction = "confirm_queue";
                    state.SuggestedOptions = new List<string> { "Yes, Queue PDF Report", "Change Options" };

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
                if (explicitAsset != null)
                {
                    state.ExtractedParameters = new ExtractedReportParametersDto
                    {
                        AssetId = explicitAsset.AssetId, AssetName = explicitAsset.Name, Mode = "raw"
                    };
                    AskForTimeframe(state);
                    return;
                }
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
            if (explicitAsset != null)
            {
                state.ExtractedParameters = new ExtractedReportParametersDto
                {
                    AssetId = explicitAsset.AssetId, AssetName = explicitAsset.Name, Mode = "raw"
                };
                AskForTimeframe(state);
                return;
            }
            state.IsOnTopic = true;
            state.Reply = "I'm having trouble processing your request right now. Could you please try again?";
        }
    }

    private static void AskForTimeframe(ChatRequestState state)
    {
        state.IsOnTopic = true;
        state.IsComplete = false;
        state.SuggestedAction = "select_timeframe";
        state.SuggestedOptions = new List<string> { "24h", "5d", "14d", "30d" };
        state.Reply = $"What timeframe would you like to inspect for {state.ExtractedParameters?.AssetName ?? "this machine"}?";
    }

    private static AssetDto? FindAssetMention(string text, IReadOnlyList<AssetDto> assets)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        string lower = text.ToLowerInvariant();
        return assets.FirstOrDefault(a =>
            lower.Contains(a.Name.ToLowerInvariant()) ||
            ((lower.Contains("crusher") || lower.Contains("crushr") || lower.Contains("cushr")) && a.Name.Contains("Crusher", StringComparison.OrdinalIgnoreCase)) ||
            ((lower.Contains("boiler") || lower.Contains("boilr") || lower.Contains("bfp") || lower.Contains("feed pump")) && a.Name.Contains("Boiler", StringComparison.OrdinalIgnoreCase)) ||
            ((lower.Contains("compressor") || lower.Contains("compresor") || lower.Contains("comp motor") || lower.Contains("air comp")) && a.Name.Contains("Compressor", StringComparison.OrdinalIgnoreCase)) ||
            ((lower.Contains("conveyor") || lower.Contains("conveior") || lower.Contains("conveyr")) && a.Name.Contains("Conveyor", StringComparison.OrdinalIgnoreCase)) ||
            ((lower.Contains("cooling") || lower.Contains("coling") || lower.Contains("tower fan")) && a.Name.Contains("Cooling", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsSupportedTimeRange(string timeRange)
    {
        if (string.IsNullOrWhiteSpace(timeRange)) return false;

        var match = System.Text.RegularExpressions.Regex.Match(timeRange.Trim(), @"^(\d+)(h|d)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) return false;

        if (!int.TryParse(match.Groups[1].Value, out int amount)) return false;
        string unit = match.Groups[2].Value.ToLowerInvariant();

        return unit == "h"
            ? amount > 0 && amount <= 24 * 365
            : amount > 0 && amount <= 365;
    }

    private static bool HasDomainHint(string lowerText)
    {
        if (string.IsNullOrWhiteSpace(lowerText)) return false;

        string[] hints =
        {
            "asset", "machine", "motor", "motr", "moter", "pump", "pmp", "compressor", "compresor", "conveyor", "conveior",
            "conveyr", "cooling", "coling", "crusher", "crushr", "cushr",
            "boiler", "boilr", "bfp", "fan", "feed", "report", "reprt", "genrate", "pdf", "telemetry", "sensor", "sensr",
            "signal", "measurement", "measurements", "inspect", "timeframe", "period", "data", "reading", "readings", "alert", "event"
        };

        return hints.Any(lowerText.Contains);
    }

    private static bool HasOffTopicHint(string lowerText)
    {
        if (string.IsNullOrWhiteSpace(lowerText)) return false;

        string[] offTopics =
        {
            "cricket", "football", "soccer", "match", "game", "score", "scores", "weather", "temperature outside",
            "rain", "forecast", "president", "election", "politics", "movie", "song", "actor",
            "joke", "riddle", "recipe", "cook", "capital of", "who is", "who won", "write code",
            "how to hack", "love", "dating", "stock price", "bitcoin", "crypto"
        };

        return offTopics.Any(lowerText.Contains);
    }

    private static string? TryExtractTimeRange(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string t = text.Trim().ToLowerInvariant();

        // Hours: e.g. "last 24 hours", "24 hrs", "24h", "12 hours"
        var hourMatch = System.Text.RegularExpressions.Regex.Match(t, @"(?:last\s+|past\s+)?(\d+)\s*(?:hours?|hrs?|hr\b|h\b)");
        if (hourMatch.Success) return $"{hourMatch.Groups[1].Value}h";

        // Days: e.g. "for last 3 days", "3 days", "i said 3 days", "5d", "last 2 days", "5 dais"
        var dayMatch = System.Text.RegularExpressions.Regex.Match(t, @"(?:for\s+|last\s+|past\s+|said\s+)?(\d+)\s*(?:days?|dais?|d\b)");
        if (dayMatch.Success) return $"{dayMatch.Groups[1].Value}d";

        // Weeks: e.g. "last 2 weeks", "2w", "1 week", "2 weks"
        var weekMatch = System.Text.RegularExpressions.Regex.Match(t, @"(?:last\s+|past\s+)?(\d+)\s*(?:weeks?|weks?|w\b)");
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

        if (t.Contains("daily") || t.Contains("today") || t.Contains("yesterday") || t.Contains("1 day") || t.Contains("last day") || t.Contains("past day")) return "24h";
        if (t.Contains("weekly") || t.Contains("a week") || t.Contains("one week") || t.Contains("1 week") || t.Contains("last week") || t.Contains("past week") || t.Contains("previous week") || t.Contains("this week")) return "7d";
        if (t.Contains("bi-weekly") || t.Contains("biweekly") || t.Contains("fortnight") || t.Contains("fortnt") || t.Contains("two weeks") || t.Contains("2 weks") || t.Contains("last 2 weeks")) return "14d";
        if (t.Contains("monthly") || t.Contains("a month") || t.Contains("one month") || t.Contains("last month") || t.Contains("past month") || t.Contains("previous month") || t.Contains("this month")) return "30d";

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
