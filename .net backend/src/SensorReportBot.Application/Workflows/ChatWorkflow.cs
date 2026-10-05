namespace SensorReportBot.Application.Workflows;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SensorReportBot.Application.Interfaces;
using SensorReportBot.Domain.Entities;

/// <summary>
/// Pipeline orchestrator that coordinates sequential execution of conversation steps
/// (message recording, history loading, neuro-symbolic intent extraction, and response persistence).
/// </summary>
public class ChatWorkflow
{
    private readonly IEnumerable<IChatStep> _steps;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatWorkflow"/> class.
    /// </summary>
    /// <param name="steps">Ordered sequence of workflow pipeline steps.</param>
    public ChatWorkflow(IEnumerable<IChatStep> steps)
    {
        _steps = steps;
    }

    /// <summary>
    /// Executes each configured workflow step in sequence against the shared request state.
    /// </summary>
    /// <param name="state">Shared context encapsulating session parameters, user input, and extracted intent.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous workflow execution.</returns>
    public async Task ExecuteAsync(ChatRequestState state, CancellationToken ct = default)
    {
        foreach (var step in _steps)
        {
            await step.RunAsync(state, ct);
        }

        if (state.IsOnTopic && string.IsNullOrEmpty(state.Reply))
        {
            // Fallback: IntentExtractionStep should always produce a reply; this is a last-resort safety net
            state.Reply = "I'm ready to help. Which asset and timeframe would you like a report for?";
        }
    }
}
