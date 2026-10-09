using System.Globalization;
using System.Text.RegularExpressions;
using Biolog.Features.EvidenceComparison.Audit;

namespace Biolog.Features.EvidenceComparison.Model;

/// <summary>
/// The full result of comparing evidence across sources for one document session
/// (BIO-DOC-008-AC01 through AC05).
///
/// <para>
/// <b>AC05, "versioned/audited" (BIO-DOC-008-QA-01, Chioma's acceptance review):</b> a bare
/// caller-supplied integer and attribution strings are not, on their own, a validated version or
/// evidence an audit occurred - Chioma's review found the original design accepted arbitrary,
/// unvalidated version/actor/time values and wrote no durable audit event at all. Fixed:
/// <see cref="Compute"/> now (1) validates <c>versionNumber</c> is at least 1, <c>computedByActorId</c>
/// is non-blank, and <c>computedAt</c> is a full RFC 3339 date-time (BIO-DOC-008-QA-02, Seyi's
/// round-2 re-review: <c>DateTimeOffset.TryParse</c> alone is permissive enough to accept a bare
/// date like <c>"2026-09-23"</c>, which is not RFC 3339 despite parsing - <see cref="Rfc3339Pattern"/>
/// requires the full date-time-with-offset SHAPE before <c>TryParse</c> is ever consulted for
/// semantic validity) - a caller cannot forge an invalid version/actor/timestamp into a result
/// that looks valid - and (2) durably writes an
/// <see cref="EvidenceComparisonAuditEvent"/> to an <see cref="IEvidenceComparisonAuditSink"/>
/// BEFORE returning, using the same "audit-before-commit" pattern every hard-audited BIOLOG
/// feature uses (ADR-0002 §5 / ADR-0003; <c>TenantService.AuditAsync</c>'s ambiguous-write
/// resolution, mirrored here via <see cref="IEvidenceComparisonAuditSink.ExistsAsync"/>). A sink
/// failure that cannot be confirmed durable throws <see cref="EvidenceComparisonAuditUnavailableException"/>
/// - <see cref="Compute"/> NEVER returns a "successful" comparison whose audit write is unconfirmed.
/// <see cref="AuditEventId"/> lets a caller look up the exact durable event this result was
/// audited under.
/// </para>
///
/// <para>
/// <b>BIO-DOC-008-QA-03 (Chioma's third acceptance review):</b> a bare <c>versionNumber &gt;= 1</c>
/// range check is not controlled versioning - a caller could still label any two different
/// comparisons of the SAME document session version 1, or move backwards, or skip ahead
/// arbitrarily. <see cref="Compute"/> now requires an <see cref="IVersionStore"/> to check the
/// caller's claimed <c>versionNumber</c> against this session's own tracked sequence - it is
/// accepted only if it is EXACTLY the next expected value. This is the same monotonic-version
/// discipline ADR-0001 Decision 3 establishes for
/// <c>DocumentSession.Version</c>/<c>ParserRecord.ConcurrencyVersion</c>, and the same rule
/// BIO-DOC-006's <c>DocumentIntelligenceRegistry.Register</c> already applies to its own catalog
/// entries. A rejected version throws <see cref="StaleComparisonVersionException"/>.
/// </para>
///
/// <para>
/// <b>BIO-DOC-008-QA-04 (Chioma's fourth acceptance review):</b> the QA-03 fix committed the
/// version claim BEFORE the mandatory audit write - if <see cref="WriteAuditEventOrThrow"/> then
/// failed, <see cref="Compute"/> returned no comparison, but the version store's sequence had
/// already advanced. A retry of the SAME legitimate version then threw
/// <see cref="StaleComparisonVersionException"/>, and skipping to the next version instead left a
/// permanent hole for a result that never existed - an audit outage irrecoverably consumed trusted
/// sequence state. Fixed by switching to a genuine two-phase <see cref="IVersionStore.AllocateVersion"/>
/// / <see cref="IVersionStore.CommitVersion"/> / <see cref="IVersionStore.ReleaseVersion"/>
/// protocol: <see cref="Compute"/> reserves the version FIRST, then only calls
/// <see cref="IVersionStore.CommitVersion"/> AFTER the audit event is confirmed durable; if
/// anything after the reservation fails, the reservation is released (never committed) so the
/// same version number is legitimately reissuable to a retry with a healthy sink.
/// </para>
///
/// <para>
/// <b>Why <see cref="Compute"/> is synchronous despite calling into an async audit sink:</b> every
/// hard-audited BIOLOG feature's own sink interface is <c>Task</c>-returning
/// (<c>ITenantAuditSink.WriteAsync</c> etc.), and this story's <see cref="IEvidenceComparisonAuditSink"/>
/// matches that shape exactly - a later story wiring a real, durably-I/O-bound sink implementation
/// still satisfies the interface unchanged. <see cref="Compute"/> itself stays synchronous and
/// blocks on that call (<c>GetAwaiter().GetResult()</c>) rather than becoming
/// <c>async Task&lt;CrossComparisonResult&gt;</c>: this library has no HTTP/ASP.NET boundary
/// anywhere (no <c>SynchronizationContext</c> to deadlock against), and
/// <see cref="InMemoryEvidenceComparisonAuditSink"/> always completes synchronously today, so
/// blocking here carries none of sync-over-async's usual thread-pool-starvation risk. This is a
/// deliberate, proportionate choice for THIS story's synchronous domain-library shape, not a
/// general recommendation - a later story that wires a genuinely I/O-bound durable sink should
/// revisit whether <see cref="Compute"/> needs to become properly async at that point.
/// </para>
///
/// <para>
/// <b>AC03, "conflicts route to review":</b> <see cref="FieldsRequiringReview"/> names every field
/// whose <see cref="FieldComparison.Status"/> is <see cref="FieldMatchStatus.Mismatch"/>, explicit
/// rather than a bare count. <see cref="RequiresManualReview"/> is true whenever
/// <see cref="FieldsRequiringReview"/> is non-empty OR NFC passive authentication explicitly
/// failed (<see cref="NfcCorroboration.PassiveAuthenticationPassed"/> is <c>false</c>) - a genuine
/// authenticity concern that field-level agreement among the other sources cannot outweigh.
/// </para>
///
/// <para>Locked down (<c>internal init</c>, no public constructor) - the same reasoning as every
/// prior BIO-DOC-00x result type this epic: a public <c>init</c> would let a caller launder a
/// result with real mismatches into one that claims no review is needed.</para>
/// </summary>
public sealed partial record CrossComparisonResult
{
    // BIO-DOC-008-QA-02 (Seyi's round-2 re-review): DateTimeOffset.TryParse alone is permissive -
    // it accepts a bare date like "2026-09-23" (defaulting the missing time/offset components),
    // which is NOT an RFC 3339 date-time despite parsing successfully. This pattern requires the
    // full RFC 3339 date-time shape (date, literal 'T', time, and an explicit 'Z' or numeric
    // offset) before TryParse is ever consulted for semantic validity (real calendar date, valid
    // time-of-day).
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex Rfc3339Pattern();


    public string DocumentSessionId { get; internal init; } = null!;
    public int VersionNumber { get; internal init; }
    public string ComputedByActorId { get; internal init; } = null!;
    public string ComputedAt { get; internal init; } = null!;

    /// <summary>The durable <see cref="EvidenceComparisonAuditEvent.EventId"/> this result was
    /// audited under - confirmed durable before this result was ever returned.</summary>
    public string AuditEventId { get; internal init; } = null!;

    public IReadOnlyList<FieldComparison> FieldComparisons { get; internal init; } = [];
    public IReadOnlyList<string> FieldsRequiringReview { get; internal init; } = [];
    public NfcCorroboration NfcCorroboration { get; internal init; } = Model.NfcCorroboration.NotAvailable;
    public bool RequiresManualReview { get; internal init; }

    internal CrossComparisonResult(
        string documentSessionId,
        int versionNumber,
        string computedByActorId,
        string computedAt,
        string auditEventId,
        IReadOnlyList<FieldComparison> fieldComparisons,
        IReadOnlyList<string> fieldsRequiringReview,
        NfcCorroboration nfcCorroboration,
        bool requiresManualReview)
    {
        DocumentSessionId = documentSessionId;
        VersionNumber = versionNumber;
        ComputedByActorId = computedByActorId;
        ComputedAt = computedAt;
        AuditEventId = auditEventId;
        // Fresh copies wrapped in AsReadOnly() - the BIO-DOC-007-QA-02/QA-03 lesson applied
        // proactively, not the caller's own mutable lists viewed through an interface.
        FieldComparisons = new List<FieldComparison>(fieldComparisons).AsReadOnly();
        FieldsRequiringReview = new List<string>(fieldsRequiringReview).AsReadOnly();
        NfcCorroboration = nfcCorroboration;
        RequiresManualReview = requiresManualReview;
    }

    /// <summary>
    /// Computes a cross-comparison and durably audits it before returning (BIO-DOC-008-AC05).
    /// </summary>
    /// <param name="correlationId">A non-blank caller-supplied tracing id for the audit event -
    /// this domain library has no HTTP boundary of its own to mint one from.</param>
    public static CrossComparisonResult Compute(
        string documentSessionId,
        int versionNumber,
        string computedByActorId,
        string computedAt,
        string correlationId,
        IReadOnlyList<FieldComparison> fieldComparisons,
        NfcCorroboration nfcCorroboration,
        IEvidenceComparisonAuditSink auditSink,
        IVersionStore versionStore)
    {
        // Cheap, side-effect-free input checks first - none of these mutate versionStore or
        // auditSink, so a rejected call here never consumes a version number a caller could
        // legitimately retry with (BIO-DOC-008-QA-03's remediation must not itself introduce a
        // new way to burn a version on an unrelated validation failure).
        if (versionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(versionNumber), versionNumber, "versionNumber must be at least 1");
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

        // Now the version claim itself - RESERVED, not yet committed. BIO-DOC-008-QA-04: committing
        // here (before the audit write below) let an audit failure permanently burn the version.
        // The reservation is only finalized via CommitVersion once the audit event is confirmed
        // durable; any failure between here and CommitVersion MUST release the reservation (see
        // the try/catch below) so the same version number is legitimately reissuable.
        if (!versionStore.AllocateVersion(documentSessionId, versionNumber))
        {
            throw new StaleComparisonVersionException(
                $"version {versionNumber} is not the next expected version for document session '{documentSessionId}' - it may be a duplicate, a skipped, or a backward version, or another reservation for this session has not yet resolved");
        }

        try
        {
            var fieldsRequiringReview = fieldComparisons
                .Where(f => f.Status == FieldMatchStatus.Mismatch)
                .Select(f => f.FieldName)
                .ToList();

            var requiresManualReview = fieldsRequiringReview.Count > 0 || nfcCorroboration.PassiveAuthenticationPassed == false;

            // ADR-0003: "structured, never a bare string" - built internally from the caller's plain
            // id; this feature does not know the actor's TYPE from a bare string, so it is honestly
            // recorded as Unknown rather than guessed.
            var actor = new AuditActor(ActorType.Unknown, computedByActorId);

            var auditEvent = EvidenceComparisonAuditEventBuilder.Build(
                actor,
                documentSessionId,
                reasonCode: requiresManualReview ? "comparison_requires_review" : "comparison_computed",
                correlationId,
                fieldCount: fieldComparisons.Count,
                mismatchCount: fieldsRequiringReview.Count,
                requiresManualReview,
                outcome: AuditOutcome.Allowed);

            WriteAuditEventOrThrow(auditSink, auditEvent);

            // Only now, with the audit event confirmed durable, does the version claim become real.
            versionStore.CommitVersion(documentSessionId, versionNumber);

            return new CrossComparisonResult(
                documentSessionId,
                versionNumber,
                computedByActorId,
                computedAt,
                auditEvent.EventId,
                fieldComparisons,
                fieldsRequiringReview,
                nfcCorroboration,
                requiresManualReview);
        }
        catch
        {
            // The reservation never became durable - release it so a retry of the SAME version
            // number (once the underlying problem, e.g. a sink outage, is resolved) can succeed.
            versionStore.ReleaseVersion(documentSessionId, versionNumber);
            throw;
        }
    }

    /// <summary>The same ambiguous-write resolution every hard-audited BIOLOG feature's own
    /// <c>AuditAsync</c> helper uses: a write that throws is not, on its own, proof nothing was
    /// persisted - only a confirmed-absent <see cref="IEvidenceComparisonAuditSink.ExistsAsync"/>
    /// (or a failure to even check) is treated as a genuine audit failure.</summary>
    private static void WriteAuditEventOrThrow(IEvidenceComparisonAuditSink auditSink, EvidenceComparisonAuditEvent auditEvent)
    {
        try
        {
            auditSink.WriteAsync(auditEvent).GetAwaiter().GetResult();
            return; // confirmed: the write completed without throwing
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
            throw new EvidenceComparisonAuditUnavailableException("audit sink unavailable: could not even verify a prior write", ex);
        }

        if (!exists)
        {
            throw new EvidenceComparisonAuditUnavailableException("audit sink unavailable: write did not durably persist");
        }

        // else: confirmed durably present despite the throw on WriteAsync - proceed as a success.
    }
}
