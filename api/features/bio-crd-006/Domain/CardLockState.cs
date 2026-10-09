namespace Biolog.Features.CardLifecycle.Domain;

/// <summary>
/// BIO-CRD-006's own lock state layered over a BIO-CRD-005 physical card issue - deliberately
/// NOT stored on that story's own <c>CardIssueStatus</c> (owned by a different, already-shipped
/// story this story must not reopen). No record at all means "never reported" (the common
/// case); a record only exists once a card has been reported, or swept, at least once.
///
/// <b>Why a separate layer, not BIO-CRD-005's own <c>Deactivated</c> status.</b>
/// <c>CardIssueStatus.Deactivated</c> is a ONE-WAY terminal state there (no operation ever
/// moves a card back out of it) - reusing it for AC01's "report" would make AC02's "recovery
/// may reactivate" impossible without reopening BIO-CRD-005's own state machine, which this
/// story has no standing to do. This enum is what stays freely reversible
/// (<see cref="Locked"/> ↔ <see cref="Recovered"/>) right up until AC04's replacement
/// activation makes it genuinely permanent (<see cref="PermanentlyRevoked"/>), which BIO-CRD-005's
/// own <c>Deactivated</c> already, separately, and best-effort also reflects (see
/// <see cref="CardLifecycleService"/>'s activate-replacement sweep).
///
/// <b>BIO-CRD-006-QA-03 (Chioma's acceptance review round 3).</b> <see cref="RevocationPending"/>
/// and <see cref="Unlocked"/> exist so the earlier-card sweep can be a genuine compensating
/// transaction against BIO-CRD-005's own independently-locked, authoritative
/// <c>ActivateAsync</c> compare-and-set - see <see cref="CardLifecycleService"/>'s own header for
/// why this project cannot instead add a true cross-service reservation primitive there.
/// </summary>
public enum CardLockState
{
    /// <summary>AC01: reported lost/missing/stolen/damaged - presentation must deny.</summary>
    Locked,

    /// <summary>AC02: recovered before a replacement activated - presentation is allowed again.</summary>
    Recovered,

    /// <summary>
    /// BIO-CRD-006-QA-03: this card is being swept as part of an in-flight
    /// activate-replacement attempt whose own authoritative activation has not yet been
    /// confirmed - presentation must still deny (conservative), and it is not recoverable
    /// (its fate is not yet decided), but it is NOT yet <see cref="PermanentlyRevoked"/>.
    /// Resolves forward to <see cref="PermanentlyRevoked"/> once activation is confirmed, or
    /// back to whichever state preceded it (recorded on the record itself) if activation fails -
    /// never a silent delete, matching this codebase's "no delete operation, ever" convention.
    /// </summary>
    RevocationPending,

    /// <summary>AC04: a later replacement for this (person, card class) has activated - terminal, never reversible.</summary>
    PermanentlyRevoked,

    /// <summary>
    /// BIO-CRD-006-QA-03: the compensated outcome of a <see cref="RevocationPending"/> sweep
    /// whose triggering activation ultimately failed (e.g. a concurrent mutation raced
    /// BIO-CRD-005's own authoritative compare-and-set) for a card that had NO prior report -
    /// behaves exactly like "never reported" for every purpose (presentation allowed, nothing
    /// to recover), but leaves a durable record of the attempt rather than silently vanishing.
    /// </summary>
    Unlocked,
}

public static class CardLockStateExtensions
{
    public static string ToWireString(this CardLockState state) => state switch
    {
        CardLockState.Locked => "locked",
        CardLockState.Recovered => "recovered",
        CardLockState.RevocationPending => "revocation_pending",
        CardLockState.PermanentlyRevoked => "permanently_revoked",
        CardLockState.Unlocked => "unlocked",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "unreachable: exhaustive switch"),
    };
}
