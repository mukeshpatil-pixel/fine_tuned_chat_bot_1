namespace SensorReportBot.Application.Interfaces;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SensorReportBot.Application.DTOs;

/// <summary>
/// Telemetry data access repository contract for retrieving industrial equipment catalogs,
/// sensor signal descriptors, and aggregating hypertable time-series data from TimescaleDB.
/// </summary>
public interface ITelemetryRepository
{
    /// <summary>
    /// Retrieves all registered physical assets and machines in the plant database.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Read-only list of asset descriptors.</returns>
    Task<IReadOnlyList<AssetDto>> GetAssetsAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves all telemetry sensor signals configured for a specific asset.
    /// </summary>
    /// <param name="assetId">Database primary key identifier for the asset.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Read-only list of signal descriptors.</returns>
    Task<IReadOnlyList<SignalDto>> GetSignalsByAssetAsync(int assetId, CancellationToken ct = default);

    /// <summary>
    /// Compiles historical telemetry sensor readings, anomaly alerts, operational events,
    /// and statistical metric summaries for the requested asset and temporal window.
    /// </summary>
    /// <param name="request">Report parameters specifying asset, signals, and temporal bounds.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Comprehensive telemetry dataset ready for preview or PDF compilation.</returns>
    Task<ReportDataDto> GenerateReportDataAsync(ReportRequestDto request, CancellationToken ct = default);
}
