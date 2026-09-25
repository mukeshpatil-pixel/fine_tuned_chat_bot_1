namespace SensorReportBot.Application.Interfaces;

using System.Threading;
using System.Threading.Tasks;

public interface IPromptProvider
{
    Task<string> GetPromptAsync(string promptName, CancellationToken ct = default);
}
