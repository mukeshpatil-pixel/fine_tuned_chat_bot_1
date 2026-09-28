# Sensor Report Chatbot Backend

A .NET 8 Web API built using Clean Architecture. The application processes user chat messages through an extensible workflow pipeline featuring an LLM-based Guard classification step to filter on-topic vs. off-topic requests using structured JSON outputs.

## Architecture & System Prompts

- **`src/SensorReportBot.Domain`**: Core domain entities and enums (`ChatRequestState`, `GuardDecision`).
- **`src/SensorReportBot.Application`**: Workflow pipeline (`IChatStep`, `ChatWorkflow`, `GuardStep`), interfaces (`IGuardService`, `ILlmService`, `IPromptProvider`), DTOs, and FluentValidation.
- **`src/SensorReportBot.Infrastructure`**:
  - **`Prompts/`**: Dedicated folder storing system prompt files (e.g. `Prompts/GuardPrompt.txt`).
  - **`Services/FilePromptProvider.cs`**: System prompt manager loading prompt text files.
  - **`Services/LlmService.cs`**: HTTP client querying the configured LLM / SLM endpoint using structured JSON output.
  - **`Services/LlmGuardService.cs`**: Primary guard service parsing structured JSON from LLM (with graceful rule-based fallback if the LLM endpoint is offline).
- **`src/SensorReportBot.Api`**: Controllers, Serilog logging, CORS, Swagger UI, Health check endpoints, and Exception Handling Middleware.

## LLM Configuration (`appsettings.json`)

```json
"Llm": {
  "Endpoint": "http://localhost:11434/v1/chat/completions",
  "ApiKey": "",
  "Model": "qwen2.5:0.5b",
  "Temperature": 0.0,
  "MaxTokens": 250
}
```

## How to Run

### Build Solution
```bash
dotnet build SensorReportBot.sln
```

### Start Web API
```bash
dotnet run --project src/SensorReportBot.Api/SensorReportBot.Api.csproj
```

Swagger UI is enabled at `/swagger`.
Health check endpoint is available at `/health`.

## Example Request

**Curl Command:**
```bash
curl -X POST "http://localhost:5000/api/chat" \
  -H "Content-Type: application/json" \
  -d '{
    "message": "give me report for pump 3 for last 2 days",
    "sessionId": "session-101"
  }'
```

**Response (200 OK):**
```json
{
  "reply": "Guard passed. Report generation is not implemented yet.",
  "sessionId": "session-101",
  "isOnTopic": true
}
```
