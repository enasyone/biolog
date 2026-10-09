using Biolog.Features.Transport.Domain;
using Xunit;

namespace Biolog.Features.Transport.Tests;

/// <summary>AC02: automatic expiry is a pure, read-time-only overlay - never persisted, never auto-mutated.</summary>
public class TransportStatusEvaluatorTests
{
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string PersonId = "per_0199e0c2-1111-7222-8333-444455556666";
    private const string RouteId = "11111111-1111-7111-8111-111111111111";
    private const string StopId = "22222222-2222-7222-8222-222222222222";

    private static PersonAssignment MakeAssignment(string validFrom, string? validUntil) =>
        PersonAssignment.Create(TenantId, PersonId, RouteId, StopId, validFrom, validUntil, "usr-1", validFrom);

    [Fact]
    public void AC02_a_a_future_valid_from_is_upcoming()
    {
        var assignment = MakeAssignment("2026-06-01T00:00:00.000Z", null);
        Assert.Equal(EffectiveAssignmentStatus.Upcoming, TransportStatusEvaluator.EffectiveStatus(assignment, "2026-01-01T00:00:00.000Z"));
    }

    [Fact]
    public void AC02_b_within_an_open_ended_window_is_active()
    {
        var assignment = MakeAssignment("2026-01-01T00:00:00.000Z", null);
        Assert.Equal(EffectiveAssignmentStatus.Active, TransportStatusEvaluator.EffectiveStatus(assignment, "2026-06-01T00:00:00.000Z"));
    }

    [Fact]
    public void AC02_c_past_valid_until_is_ended()
    {
        var assignment = MakeAssignment("2026-01-01T00:00:00.000Z", "2026-06-01T00:00:00.000Z");
        Assert.Equal(EffectiveAssignmentStatus.Ended, TransportStatusEvaluator.EffectiveStatus(assignment, "2026-07-01T00:00:00.000Z"));
    }

    [Fact]
    public void AC02_d_exactly_at_valid_until_is_ended_half_open_interval()
    {
        var assignment = MakeAssignment("2026-01-01T00:00:00.000Z", "2026-06-01T00:00:00.000Z");
        Assert.Equal(EffectiveAssignmentStatus.Ended, TransportStatusEvaluator.EffectiveStatus(assignment, "2026-06-01T00:00:00.000Z"));
    }

    [Fact]
    public void AC02_e_the_identical_stored_record_classifies_differently_at_two_different_read_times_proving_expiry_is_computed_not_stored()
    {
        var assignment = MakeAssignment("2026-01-01T00:00:00.000Z", "2026-06-01T00:00:00.000Z");

        var before = TransportStatusEvaluator.EffectiveStatus(assignment, "2026-03-01T00:00:00.000Z");
        var after = TransportStatusEvaluator.EffectiveStatus(assignment, "2026-07-01T00:00:00.000Z");

        Assert.Equal(EffectiveAssignmentStatus.Active, before);
        Assert.Equal(EffectiveAssignmentStatus.Ended, after);
        // The record itself never changed - ValidUntil is fixed at construction, proving neither
        // read mutated anything.
        Assert.Equal("2026-06-01T00:00:00.000Z", assignment.ValidUntil);
    }
}
