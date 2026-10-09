namespace Biolog.Features.AuthenticityAssessment.Model;

/// <summary>
/// One reported check (BIO-DOC-009-AC01). Locked down the same way as every prior BIO-DOC-00x
/// result type this epic uses (<c>internal init</c>, no public constructor) - a public <c>init</c>
/// would let a caller launder a <see cref="AuthenticityCheckOutcome.Failed"/> or
/// <see cref="AuthenticityCheckOutcome.NotPerformed"/> check into a
/// <see cref="AuthenticityCheckOutcome.Passed"/> one via <c>with</c> without
/// <see cref="AuthenticityPolicyEngine.Evaluate"/> ever actually deciding that.
/// </summary>
public sealed record AuthenticityCheckResult
{
    public AuthenticityCheckId CheckId { get; internal init; }
    public AuthenticityCheckOutcome Outcome { get; internal init; }

    /// <summary>Human-readable, always non-blank - explains WHY this outcome was reached (AC01's
    /// "explainable"), not just what it is.</summary>
    public string Detail { get; internal init; } = null!;

    internal AuthenticityCheckResult(AuthenticityCheckId checkId, AuthenticityCheckOutcome outcome, string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            throw new ArgumentException("detail must not be blank - every check must explain its own outcome", nameof(detail));
        }

        CheckId = checkId;
        Outcome = outcome;
        Detail = detail;
    }

    /// <summary>AC03: the sanctioned way to report an unsupported/unattempted check - never
    /// construct <see cref="AuthenticityCheckOutcome.NotPerformed"/> ad hoc elsewhere, so every
    /// "not performed" reason reads the same way.</summary>
    internal static AuthenticityCheckResult NotPerformed(AuthenticityCheckId checkId, string reason) =>
        new(checkId, AuthenticityCheckOutcome.NotPerformed, reason);
}
