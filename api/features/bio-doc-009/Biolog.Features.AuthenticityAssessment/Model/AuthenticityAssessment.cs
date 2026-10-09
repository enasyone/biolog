using Biolog.Features.AuthenticityAssessment.Audit;

namespace Biolog.Features.AuthenticityAssessment.Model;

/// <summary>
/// The mutable, CAS-versioned aggregate a document session's authenticity assessment lives as
/// (BIO-DOC-009-AC05). Wraps an immutable <see cref="AuthenticityAssessmentResult"/> ("raw
/// evidence" - the individual checks AC01 reports) plus an optional
/// <see cref="AuthenticityManualOverride"/>.
///
/// <para>
/// <b>AC05, "manual review can override workflow disposition but cannot rewrite raw evidence" -
/// enforced structurally:</b> <see cref="ApplyManualOverride"/> never assigns to
/// <see cref="Result"/> at all - the only field it writes is <see cref="Override"/>, a wholly
/// separate reference. <see cref="Result"/>'s own <see cref="AuthenticityCheckResult"/> entries are
/// themselves <c>internal init</c>-locked records with no public mutator anywhere, so even a caller
/// holding a direct reference to <see cref="Result"/> cannot rewrite it. <see cref="EffectiveDisposition"/>
/// is the one place the override actually changes downstream behaviour: it is read by any code
/// that needs "what should the workflow do now", while <see cref="Result.PolicyDisposition"/> stays
/// exactly what the policy engine computed, permanently, for audit/dispute purposes.
/// </para>
///
/// <para>
/// <see cref="ReplaceResult"/> (used when evidence changes and the assessment is recomputed) is a
/// DIFFERENT action from overriding: it installs a brand-new <see cref="AuthenticityAssessmentResult"/>
/// (itself never mutated after construction) and deliberately does NOT clear any existing
/// <see cref="Override"/> - a human's override decision persists across a recompute unless a
/// caller explicitly calls <see cref="ClearOverride"/>, since "new evidence arrived" and "the human
/// changed their mind about the override" are independent events.
/// </para>
/// </summary>
public sealed class AuthenticityAssessment
{
    private readonly object _gate = new();

    public string DocumentSessionId { get; }
    public AuthenticityAssessmentResult Result { get; private set; }
    public AuthenticityManualOverride? Override { get; private set; }
    public int Version { get; private set; }

    /// <summary>What the workflow should act on right now: the override's disposition if one is
    /// active, otherwise the policy engine's own computed disposition.</summary>
    public AuthenticityDisposition EffectiveDisposition => Override?.Disposition ?? Result.PolicyDisposition;

    private AuthenticityAssessment(AuthenticityAssessmentResult result)
    {
        DocumentSessionId = result.DocumentSessionId;
        Result = result;
        Override = null;
        Version = 1;
    }

    public static AuthenticityAssessment FromResult(AuthenticityAssessmentResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new AuthenticityAssessment(result);
    }

    /// <summary>Installs a freshly recomputed <see cref="AuthenticityAssessmentResult"/> for this
    /// same document session (e.g. new evidence arrived). Never mutates the OLD result in place -
    /// a distinct object replaces it, so any caller still holding a reference to the previous
    /// <see cref="Result"/> continues to see exactly what was true when they read it.</summary>
    public void ReplaceResult(AuthenticityAssessmentResult newResult, int expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(newResult);
        if (newResult.DocumentSessionId != DocumentSessionId)
        {
            throw new ArgumentException(
                $"newResult.DocumentSessionId ('{newResult.DocumentSessionId}') does not match this assessment's own session ('{DocumentSessionId}')",
                nameof(newResult));
        }

        lock (_gate)
        {
            if (expectedVersion != Version)
            {
                throw new AuthenticityAssessmentConcurrencyException(
                    $"expected version {expectedVersion} but the current version for document session '{DocumentSessionId}' is {Version}");
            }

            Result = newResult;
            Version += 1;
        }
    }

    /// <summary>BIO-DOC-009-AC05. Audited (<see cref="IAuthenticityAssessmentAuditSink"/>) before
    /// returning, the same audit-before-commit discipline <see cref="AuthenticityAssessmentResult.Compute"/>
    /// uses.</summary>
    public void ApplyManualOverride(
        AuthenticityDisposition disposition,
        string reason,
        string overriddenBy,
        string overriddenAt,
        string correlationId,
        int expectedVersion,
        IAuthenticityAssessmentAuditSink auditSink)
    {
        ArgumentNullException.ThrowIfNull(auditSink);
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("reason must not be blank - an override must explain itself", nameof(reason));
        }

        if (string.IsNullOrWhiteSpace(overriddenBy))
        {
            throw new ArgumentException("overriddenBy must not be blank", nameof(overriddenBy));
        }

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new ArgumentException("correlationId must not be blank", nameof(correlationId));
        }

        lock (_gate)
        {
            if (expectedVersion != Version)
            {
                throw new AuthenticityAssessmentConcurrencyException(
                    $"expected version {expectedVersion} but the current version for document session '{DocumentSessionId}' is {Version}");
            }

            var originalDisposition = EffectiveDisposition;
            var actor = new AuditActor(ActorType.Unknown, overriddenBy);
            var auditEvent = AuthenticityAssessmentAuditEventBuilder.BuildManuallyOverridden(
                actor, DocumentSessionId, correlationId, originalDisposition, disposition, reason);

            WriteAuditEventOrThrow(auditSink, auditEvent);

            // Result is deliberately untouched here - see this type's own remarks.
            Override = new AuthenticityManualOverride(disposition, reason, overriddenBy, overriddenAt);
            Version += 1;
        }
    }

    public void ClearOverride(int expectedVersion)
    {
        lock (_gate)
        {
            if (expectedVersion != Version)
            {
                throw new AuthenticityAssessmentConcurrencyException(
                    $"expected version {expectedVersion} but the current version for document session '{DocumentSessionId}' is {Version}");
            }

            Override = null;
            Version += 1;
        }
    }

    private static void WriteAuditEventOrThrow(IAuthenticityAssessmentAuditSink auditSink, AuthenticityAssessmentAuditEvent auditEvent)
    {
        try
        {
            auditSink.WriteAsync(auditEvent).GetAwaiter().GetResult();
            return;
        }
        catch (Exception)
        {
            // Ambiguous - fall through to verify rather than assume failure.
        }

        bool exists;
        try
        {
            exists = auditSink.ExistsAsync(auditEvent.EventId).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            throw new AuthenticityAssessmentAuditUnavailableException("audit sink unavailable: could not even verify a prior write", ex);
        }

        if (!exists)
        {
            throw new AuthenticityAssessmentAuditUnavailableException("audit sink unavailable: write did not durably persist");
        }
    }
}
