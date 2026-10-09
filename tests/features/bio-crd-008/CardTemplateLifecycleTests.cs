using Biolog.Features.CardTemplates.Domain;
using Xunit;

namespace Biolog.Features.CardTemplates.Tests;

/// <summary>Domain-level tests for <see cref="CardTemplate"/>'s own state machine - BIO-CRD-008-AC01/AC02, no repository/service involved.</summary>
public class CardTemplateLifecycleTests
{
    private static CardTemplate NewDraft() => new(TestData.TenantA, "tpl_018f2e5a-3333-7abc-89ab-0123456789ab", version: 1)
    {
        Revision = 1,
        Status = CardTemplateStatus.Draft,
        Name = "Staff ID v1",
        Snapshot = TestData.MinimalSnapshot(),
        CreatedBy = TestData.Actor,
        CreatedAt = "2026-09-24T00:00:00.000Z",
        UpdatedAt = "2026-09-24T00:00:00.000Z",
    };

    [Fact]
    public void AC01_editing_a_draft_mutates_in_place_and_bumps_revision()
    {
        var draft = NewDraft();
        var newSnapshot = TestData.MinimalSnapshot() with { Watermark = WatermarkPresetKind.Visitor };

        var edited = draft.Edit(newSnapshot, TestData.Actor, "2026-09-24T00:01:00.000Z");

        Assert.Equal(WatermarkPresetKind.Visitor, edited.Snapshot.Watermark);
        Assert.Equal(2, edited.Revision);
        Assert.Equal(CardTemplateStatus.Draft, edited.Status);
        Assert.Equal(1, edited.Version); // same row, same lifecycle version
    }

    [Fact]
    public void full_happy_path_draft_to_validation_to_approval_to_published_to_retired()
    {
        var now = "2026-09-24T00:00:00.000Z";
        var t = NewDraft();

        t = t.SubmitForValidation(TestData.Actor, now);
        Assert.Equal(CardTemplateStatus.Validation, t.Status);

        t = t.RecordValidation(passed: true, note: "looks good", TestData.Actor, now);
        Assert.Equal(CardTemplateStatus.Approval, t.Status);

        t = t.Publish(TestData.Actor, now);
        Assert.Equal(CardTemplateStatus.Published, t.Status);
        Assert.Equal(now, t.PublishedAt);

        t = t.Retire(TestData.Actor, now);
        Assert.Equal(CardTemplateStatus.Retired, t.Status);
    }

    [Fact]
    public void failed_validation_sends_the_template_back_to_draft_not_a_dead_end()
    {
        var t = NewDraft().SubmitForValidation(TestData.Actor, "now");
        t = t.RecordValidation(passed: false, note: "missing barcode", TestData.Actor, "now");
        Assert.Equal(CardTemplateStatus.Draft, t.Status);
    }

    [Fact]
    public void a_rejected_approval_sends_the_template_back_to_draft()
    {
        var t = NewDraft().SubmitForValidation(TestData.Actor, "now").RecordValidation(true, null, TestData.Actor, "now");
        t = t.Reject("needs a redesign", TestData.Actor, "now");
        Assert.Equal(CardTemplateStatus.Draft, t.Status);
    }

    [Theory]
    [InlineData(CardTemplateStatus.Draft, "publish")]
    [InlineData(CardTemplateStatus.Validation, "publish")]
    [InlineData(CardTemplateStatus.Published, "submit_for_validation")]
    [InlineData(CardTemplateStatus.Retired, "submit_for_validation")]
    public void an_out_of_order_transition_is_refused_not_silently_skipped(CardTemplateStatus from, string action)
    {
        var t = NewDraft() with { Status = from };
        var error = Assert.Throws<CardTemplateError>(() => action switch
        {
            "publish" => t.Publish(TestData.Actor, "now"),
            "submit_for_validation" => t.SubmitForValidation(TestData.Actor, "now"),
            _ => throw new InvalidOperationException(),
        });
        Assert.Equal("card_template_invalid_transition", error.Code);
        Assert.Equal(409, error.Status);
    }

    [Fact]
    public void AC01_a_published_template_refuses_edit_in_place()
    {
        var published = NewDraft() with { Status = CardTemplateStatus.Published };
        var error = Assert.Throws<CardTemplateError>(() => published.Edit(TestData.MinimalSnapshot(), TestData.Actor, "now"));
        Assert.Equal("card_template_immutable", error.Code);
        Assert.Equal(409, error.Status);
    }

    [Fact]
    public void AC01_a_retired_template_also_refuses_edit_in_place()
    {
        var retired = NewDraft() with { Status = CardTemplateStatus.Retired };
        Assert.Throws<CardTemplateError>(() => retired.Edit(TestData.MinimalSnapshot(), TestData.Actor, "now"));
    }

    [Fact]
    public void AC02_a_new_version_from_a_published_template_is_a_brand_new_draft_row_the_source_untouched()
    {
        var published = NewDraft() with { Status = CardTemplateStatus.Published, PublishedAt = "now", PublishedBy = TestData.Actor };
        var overrideSnapshot = TestData.MinimalSnapshot() with { Watermark = WatermarkPresetKind.Student };

        var next = published.CreateNextDraftVersion(nextVersion: 2, overrideSnapshot, TestData.Actor, "now");

        Assert.Equal(2, next.Version);
        Assert.Equal(1, next.Revision);
        Assert.Equal(CardTemplateStatus.Draft, next.Status);
        Assert.Equal(published.TemplateId, next.TemplateId);
        Assert.Equal(WatermarkPresetKind.Student, next.Snapshot.Watermark);

        // the source row itself is a different object entirely - never mutated
        Assert.Equal(CardTemplateStatus.Published, published.Status);
        Assert.Equal(WatermarkPresetKind.Staff, published.Snapshot.Watermark);
    }

    [Fact]
    public void AC02_creating_a_next_version_without_an_override_clones_the_source_snapshot_exactly()
    {
        var published = NewDraft() with { Status = CardTemplateStatus.Published };
        var next = published.CreateNextDraftVersion(2, overrideSnapshot: null, TestData.Actor, "now");
        Assert.Equal(published.Snapshot, next.Snapshot);
    }

    [Fact]
    public void creating_a_next_version_from_a_still_draft_template_is_refused_use_edit_instead()
    {
        var draft = NewDraft();
        var error = Assert.Throws<CardTemplateError>(() => draft.CreateNextDraftVersion(2, null, TestData.Actor, "now"));
        Assert.Equal("card_template_invalid_transition", error.Code);
    }

    [Fact]
    public void every_transition_is_recorded_in_provenance_order()
    {
        var now = "2026-09-24T00:00:00.000Z";
        var t = NewDraft()
            .SubmitForValidation(TestData.Actor, now)
            .RecordValidation(true, null, TestData.Actor, now)
            .Publish(TestData.Actor, now);

        Assert.Collection(t.Transitions,
            e => Assert.Equal((CardTemplateStatus.Draft, CardTemplateStatus.Validation), (e.FromStatus, e.ToStatus)),
            e => Assert.Equal((CardTemplateStatus.Validation, CardTemplateStatus.Approval), (e.FromStatus, e.ToStatus)),
            e => Assert.Equal((CardTemplateStatus.Approval, CardTemplateStatus.Published), (e.FromStatus, e.ToStatus)));
    }
}
