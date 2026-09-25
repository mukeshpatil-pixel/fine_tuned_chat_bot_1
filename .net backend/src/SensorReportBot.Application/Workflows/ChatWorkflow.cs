namespace SensorReportBot.Application.Workflows;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SensorReportBot.Application.Interfaces;
using SensorReportBot.Domain.Entities;

public class ChatWorkflow
{
    private readonly IEnumerable<IChatStep> _steps;

    public ChatWorkflow(IEnumerable<IChatStep> steps)
    {
        _steps = steps;
    }

    public async Task ExecuteAsync(ChatRequestState state, CancellationToken ct = default)
    {
        foreach (var step in _steps)
        {
            if (!string.IsNullOrEmpty(state.Reply))
            {
                break;
            }

            await step.RunAsync(state, ct);
        }

        if (state.IsOnTopic && string.IsNullOrEmpty(state.Reply))
        {
            // Fallback: IntentExtractionStep should always produce a reply; this is a last-resort safety net
            state.Reply = "I'm ready to help. Which asset and timeframe would you like a report for?";
        }
    }
}
