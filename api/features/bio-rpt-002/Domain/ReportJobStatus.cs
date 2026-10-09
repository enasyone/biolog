namespace Biolog.Features.ReportBuilder.Domain;

/// <summary>
/// BIO-RPT-002-AC04's job lifecycle. Every report - large or small - runs through this same
/// state machine (never a synchronous inline result), so "asynchronous" is a property of every
/// report, not a threshold-dependent special case, and "cancellable" is always meaningful:
/// <see cref="Queued"/> is a real, if usually brief, window; <see cref="Running"/> is
/// cancellable cooperatively (<see cref="ReportJobRunner"/>).
/// </summary>
public enum ReportJobStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled,
}

public static class ReportJobStatusExtensions
{
    public static string ToWireString(this ReportJobStatus status) => status switch
    {
        ReportJobStatus.Queued => "queued",
        ReportJobStatus.Running => "running",
        ReportJobStatus.Completed => "completed",
        ReportJobStatus.Failed => "failed",
        ReportJobStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };

    public static bool IsTerminal(this ReportJobStatus status) =>
        status is ReportJobStatus.Completed or ReportJobStatus.Failed or ReportJobStatus.Cancelled;
}
