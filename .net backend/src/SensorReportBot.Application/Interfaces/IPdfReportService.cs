namespace SensorReportBot.Application.Interfaces;

using System.Threading;
using System.Threading.Tasks;
using SensorReportBot.Application.DTOs;

/// <summary>
/// Service contract for generating PDF document binaries from structured telemetry datasets.
/// </summary>
public interface IPdfReportService
{
    /// <summary>
    /// Compiles and renders report data into PDF bytes.
    /// </summary>
    /// <param name="reportData">Structured telemetry report data.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task returning the raw byte content of the PDF document.</returns>
    Task<byte[]> GeneratePdfAsync(ReportDataDto reportData, CancellationToken ct = default);
}
