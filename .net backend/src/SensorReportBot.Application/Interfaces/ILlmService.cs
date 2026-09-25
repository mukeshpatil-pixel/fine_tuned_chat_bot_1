namespace SensorReportBot.Application.Interfaces;

using System.Threading;
using System.Threading.Tasks;

public interface ILlmService
{
    Task<TResponse?> GenerateJsonResponseAsync<TResponse>(string systemPrompt, string userMessage, CancellationToken ct = default);
}
