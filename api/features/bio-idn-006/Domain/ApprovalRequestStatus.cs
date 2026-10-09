namespace Biolog.Features.Rbac.Domain;

/// <summary>
/// BIO-IDN-006-AC03's maker-checker workflow. <see cref="Approved"/> and <see cref="Rejected"/>
/// are both terminal - a decided request is never re-decided (see
/// <see cref="ApprovalRequest"/>'s header for why a correction is a NEW request at the next
/// <see cref="ApprovalRequest.SequenceNumber"/>, mirroring BIO-IDN-003's
/// Retire-then-reassign pattern).
/// </summary>
public enum ApprovalRequestStatus
{
    Pending,
    Approved,
    Rejected,
}

public static class ApprovalRequestStatusExtensions
{
    public static string ToWireString(this ApprovalRequestStatus status) => status switch
    {
        ApprovalRequestStatus.Pending => "PENDING",
        ApprovalRequestStatus.Approved => "APPROVED",
        ApprovalRequestStatus.Rejected => "REJECTED",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };
}
