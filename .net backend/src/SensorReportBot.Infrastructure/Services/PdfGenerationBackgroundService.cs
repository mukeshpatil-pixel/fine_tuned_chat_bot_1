namespace SensorReportBot.Infrastructure.Services;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SensorReportBot.Application.DTOs;
using SensorReportBot.Application.Interfaces;
using SensorReportBot.Domain.Entities;

public class PdfGenerationBackgroundService : BackgroundService
{
    private readonly IPdfJobQueue _jobQueue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PdfGenerationBackgroundService> _logger;

    public PdfGenerationBackgroundService(
        IPdfJobQueue jobQueue,
        IServiceScopeFactory scopeFactory,
        ILogger<PdfGenerationBackgroundService> logger)
    {
        _jobQueue = jobQueue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PdfGenerationBackgroundService started and monitoring job queue.");

        await foreach (var jobId in _jobQueue.ReadJobsAsync(stoppingToken))
        {
            try
            {
                var job = await _jobQueue.GetJobAsync(jobId);
                if (job == null) continue;

                _logger.LogInformation("Background worker picked up Job {JobId} for asset '{Asset}'", jobId, job.AssetName);

                await ProcessJobAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error processing Job {JobId}", jobId);
                await _jobQueue.FailJobAsync(jobId, ex.Message);
            }
        }

        _logger.LogInformation("PdfGenerationBackgroundService stopping.");
    }

    private async Task ProcessJobAsync(ReportJob job, CancellationToken ct)
    {
        if (job.Status == SensorReportBot.Domain.Enums.ReportJobStatus.Cancelled)
        {
            _logger.LogInformation("Job {JobId} was cancelled before worker pickup. Skipping execution.", job.Id);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var telemetryRepo = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
        var pdfService = scope.ServiceProvider.GetRequiredService<IPdfReportService>();

        // Step 1: Query Database
        await _jobQueue.UpdateJobProgressAsync(job.Id, 20, "Querying raw telemetry readings from TimescaleDB...");

        if (job.Status == SensorReportBot.Domain.Enums.ReportJobStatus.Cancelled)
        {
            _logger.LogInformation("Job {JobId} was cancelled. Aborting DB query processing.", job.Id);
            return;
        }

        var requestDto = new ReportRequestDto
        {
            AssetId = job.AssetId,
            SignalIds = job.SignalIds,
            From = job.From,
            To = job.To,
            IncludeEvents = job.IncludeEvents,
            IncludeAlerts = job.IncludeAlerts,
            IncludeInsights = job.IncludeInsights,
            IncludeCharts = job.IncludeCharts,
            IncludeFullRawData = job.IncludeFullRawData
        };

        var reportData = await telemetryRepo.GenerateReportDataAsync(requestDto, ct);

        if (job.Status == SensorReportBot.Domain.Enums.ReportJobStatus.Cancelled)
        {
            _logger.LogInformation("Job {JobId} was cancelled after DB query. Aborting PDF rendering.", job.Id);
            return;
        }

        int totalPoints = 0;
        foreach (var sig in reportData.Signals)
        {
            totalPoints += sig.DataPoints.Count;
        }

        // Step 2: Render PDF
        await _jobQueue.UpdateJobProgressAsync(job.Id, 65, $"Rendering publication PDF with {totalPoints:N0} unaggregated readings...");

        if (job.Status == SensorReportBot.Domain.Enums.ReportJobStatus.Cancelled)
        {
            _logger.LogInformation("Job {JobId} was cancelled before PDF render. Aborting.", job.Id);
            return;
        }

        byte[] pdfBytes = await pdfService.GeneratePdfAsync(reportData, ct);

        if (job.Status == SensorReportBot.Domain.Enums.ReportJobStatus.Cancelled)
        {
            _logger.LogInformation("Job {JobId} was cancelled after PDF render. Aborting completion.", job.Id);
            return;
        }

        // Step 3: Complete
        await _jobQueue.UpdateJobProgressAsync(job.Id, 95, "Finalizing report file...");

        if (job.Status == SensorReportBot.Domain.Enums.ReportJobStatus.Cancelled) return;

        string safeAssetName = string.Join("_", job.AssetName.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
        string fileName = $"{safeAssetName}_Report_{DateTime.UtcNow:yyyyMMdd_HHmm}.pdf";

        await _jobQueue.CompleteJobAsync(job.Id, pdfBytes, fileName);
    }
}
