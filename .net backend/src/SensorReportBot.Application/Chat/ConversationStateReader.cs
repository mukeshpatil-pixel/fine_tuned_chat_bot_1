namespace SensorReportBot.Application.Chat;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SensorReportBot.Domain.Entities;

public static class ConversationStateReader
{
    public static ExtractedReportParametersDto? ReadPrevious(IReadOnlyList<ChatMessageEntity>? history)
    {
        if (history == null || history.Count == 0) return null;

        foreach (var msg in history.Where(m => string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase)).Reverse())
        {
            if (string.IsNullOrWhiteSpace(msg.Metadata)) continue;
            try
            {
                using var doc = JsonDocument.Parse(msg.Metadata);
                if (doc.RootElement.TryGetProperty("extractedParameters", out var p) && p.ValueKind == JsonValueKind.Object)
                {
                    return JsonSerializer.Deserialize<ExtractedReportParametersDto>(p.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
            }
            catch
            {
                // ignore malformed metadata
            }
        }

        return null;
    }
}
