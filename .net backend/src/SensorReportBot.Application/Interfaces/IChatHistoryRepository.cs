namespace SensorReportBot.Application.Interfaces;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SensorReportBot.Domain.Entities;

public interface IChatHistoryRepository
{
    Task SaveMessageAsync(string sessionId, string role, string content, string? metadata = null, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessageEntity>> GetRecentHistoryAsync(string sessionId, int limit = 10, CancellationToken ct = default);
    Task ClearHistoryAsync(string sessionId, CancellationToken ct = default);
}
