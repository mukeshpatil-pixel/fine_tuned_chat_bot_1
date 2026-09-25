namespace SensorReportBot.Api.Controllers;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SensorReportBot.Application.DTOs;
using SensorReportBot.Application.Interfaces;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly ILogger<ChatController> _logger;

    public ChatController(IChatService chatService, ILogger<ChatController> logger)
    {
        _chatService = chatService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] ChatRequestDto request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { error = "Message cannot be empty." });
        }

        string sessionId = string.IsNullOrWhiteSpace(request.SessionId) ? "sess-default" : request.SessionId;
        var result = await _chatService.ProcessChatMessageAsync(sessionId, request.Message, ct);

        return Ok(new
        {
            reply = result.ReplyMessage,
            sessionId = sessionId,
            isOnTopic = result.IsOnTopic,
            isComplete = result.IsComplete,
            extractedParameters = result.ExtractedParameters
        });
    }

    [HttpGet("history/{sessionId}")]
    public async Task<IActionResult> GetHistory(string sessionId, CancellationToken ct)
    {
        var history = await _chatService.GetHistoryAsync(sessionId, ct);
        return Ok(history);
    }

    [HttpDelete("history/{sessionId}")]
    public async Task<IActionResult> ClearHistory(string sessionId, CancellationToken ct)
    {
        await _chatService.ClearHistoryAsync(sessionId, ct);
        return Ok(new { message = $"History cleared for session {sessionId}" });
    }
}
