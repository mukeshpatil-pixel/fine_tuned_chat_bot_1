namespace SensorReportBot.Api.Hubs;

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SensorReportBot.Application.Interfaces;

/// <summary>
/// Real-time SignalR hub providing full-duplex WebSocket communication
/// for conversational chat sessions, intent clarification, and PDF queue triggers.
/// </summary>
public class ChatHub : Hub
{
    private readonly IChatService _chatService;
    private readonly ILogger<ChatHub> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatHub"/> class.
    /// </summary>
    /// <param name="chatService">Service orchestrating conversational multi-step chat workflows.</param>
    /// <param name="logger">Logger instance.</param>
    public ChatHub(IChatService chatService, ILogger<ChatHub> logger)
    {
        _chatService = chatService;
        _logger = logger;
    }

    /// <summary>
    /// Receives a chat message from a connected client, runs it through the intent extraction
    /// and PDF workflow engine, and streams the response back to the caller.
    /// </summary>
    /// <param name="sessionId">Unique identifier for the user session.</param>
    /// <param name="userMessage">Raw text entered by the user.</param>
    /// <returns>A task representing the asynchronous SignalR push operation.</returns>
    public async Task SendMessage(string sessionId, string userMessage)
    {
        try
        {
            _logger.LogInformation("WebSocket Chat Message Received from Session {SessionId}: '{Message}'", sessionId, userMessage);

            var result = await _chatService.ProcessChatMessageAsync(sessionId, userMessage);

            // Push the response back over WebSocket to the client who sent the message.
            // "ReceiveChatResponse" must match the connection.on(...) listener in React.
            await Clients.Caller.SendAsync("ReceiveChatResponse", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling WebSocket SendMessage in ChatHub");
            await Clients.Caller.SendAsync("ReceiveError", ex.Message);
        }
    }

    /// <summary>
    /// Retrieves historical conversation messages for the specified session.
    /// </summary>
    /// <param name="sessionId">Unique identifier for the user session.</param>
    /// <returns>A task representing the asynchronous push operation.</returns>
    public async Task GetHistory(string sessionId)
    {
        try
        {
            var history = await _chatService.GetHistoryAsync(sessionId);
            await Clients.Caller.SendAsync("ReceiveHistory", history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching history in ChatHub");
        }
    }

    /// <summary>
    /// Clears conversation history for the specified session.
    /// </summary>
    /// <param name="sessionId">Unique identifier for the user session.</param>
    /// <returns>A task representing the asynchronous push operation.</returns>
    public async Task ClearHistory(string sessionId)
    {
        try
        {
            await _chatService.ClearHistoryAsync(sessionId);
            await Clients.Caller.SendAsync("ReceiveHistoryCleared", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing history in ChatHub");
        }
    }
}
