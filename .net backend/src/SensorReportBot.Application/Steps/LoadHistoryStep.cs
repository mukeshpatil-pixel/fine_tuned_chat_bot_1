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
            state.History = await _historyRepo.GetRecentHistoryAsync(state.SessionId, limit: 10, ct);
        }
    }
}
