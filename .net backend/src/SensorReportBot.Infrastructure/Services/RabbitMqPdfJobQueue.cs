namespace SensorReportBot.Infrastructure.Services;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using SensorReportBot.Application.DTOs;
using SensorReportBot.Application.Interfaces;
using SensorReportBot.Domain.Entities;
using SensorReportBot.Domain.Enums;
using SensorReportBot.Infrastructure.Configuration;

public class RabbitMqPdfJobQueue : IPdfJobQueue, IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqPdfJobQueue> _logger;
    private readonly ConcurrentDictionary<Guid, ReportJob> _jobs = new();
    private IConnection? _connection;
    private IChannel? _publishChannel;
    private IChannel? _consumeChannel;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    public RabbitMqPdfJobQueue(IOptions<RabbitMqOptions> options, ILogger<RabbitMqPdfJobQueue> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    private async Task EnsureConnectedAsync(CancellationToken ct = default)
    {
        if (_connection != null && _connection.IsOpen) return;

        await _connectionLock.WaitAsync(ct);
        try
        {
            if (_connection != null && _connection.IsOpen) return;

            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                AutomaticRecoveryEnabled = true
            };

            _logger.LogInformation("Connecting to RabbitMQ at {Host}:{Port}...", _options.HostName, _options.Port);
            _connection = await factory.CreateConnectionAsync(ct);
            _publishChannel = await _connection.CreateChannelAsync(cancellationToken: ct);

            await _publishChannel.QueueDeclareAsync(
                queue: _options.QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: ct);

            _logger.LogInformation("Connected to RabbitMQ. Queue '{QueueName}' declared successfully.", _options.QueueName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to RabbitMQ at {Host}:{Port}", _options.HostName, _options.Port);
            throw;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task<ReportJob> EnqueueAsync(ReportRequestDto request, string assetName, CancellationToken ct = default)
    {
        var job = new ReportJob
        {
            Id = Guid.NewGuid(),
            AssetId = request.AssetId,
            AssetName = assetName,
            SignalIds = request.SignalIds,
            From = request.From,
            To = request.To,
            IncludeEvents = request.IncludeEvents,
            IncludeAlerts = request.IncludeAlerts,
            IncludeInsights = request.IncludeInsights,
            IncludeCharts = request.IncludeCharts,
            IncludeFullRawData = request.IncludeFullRawData,
            Status = ReportJobStatus.Queued,
            StatusMessage = "Job published to RabbitMQ queue",
            ProgressPercentage = 0,
            CreatedAt = DateTime.UtcNow
        };

        _jobs[job.Id] = job;

        try
        {
            await EnsureConnectedAsync(ct);

            var body = Encoding.UTF8.GetBytes(job.Id.ToString());
            var props = new BasicProperties
            {
                Persistent = true
            };

            await _publishChannel!.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: _options.QueueName,
                mandatory: false,
                basicProperties: props,
                body: body,
                cancellationToken: ct);

            _logger.LogInformation("Enqueued PDF generation job {JobId} to RabbitMQ queue '{QueueName}' for asset '{AssetName}'", job.Id, _options.QueueName, assetName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing job {JobId} to RabbitMQ. Job recorded in internal tracking.", job.Id);
        }

        return job;
    }

    public Task<ReportJob?> GetJobAsync(Guid jobId)
    {
        _jobs.TryGetValue(jobId, out var job);
        return Task.FromResult(job);
    }

    public Task<IReadOnlyList<ReportJobDto>> GetAllJobsAsync()
    {
        var list = _jobs.Values
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new ReportJobDto
            {
                JobId = j.Id,
                AssetId = j.AssetId,
                AssetName = j.AssetName,
                Status = j.Status,
                StatusMessage = j.StatusMessage,
                ProgressPercentage = j.ProgressPercentage,
                CreatedAt = j.CreatedAt,
                StartedAt = j.StartedAt,
                CompletedAt = j.CompletedAt,
                ErrorMessage = j.ErrorMessage,
                FileSizeBytes = j.FileSizeBytes,
                FileName = j.FileName
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<ReportJobDto>>(list);
    }

    public Task UpdateJobProgressAsync(Guid jobId, int progressPercentage, string statusMessage)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            if (job.Status == ReportJobStatus.Cancelled) return Task.CompletedTask;

            if (job.StartedAt == null) job.StartedAt = DateTime.UtcNow;
            job.Status = ReportJobStatus.Processing;
            job.ProgressPercentage = progressPercentage;
            job.StatusMessage = statusMessage;
        }
        return Task.CompletedTask;
    }

    public async Task CompleteJobAsync(Guid jobId, byte[] pdfData, string fileName)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            if (job.Status == ReportJobStatus.Cancelled) return;

            try
            {
                string storageDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "storage", "reports");
                if (!System.IO.Directory.Exists(storageDir))
                {
                    System.IO.Directory.CreateDirectory(storageDir);
                }

                string safeFileName = $"{jobId}_{fileName}";
                string filePath = System.IO.Path.Combine(storageDir, safeFileName);
                await System.IO.File.WriteAllBytesAsync(filePath, pdfData);

                job.FilePath = filePath;
                job.FileSizeBytes = pdfData.Length;
                job.FileName = fileName;
                job.PdfData = null; // Do not store raw binary in RAM
                job.Status = ReportJobStatus.Completed;
                job.ProgressPercentage = 100;
                job.StatusMessage = "PDF generated and saved to server disk storage.";
                job.CompletedAt = DateTime.UtcNow;

                _logger.LogInformation("Job {JobId} marked Completed via RabbitMQ worker. File saved to {FilePath} ({Size} bytes)", jobId, filePath, pdfData.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save PDF file to disk for job {JobId}", jobId);
                job.Status = ReportJobStatus.Failed;
                job.ErrorMessage = $"Failed to save report file: {ex.Message}";
            }
        }
    }

    public Task FailJobAsync(Guid jobId, string errorMessage)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            if (job.Status == ReportJobStatus.Cancelled) return Task.CompletedTask;

            job.Status = ReportJobStatus.Failed;
            job.StatusMessage = "Failed to generate report";
            job.ErrorMessage = errorMessage;
            job.CompletedAt = DateTime.UtcNow;
            _logger.LogError("Job {JobId} marked Failed: {Error}", jobId, errorMessage);
        }
        return Task.CompletedTask;
    }

    public Task<bool> CancelJobAsync(Guid jobId)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            if (job.Status == ReportJobStatus.Queued || job.Status == ReportJobStatus.Processing)
            {
                job.Status = ReportJobStatus.Cancelled;
                job.StatusMessage = "Job cancelled by user request.";
                job.CompletedAt = DateTime.UtcNow;

                if (!string.IsNullOrEmpty(job.FilePath) && System.IO.File.Exists(job.FilePath))
                {
                    try { System.IO.File.Delete(job.FilePath); } catch { }
                    job.FilePath = null;
                }

                _logger.LogInformation("Job {JobId} was cancelled by user.", jobId);
                return Task.FromResult(true);
            }
        }
        return Task.FromResult(false);
    }

    public async Task<byte[]?> GetJobPdfAsync(Guid jobId)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            if (!string.IsNullOrEmpty(job.FilePath) && System.IO.File.Exists(job.FilePath))
            {
                return await System.IO.File.ReadAllBytesAsync(job.FilePath);
            }
            return job.PdfData;
        }
        return null;
    }

    public Task<System.IO.Stream?> GetJobPdfStreamAsync(Guid jobId)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            if (!string.IsNullOrEmpty(job.FilePath) && System.IO.File.Exists(job.FilePath))
            {
                System.IO.Stream stream = new System.IO.FileStream(job.FilePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read);
                return Task.FromResult<System.IO.Stream?>(stream);
            }
        }
        return Task.FromResult<System.IO.Stream?>(null);
    }

    public async IAsyncEnumerable<Guid> ReadJobsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await EnsureConnectedAsync(ct);
            }
            catch
            {
                await Task.Delay(3000, ct);
                continue;
            }

            if (_consumeChannel == null || !_consumeChannel.IsOpen)
            {
                try
                {
                    _consumeChannel = await _connection!.CreateChannelAsync(cancellationToken: ct);
                    await _consumeChannel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to create consumer channel: {Message}. Retrying...", ex.Message);
                    await Task.Delay(3000, ct);
                    continue;
                }
            }

            BasicGetResult? result = null;
            try
            {
                result = await _consumeChannel.BasicGetAsync(queue: _options.QueueName, autoAck: false, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error polling RabbitMQ queue: {Message}", ex.Message);
                await Task.Delay(2000, ct);
                continue;
            }

            if (result != null)
            {
                var bodyStr = Encoding.UTF8.GetString(result.Body.ToArray());
                if (Guid.TryParse(bodyStr, out var jobId))
                {
                    yield return jobId;
                }
                else
                {
                    _logger.LogWarning("Received invalid job ID format from RabbitMQ: {Body}", bodyStr);
                }

                try
                {
                    await _consumeChannel.BasicAckAsync(deliveryTag: result.DeliveryTag, multiple: false, cancellationToken: ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to acknowledge RabbitMQ message for job {JobId}", bodyStr);
                }
            }
            else
            {
                await Task.Delay(500, ct);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_publishChannel != null)
        {
            await _publishChannel.CloseAsync();
            _publishChannel.Dispose();
        }
        if (_consumeChannel != null)
        {
            await _consumeChannel.CloseAsync();
            _consumeChannel.Dispose();
        }
        if (_connection != null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
        }
    }
}
