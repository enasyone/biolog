using System.Security.Cryptography;
using Biolog.Features.DocumentTemplateEditor.Audit;
using Biolog.Features.DocumentTemplateEditor.Domain;
using Biolog.Features.DocumentTemplateEditor.Identifiers;
using Biolog.Features.DocumentTemplateEditor.Tenancy;
using Biolog.Features.OcrEngine.Model;

namespace Biolog.Features.DocumentTemplateEditor;

/// <summary>
/// The application-facing surface this feature's endpoints call, the same role BIO-CRD-008's own
/// <c>CardTemplateService</c> plays. Resolves the current tenant itself rather than trusting a
/// caller-supplied one (AC04, ADR-0002).
///
/// <b>Round-2 security fix (Zainab, ZB-DOC011-01, Critical):</b> every mutation now requires a
/// live <see cref="IPermissionGateway"/> grant for the resolved (tenant, actor) pair - round 1
/// shipped no permission check at all. Publishing requires <see cref="PublishPermission"/>, a
/// distinct, higher-impact permission from every other mutation's <see cref="ManagePermission"/>.
/// Genuine maker-checker separation-of-duties for publish is NOT attempted: ADR-0006's own
/// Decision 3 text discloses that the interim provenance-only model cannot yet prove two
/// *distinct* people approved an action (a single caller can self-approve by presenting two
/// different asserted actor ids) - building an apparent dual-control gate on that foundation
/// would be security theater, not a real control, so it is deferred to whichever story lands
/// genuine per-operator authentication, the same disclosed-boundary discipline ADR-0006 itself
/// uses.
///
/// <b>Round-2 follow-up (Seyi F01 / Zainab ZB-DOC011-03, High):</b> round 2's own reads
/// (<see cref="ListVersionsAsync"/>, <see cref="GetVersionAsync"/>, <see cref="GetPublishedAsync"/>)
/// verified provenance but performed no permission lookup at all, so any provenance-valid actor
/// in the resolved tenant could read another administrator's in-progress template content. Reads
/// now require <see cref="ReadPermission"/> - or either mutation permission, since a caller who
/// can manage or publish a lineage can obviously also view it - via <see cref="RequireReadPermissionAsync"/>.
///
/// <b>Round-2 audit-integrity fix (Zainab, ZB-DOC011-02, High):</b> round 1's own repository-level
/// optimistic-concurrency (<c>TryUpdate(expectedRevision)</c>) left two ordering bugs: committing
/// before auditing (an audit-sink failure could leave a persisted, unaudited change), and a raw
/// <c>Max()+1</c> race in <see cref="CreateNextVersionAsync"/>. Both are fixed the same way
/// BIO-INT-002 already established for this exact class of problem: every mutation to a lineage
/// is serialized through <see cref="DocumentTemplateDraftLockRegistry"/> first, which removes the
/// race entirely, which in turn makes it safe to audit BEFORE committing again (ADR-0003
/// Decision 7) - <see cref="IDocumentTemplateDraftAuditSink.ExistsAsync"/> resolves an ambiguous
/// write-throw instead of the caller ever assuming failure.
/// </summary>
internal sealed class DocumentTemplateDraftService(
    ICurrentTenantProvider tenantProvider,
    IDocumentTemplateDraftRepository repository,
    IDocumentTemplateDraftAuditSink auditSink,
    DocumentTemplateDraftLockRegistry lockRegistry,
    IPermissionGateway permissionGateway)
{
    public const string ManagePermission = "document_template_editor.manage";
    public const string PublishPermission = "document_template_editor.publish";
    public const string ReadPermission = "document_template_editor.read";

    public async Task<DocumentTemplateDraft> GetVersionAsync(string templateId, int version, string actorId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        await RequireReadPermissionAsync(tenantId, actorId, cancellationToken);
        return repository.GetVersion(tenantId, templateId, version) ?? throw DocumentTemplateDraftError.NotFound(templateId, version);
    }

    public async Task<IReadOnlyList<DocumentTemplateDraft>> ListVersionsAsync(string templateId, string actorId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        await RequireReadPermissionAsync(tenantId, actorId, cancellationToken);
        return repository.ListVersions(tenantId, templateId);
    }

    /// <summary>AC04: the one sanctioned source of "the exact published version" for this tenant - never a Draft/Test/Approval row.</summary>
    public async Task<DocumentTemplateDraft?> GetPublishedAsync(string templateId, string actorId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        await RequireReadPermissionAsync(tenantId, actorId, cancellationToken);
        return repository.GetPublished(tenantId, templateId);
    }

    public async Task<DocumentTemplateDraft> CreateDraftAsync(string name, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        await RequirePermissionAsync(tenantId, actor.Id, ManagePermission, cancellationToken);

        var templateId = CanonicalId.NewDraftId();
        using var _ = await lockRegistry.AcquireAsync(tenantId, templateId, cancellationToken);

        var now = UtcInstant.Now();
        var draft = new DocumentTemplateDraft(tenantId, templateId, version: 1)
        {
            Revision = 1,
            Status = DocumentTemplateDraftStatus.Draft,
            Name = name,
            CreatedBy = actor,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await AuditAsync(tenantId, actor, "document_template_draft.create", draft.TemplateId, draft.Version, AuditOutcome.Allowed, "document_template_draft_created", correlationId, cancellationToken);
        return repository.Add(draft);
    }

    /// <summary>AC01: "upload reference layout." <paramref name="content"/> is hashed here (never stored inline - no blob store exists in this codebase, the same by-reference boundary BIO-CRD-008 draws for signature assets).</summary>
    public Task<DocumentTemplateDraft> AttachReferenceLayoutAsync(
        string templateId, int version, int width, int height, byte[] content, int expectedRevision, AuditActor actor, string correlationId, CancellationToken cancellationToken = default) =>
        MutateAsync(templateId, version, expectedRevision, actor, correlationId, "document_template_draft.attach_reference_layout", "document_template_draft_reference_layout_attached", ManagePermission, cancellationToken,
            current =>
            {
                var contentHash = Convert.ToHexStringLower(SHA256.HashData(content));
                var layout = new DocumentTemplateReferenceLayout(width, height, contentHash, UtcInstant.Now());
                return current.AttachReferenceLayout(layout, actor, UtcInstant.Now());
            });

    /// <summary>AC01: "draw named field/graphic regions."</summary>
    public Task<DocumentTemplateDraft> DefineRegionsAsync(
        string templateId, int version, IReadOnlyList<OcrTemplateField> fieldRegions, IReadOnlyList<DocumentTemplateGraphicRegion> graphicRegions,
        int expectedRevision, AuditActor actor, string correlationId, CancellationToken cancellationToken = default) =>
        MutateAsync(templateId, version, expectedRevision, actor, correlationId, "document_template_draft.define_regions", "document_template_draft_regions_defined", ManagePermission, cancellationToken,
            current => current.DefineRegions(fieldRegions, graphicRegions, actor, UtcInstant.Now()));

    public Task<DocumentTemplateDraft> SubmitForTestAsync(string templateId, int version, int expectedRevision, AuditActor actor, string correlationId, CancellationToken cancellationToken = default) =>
        MutateAsync(templateId, version, expectedRevision, actor, correlationId, "document_template_draft.submit_for_test", "document_template_draft_submitted", ManagePermission, cancellationToken,
            current => current.SubmitForTest(actor, UtcInstant.Now()));

    /// <summary>
    /// AC03: actually RUNS the supplied test corpus through BIO-DOC-004's real
    /// <see cref="TemplateOcrEngine"/> (<see cref="SyntheticCorpusRenderer"/>) and records
    /// whatever it genuinely computed - never a caller-supplied pass/fail.
    /// </summary>
    public Task<DocumentTemplateDraft> RunCorpusAsync(
        string templateId, int version, IReadOnlyList<DocumentTemplateCorpusSample> samples, int expectedRevision, AuditActor actor, string correlationId, CancellationToken cancellationToken = default) =>
        MutateAsync(templateId, version, expectedRevision, actor, correlationId, "document_template_draft.run_corpus", "document_template_draft_corpus_recorded", ManagePermission, cancellationToken,
            current =>
            {
                if (current.ReferenceLayout is null)
                {
                    throw DocumentTemplateDraftError.ReferenceLayoutRequired();
                }

                var result = SyntheticCorpusRenderer.Run(current.ReferenceLayout, current.FieldRegions, samples, UtcInstant.Now());
                return current.RecordCorpusRun(result, actor, UtcInstant.Now());
            });

    /// <summary>AC05: freezes this draft's content forever. Gated by <see cref="PublishPermission"/>, distinct from <see cref="ManagePermission"/> - see this class's own header.</summary>
    public Task<DocumentTemplateDraft> PublishAsync(string templateId, int version, int expectedRevision, AuditActor actor, string correlationId, CancellationToken cancellationToken = default) =>
        MutateAsync(templateId, version, expectedRevision, actor, correlationId, "document_template_draft.publish", "document_template_draft_published", PublishPermission, cancellationToken,
            current => current.Publish(actor, UtcInstant.Now()));

    /// <summary>AC05: the only way to change a Published draft's content - a brand new version row. Source must itself be Published. The whole read-compute-audit-add sequence runs under this lineage's own lock, closing ZB-DOC011-02's <c>Max()+1</c> race.</summary>
    public async Task<DocumentTemplateDraft> CreateNextVersionAsync(string templateId, int fromVersion, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        await RequirePermissionAsync(tenantId, actor.Id, ManagePermission, cancellationToken);

        using var _ = await lockRegistry.AcquireAsync(tenantId, templateId, cancellationToken);

        var source = repository.GetVersion(tenantId, templateId, fromVersion) ?? throw DocumentTemplateDraftError.NotFound(templateId, fromVersion);
        var nextVersion = repository.ListVersions(tenantId, templateId).Max(d => d.Version) + 1;
        var next = source.CreateNextDraftVersion(nextVersion, actor, UtcInstant.Now());

        await AuditAsync(tenantId, actor, "document_template_draft.create_next_version", templateId, next.Version, AuditOutcome.Allowed, "document_template_draft_new_version_created", correlationId, cancellationToken);
        return repository.Add(next);
    }

    /// <summary>
    /// Serializes every mutation to (tenantId, templateId) through <see cref="lockRegistry"/>
    /// before touching it (ZB-DOC011-02) - under that lock, this is the ONLY writer for this
    /// lineage, so it is safe to audit Allowed and then commit, matching ADR-0003 Decision 7's
    /// audit-before-commit discipline without the false-positive risk a repository-level CAS
    /// introduced in round 1. The caller's own asserted <paramref name="expectedRevision"/> is
    /// still checked (now a plain in-service comparison, not a CAS) to surface staleness to a
    /// caller whose own view was out of date - a client-communication concern, not a
    /// concurrency-safety one now that the lock is the real guarantee.
    /// </summary>
    private async Task<DocumentTemplateDraft> MutateAsync(
        string templateId, int version, int expectedRevision, AuditActor actor, string correlationId,
        string action, string reasonCode, string permissionKey, CancellationToken cancellationToken, Func<DocumentTemplateDraft, DocumentTemplateDraft> apply)
    {
        var tenantId = RequireTenant();
        await RequirePermissionAsync(tenantId, actor.Id, permissionKey, cancellationToken);

        using var _ = await lockRegistry.AcquireAsync(tenantId, templateId, cancellationToken);

        var current = repository.GetVersion(tenantId, templateId, version) ?? throw DocumentTemplateDraftError.NotFound(templateId, version);

        if (current.Revision != expectedRevision)
        {
            var conflict = DocumentTemplateDraftError.ConcurrencyConflict(templateId, version, expectedRevision);
            await AuditAsync(tenantId, actor, action, templateId, version, AuditOutcome.Denied, conflict.Code, correlationId, cancellationToken);
            throw conflict;
        }

        DocumentTemplateDraft updated;
        try
        {
            updated = apply(current);
        }
        catch (DocumentTemplateDraftError error)
        {
            await AuditAsync(tenantId, actor, action, templateId, version, AuditOutcome.Denied, error.Code, correlationId, cancellationToken);
            throw;
        }

        await AuditAsync(tenantId, actor, action, templateId, version, AuditOutcome.Allowed, reasonCode, correlationId, cancellationToken);
        repository.Update(updated);

        return updated;
    }

    private async Task RequirePermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken)
    {
        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actorId, permissionKey, cancellationToken))
        {
            throw DocumentTemplateDraftError.PermissionRequired(permissionKey);
        }
    }

    /// <summary>
    /// Seyi F01 / ZB-DOC011-03: a read still exposes tenant-administrative template content
    /// (reference-layout hashes, region geometry, corpus results, author metadata) - provenance
    /// alone proves only who is asking, not that they are authorised to see it. Granted by
    /// <see cref="ReadPermission"/> on its own, or by either mutation permission (a caller who can
    /// manage or publish a lineage can obviously also view it).
    /// </summary>
    private async Task RequireReadPermissionAsync(string tenantId, string actorId, CancellationToken cancellationToken)
    {
        if (await permissionGateway.ActorHasPermissionAsync(tenantId, actorId, ReadPermission, cancellationToken) ||
            await permissionGateway.ActorHasPermissionAsync(tenantId, actorId, ManagePermission, cancellationToken) ||
            await permissionGateway.ActorHasPermissionAsync(tenantId, actorId, PublishPermission, cancellationToken))
        {
            return;
        }

        throw DocumentTemplateDraftError.PermissionRequired(ReadPermission);
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw DocumentTemplateDraftError.TenantContextMissing();

    /// <summary>
    /// ZB-DOC011-02: a stable <see cref="DocumentTemplateDraftAuditEvent.EventId"/> is minted
    /// BEFORE the write attempt so a thrown exception from <see cref="IDocumentTemplateDraftAuditSink.WriteAsync"/>
    /// can be resolved via <see cref="IDocumentTemplateDraftAuditSink.ExistsAsync"/> instead of
    /// assumed to mean the event never landed (ADR-0003 Decision 7, element 4) - mirrors
    /// BIO-INT-002's own <c>AuditAsync</c> helper exactly.
    /// </summary>
    private async Task AuditAsync(string tenantId, AuditActor actor, string action, string templateId, int version, AuditOutcome outcome, string reasonCode, string correlationId, CancellationToken cancellationToken)
    {
        var now = UtcInstant.Now();
        var auditEvent = new DocumentTemplateDraftAuditEvent
        {
            EventId = CanonicalId.NewEventId(),
            TenantId = tenantId,
            Actor = actor,
            Action = action,
            Target = new AuditTarget("document_template_draft", $"{templateId}/v{version}"),
            Outcome = outcome,
            ReasonCode = reasonCode,
            CorrelationId = correlationId,
            OccurredAt = now,
            ReceivedAt = now,
            Classification = "internal",
        };

        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
        }
        catch
        {
            if (!await auditSink.ExistsAsync(auditEvent.EventId, CancellationToken.None))
            {
                throw DocumentTemplateDraftError.AuditUnavailable();
            }
        }
    }
}
