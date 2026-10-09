using Biolog.Features.CardTemplates.Audit;
using Biolog.Features.CardTemplates.Domain;
using Biolog.Features.CardTemplates.Identifiers;
using Biolog.Features.CardTemplates.Tenancy;

namespace Biolog.Features.CardTemplates;

/// <summary>
/// The application-facing surface this feature's endpoints call, the same role BIO-CRD-007's own
/// <c>ProductionService</c>/BIO-RPT-002's own <c>ReportService</c> play for their own features.
/// Resolves the current tenant itself rather than trusting a caller-supplied one; every mutation
/// audits BEFORE persisting (ADR-0003 Decision 7) - the story title's own "provenance"
/// requirement lives here, not as an afterthought.
/// </summary>
internal sealed class CardTemplateService(
    ICurrentTenantProvider tenantProvider,
    ICardTemplateRepository repository,
    ISignatureAssetGateway signatureGateway,
    ICardTemplateAuditSink auditSink)
{
    public IReadOnlyList<CardTemplateLineageSummary> ListLineages() => repository.ListLineages(RequireTenant());

    public IReadOnlyList<CardTemplate> ListVersions(string templateId) => repository.ListVersions(RequireTenant(), templateId);

    public CardTemplate GetVersion(string templateId, int version) =>
        repository.GetVersion(RequireTenant(), templateId, version) ?? throw CardTemplateError.NotFound(templateId, version);

    /// <summary>AC04: the one sanctioned source of "the exact published version" - never returns a Draft/Validation/Approval/Retired row.</summary>
    public CardTemplate GetPublished(string templateId) =>
        repository.GetPublished(RequireTenant(), templateId) ?? throw CardTemplateError.NoPublishedVersion(templateId);

    public async Task<CardTemplate> CreateDraftAsync(string name, CardTemplateSnapshot snapshot, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        CardTemplateSnapshotValidator.Validate(snapshot, tenantId, signatureGateway);

        var now = UtcInstant.Now();
        var template = new CardTemplate(tenantId, CanonicalId.NewTemplateId(), version: 1)
        {
            Revision = 1,
            Status = CardTemplateStatus.Draft,
            Name = name,
            Snapshot = snapshot,
            CreatedBy = actor,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await AuditAsync(tenantId, actor, "card_template.create_draft", template.TemplateId, template.Version, AuditOutcome.Allowed, "card_template_draft_created", correlationId, cancellationToken);
        return repository.Add(template);
    }

    public Task<CardTemplate> EditAsync(string templateId, int version, string? name, CardTemplateSnapshot snapshot, int expectedRevision, AuditActor actor, string correlationId, CancellationToken cancellationToken = default) =>
        MutateAsync(templateId, version, expectedRevision, actor, correlationId, "card_template.edit", "card_template_edited", cancellationToken, current =>
        {
            CardTemplateSnapshotValidator.Validate(snapshot, current.TenantId, signatureGateway);
            var edited = current.Edit(snapshot, actor, UtcInstant.Now());
            return string.IsNullOrWhiteSpace(name) ? edited : edited with { Name = name };
        });

    public Task<CardTemplate> SubmitForValidationAsync(string templateId, int version, int expectedRevision, AuditActor actor, string correlationId, CancellationToken cancellationToken = default) =>
        MutateAsync(templateId, version, expectedRevision, actor, correlationId, "card_template.submit_for_validation", "card_template_submitted", cancellationToken,
            current => current.SubmitForValidation(actor, UtcInstant.Now()));

    public Task<CardTemplate> RecordValidationAsync(string templateId, int version, bool passed, string? note, int expectedRevision, AuditActor actor, string correlationId, CancellationToken cancellationToken = default) =>
        MutateAsync(templateId, version, expectedRevision, actor, correlationId, "card_template.record_validation", passed ? "card_template_validation_passed" : "card_template_validation_failed", cancellationToken,
            current => current.RecordValidation(passed, note, actor, UtcInstant.Now()));

    public Task<CardTemplate> RejectAsync(string templateId, int version, string? note, int expectedRevision, AuditActor actor, string correlationId, CancellationToken cancellationToken = default) =>
        MutateAsync(templateId, version, expectedRevision, actor, correlationId, "card_template.reject", "card_template_rejected", cancellationToken,
            current => current.Reject(note, actor, UtcInstant.Now()));

    /// <summary>AC01: freezes <see cref="CardTemplate.Snapshot"/> forever from this point on. If another version of this same lineage is currently Published, it is retired first (AC02: only one Published version per lineage at a time).</summary>
    public async Task<CardTemplate> PublishAsync(string templateId, int version, int expectedRevision, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var currentlyPublished = repository.GetPublished(tenantId, templateId);
        if (currentlyPublished is not null && currentlyPublished.Version != version)
        {
            var retired = currentlyPublished.Retire(actor, UtcInstant.Now());
            await AuditAsync(tenantId, actor, "card_template.retire", templateId, currentlyPublished.Version, AuditOutcome.Allowed, "card_template_auto_retired_on_republish", correlationId, cancellationToken);
            if (!repository.TryUpdate(retired, currentlyPublished.Revision))
            {
                throw CardTemplateError.ConcurrencyConflict(templateId, currentlyPublished.Version, currentlyPublished.Revision);
            }
        }

        return await MutateAsync(templateId, version, expectedRevision, actor, correlationId, "card_template.publish", "card_template_published", cancellationToken,
            current => current.Publish(actor, UtcInstant.Now()));
    }

    public Task<CardTemplate> RetireAsync(string templateId, int version, int expectedRevision, AuditActor actor, string correlationId, CancellationToken cancellationToken = default) =>
        MutateAsync(templateId, version, expectedRevision, actor, correlationId, "card_template.retire", "card_template_retired", cancellationToken,
            current => current.Retire(actor, UtcInstant.Now()));

    /// <summary>AC02: "Edit creates new version" - the only way to change a Published/Retired template's content. Source must itself be Published or Retired.</summary>
    public async Task<CardTemplate> CreateNextVersionAsync(string templateId, int fromVersion, CardTemplateSnapshot? overrideSnapshot, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var source = repository.GetVersion(tenantId, templateId, fromVersion) ?? throw CardTemplateError.NotFound(templateId, fromVersion);

        if (overrideSnapshot is not null)
        {
            CardTemplateSnapshotValidator.Validate(overrideSnapshot, tenantId, signatureGateway);
        }

        var nextVersion = repository.ListVersions(tenantId, templateId).Max(t => t.Version) + 1;
        var next = source.CreateNextDraftVersion(nextVersion, overrideSnapshot, actor, UtcInstant.Now());

        await AuditAsync(tenantId, actor, "card_template.create_next_version", templateId, next.Version, AuditOutcome.Allowed, "card_template_new_version_created", correlationId, cancellationToken);
        return repository.Add(next);
    }

    private async Task<CardTemplate> MutateAsync(
        string templateId, int version, int expectedRevision, AuditActor actor, string correlationId,
        string action, string reasonCode, CancellationToken cancellationToken, Func<CardTemplate, CardTemplate> apply)
    {
        var tenantId = RequireTenant();
        var current = repository.GetVersion(tenantId, templateId, version) ?? throw CardTemplateError.NotFound(templateId, version);

        CardTemplate updated;
        try
        {
            updated = apply(current);
        }
        catch (CardTemplateError error)
        {
            await AuditAsync(tenantId, actor, action, templateId, version, AuditOutcome.Denied, error.Code, correlationId, cancellationToken);
            throw;
        }

        await AuditAsync(tenantId, actor, action, templateId, version, AuditOutcome.Allowed, reasonCode, correlationId, cancellationToken);

        if (!repository.TryUpdate(updated, expectedRevision))
        {
            throw CardTemplateError.ConcurrencyConflict(templateId, version, expectedRevision);
        }

        return updated;
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw CardTemplateError.TenantContextMissing();

    private async Task AuditAsync(string tenantId, AuditActor actor, string action, string templateId, int version, AuditOutcome outcome, string reasonCode, string correlationId, CancellationToken cancellationToken)
    {
        var now = UtcInstant.Now();
        try
        {
            await auditSink.WriteAsync(new CardTemplateAuditEvent
            {
                EventId = CanonicalId.NewEventId(),
                TenantId = tenantId,
                Actor = actor,
                Action = action,
                Target = new AuditTarget("card_template", $"{templateId}/v{version}"),
                Outcome = outcome,
                ReasonCode = reasonCode,
                CorrelationId = correlationId,
                OccurredAt = now,
                ReceivedAt = now,
                Classification = "internal",
            }, cancellationToken);
        }
        catch
        {
            throw CardTemplateError.AuditUnavailable();
        }
    }
}
