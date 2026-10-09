using Biolog.Features.DocumentTemplateEditor.Audit;
using Biolog.Features.DocumentTemplateEditor.Identifiers;
using Biolog.Features.OcrEngine.Model;

namespace Biolog.Features.DocumentTemplateEditor.Domain;

/// <summary>
/// One version row within a tenant document-template-editor lineage (BIO-DOC-011).
/// <see cref="TemplateId"/> is the stable lineage id; <see cref="Version"/> is this row's own
/// immutable, monotonically-increasing identity within that lineage - the exact shape BIO-CRD-008's
/// own <c>CardTemplate</c> established for the identical problem. <see cref="Revision"/> is a
/// SEPARATE optimistic-concurrency token for in-place edits while still Draft.
///
/// <b>AC02: "draft/test/approval/publish lifecycle."</b> Draft -&gt; Test (submit) -&gt; Approval
/// (corpus passes) or back to Draft (corpus fails) -&gt; Published (publish). See
/// <see cref="DocumentTemplateDraftStatus"/>.
///
/// <b>AC04: "tenant templates cannot masquerade as official global templates."</b>
/// <see cref="TenantId"/> is REQUIRED and resolved server-side only (never caller-supplied raw -
/// ADR-0002's "an identifier arriving in a request is a claim, not an authorisation"), and
/// <see cref="PublicReference"/> carries a <c>tenant:</c>-prefixed identifier that can never
/// collide with or be confused for a BIO-DOC-010 global pack entry id (which has no
/// <c>TenantId</c> field at all - a structurally different aggregate, not merely a different
/// value). This is the hard structural distinction AC04 asks for, not a trust-the-caller flag.
///
/// <b>Construction is internal</b> (BIO-IDN-001-QA-01's lesson, applied from the start).
/// </summary>
public sealed record DocumentTemplateDraft
{
    public string TenantId { get; }
    public string TemplateId { get; }
    public int Version { get; }

    public required int Revision { get; init; }
    public required DocumentTemplateDraftStatus Status { get; init; }
    public required string Name { get; init; }
    public DocumentTemplateReferenceLayout? ReferenceLayout { get; init; }
    public IReadOnlyList<OcrTemplateField> FieldRegions { get; init; } = [];
    public IReadOnlyList<DocumentTemplateGraphicRegion> GraphicRegions { get; init; } = [];
    public DocumentTemplateCorpusRunResult? LastCorpusRun { get; init; }
    public required AuditActor CreatedBy { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }
    public string? PublishedAt { get; init; }
    public AuditActor? PublishedBy { get; init; }
    public IReadOnlyList<DocumentTemplateDraftTransition> Transitions { get; init; } = [];

    /// <summary>AC04: the wire-visible identifier proving tenant origin - see this type's own header.</summary>
    public string PublicReference => $"tenant:{TenantId}:{TemplateId}:v{Version}";

    internal DocumentTemplateDraft(string tenantId, string templateId, int version)
    {
        if (!CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        if (!CanonicalId.IsDraftId(templateId))
        {
            throw new ArgumentException($"'{templateId}' is not a well-formed document template draft id (ADR-0001)", nameof(templateId));
        }

        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "a document template draft's version starts at 1");
        }

        TenantId = tenantId;
        TemplateId = templateId;
        Version = version;
    }

    /// <summary>AC01: "Users can upload reference layout." Draft only - the canvas regions are drawn against cannot change once testing has begun without starting a new version.</summary>
    public DocumentTemplateDraft AttachReferenceLayout(DocumentTemplateReferenceLayout layout, AuditActor actor, string now)
    {
        RequireDraft("attach_reference_layout");

        return this with
        {
            ReferenceLayout = layout,
            FieldRegions = [],
            GraphicRegions = [],
            Revision = Revision + 1,
            UpdatedAt = now,
            Transitions = [.. Transitions, Note(Status, actor, now, "reference_layout_attached")],
        };
    }

    /// <summary>AC01: "draw named field/graphic regions." Replaces the full region set each call (not an append) - Draft only, and only once a reference layout exists to validate bounds against.</summary>
    public DocumentTemplateDraft DefineRegions(IReadOnlyList<OcrTemplateField> fieldRegions, IReadOnlyList<DocumentTemplateGraphicRegion> graphicRegions, AuditActor actor, string now)
    {
        RequireDraft("define_regions");

        if (ReferenceLayout is null)
        {
            throw DocumentTemplateDraftError.ReferenceLayoutRequired();
        }

        ValidateFieldRegions(fieldRegions, ReferenceLayout);
        ValidateGraphicRegions(graphicRegions, ReferenceLayout);

        return this with
        {
            FieldRegions = fieldRegions,
            GraphicRegions = graphicRegions,
            Revision = Revision + 1,
            UpdatedAt = now,
            Transitions = [.. Transitions, Note(Status, actor, now, "regions_defined")],
        };
    }

    /// <summary>AC02: Draft -&gt; Test.</summary>
    public DocumentTemplateDraft SubmitForTest(AuditActor actor, string now)
    {
        if (Status != DocumentTemplateDraftStatus.Draft)
        {
            throw DocumentTemplateDraftError.InvalidTransition(Status, "submit_for_test");
        }

        if (ReferenceLayout is null)
        {
            throw DocumentTemplateDraftError.ReferenceLayoutRequired();
        }

        if (FieldRegions.Count == 0)
        {
            throw DocumentTemplateDraftError.NoFieldRegions();
        }

        return Transition(DocumentTemplateDraftStatus.Test, actor, now, null);
    }

    /// <summary>AC03: Test -&gt; Approval when the corpus passed, else back to Draft. <paramref name="result"/> is always computed by <see cref="DocumentTemplateDraftService.RunCorpusAsync"/>, never caller-attested.</summary>
    public DocumentTemplateDraft RecordCorpusRun(DocumentTemplateCorpusRunResult result, AuditActor actor, string now)
    {
        if (Status != DocumentTemplateDraftStatus.Test)
        {
            throw DocumentTemplateDraftError.InvalidTransition(Status, "record_corpus_run");
        }

        var next = Transition(result.Passed ? DocumentTemplateDraftStatus.Approval : DocumentTemplateDraftStatus.Draft, actor, now,
            result.Passed ? "corpus_passed" : "corpus_failed");
        return next with { LastCorpusRun = result };
    }

    /// <summary>AC05: from this point on, every field this draft carries is frozen forever.</summary>
    public DocumentTemplateDraft Publish(AuditActor actor, string now)
    {
        if (Status != DocumentTemplateDraftStatus.Approval)
        {
            throw DocumentTemplateDraftError.InvalidTransition(Status, "publish");
        }

        return Transition(DocumentTemplateDraftStatus.Published, actor, now, null) with { PublishedAt = now, PublishedBy = actor };
    }

    /// <summary>AC05: the only way to change a Published template's content - a brand new row, same lineage, next version, starting fresh at Draft with this version's content as a starting point (ADR-0001 §3.3).</summary>
    public DocumentTemplateDraft CreateNextDraftVersion(int nextVersion, AuditActor actor, string now)
    {
        if (!Status.IsImmutable())
        {
            throw DocumentTemplateDraftError.InvalidTransition(Status, "create_next_draft_version");
        }

        return new DocumentTemplateDraft(TenantId, TemplateId, nextVersion)
        {
            Revision = 1,
            Status = DocumentTemplateDraftStatus.Draft,
            Name = Name,
            ReferenceLayout = ReferenceLayout,
            FieldRegions = FieldRegions,
            GraphicRegions = GraphicRegions,
            LastCorpusRun = null,
            CreatedBy = actor,
            CreatedAt = now,
            UpdatedAt = now,
            Transitions = [new DocumentTemplateDraftTransition { FromStatus = Status, ToStatus = DocumentTemplateDraftStatus.Draft, Actor = actor, At = now, Note = $"created from version {Version}" }],
        };
    }

    private void RequireDraft(string action)
    {
        if (Status != DocumentTemplateDraftStatus.Draft)
        {
            throw DocumentTemplateDraftError.InvalidTransition(Status, action);
        }
    }

    private static void ValidateFieldRegions(IReadOnlyList<OcrTemplateField> fields, DocumentTemplateReferenceLayout layout)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (!seen.Add(field.FieldName))
            {
                throw DocumentTemplateDraftError.DuplicateRegionName(field.FieldName);
            }

            if (field.Left < 0 || field.Top < 0 || field.CharacterCount <= 0)
            {
                throw DocumentTemplateDraftError.InvalidRegionGeometry(field.FieldName);
            }

            var widthPx = field.CharacterCount * OcrGlyphFont.CellPitchX - 1;
            if (field.Left + widthPx > layout.Width || field.Top + OcrGlyphFont.GlyphHeight > layout.Height)
            {
                throw DocumentTemplateDraftError.RegionOutOfBounds(field.FieldName, field.Left, field.Top, widthPx, OcrGlyphFont.GlyphHeight, layout.Width, layout.Height);
            }
        }
    }

    private static void ValidateGraphicRegions(IReadOnlyList<DocumentTemplateGraphicRegion> regions, DocumentTemplateReferenceLayout layout)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var region in regions)
        {
            if (!seen.Add(region.Name))
            {
                throw DocumentTemplateDraftError.DuplicateRegionName(region.Name);
            }

            if (region.Left < 0 || region.Top < 0 || region.Width <= 0 || region.Height <= 0)
            {
                throw DocumentTemplateDraftError.InvalidRegionGeometry(region.Name);
            }

            if (region.Left + region.Width > layout.Width || region.Top + region.Height > layout.Height)
            {
                throw DocumentTemplateDraftError.RegionOutOfBounds(region.Name, region.Left, region.Top, region.Width, region.Height, layout.Width, layout.Height);
            }
        }
    }

    private DocumentTemplateDraftTransition Note(DocumentTemplateDraftStatus status, AuditActor actor, string now, string note) =>
        new() { FromStatus = status, ToStatus = status, Actor = actor, At = now, Note = note };

    private DocumentTemplateDraft Transition(DocumentTemplateDraftStatus to, AuditActor actor, string now, string? note) => this with
    {
        Status = to,
        Revision = Revision + 1,
        UpdatedAt = now,
        Transitions = [.. Transitions, new DocumentTemplateDraftTransition { FromStatus = Status, ToStatus = to, Actor = actor, At = now, Note = note }],
    };
}
