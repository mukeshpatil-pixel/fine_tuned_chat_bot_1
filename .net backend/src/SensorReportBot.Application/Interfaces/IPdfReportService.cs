namespace SensorReportBot.Application.Interfaces;

using System.Threading;
using System.Threading.Tasks;
using SensorReportBot.Application.DTOs;

public interface IPdfReportService
{
    Task<byte[]> GeneratePdfAsync(ReportDataDto reportData, CancellationToken ct = default);
}
