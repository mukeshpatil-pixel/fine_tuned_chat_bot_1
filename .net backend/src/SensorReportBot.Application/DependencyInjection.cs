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
        // Sequential Chat Workflow Pipeline Steps
        // NOTE: GuardStep removed - IntentExtractionStep handles isOnTopic classification
        // in the same LLM call, eliminating the redundant second call and empty-response errors.
        services.AddScoped<IChatStep, SaveUserMessageStep>();
        services.AddScoped<IChatStep, LoadHistoryStep>();
        services.AddScoped<IChatStep, IntentExtractionStep>();
        services.AddScoped<IChatStep, PersistAssistantReplyStep>();

        services.AddScoped<ChatWorkflow>();
        return services;
    }
}
