namespace SensorReportBot.Application.Steps;

using System.Threading;
using System.Threading.Tasks;
using SensorReportBot.Application.Chat;
using SensorReportBot.Application.Interfaces;
using SensorReportBot.Domain.Entities;

public class RuleBasedIntentStep : IChatStep
{
    private readonly ITelemetryRepository _telemetryRepo;

    public RuleBasedIntentStep(ITelemetryRepository telemetryRepo)
    {
        _telemetryRepo = telemetryRepo;
    }

    public async Task RunAsync(ChatRequestState state, CancellationToken ct)
    {
        var assets = await _telemetryRepo.GetAssetsAsync(ct);
        RuleBasedResolver.TryResolve(state, assets);
    }
}
