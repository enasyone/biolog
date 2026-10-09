using Biolog.Features.VisitorCredentials.Domain;
using Xunit;

namespace Biolog.Features.VisitorCredentials.Tests;

public sealed class VisitorOverstayEvaluatorTests
{
    private static VisitorRecordView View(string visitStatusWire, string expiresAt, string? checkedOutAt = null) => new()
    {
        Id = "v1",
        TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77",
        PersonId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d",
        FullName = "Test Visitor",
        HostApprovalStatusWire = "approved",
        VisitStatusWire = visitStatusWire,
        CheckedInAt = visitStatusWire == "checked-in" || checkedOutAt is not null ? "2026-01-01T08:00:00.000Z" : null,
        CheckedOutAt = checkedOutAt,
        ExpiresAt = expiresAt,
        CreatedAt = "2026-01-01T07:00:00.000Z",
        Version = 1,
    };

    [Fact]
    public void Preregistered_before_expiry_is_preregistered()
    {
        var view = View("preregistered", "2026-01-01T20:00:00.000Z");
        Assert.Equal(VisitorEffectiveStatus.Preregistered, VisitorOverstayEvaluator.EffectiveStatus(view, "2026-01-01T10:00:00.000Z"));
    }

    [Fact]
    public void Preregistered_past_expiry_is_expired_not_overstay()
    {
        var view = View("preregistered", "2026-01-01T09:00:00.000Z");
        Assert.Equal(VisitorEffectiveStatus.Expired, VisitorOverstayEvaluator.EffectiveStatus(view, "2026-01-01T10:00:00.000Z"));
    }

    [Fact]
    public void CheckedIn_before_expiry_is_checked_in()
    {
        var view = View("checked-in", "2026-01-01T20:00:00.000Z");
        Assert.Equal(VisitorEffectiveStatus.CheckedIn, VisitorOverstayEvaluator.EffectiveStatus(view, "2026-01-01T10:00:00.000Z"));
    }

    [Fact]
    public void CheckedIn_past_expiry_is_overstay_not_expired()
    {
        var view = View("checked-in", "2026-01-01T09:00:00.000Z");
        Assert.Equal(VisitorEffectiveStatus.Overstay, VisitorOverstayEvaluator.EffectiveStatus(view, "2026-01-01T10:00:00.000Z"));
    }

    [Fact]
    public void CheckedOut_past_expiry_never_retroactively_expires_or_overstays()
    {
        var view = View("checked-out", "2026-01-01T09:00:00.000Z", checkedOutAt: "2026-01-01T08:30:00.000Z");
        Assert.Equal(VisitorEffectiveStatus.CheckedOut, VisitorOverstayEvaluator.EffectiveStatus(view, "2026-01-01T10:00:00.000Z"));
    }

    [Fact]
    public void The_identical_stored_view_classifies_differently_at_two_different_read_times()
    {
        var view = View("checked-in", "2026-01-01T09:00:00.000Z");
        Assert.Equal(VisitorEffectiveStatus.CheckedIn, VisitorOverstayEvaluator.EffectiveStatus(view, "2026-01-01T08:00:00.000Z"));
        Assert.Equal(VisitorEffectiveStatus.Overstay, VisitorOverstayEvaluator.EffectiveStatus(view, "2026-01-01T10:00:00.000Z"));
    }
}
