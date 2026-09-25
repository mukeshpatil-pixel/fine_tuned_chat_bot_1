namespace SensorReportBot.Application.Steps;

using System.Threading;
using System.Threading.Tasks;
using SensorReportBot.Application.Interfaces;
using SensorReportBot.Domain.Entities;

public class PersistAssistantReplyStep : IChatStep
{
    private readonly IChatHistoryRepository _historyRepo;

    public PersistAssistantReplyStep(IChatHistoryRepository historyRepo)
    {
        _historyRepo = historyRepo;
    }

    public async Task RunAsync(ChatRequestState state, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(state.Reply) && !string.IsNullOrWhiteSpace(state.SessionId))
        {
            await _historyRepo.SaveMessageAsync(state.SessionId, "assistant", state.Reply, ct);
        }
    }
}
