namespace SensorReportBot.Application.Steps;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SensorReportBot.Application.Interfaces;
using SensorReportBot.Domain.Entities;
using SensorReportBot.Domain.Enums;

public class GuardStep : IChatStep
{
    private readonly IGuardService _guardService;

    public GuardStep(IGuardService guardService)
    {
        _guardService = guardService;
    }

    public async Task RunAsync(ChatRequestState state, CancellationToken ct)
    {
        string historyContext = state.History != null && state.History.Any()
            ? string.Join("\n", state.History.Select(h => $"{h.Role.ToUpper()}: {h.Content}"))
            : string.Empty;

        var decision = await _guardService.CheckAsync(state.UserMessage, historyContext, ct);

        if (decision == GuardDecision.OffTopic)
        {
            state.IsOnTopic = false;
            state.Reply = "Sorry, I can only help with sensor and asset reports.";
        }
        else
        {
            state.IsOnTopic = true;
        }
    }
}
