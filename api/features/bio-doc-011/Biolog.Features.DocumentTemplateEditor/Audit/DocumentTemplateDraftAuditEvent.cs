namespace Biolog.Features.DocumentTemplateEditor.Audit;

/// <summary>
/// One append-only audit envelope (ADR-0003 Decision 1), scoped to this feature's draft
/// lifecycle transitions (create/attach_reference_layout/define_regions/submit_for_test/
/// record_corpus_run/publish/create_next_version) - the story's own "provenance" requirement.
/// No <c>version</c> field on the envelope itself, ever (ADR-0001 §3.3); the draft's own
/// <c>version</c> number travels in <see cref="AuditTarget"/>.
/// </summary>
public sealed record DocumentTemplateDraftAuditEvent
{
    public required string EventId { get; init; }
    public int SchemaVersion { get; init; } = 1;
    public required string TenantId { get; init; }
    public required AuditActor Actor { get; init; }
    public required string Action { get; init; }
    public required AuditTarget Target { get; init; }
    public required AuditOutcome Outcome { get; init; }
    public required string ReasonCode { get; init; }
    public required string CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public required string OccurredAt { get; init; }
    public required string ReceivedAt { get; init; }
    public required string Classification { get; init; }
    public IReadOnlyList<string> Withheld { get; init; } = [];
    public IReadOnlyDictionary<string, object?> Detail { get; init; } = new Dictionary<string, object?>();
}
