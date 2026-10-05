namespace SensorReportBot.Api.Controllers;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SensorReportBot.Application.DTOs;
using SensorReportBot.Application.Interfaces;

/// <summary>
/// RESTful controller for querying industrial assets, sensor telemetry,
/// and orchestrating asynchronous PDF generation jobs.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ReportsController : ControllerBase
{
    private readonly ITelemetryRepository _telemetryRepository;
    private readonly IPdfJobQueue _pdfJobQueue;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReportsController"/> class.
    /// </summary>
    /// <param name="telemetryRepository">Repository for querying TimescaleDB telemetry and asset metadata.</param>
    /// <param name="pdfJobQueue">Asynchronous queue provider for scheduling PDF generation workloads.</param>
    public ReportsController(
        ITelemetryRepository telemetryRepository,
        IPdfJobQueue pdfJobQueue)
    {
        _telemetryRepository = telemetryRepository;
        _pdfJobQueue = pdfJobQueue;
    }

    /// <summary>
    /// Retrieves all registered physical assets and machines in the plant catalog.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of asset metadata objects.</returns>
    [HttpGet("assets")]
    public async Task<IActionResult> GetAssets(CancellationToken ct)
    {
        var assets = await _telemetryRepository.GetAssetsAsync(ct);
        return Ok(assets);
    }

    /// <summary>
    /// Retrieves all active telemetry sensor signals attached to a specific machine.
    /// </summary>
    /// <param name="assetId">Database primary key identifier for the asset.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of signal metadata definitions including units and physical descriptions.</returns>
    [HttpGet("assets/{assetId:int}/signals")]
    public async Task<IActionResult> GetSignals(int assetId, CancellationToken ct)
    {
        var signals = await _telemetryRepository.GetSignalsByAssetAsync(assetId, ct);
        return Ok(signals);
    }

    /// <summary>
    /// Computes and returns an inline JSON telemetry preview containing statistical
    /// aggregates, operational anomalies, and vector trend series without generating a PDF.
    /// </summary>
    /// <param name="request">Report parameters specifying asset, signals, and temporal bounds.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Structured report dataset including metrics and sparkline data.</returns>
    [HttpPost("preview")]
    public async Task<IActionResult> GeneratePreview([FromBody] ReportRequestDto request, CancellationToken ct)
    {
        if (request.AssetId <= 0)
        {
            return BadRequest(new { error = "Valid AssetId is required." });
        }

        var reportData = await _telemetryRepository.GenerateReportDataAsync(request, ct);
        return Ok(reportData);
    }

    /// <summary>
    /// Enqueues an asynchronous PDF report generation job to RabbitMQ / Background Service.
    /// </summary>
    /// <param name="request">Report specifications including asset ID, signal IDs, and time bounds.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>202 Accepted status with the tracking Job ID.</returns>
    [HttpPost("queue")]
    public async Task<IActionResult> QueueReportGeneration([FromBody] ReportRequestDto request, CancellationToken ct)
    {
        if (request.AssetId <= 0)
        {
            return BadRequest(new { error = "Valid AssetId is required." });
        }

        var assets = await _telemetryRepository.GetAssetsAsync(ct);
        var asset = assets.FirstOrDefault(a => a.AssetId == request.AssetId);
        string assetName = asset?.Name ?? $"Asset-{request.AssetId}";

        var job = await _pdfJobQueue.EnqueueAsync(request, assetName, ct);

        return Accepted($"/api/reports/jobs/{job.Id}", new EnqueueJobResponseDto
        {
            JobId = job.Id,
            Status = "Queued",
            Message = $"Report for '{assetName}' placed in background queue.",
            CreatedAt = job.CreatedAt
        });
    }

    /// <summary>
    /// Lists all current and historical PDF generation jobs tracked by the server.
    /// </summary>
    /// <returns>List of background report jobs with their execution states.</returns>
    [HttpGet("jobs")]
    public async Task<IActionResult> GetAllJobs()
    {
        var jobs = await _pdfJobQueue.GetAllJobsAsync();
        return Ok(jobs);
    }

    /// <summary>
    /// Retrieves the current status, progress percentage, and timestamps of a specific PDF job.
    /// </summary>
    /// <param name="jobId">Unique identifier of the PDF job.</param>
    /// <returns>Report job status DTO.</returns>
    [HttpGet("jobs/{jobId:guid}")]
    public async Task<IActionResult> GetJobStatus(Guid jobId)
    {
        var job = await _pdfJobQueue.GetJobAsync(jobId);
        if (job == null)
        {
            return NotFound(new { error = $"Job {jobId} not found." });
        }

        return Ok(new ReportJobDto
        {
            JobId = job.Id,
            AssetId = job.AssetId,
            AssetName = job.AssetName,
            Status = job.Status,
            StatusMessage = job.StatusMessage,
            ProgressPercentage = job.ProgressPercentage,
            CreatedAt = job.CreatedAt,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt,
            ErrorMessage = job.ErrorMessage,
            FileSizeBytes = job.FileSizeBytes,
            FileName = job.FileName
        });
    }

    /// <summary>
    /// Requests cancellation of an in-flight or queued report generation job.
    /// </summary>
    /// <param name="jobId">Unique identifier of the PDF job to cancel.</param>
    /// <returns>Cancellation confirmation status.</returns>
    [HttpDelete("jobs/{jobId:guid}")]
    public async Task<IActionResult> CancelJob(Guid jobId)
    {
        var job = await _pdfJobQueue.GetJobAsync(jobId);
        if (job == null)
        {
            return NotFound(new { error = $"Job {jobId} not found." });
        }

        bool success = await _pdfJobQueue.CancelJobAsync(jobId);
        if (!success)
        {
            return BadRequest(new { error = $"Job {jobId} cannot be cancelled because its current status is '{job.Status}'." });
        }

        return Ok(new { message = $"Job {jobId} has been cancelled successfully.", jobId, status = "Cancelled" });
    }

    /// <summary>
    /// Streams the rendered PDF binary document for a completed report job.
    /// </summary>
    /// <param name="jobId">Unique identifier of the completed PDF job.</param>
    /// <returns>PDF file stream with application/pdf content type header.</returns>
    [HttpGet("jobs/{jobId:guid}/download")]
    public async Task<IActionResult> DownloadJobPdf(Guid jobId)
    {
        var job = await _pdfJobQueue.GetJobAsync(jobId);
        if (job == null)
        {
            return NotFound(new { error = $"Job {jobId} not found." });
        }

        if (job.Status != SensorReportBot.Domain.Enums.ReportJobStatus.Completed)
        {
            return BadRequest(new { error = $"Job is not completed yet. Current status: {job.Status}", status = job.Status.ToString() });
        }

        var pdfStream = await _pdfJobQueue.GetJobPdfStreamAsync(jobId);
        if (pdfStream == null)
        {
            return NotFound(new { error = "PDF report file not found on server disk storage." });
        }

        string fileName = job.FileName ?? $"Report_{job.AssetName}_{DateTime.UtcNow:yyyyMMdd_HHmm}.pdf";
        return File(pdfStream, "application/pdf", fileName);
    }
}
