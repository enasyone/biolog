namespace Biolog.Features.FingerprintEnrolment.Domain;

/// <summary>Domain-level error codes this feature raises. Plain strings, not an HTTP status -
/// this project has no ASP.NET dependency (same deferred boundary BIO-BIO-001's
/// BiometricServiceException documents).</summary>
public sealed class FingerprintEnrolmentException : InvalidOperationException
{
    public string Code { get; }

    private FingerprintEnrolmentException(string code, string message) : base(message)
    {
        Code = code;
    }

    public static FingerprintEnrolmentException SessionNotFound(string sessionId) =>
        new("fingerprint_enrolment_session_not_found", $"session '{sessionId}' does not exist.");

    public static FingerprintEnrolmentException ConcurrencyConflict(string sessionId, int expected, int actual) =>
        new("concurrency_conflict", $"session '{sessionId}' expected version {expected} but was {actual}.");

    public static FingerprintEnrolmentException SessionNotInProgress(string sessionId, EnrolmentSessionStatus status) =>
        new("fingerprint_enrolment_session_not_in_progress", $"session '{sessionId}' is '{status}', not IN_PROGRESS.");

    public static FingerprintEnrolmentException PositionNotTargetedByMode(FingerPosition position, EnrolmentMode mode) =>
        new("fingerprint_position_not_targeted_by_mode", $"'{position}' is not a target position for mode '{mode}' on this session.");

    public static FingerprintEnrolmentException IncompleteEnrolment(IReadOnlyList<FingerPosition> unresolved) =>
        new("fingerprint_enrolment_incomplete", $"cannot complete: unresolved positions [{string.Join(", ", unresolved)}].");

    public static FingerprintEnrolmentException SingleFingerPositionRequired() =>
        new("fingerprint_single_finger_position_required", "SingleFinger mode requires exactly one target position at session start.");

    public static FingerprintEnrolmentException PositionAlreadyMissingAmputated(FingerPosition position) =>
        new("fingerprint_position_already_missing_amputated", $"'{position}' is already resolved as missing/amputated and cannot be captured over.");
}
