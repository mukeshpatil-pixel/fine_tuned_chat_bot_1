namespace SensorReportBot.Infrastructure.Services;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SensorReportBot.Application.Interfaces;

public class FilePromptProvider : IPromptProvider
{
    public async Task<string> GetPromptAsync(string promptName, CancellationToken ct = default)
    {
        string fileName = promptName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? promptName : $"{promptName}.txt";
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Prompts", fileName);

        if (!File.Exists(path))
        {
            path = Path.Combine(Directory.GetCurrentDirectory(), "src", "SensorReportBot.Infrastructure", "Prompts", fileName);
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"System prompt '{fileName}' not found.");
        }

        return await File.ReadAllTextAsync(path, ct);
    }
}
