namespace SensorReportBot.Application.Chat;

using System;
using System.Collections.Generic;
using System.Linq;
using SensorReportBot.Application.DTOs;

public readonly record struct AssetMatchResult(bool IsMatch, bool IsAmbiguous, AssetDto? Asset, IReadOnlyList<AssetDto> Candidates)
{
    public static AssetMatchResult None => new(false, false, null, Array.Empty<AssetDto>());
    public static AssetMatchResult Single(AssetDto asset) => new(true, false, asset, new[] { asset });
    public static AssetMatchResult Multiple(IReadOnlyList<AssetDto> candidates) => new(false, true, null, candidates);
}

public static class AssetMatcher
{
    private static readonly HashSet<string> NoiseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "motor", "drive", "feed", "tower", "machine", "asset", "equipment", "unit", "the", "a", "an"
    };

    public static HashSet<string> CatalogWords(IEnumerable<AssetDto> assets)
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in assets)
        {
            foreach (var w in Tokenize(a.Name)) words.Add(w);
        }
        return words;
    }

    public static bool IsAssetWord(string word, HashSet<string> catalog) => catalog.Contains(word);

    public static AssetMatchResult Match(string input, IReadOnlyList<AssetDto> assets)
    {
        if (string.IsNullOrWhiteSpace(input) || assets == null || assets.Count == 0)
            return AssetMatchResult.None;

        string normInput = Normalize(input);

        // 1. Exact match
        foreach (var a in assets)
        {
            string normName = Normalize(a.Name);
            if (normName.Equals(normInput, StringComparison.OrdinalIgnoreCase))
                return AssetMatchResult.Single(a);
        }

        // 2. Substring match
        var containsMatches = assets
            .Where(a => normInput.Contains(Normalize(a.Name), StringComparison.OrdinalIgnoreCase) ||
                        Normalize(a.Name).Contains(normInput, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (containsMatches.Count == 1) return AssetMatchResult.Single(containsMatches[0]);

        // 3. Keyword matching (distinct non-noise words)
        var inputTokens = Tokenize(input).Where(t => !NoiseWords.Contains(t) && t.Length > 2).ToList();
        if (inputTokens.Count == 0) return AssetMatchResult.None;

        var scored = new List<(AssetDto Asset, int Score)>();
        foreach (var a in assets)
        {
            var assetTokens = Tokenize(a.Name).Where(t => !NoiseWords.Contains(t)).ToList();
            int score = inputTokens.Count(t => assetTokens.Any(at => at.Contains(t, StringComparison.OrdinalIgnoreCase) || t.Contains(at, StringComparison.OrdinalIgnoreCase)));
            if (score > 0) scored.Add((a, score));
        }

        if (scored.Count == 0) return AssetMatchResult.None;

        int maxScore = scored.Max(s => s.Score);
        var best = scored.Where(s => s.Score == maxScore).Select(s => s.Asset).ToList();

        if (best.Count == 1) return AssetMatchResult.Single(best[0]);
        if (best.Count > 1) return AssetMatchResult.Multiple(best);

        return AssetMatchResult.None;
    }

    private static string Normalize(string s) => string.Join(" ", Tokenize(s));
    private static IEnumerable<string> Tokenize(string s) =>
        System.Text.RegularExpressions.Regex.Matches(s.ToLowerInvariant(), @"[a-z0-9]+")
            .Select(m => m.Value);
}
