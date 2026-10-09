namespace Biolog.Features.CardProduction.Domain;

/// <summary>
/// BIO-CRD-007-AC01's job lifecycle. A job starts <see cref="InProgress"/> and resolves once
/// every required step (print/encode/read-back-QC) has been recorded: <see cref="Passed"/> only
/// if all three passed, <see cref="Failed"/> if any one of them failed. <see cref="Rejected"/> is
/// a separate, explicit operator action (a card that passed every automated check but is
/// visually/physically flawed) - never derived from step outcomes alone.
/// <see cref="Cancelled"/> is a necessary, disclosed addition (mirrors BIO-CRD-005's own
/// "DeactivateAsync is a necessary, disclosed addition" precedent): an operator abandoning a job
/// queued in error, e.g. the wrong card was queued, needs a real way to close it out.
/// </summary>
public enum ProductionJobStatus
{
    InProgress,
    Passed,
    Failed,
    Rejected,
    Cancelled,
}

public static class ProductionJobStatusExtensions
{
    public static string ToWireString(this ProductionJobStatus status) => status switch
    {
        ProductionJobStatus.InProgress => "in_progress",
        ProductionJobStatus.Passed => "passed",
        ProductionJobStatus.Failed => "failed",
        ProductionJobStatus.Rejected => "rejected",
        ProductionJobStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };

    /// <summary>BIO-CRD-007-AC02: the ONLY status that may ever gate a subsequent activation.</summary>
    public static bool AllowsActivation(this ProductionJobStatus status) => status == ProductionJobStatus.Passed;

    /// <summary>A job may still receive step results or be rejected/cancelled only while in progress.</summary>
    public static bool IsOpen(this ProductionJobStatus status) => status == ProductionJobStatus.InProgress;
}
