using Biolog.Features.Visitors.Domain;
using Xunit;

namespace Biolog.Features.Visitors.Tests;

/// <summary>AC04 - the derived, read-time-only expiry overlay.</summary>
public class VisitorStatusEvaluatorTests
{
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string HostPersonId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";

    private static VisitorRecord MakeRecord(VisitStatus visitStatus, string expiresAt) => VisitorRecord.Preregister(
        TenantId, personId: null, "Test Visitor", contactEmail: null, contactPhone: null, HostPersonId, vehiclePlate: null,
        temporaryCredentialRequested: false, expiresAt, now: "2026-09-24T06:00:00.000Z") with
    { VisitStatus = visitStatus };

    [Fact]
    public void a_preregistered_record_before_its_expiry_is_effectively_preregistered()
    {
        var record = MakeRecord(VisitStatus.Preregistered, "2026-09-24T18:00:00.000Z");
        Assert.Equal(EffectiveVisitStatus.Preregistered, VisitorStatusEvaluator.EffectiveStatus(record, "2026-09-24T08:00:00.000Z"));
    }

    [Fact]
    public void a_preregistered_record_past_its_expiry_is_effectively_expired()
    {
        var record = MakeRecord(VisitStatus.Preregistered, "2026-09-24T07:00:00.000Z");
        Assert.Equal(EffectiveVisitStatus.Expired, VisitorStatusEvaluator.EffectiveStatus(record, "2026-09-24T08:00:00.000Z"));
    }

    [Fact]
    public void a_checked_in_record_past_its_expiry_is_also_effectively_expired()
    {
        var record = MakeRecord(VisitStatus.CheckedIn, "2026-09-24T07:00:00.000Z");
        Assert.Equal(EffectiveVisitStatus.Expired, VisitorStatusEvaluator.EffectiveStatus(record, "2026-09-24T08:00:00.000Z"));
    }

    [Fact]
    public void a_checked_out_record_never_reads_as_expired_even_long_past_its_expiry_time()
    {
        var record = MakeRecord(VisitStatus.CheckedOut, "2026-09-24T07:00:00.000Z");
        Assert.Equal(EffectiveVisitStatus.CheckedOut, VisitorStatusEvaluator.EffectiveStatus(record, "2026-09-24T08:00:00.000Z"));
    }

    [Fact]
    public void the_identical_stored_record_classifies_differently_at_two_different_read_times_proving_expiry_is_computed_not_stored()
    {
        var record = MakeRecord(VisitStatus.Preregistered, "2026-09-24T12:00:00.000Z");
        Assert.Equal(EffectiveVisitStatus.Preregistered, VisitorStatusEvaluator.EffectiveStatus(record, "2026-09-24T08:00:00.000Z"));
        Assert.Equal(EffectiveVisitStatus.Expired, VisitorStatusEvaluator.EffectiveStatus(record, "2026-09-24T13:00:00.000Z"));
    }
}
