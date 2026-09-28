namespace SensorReportBot.Application.Chat;

using System;
using System.Text.RegularExpressions;

public enum ChatIntent
{
    None,
    Greeting,
    ListAssets,
    ChangeAsset,
    ChangeTimeframe,
    ChangeOptions,
    Reset,
    Confirm
}

public static class ChatIntentDetector
{
    public static (ChatIntent Intent, bool Frustrated) Detect(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return (ChatIntent.None, false);
        string text = input.Trim().ToLowerInvariant();

        bool frustrated = text.Contains("told you") || text.Contains("already said") || text.Contains("didnt listen") || text.Contains("wrong");

        if (Regex.IsMatch(text, @"^(hi|hello|hey|good morning|good afternoon|good evening)\b"))
            return (ChatIntent.Greeting, frustrated);

        if (text == "yes" || text == "yeah" || text == "yep" || text == "confirm" || text == "proceed" || text == "yes, queue pdf report" || text == "generate" || text == "queue report")
            return (ChatIntent.Confirm, frustrated);

        if (text == "change options" || text == "i want to change the settings" || text == "change settings" || text == "change options")
            return (ChatIntent.ChangeOptions, frustrated);

        if (text.Contains("change asset") || text.Contains("different asset") || text.Contains("switch asset") || text.Contains("choose another asset") || text.Contains("other machine"))
            return (ChatIntent.ChangeAsset, frustrated);

        if (text.Contains("change time") || text.Contains("different time") || text.Contains("change timeframe") || text.Contains("different timeframe"))
            return (ChatIntent.ChangeTimeframe, frustrated);

        if (text.Contains("how many assets") || text.Contains("which assets") || text.Contains("what assets") || text.Contains("list machines") || text.Contains("list assets") || text.Contains("suggest me assets"))
            return (ChatIntent.ListAssets, frustrated);

        if (text == "reset" || text == "start over" || text == "clear")
            return (ChatIntent.Reset, frustrated);

        return (ChatIntent.None, frustrated);
    }
}
