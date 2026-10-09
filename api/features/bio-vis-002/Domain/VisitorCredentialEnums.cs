namespace Biolog.Features.VisitorCredentials.Domain;

/// <summary>
/// AC02 "temporary credential has ... scope" - a small, synthetic, feature-local taxonomy (no AC
/// enumerates an exact scope list, so this story defines one rather than leave the field a bare
/// string). <see cref="General"/> is the default; the other two model the two realistic physical-
/// access shapes a visitor credential needs to distinguish for a reader/gate to act on later.
/// </summary>
public enum VisitorCredentialScope
{
    General,
    Escorted,
    RestrictedArea,
}

public static class VisitorCredentialScopeExtensions
{
    public static string ToWireString(this VisitorCredentialScope scope) => scope switch
    {
        VisitorCredentialScope.General => "general",
        VisitorCredentialScope.Escorted => "escorted",
        VisitorCredentialScope.RestrictedArea => "restricted_area",
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out VisitorCredentialScope scope)
    {
        switch (value)
        {
            case "general": scope = VisitorCredentialScope.General; return true;
            case "escorted": scope = VisitorCredentialScope.Escorted; return true;
            case "restricted_area": scope = VisitorCredentialScope.RestrictedArea; return true;
            default: scope = default; return false;
        }
    }
}

/// <summary>
/// AC02/AC03 - a credential's own persisted lifecycle, independent of the visit's own
/// <c>VisitStatus</c>. The only allowed transitions (enforced by
/// <see cref="InMemoryVisitorCredentialRepository.ReplaceAsync"/>): <see cref="Pending"/> -&gt;
/// <see cref="Granted"/>, <see cref="Pending"/> -&gt; <see cref="Failed"/>, <see cref="Granted"/>
/// -&gt; <see cref="Revoked"/>. <see cref="Revoked"/> and <see cref="Failed"/> are both terminal.
///
/// <b>Post-review remediation (BIO-VIS-002-F01, Seyi's FAIL on commit 45f4736).</b>
/// <see cref="Pending"/>/<see cref="Failed"/> did not exist before this round: every grant used to
/// go straight from "nothing persisted" to <see cref="Granted"/> in one commit AFTER the backing
/// BIO-CRD-005 card was already issued and activated, so a failure in that one final commit left
/// an Active card with no local row at all - the exact defect this status split closes. See
/// <see cref="VisitorCredentialService.GrantAsync"/>'s own header and
/// <c>docs/features/bio-vis-002/README.md</c> "Post-review remediation" for the full record.
/// </summary>
public enum VisitorCredentialStatus
{
    /// <summary>A grant attempt's own durable anchor, created BEFORE any BIO-CRD-005 call - never visible as "this visitor's active credential" (only <see cref="Granted"/> is).</summary>
    Pending,
    Granted,
    Revoked,
    /// <summary>Terminal: this grant attempt did not complete. <see cref="VisitorCredential.CardSequenceNumber"/> may still be set (a card was genuinely issued/activated before the failure) - see <see cref="VisitorCredential.FailureReason"/>.</summary>
    Failed,
}

public static class VisitorCredentialStatusExtensions
{
    public static string ToWireString(this VisitorCredentialStatus status) => status switch
    {
        VisitorCredentialStatus.Pending => "pending",
        VisitorCredentialStatus.Granted => "granted",
        VisitorCredentialStatus.Revoked => "revoked",
        VisitorCredentialStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };
}

/// <summary>
/// AC04 - the one place BIO-VIS-001's own <c>EffectiveVisitStatus</c> becomes this story's own,
/// genuinely richer overlay: a checked-in visitor past <c>ExpiresAt</c> is <see cref="Overstay"/>
/// (they arrived; something needs to happen about them being still here), never collapsed into
/// the same bare <see cref="Expired"/> value a never-arrived, lapsed preregistration also
/// produces (BIO-VIS-001's own <c>VisitorStatusEvaluator</c> treats both identically - the real
/// gap this story closes). A pure, read-time-only overlay - see
/// <see cref="VisitorOverstayEvaluator"/>.
/// </summary>
public enum VisitorEffectiveStatus
{
    Preregistered,
    CheckedIn,
    CheckedOut,
    Overstay,
    Expired,
}

public static class VisitorEffectiveStatusExtensions
{
    public static string ToWireString(this VisitorEffectiveStatus status) => status switch
    {
        VisitorEffectiveStatus.Preregistered => "preregistered",
        VisitorEffectiveStatus.CheckedIn => "checked-in",
        VisitorEffectiveStatus.CheckedOut => "checked-out",
        VisitorEffectiveStatus.Overstay => "overstay",
        VisitorEffectiveStatus.Expired => "expired",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };
}
