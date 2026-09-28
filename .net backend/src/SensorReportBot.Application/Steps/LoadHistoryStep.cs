namespace SensorReportBot.Application.Steps;

using System.Threading;
using System.Threading.Tasks;
using SensorReportBot.Application.Interfaces;
using SensorReportBot.Domain.Entities;

public class LoadHistoryStep : IChatStep
{
    private readonly IChatHistoryRepository _historyRepo;

    public LoadHistoryStep(IChatHistoryRepository historyRepo)
    {
        _historyRepo = historyRepo;
    }

    public async Task RunAsync(ChatRequestState state, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(state.SessionId))
        {
            // Limit to last 6 messages (3 conversational turns) for optimal token efficiency
            state.History = await _historyRepo.GetRecentHistoryAsync(state.SessionId, limit: 6, ct);

            // Restore PreviousParameters from the most recent assistant message with valid parameters
            if (state.History != null && state.History.Any())
            {
                var lastAssistantMsg = state.History
                    .Reverse()
                    .FirstOrDefault(m => m.Role.Equals("assistant", System.StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(m.Metadata));

                if (lastAssistantMsg != null)
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(lastAssistantMsg.Metadata!);
                        if (doc.RootElement.TryGetProperty("extractedParameters", out var paramsProp) && paramsProp.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            state.PreviousParameters = System.Text.Json.JsonSerializer.Deserialize<ExtractedReportParametersDto>(paramsProp.GetRawText(), new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        }
                    }
                    catch
                    {
                        // Ignore malformed previous metadata
                    }
                }
            }
        }
    }
}
