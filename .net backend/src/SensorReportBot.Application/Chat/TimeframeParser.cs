namespace SensorReportBot.Application.Chat;

using System;
using System.Text.RegularExpressions;

public readonly record struct TimeframeResult(bool Found, bool HasProblem, string? TimeRange, string? FromDate, string? ToDate, string? Problem)
{
    public static TimeframeResult None => new(false, false, null, null, null, null);
    public static TimeframeResult Relative(string code) => new(true, false, code, null, null, null);
    public static TimeframeResult Absolute(string fromUtc, string toUtc) => new(true, false, null, fromUtc, toUtc, null);
    public static TimeframeResult Error(string msg) => new(false, true, null, null, null, msg);
}

public static class TimeframeParser
{
    private static readonly Regex RelativePattern = new(
        @"\b(?:last|past|previous|for\s+the\s+last|for)?\s*(\d{1,3}|one|two|three|four|five|six|seven|eight|nine|ten|a couple of|a few|half\s+a)\s*(h|hr|hrs|hour|hours|d|day|days|w|wk|wks|week|weeks|m|mo|mos|month|months)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NumberWordPattern = new(
        @"^(\d{1,3}|one|two|three|four|five|six|seven|eight|nine|ten|half|ten and half)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static TimeframeResult Parse(string input, bool allowBareNumber = false, DateTime? utcNow = null)
    {
        if (string.IsNullOrWhiteSpace(input)) return TimeframeResult.None;
        string text = input.Trim().ToLowerInvariant();

        // 1. Common aliases
        if (text == "24h" || text == "today" || text == "yesterday" || text == "1d" || text == "last 24 hours" || text == "24 hours" || text == "1 day")
            return TimeframeResult.Relative("24h");
        if (text == "5d" || text == "5 days" || text == "last 5 days" || text == "five days")
            return TimeframeResult.Relative("5d");
        if (text == "14d" || text == "14 days" || text == "2 weeks" || text == "two weeks" || text == "last 2 weeks" || text == "last 14 days")
            return TimeframeResult.Relative("14d");
        if (text == "30d" || text == "30 days" || text == "1 month" || text == "one month" || text == "last 30 days" || text == "last month")
            return TimeframeResult.Relative("30d");

        // 2. Relative regex matching
        var match = RelativePattern.Match(text);
        if (match.Success)
        {
            int num = ParseNumber(match.Groups[1].Value);
            string unit = match.Groups[2].Value.ToLowerInvariant();

            if (unit.StartsWith("h")) return TimeframeResult.Relative($"{num}h");
            if (unit.StartsWith("d")) return TimeframeResult.Relative($"{num}d");
            if (unit.StartsWith("w")) return TimeframeResult.Relative($"{num * 7}d");
            if (unit.StartsWith("m")) return TimeframeResult.Relative($"{num * 30}d");
        }

        // 3. Bare number fallback when awaiting timeframe
        if (allowBareNumber)
        {
            var numMatch = NumberWordPattern.Match(text);
            if (numMatch.Success)
            {
                int num = ParseNumber(numMatch.Groups[1].Value);
                if (num > 0 && num <= 365) return TimeframeResult.Relative($"{num}d");
            }
        }

        // 4. ISO Date range "from 2026-09-01 to 2026-09-05"
        var dateMatch = Regex.Match(text, @"(\d{4}-\d{2}-\d{2})\s*(?:to|till|until|-)\s*(\d{4}-\d{2}-\d{2})");
        if (dateMatch.Success &&
            DateTime.TryParse(dateMatch.Groups[1].Value, out var d1) &&
            DateTime.TryParse(dateMatch.Groups[2].Value, out var d2))
        {
            return TimeframeResult.Absolute(
                DateTime.SpecifyKind(d1, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                DateTime.SpecifyKind(d2, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ"));
        }

        return TimeframeResult.None;
    }

    private static int ParseNumber(string s)
    {
        s = s.Trim().ToLowerInvariant();
        if (int.TryParse(s, out int n)) return n;
        return s switch
        {
            "one" => 1,
            "two" or "a couple of" => 2,
            "three" => 3,
            "four" => 4,
            "five" or "a few" => 5,
            "six" => 6,
            "seven" => 7,
            "eight" => 8,
            "nine" => 9,
            "ten" or "ten and half" => 10,
            _ => 1
        };
    }
}
