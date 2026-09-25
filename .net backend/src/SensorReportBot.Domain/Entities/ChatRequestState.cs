namespace SensorReportBot.Domain.Entities;

using System;
using System.Collections.Generic;

public class ChatMessageEntity
{
    public long Id { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty; // "user" or "assistant"
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class ExtractedReportParametersDto
{
    public int? AssetId { get; set; }
    public string? AssetName { get; set; }
    public List<int>? SignalIds { get; set; }

    /// <summary>Relative duration code e.g. "5d", "24h". Use when user says "last 5 days".</summary>
    public string? TimeRange { get; set; }

    /// <summary>Absolute start datetime ISO-8601 UTC. Use when user specifies a concrete date like "24 sept".</summary>
    public string? FromDate { get; set; }

    /// <summary>Absolute end datetime ISO-8601 UTC. Defaults to current time when only FromDate is given.</summary>
    public string? ToDate { get; set; }

    public string? Mode { get; set; }
}

public class ChatRequestState
{
    public string UserMessage { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public bool IsOnTopic { get; set; } = true;
    public bool IsComplete { get; set; } = false;
    public string? Reply { get; set; }

    // Pipeline Step States
    public IReadOnlyList<ChatMessageEntity>? History { get; set; }
    public ExtractedReportParametersDto? ExtractedParameters { get; set; }
}
