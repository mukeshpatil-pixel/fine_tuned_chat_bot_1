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

        // 100% LLM Sequential Chat Pipeline:
        // 1. Load conversation history
        services.AddScoped<IChatStep, LoadHistoryStep>();
        // 2. Save incoming user message
        services.AddScoped<IChatStep, SaveUserMessageStep>();
        // 3. 100% LLM (qwen2.5:1.5b) extraction for EVERY turn (natural language, entities, timeframe, actions)
        services.AddScoped<IChatStep, IntentExtractionStep>();
        // 4. Persist assistant reply & metadata
        services.AddScoped<IChatStep, PersistAssistantReplyStep>();

        services.AddScoped<ChatWorkflow>();
        return services;
    }
}
