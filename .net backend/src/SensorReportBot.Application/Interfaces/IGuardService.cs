namespace SensorReportBot.Application.Interfaces;

using System.Threading;
using System.Threading.Tasks;
using SensorReportBot.Domain.Enums;

public interface IGuardService
{
    Task<GuardDecision> CheckAsync(string message, string? historyContext = null, CancellationToken ct = default);
}
