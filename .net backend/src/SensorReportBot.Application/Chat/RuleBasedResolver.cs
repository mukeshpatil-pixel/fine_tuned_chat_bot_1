namespace SensorReportBot.Application.Chat;

using System;
using System.Collections.Generic;
using System.Linq;
using SensorReportBot.Application.DTOs;
using SensorReportBot.Domain.Entities;
using static SensorReportBot.Application.Chat.ChatReplyBuilder;

public static class RuleBasedResolver
{
    private readonly record struct Extraction(AssetMatchResult Asset, TimeframeResult Time)
    {
        public bool Any => Asset.IsMatch || Asset.IsAmbiguous || Time.Found || Time.HasProblem;
    }

    private static readonly HashSet<string> Fillers = new(StringComparer.OrdinalIgnoreCase)
    {
        "a","an","the","of","and","or","with","on","at","in","from","to","till","until","through","for","by",
        "i","im","m","me","my","we","our","us","you","your","it","its","this","that","there","here","one","ones",
        "is","are","am","be","do","does","did","have","has","can","could","would","will","shall","let","lets",
        "want","need","like","get","got","give","gimme","show","make","set","use","take","put","run","start","generate","queue",
        "please","pls","plz","thanks","thank","thx","yes","yeah","yep","ok","okay","sure","no","not","nope","confirm","proceed",
        "hi","hello","hey","same","instead","also","too","only","just","again","now","then","but","so","about","around","roughly","approx",
        "report","reports","pdf","telemetry","data","machine","machines","asset","assets","equipment","unit",
        "change","switch","choose","select","pick","different","another","other","new","main","primary",
        "last","past","previous","next","this","today","yesterday","couple","few","half","week","weeks","month","months","day","days",
        "hour","hours","hr","hrs","h","d","w","wk","wks","mo","mos","time","timeframe","range","period","duration",
        "what","which","how","many","list","suggest","sorry","told","bro","dude","correct","right","wrong","ago"
    };

    private static bool IsReportLike(string text, IReadOnlyList<AssetDto> assets)
    {
        var catalog = AssetMatcher.CatalogWords(assets);
        int unusual = 0;
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text.ToLowerInvariant(), @"[a-z0-9']+"))
        {
            string token = m.Value.Trim('\'');
            if (token.Length == 0 || token.All(char.IsDigit) || Fillers.Contains(token)) continue;
            if (AssetMatcher.IsAssetWord(token, catalog)) continue;
            unusual++;
        }
        return unusual <= 2;
    }

    public static bool TryResolve(ChatRequestState state, IReadOnlyList<AssetDto> assets, DateTime? utcNow = null, bool requireReportLike = true)
    {
        if (assets.Count == 0 || string.IsNullOrWhiteSpace(state.UserMessage)) return false;

        var prev = ConversationStateReader.ReadPrevious(state.History) ?? new ExtractedReportParametersDto();
        state.PreviousParameters = prev.Clone();
        var cur = prev.Clone();

        string text = state.UserMessage.Trim();
        var (intent, frustrated) = ChatIntentDetector.Detect(text);
        var names = assets.Select(a => a.Name).ToList();

        if (intent == ChatIntent.Reset)
        {
            ApplyAuto(state, new ExtractedReportParametersDto(), assets, "Sure, starting fresh. ");
            return true;
        }

        // 1. Try to pull an asset and/or timeframe out of the message
        bool awaitingTime = HasAsset(cur) && !HasTime(cur);
        var ext = Extract(text, assets, awaitingTime, utcNow);
        if (ext.Any && requireReportLike && !IsReportLike(text, assets)) return false;

        if (!ext.Any && frustrated)
        {
            foreach (var earlier in (state.History ?? Array.Empty<ChatMessageEntity>())
                         .Where(h => string.Equals(h.Role, "user", StringComparison.OrdinalIgnoreCase))
                         .Reverse().Take(3))
            {
                ext = Extract(earlier.Content, assets, allowBareNumber: false, utcNow);
                if (ext.Any) break;
            }
        }

        if (ext.Any)
        {
            int? prevAssetId = cur.AssetId;
            bool hadTime = HasTime(cur);
            string oldRange = DescribeRange(cur);

            if (ext.Asset.IsAmbiguous)
            {
                if (ext.Time.Found) ApplyTime(cur, ext.Time);
                cur.AssetId = null;
                cur.AssetName = null;
                AskAsset(state, cur, ext.Asset.Candidates.Select(a => a.Name).ToList(),
                    "I found more than one possible match. Which one did you mean?");
                return true;
            }

            if (ext.Asset.IsMatch)
            {
                cur.AssetId = ext.Asset.Asset!.AssetId;
                cur.AssetName = ext.Asset.Asset.Name;
            }

            if (ext.Time.HasProblem)
            {
                ClearTime(cur);
                AskTimeframe(state, cur, ext.Time.Problem!);
                return true;
            }

            if (ext.Time.Found) ApplyTime(cur, ext.Time);

            string prefix = frustrated ? "Sorry about that! " : string.Empty;

            if (ext.Asset.IsMatch && !ext.Time.Found && hadTime && HasAsset(cur))
            {
                string lead = prevAssetId.HasValue && prevAssetId != cur.AssetId
                    ? $"Switched to {cur.AssetName}, keeping {oldRange}."
                    : !prevAssetId.HasValue ? $"Got it — {cur.AssetName}, keeping {oldRange}." : string.Empty;

                if (lead.Length > 0)
                {
                    Complete(state, cur, $"{prefix}{lead} Would you like me to queue and generate this PDF report now?");
                    return true;
                }
            }

            ApplyAuto(state, cur, assets, prefix);
            return true;
        }

        // 2. Conversational commands
        switch (intent)
        {
            case ChatIntent.Confirm:
                if (HasAsset(cur) && HasTime(cur))
                    Complete(state, cur, $"Great — queuing your PDF report for {cur.AssetName} covering {DescribeRange(cur)}.");
                else
                    ApplyAuto(state, cur, assets);
                return true;

            case ChatIntent.ChangeOptions:
            case ChatIntent.ChangeAsset:
                if (intent == ChatIntent.ChangeAsset)
                {
                    cur.AssetId = null;
                    cur.AssetName = null;
                }
                AskAsset(state, cur, names, HasTime(cur)
                    ? $"Sure! Which machine would you like to switch to? I'll keep {DescribeRange(cur)} unless you give me a new timeframe."
                    : "Sure! Which machine would you like to switch to?");
                return true;

            case ChatIntent.ChangeTimeframe:
                ClearTime(cur);
                AskTimeframe(state, cur, "Sure — what timeframe would you like to inspect?");
                return true;

            case ChatIntent.ListAssets:
                AskAsset(state, cur, names,
                    $"We currently monitor {names.Count} machine{(names.Count == 1 ? "" : "s")}: {JoinNames(names)}. Which one would you like to inspect?");
                return true;

            case ChatIntent.Greeting:
                ApplyAuto(state, new ExtractedReportParametersDto(), assets,
                    "Hello! I'm SensorBot, your industrial report assistant. ");
                return true;
        }

        if (frustrated)
        {
            ApplyAuto(state, cur, assets, "Sorry about that! Let's sort it out. ");
            return true;
        }

        return false; // needs LLM
    }

    private static Extraction Extract(string text, IReadOnlyList<AssetDto> assets, bool allowBareNumber, DateTime? utcNow) =>
        new(AssetMatcher.Match(text, assets), TimeframeParser.Parse(text, allowBareNumber, utcNow));

    private static void ApplyTime(ExtractedReportParametersDto p, TimeframeResult t)
    {
        p.TimeRange = t.TimeRange;
        p.FromDate = t.FromDate;
        p.ToDate = t.ToDate;
    }

    private static void ClearTime(ExtractedReportParametersDto p)
    {
        p.TimeRange = null;
        p.FromDate = null;
        p.ToDate = null;
    }
}
