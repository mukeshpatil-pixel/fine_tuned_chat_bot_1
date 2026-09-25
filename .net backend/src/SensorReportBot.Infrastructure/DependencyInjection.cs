namespace SensorReportBot.Infrastructure;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SensorReportBot.Application.Interfaces;
using SensorReportBot.Infrastructure.Configuration;
using SensorReportBot.Infrastructure.Repositories;
using SensorReportBot.Infrastructure.Services;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind LLM and RabbitMQ configuration sections
        services.Configure<LlmOptions>(configuration.GetSection(LlmOptions.SectionName));
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));

        // Register HttpClient for LLM Service
        services.AddHttpClient<ILlmService, LlmService>();

        // System prompt provider (reads system prompts from Prompts/ directory)
        services.AddSingleton<IPromptProvider, FilePromptProvider>();

        // Primary Guard service (pure LLM/SLM classifier using structured JSON output)
        services.AddScoped<IGuardService, LlmGuardService>();

        // TimescaleDB Telemetry Repository
        services.AddScoped<ITelemetryRepository, TelemetryRepository>();

        // PostgreSQL Chat History Repository & Conversational AI Chat Service
        services.AddScoped<IChatHistoryRepository, ChatHistoryRepository>();
        services.AddScoped<IChatService, ChatService>();

        // QuestPDF Report Service
        services.AddSingleton<IPdfReportService, QuestPdfReportService>();

        // PDF Generation Asynchronous RabbitMQ Job Queue & Background Service
        services.AddSingleton<IPdfJobQueue, RabbitMqPdfJobQueue>();
        services.AddHostedService<PdfGenerationBackgroundService>();

        return services;
    }
}
