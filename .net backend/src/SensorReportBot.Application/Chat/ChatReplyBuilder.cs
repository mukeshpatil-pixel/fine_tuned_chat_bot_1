namespace SensorReportBot.Application.Chat;

using System;
using System.Collections.Generic;
using System.Linq;
using SensorReportBot.Application.DTOs;
using SensorReportBot.Domain.Entities;

public static class ChatReplyBuilder
{
    public static bool HasAsset(ExtractedReportParametersDto p) =>
        p.AssetId.HasValue && p.AssetId.Value > 0 && !string.IsNullOrWhiteSpace(p.AssetName);

    public static bool HasTime(ExtractedReportParametersDto p) =>
        !string.IsNullOrWhiteSpace(p.TimeRange) || !string.IsNullOrWhiteSpace(p.FromDate);

    public static string DescribeRange(ExtractedReportParametersDto p)
    {
        if (!string.IsNullOrWhiteSpace(p.TimeRange)) return p.TimeRange;
        if (!string.IsNullOrWhiteSpace(p.FromDate)) return $"from {p.FromDate}";
        return "the requested timeframe";
    }

    public static string JoinNames(IReadOnlyList<string> names) =>
        string.Join(", ", names);

    public static void AskAsset(ChatRequestState state, ExtractedReportParametersDto cur, IReadOnlyList<string> options, string message)
    {
        state.IsOnTopic = true;
        state.IsComplete = false;
        state.IsHandled = true;
        state.Reply = message;
        state.SuggestedAction = "select_asset";
        state.SuggestedOptions = options.ToList();
        state.ExtractedParameters = cur;
    }

    public static void AskTimeframe(ChatRequestState state, ExtractedReportParametersDto cur, string message)
    {
        state.IsOnTopic = true;
        state.IsComplete = false;
        state.IsHandled = true;
        state.Reply = message;
        state.SuggestedAction = "select_timeframe";
        state.SuggestedOptions = new List<string> { "24h", "5d", "14d", "30d" };
        state.ExtractedParameters = cur;
    }

    public static void Complete(ChatRequestState state, ExtractedReportParametersDto cur, string message)
    {
        state.IsOnTopic = true;
        state.IsComplete = true;
        state.IsHandled = true;
        state.Reply = message;
        state.SuggestedAction = "confirm_queue";
        state.SuggestedOptions = new List<string> { "Yes, Queue PDF Report", "Change Options" };
        state.ExtractedParameters = cur;
    }

    public static void ApplyAuto(ChatRequestState state, ExtractedReportParametersDto cur, IReadOnlyList<AssetDto> assets, string prefix = "")
    {
        var names = assets.Select(a => a.Name).ToList();

        if (HasAsset(cur) && HasTime(cur))
        {
            Complete(state, cur, $"{prefix}I have configured your report for {cur.AssetName} covering {DescribeRange(cur)}. Would you like me to queue and generate this PDF report now?");
        }
        else if (HasAsset(cur))
        {
            AskTimeframe(state, cur, $"{prefix}Got it — {cur.AssetName}. What timeframe would you like to inspect?");
        }
        else
        {
            AskAsset(state, cur, names, $"{prefix}Which machine or asset would you like a report for?");
        }
    }
}
