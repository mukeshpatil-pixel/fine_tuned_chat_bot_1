namespace SensorReportBot.Application.DTOs;

public class ChatResponseDto
{
    public string Reply { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public bool IsOnTopic { get; set; }
}
