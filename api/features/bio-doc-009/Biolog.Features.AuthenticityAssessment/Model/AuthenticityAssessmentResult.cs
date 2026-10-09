using System.Globalization;
using System.Text.RegularExpressions;
using Biolog.Features.AuthenticityAssessment.Audit;
using Biolog.Features.DocumentClassifier.Model;

namespace Biolog.Features.AuthenticityAssessment.Model;

/// <summary>
/// The full, immutable result of one authenticity assessment (BIO-DOC-009-AC01 through AC04) -
/// this story's "raw evidence" (BIO-DOC-009-AC05's own wording: what a manual override may never
/// rewrite, see <see cref="AuthenticityAssessment"/>).
///
/// <para>
/// <b>AC04, "evidence, template and ruleset versions are recorded":</b> <see cref="EvidenceVersion"/>
/// is BIO-DOC-008's own <c>CrossComparisonResult.VersionNumber</c> - the version of the underlying
/// evidence set this assessment was computed against (this story mints no separate version
/// sequence of its own; reusing 008's already-controlled sequence avoids a second, possibly
/// inconsistent counter for the same underlying evidence). <see cref="TemplateId"/>/
/// <see cref="TemplateVersionNumber"/> come from the winning classification candidate's own
/// <see cref="DocumentIntelligenceRegistryEntry"/> (BIO-DOC-006) - null when classification was not
/// performed or found no match. <see cref="RulesetVersion"/> is
/// <see cref="AuthenticityPolicyEngine.RulesetVersion"/>, recorded verbatim so a later audit can
/// tell which POLICY (not just which evidence) produced a given disposition.
/// </para>
///
/// <para>
/// <b>Audited before returning, the same "audit-before-commit" pattern every hard-audited BIOLOG
/// feature uses (ADR-0002 S5 / ADR-0003):</b> mirrors BIO-DOC-008's own <c>CrossComparisonResult.Compute</c>
/// exactly - actor/timestamp/correlation-id validated, a durable audit event written and confirmed
/// (via <see cref="IAuthenticityAssessmentAuditSink.ExistsAsync"/> if the write itself throws)
/// BEFORE this method ever returns a result. A sink failure that cannot be confirmed durable throws
/// <see cref="AuthenticityAssessmentAuditUnavailableException"/> - <see cref="Compute"/> NEVER
/// returns an assessment whose audit write is unconfirmed. Unlike <c>CrossComparisonResult.Compute</c>,
/// this method needs no two-phase version-reservation dance: it consumes 008's already-committed
/// <c>VersionNumber</c> rather than minting a new one of its own, so there is no version sequence
/// here for a failed audit write to burn.
/// </para>
///
/// <para>Locked down (<c>internal init</c>, no public constructor) - the same reasoning as every
/// prior BIO-DOC-00x result type this epic: a public <c>init</c> would let a caller launder a
/// result with real check failures into one that claims a clean disposition.</para>
/// </summary>
public sealed partial record AuthenticityAssessmentResult
{
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex Rfc3339Pattern();

    public string DocumentSessionId { get; internal init; } = null!;
    public string ComputedByActorId { get; internal init; } = null!;
    public string ComputedAt { get; internal init; } = null!;
    public string AuditEventId { get; internal init; } = null!;

    public int EvidenceVersion { get; internal init; }
    public string? TemplateId { get; internal init; }
    public int? TemplateVersionNumber { get; internal init; }
    public string RulesetVersion { get; internal init; } = null!;

    public IReadOnlyList<AuthenticityCheckResult> Checks { get; internal init; } = [];
    public AuthenticityDisposition PolicyDisposition { get; internal init; }

    internal AuthenticityAssessmentResult(
        string documentSessionId,
        string computedByActorId,
        string computedAt,
        string auditEventId,
        int evidenceVersion,
        string? templateId,
        int? templateVersionNumber,
        string rulesetVersion,
        IReadOnlyList<AuthenticityCheckResult> checks,
        AuthenticityDisposition policyDisposition)
    {
        DocumentSessionId = documentSessionId;
        ComputedByActorId = computedByActorId;
        ComputedAt = computedAt;
        AuditEventId = auditEventId;
        EvidenceVersion = evidenceVersion;
        TemplateId = templateId;
        TemplateVersionNumber = templateVersionNumber;
        RulesetVersion = rulesetVersion;
        // Fresh copy wrapped in AsReadOnly() - the BIO-DOC-007-QA-02/QA-03 lesson applied
        // proactively (BIO-DOC-008 already applies it too), not the caller's own mutable list.
        Checks = new List<AuthenticityCheckResult>(checks).AsReadOnly();
        PolicyDisposition = policyDisposition;
    }

    public static AuthenticityAssessmentResult Compute(
        AuthenticityAssessmentInputs inputs,
        string computedByActorId,
        string computedAt,
        string correlationId,
        IAuthenticityAssessmentAuditSink auditSink)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(auditSink);

        if (string.IsNullOrWhiteSpace(inputs.DocumentSessionId))
        {
            throw new ArgumentException("inputs.DocumentSessionId must not be blank", nameof(inputs));
        }

        if (string.IsNullOrWhiteSpace(computedByActorId))
        {
            throw new ArgumentException("computedByActorId must not be blank", nameof(computedByActorId));
        }

        if (!Rfc3339Pattern().IsMatch(computedAt)
            || !DateTimeOffset.TryParse(computedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new ArgumentException(
                $"computedAt must be a full RFC 3339 date-time with an explicit 'Z' or numeric offset (a bare date is not sufficient), got '{computedAt}'",
                nameof(computedAt));
        }

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new ArgumentException("correlationId must not be blank", nameof(correlationId));
        }

        var checks = AuthenticityPolicyEngine.BuildChecks(inputs);
        var disposition = AuthenticityPolicyEngine.DetermineDisposition(checks);

        var winningEntry = inputs.Classification is { Unknown: false } classification ? classification.Best?.RegistryEntry : null;
        var templateId = winningEntry?.TemplateId;
        var templateVersionNumber = winningEntry?.VersionNumber;

        var actor = new AuditActor(ActorType.Unknown, computedByActorId);
        var auditEvent = AuthenticityAssessmentAuditEventBuilder.BuildComputed(
            actor,
            inputs.DocumentSessionId,
            correlationId,
            disposition,
            checks,
            inputs.CrossComparison.VersionNumber,
            templateId,
            templateVersionNumber,
            AuthenticityPolicyEngine.RulesetVersion);

        WriteAuditEventOrThrow(auditSink, auditEvent);

        return new AuthenticityAssessmentResult(
            inputs.DocumentSessionId,
            computedByActorId,
            computedAt,
            auditEvent.EventId,
            inputs.CrossComparison.VersionNumber,
            templateId,
            templateVersionNumber,
            AuthenticityPolicyEngine.RulesetVersion,
            checks,
            disposition);
    }

    /// <summary>The same ambiguous-write resolution every hard-audited BIOLOG feature's own
    /// <c>AuditAsync</c> helper uses - see <c>CrossComparisonResult</c>'s identically-shaped
    /// private helper.</summary>
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
