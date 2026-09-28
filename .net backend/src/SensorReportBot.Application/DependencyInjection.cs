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
        services.AddScoped<IChatStep, LoadHistoryStep>();
        services.AddScoped<IChatStep, SaveUserMessageStep>();
        //Pulls the last few messages of the conversation history from the database and attaches it to the current state. This gives the LLM context (so it knows what was said previously).
        services.AddScoped<IChatStep, IntentExtractionStep>();
        //This is the "Brain" of the operation. It takes the user's message + the conversation history and sends it to the LLM via LlmService. The LLM figures out if the user is asking about an asset, extracts the dates (fromDate/toDate), and generates the natural text reply.
// Note: This step populates the state.Reply property.
        services.AddScoped<IChatStep, PersistAssistantReplyStep>();

        services.AddScoped<ChatWorkflow>();
        return services;
    }
}
