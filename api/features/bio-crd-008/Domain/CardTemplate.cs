using Biolog.Features.CardTemplates.Audit;
using Biolog.Features.CardTemplates.Identifiers;

namespace Biolog.Features.CardTemplates.Domain;

/// <summary>
/// One version row within a card-template lineage (BIO-CRD-008). <see cref="TemplateId"/> is the
/// stable lineage id (shared by every version); <see cref="Version"/> is this row's own
/// immutable, monotonically-increasing identity within that lineage, assigned once at creation
/// and never reused - the exact pair AC04 requires production to reference.
/// <see cref="Revision"/> is a SEPARATE optimistic-concurrency token (ADR-0001 §3.2) for
/// in-place edits to this one row while it is still Draft/Validation/Approval - it has nothing
/// to do with the lifecycle-facing <see cref="Version"/> number.
///
/// <b>AC01/AC02: immutability and versioning are two different mechanisms.</b> While
/// <see cref="Status"/> is Draft/Validation/Approval, <see cref="Edit"/> mutates THIS row's
/// <see cref="Snapshot"/> in place (bumping <see cref="Revision"/>) - normal pre-publish
/// iteration, nothing has been published yet so nothing needs preserving. Once
/// <see cref="Status"/> reaches Published or Retired, <see cref="Edit"/> always refuses
/// (<see cref="CardTemplateStatusExtensions.IsImmutable"/>) - the only way to change content
/// from there is <see cref="CreateNextDraftVersion"/>, which produces a brand NEW row (a new
/// <see cref="Version"/> under the same <see cref="TemplateId"/>), leaving this one exactly as
/// it was, forever.
///
/// Functional/immutable record (mirrors BIO-CRD-007's own <c>ProductionJob</c> shape): every
/// domain method returns a NEW <see cref="CardTemplate"/> instance or throws
/// <see cref="CardTemplateError"/>; the repository performs the actual CAS-checked persistence.
///
/// <b>Construction is internal</b> (BIO-IDN-001-QA-01's lesson, applied from the start).
/// </summary>
public sealed record CardTemplate
{
    public string TenantId { get; }
    public string TemplateId { get; }
    public int Version { get; }

    public required int Revision { get; init; }
    public required CardTemplateStatus Status { get; init; }
    public required string Name { get; init; }
    public required CardTemplateSnapshot Snapshot { get; init; }
    public required AuditActor CreatedBy { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }
    public string? PublishedAt { get; init; }
    public AuditActor? PublishedBy { get; init; }
    public IReadOnlyList<CardTemplateTransition> Transitions { get; init; } = [];

    internal CardTemplate(string tenantId, string templateId, int version)
    {
        if (!CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        if (!CanonicalId.IsTemplateId(templateId))
        {
            throw new ArgumentException($"'{templateId}' is not a well-formed template id (ADR-0001)", nameof(templateId));
        }

        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "a card template's version starts at 1");
        }

        TenantId = tenantId;
        TemplateId = templateId;
        Version = version;
    }

    /// <summary>AC01/AC02: in-place edit while not yet published. Refuses once <see cref="CardTemplateStatus.IsImmutable"/>.</summary>
    public CardTemplate Edit(CardTemplateSnapshot newSnapshot, AuditActor actor, string now)
    {
        if (Status.IsImmutable())
        {
            throw CardTemplateError.Immutable(TemplateId, Version, Status);
        }

        return this with
        {
            Snapshot = newSnapshot,
            Revision = Revision + 1,
            UpdatedAt = now,
            Transitions = [.. Transitions, new CardTemplateTransition { FromStatus = Status, ToStatus = Status, Actor = actor, At = now, Note = "edited" }],
        };
    }

    public CardTemplate SubmitForValidation(AuditActor actor, string now)
    {
        if (Status != CardTemplateStatus.Draft)
        {
            throw CardTemplateError.InvalidTransition(Status, "submit_for_validation");
        }

        return Transition(CardTemplateStatus.Validation, actor, now, null);
    }

    /// <summary>Validation -&gt; Approval when <paramref name="passed"/>, else back to Draft.</summary>
    public CardTemplate RecordValidation(bool passed, string? note, AuditActor actor, string now)
    {
        if (Status != CardTemplateStatus.Validation)
        {
            throw CardTemplateError.InvalidTransition(Status, "record_validation");
        }

        return Transition(passed ? CardTemplateStatus.Approval : CardTemplateStatus.Draft, actor, now, note);
    }

    public CardTemplate Reject(string? note, AuditActor actor, string now)
    {
        if (Status != CardTemplateStatus.Approval)
        {
            throw CardTemplateError.InvalidTransition(Status, "reject");
        }

        return Transition(CardTemplateStatus.Draft, actor, now, note);
    }

    /// <summary>AC01: from this point on, <see cref="Snapshot"/> is frozen forever.</summary>
    public CardTemplate Publish(AuditActor actor, string now)
    {
        if (Status != CardTemplateStatus.Approval)
        {
            throw CardTemplateError.InvalidTransition(Status, "publish");
        }

        return Transition(CardTemplateStatus.Published, actor, now, null) with { PublishedAt = now, PublishedBy = actor };
    }

    public CardTemplate Retire(AuditActor actor, string now)
    {
        if (Status != CardTemplateStatus.Published)
        {
            throw CardTemplateError.InvalidTransition(Status, "retire");
        }

        return Transition(CardTemplateStatus.Retired, actor, now, null);
    }

    /// <summary>AC02: "Edit creates new version." The only way to change a Published/Retired template's content - a brand new row, same lineage, next version number, starting fresh at Draft.</summary>
    public CardTemplate CreateNextDraftVersion(int nextVersion, CardTemplateSnapshot? overrideSnapshot, AuditActor actor, string now)
    {
        if (!Status.IsImmutable())
        {
            throw CardTemplateError.InvalidTransition(Status, "create_next_draft_version");
        }

        return new CardTemplate(TenantId, TemplateId, nextVersion)
        {
            Revision = 1,
            Status = CardTemplateStatus.Draft,
            Name = Name,
            Snapshot = overrideSnapshot ?? Snapshot,
            CreatedBy = actor,
            CreatedAt = now,
            UpdatedAt = now,
            Transitions = [new CardTemplateTransition { FromStatus = Status, ToStatus = CardTemplateStatus.Draft, Actor = actor, At = now, Note = $"created from version {Version}" }],
        };
    }

    private CardTemplate Transition(CardTemplateStatus to, AuditActor actor, string now, string? note) => this with
    {
        Status = to,
        Revision = Revision + 1,
        UpdatedAt = now,
        Transitions = [.. Transitions, new CardTemplateTransition { FromStatus = Status, ToStatus = to, Actor = actor, At = now, Note = note }],
    };
}
