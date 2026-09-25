namespace SensorReportBot.Application.Interfaces;

using System.Threading;
using System.Threading.Tasks;
using SensorReportBot.Domain.Entities;

public interface IChatStep
{
    Task RunAsync(ChatRequestState state, CancellationToken ct);
}
