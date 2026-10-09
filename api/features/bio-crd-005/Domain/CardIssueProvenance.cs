namespace Biolog.Features.CardIssuance.Domain;

/// <summary>
/// BIO-CRD-005-AC05: "Card provenance records template/photo/logo/signature/operator/printer/
/// time." Captured once, at issuance (<see cref="CardIssue"/> construction), and immutable
/// thereafter - a fixed snapshot of what was used to produce THIS physical card, not a
/// live-updating reference to whatever the template/photo/logo/signature currently are. If a
/// later card uses a different template/photo/logo/signature, that is a NEW card issue with
/// its own provenance, not a correction to this one - the same "correcting an immutable record
/// means appending a new one" principle every append-only record in this codebase already
/// follows.
///
/// Every reference field is an opaque string, never resolved or validated here - "BIO-CRD-001
/// owns real asset validation" for brand assets/logos, BIO-PHO-001 for photos, BIO-CRD-004 for
/// signature versions and card templates are BIO-CRD-002/003's own concern. This story only
/// records WHICH references were used to produce this physical card, for later audit/dispute
/// resolution - it does not re-verify that they still exist or are still valid.
/// </summary>
public sealed record CardIssueProvenance
{
    /// <summary>Opaque reference to the card template/design used (BIO-CRD-002/003's own concern).</summary>
    public required string TemplateRef { get; init; }

    /// <summary>Opaque reference to the photo master/version used (BIO-PHO-001's own concern). Null if this card class carries no photo.</summary>
    public string? PhotoRef { get; init; }

    /// <summary>Opaque reference to the logo/brand asset used (BIO-CRD-001/004's own concern). Null if this card carries no logo.</summary>
    public string? LogoRef { get; init; }

    /// <summary>Opaque reference to the signature asset version used (BIO-CRD-004's own concern). Null if this card carries no signature.</summary>
    public string? SignatureRef { get; init; }

    /// <summary>Who performed the issuance - structured actor, never a bare string (ADR-0003), matching every other audit-attributed action in this codebase.</summary>
    public required Audit.AuditActor Operator { get; init; }

    /// <summary>Opaque reference identifying the printer/production station used. Never resolved or validated here.</summary>
    public required string PrinterRef { get; init; }

    /// <summary>When this physical card was issued (UTC instant, ADR-0001 §2.1). Distinct from <see cref="CardIssue.CreatedAt"/> only in principle - in this in-memory implementation they are the same instant, set once by <see cref="CardIssueService.IssueAsync"/>.</summary>
    public required string IssuedAt { get; init; }
}
