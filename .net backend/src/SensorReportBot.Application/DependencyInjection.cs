namespace SensorReportBot.Application;

using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SensorReportBot.Application.Interfaces;
using SensorReportBot.Application.Steps;
using SensorReportBot.Application.Workflows;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        // Hybrid Sequential Chat Workflow Pipeline:
        // 1. Load prior conversation history
        services.AddScoped<IChatStep, LoadHistoryStep>();
        // 2. Save current user message
        services.AddScoped<IChatStep, SaveUserMessageStep>();
        // 3. Fast-path deterministic resolver for instant (<5ms) button clicks / clear intents
        services.AddScoped<IChatStep, RuleBasedIntentStep>();
        // 4. Local LLM (qwen2.5:1.5b) extraction for natural, complex, and conversational messages
        services.AddScoped<IChatStep, IntentExtractionStep>();
        // 5. Persist assistant reply to database
        services.AddScoped<IChatStep, PersistAssistantReplyStep>();

        services.AddScoped<ChatWorkflow>();
        return services;
    }
}
