using Biolog.Features.Attendance.Domain;
using Biolog.Features.Attendance.Identifiers;
using Xunit;

namespace Biolog.Features.Attendance.Tests;

/// <summary>AC04 - the repository's own compare-and-set, exercised directly (the locked service always re-reads before writing, so this proves the CAS mechanism itself rather than relying on the service to ever trigger a mismatch).</summary>
public class InMemoryAttendancePolicyRepositoryTests
{
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";

    [Fact]
    public async Task TrySaveAsync_succeeds_when_expected_version_matches_current()
    {
        var repository = new InMemoryAttendancePolicyRepository();
        var policy = AttendancePolicy.Create(TenantId, AttendanceInterpretationMode.Presence, null, 0, "actor-1", UtcInstant.Now());

        var saved = await repository.TrySaveAsync(policy, expectedVersion: 0, CancellationToken.None);

        Assert.True(saved);
        Assert.Equal(1, (await repository.GetAsync(TenantId)).Version);
    }

    [Fact]
    public async Task TrySaveAsync_fails_when_expected_version_is_stale()
    {
        var repository = new InMemoryAttendancePolicyRepository();
        var first = AttendancePolicy.Create(TenantId, AttendanceInterpretationMode.Presence, null, 0, "actor-1", UtcInstant.Now());
        await repository.TrySaveAsync(first, expectedVersion: 0, CancellationToken.None);

        var staleUpdate = first.Update(AttendanceInterpretationMode.Workforce, "08:00", 5, "actor-2", UtcInstant.Now());
        var saved = await repository.TrySaveAsync(staleUpdate, expectedVersion: 0, CancellationToken.None); // 0 is stale - current is now 1

        Assert.False(saved);
        Assert.Equal(1, (await repository.GetAsync(TenantId)).Version); // untouched
    }
}
