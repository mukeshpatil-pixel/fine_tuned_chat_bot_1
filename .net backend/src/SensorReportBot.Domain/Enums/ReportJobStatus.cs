namespace SensorReportBot.Domain.Enums;

public enum ReportJobStatus
{
    Queued,
    Processing,
    Completed,
    Failed,
    Cancelled
}
