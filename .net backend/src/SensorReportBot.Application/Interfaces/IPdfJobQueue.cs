namespace SensorReportBot.Application.Interfaces;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SensorReportBot.Application.DTOs;
using SensorReportBot.Domain.Entities;

/// <summary>
/// Asynchronous PDF generation job queue interface for scheduling,
/// tracking, and streaming background PDF reports.
/// </summary>
public interface IPdfJobQueue
{
    /// <summary>
    /// Enqueues a new PDF generation request.
    /// </summary>
    /// <param name="request">Report parameters.</param>
    /// <param name="assetName">Display name of the machine.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Created job entity record.</returns>
    Task<ReportJob> EnqueueAsync(ReportRequestDto request, string assetName, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a specific report job by ID.
    /// </summary>
    /// <param name="jobId">Job unique identifier.</param>
    /// <returns>Job entity, or null if not found.</returns>
    Task<ReportJob?> GetJobAsync(Guid jobId);

    /// <summary>
    /// Lists all queued, running, and completed report jobs.
    /// </summary>
    /// <returns>List of report job summary DTOs.</returns>
    Task<IReadOnlyList<ReportJobDto>> GetAllJobsAsync();

    /// <summary>
    /// Updates execution progress and status text for a running job.
    /// </summary>
    /// <param name="jobId">Job identifier.</param>
    /// <param name="progressPercentage">Progress value (0-100).</param>
    /// <param name="statusMessage">User-visible progress description.</param>
    /// <returns>A task representing the update operation.</returns>
    Task UpdateJobProgressAsync(Guid jobId, int progressPercentage, string statusMessage);

    /// <summary>
    /// Marks a job as completed and stores the generated PDF document.
    /// </summary>
    /// <param name="jobId">Job identifier.</param>
    /// <param name="pdfData">Rendered PDF byte array.</param>
    /// <param name="fileName">Output filename.</param>
    /// <returns>A task representing the completion operation.</returns>
    Task CompleteJobAsync(Guid jobId, byte[] pdfData, string fileName);

    /// <summary>
    /// Marks a job as failed with an error description.
    /// </summary>
    /// <param name="jobId">Job identifier.</param>
    /// <param name="errorMessage">Failure reason.</param>
    /// <returns>A task representing the failure operation.</returns>
    Task FailJobAsync(Guid jobId, string errorMessage);

    /// <summary>
    /// Cancels an in-flight or queued job.
    /// </summary>
    /// <param name="jobId">Job identifier.</param>
    /// <returns>True if successfully cancelled; otherwise false.</returns>
    Task<bool> CancelJobAsync(Guid jobId);

    /// <summary>
    /// Retrieves the binary PDF content of a completed job.
    /// </summary>
    /// <param name="jobId">Job identifier.</param>
    /// <returns>Raw PDF bytes, or null if unavailable.</returns>
    Task<byte[]?> GetJobPdfAsync(Guid jobId);

    /// <summary>
    /// Opens a read stream for the completed PDF document on disk.
    /// </summary>
    /// <param name="jobId">Job identifier.</param>
    /// <returns>File stream, or null if not found.</returns>
    Task<Stream?> GetJobPdfStreamAsync(Guid jobId);

    /// <summary>
    /// Asynchronously streams queued job IDs for background worker consumption.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Async enumerable of queued job IDs.</returns>
    IAsyncEnumerable<Guid> ReadJobsAsync(CancellationToken ct);
}
