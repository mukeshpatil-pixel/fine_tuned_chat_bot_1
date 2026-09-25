namespace SensorReportBot.Application.Steps;

using System.Threading;
using System.Threading.Tasks;
using SensorReportBot.Application.Interfaces;
using SensorReportBot.Domain.Entities;

public class SaveUserMessageStep : IChatStep
{
    private readonly IChatHistoryRepository _historyRepo;

    public SaveUserMessageStep(IChatHistoryRepository historyRepo)
    {
        _historyRepo = historyRepo;
    }

    public async Task RunAsync(ChatRequestState state, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(state.UserMessage) && !string.IsNullOrWhiteSpace(state.SessionId))
        {
            await _historyRepo.SaveMessageAsync(state.SessionId, "user", state.UserMessage, ct);
        }
    }
}
