namespace SensorReportBot.Application.Interfaces;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SensorReportBot.Domain.Entities;

/// <summary>
/// Data Transfer Object representing the outcome of a conversational turn,
/// including bot textual reply, state completion flags, suggested UI quick chips,
/// and bound telemetry query parameters.
/// </summary>
public class ConversationalChatResultDto
{
    /// <summary>
    /// Gets or sets a value indicating whether the user's intent is within the industrial telemetry reporting domain.
    /// </summary>
    [JsonPropertyName("isOnTopic")]
    public bool IsOnTopic { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether both asset and temporal bounds are resolved to initiate report generation.
    /// </summary>
    [JsonPropertyName("isComplete")]
    public bool IsComplete { get; set; }

    /// <summary>
    /// Gets or sets the conversational reply text rendered in the user chat interface.
    /// </summary>
    [JsonPropertyName("replyMessage")]
    public string ReplyMessage { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the recommended UI action directive (e.g. "select_asset", "select_timeframe", "confirm_queue", "none").
    /// </summary>
    [JsonPropertyName("suggestedAction")]
    public string? SuggestedAction { get; set; }

    /// <summary>
    /// Gets or sets optional quick-reply suggestion chips for the frontend UI.
    /// </summary>
    [JsonPropertyName("suggestedOptions")]
    public List<string>? SuggestedOptions { get; set; }

    /// <summary>
    /// Gets or sets the normalized parameter payload extracted from conversation context.
    /// </summary>
    [JsonPropertyName("extractedParameters")]
    public ExtractedReportParametersDto? ExtractedParameters { get; set; }

    /// <summary>
    /// Gets or sets the queued background PDF report job identifier, if generation was initiated.
    /// </summary>
    [JsonPropertyName("jobId")]
    public Guid? JobId { get; set; }
}

/// <summary>
/// Core application service responsible for orchestrating conversation turns,
/// session persistence, and invoking workflow pipeline steps.
/// </summary>
public interface IChatService
{
    /// <summary>
    /// Processes a single user message within a persistent or ephemeral session context.
    /// </summary>
    /// <param name="sessionId">Unique session identifier.</param>
    /// <param name="userMessage">Raw user message text.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Structured conversational response.</returns>
    Task<ConversationalChatResultDto> ProcessChatMessageAsync(string sessionId, string userMessage, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the ordered conversation history for a given session.
    /// </summary>
    /// <param name="sessionId">Unique session identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Read-only list of historical chat messages.</returns>
    Task<IReadOnlyList<ChatMessageEntity>> GetHistoryAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Clears all historical conversation entries and state associated with a session.
    /// </summary>
    /// <param name="sessionId">Unique session identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ClearHistoryAsync(string sessionId, CancellationToken ct = default);
}
