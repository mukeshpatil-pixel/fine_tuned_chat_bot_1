namespace SensorReportBot.Infrastructure.Configuration;

public class LlmOptions
{
    public const string SectionName = "Llm";

    public string Endpoint { get; set; } = "http://localhost:11434/v1/chat/completions";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "qwen2.5:0.5b";
    public double Temperature { get; set; } = 0.0;
    public int MaxTokens { get; set; } = 110;
}
