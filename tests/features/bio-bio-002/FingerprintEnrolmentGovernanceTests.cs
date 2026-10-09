using Biolog.Features.FingerprintEnrolment.Domain;
using Biolog.Features.FingerprintEnrolment.Service;
using Xunit;

namespace Biolog.Features.FingerprintEnrolment.Tests;

/// <summary>BIO-IDN-001-QA-01-shaped governance checks: the repository and service are internal,
/// not public, and the repository independently re-validates tenant scoping and CAS at the
/// persistence boundary.</summary>
public sealed class FingerprintEnrolmentGovernanceTests
{
    [Fact]
    public void IFingerprintEnrolmentRepository_and_InMemoryFingerprintEnrolmentRepository_are_not_public()
    {
        Assert.False(typeof(IFingerprintEnrolmentRepository).IsPublic);
        Assert.False(typeof(InMemoryFingerprintEnrolmentRepository).IsPublic);
    }

    [Fact]
    public void FingerprintEnrolmentService_is_not_public()
    {
        Assert.False(typeof(FingerprintEnrolmentService).IsPublic);
    }

    [Fact]
    public async Task ReplaceAsync_rejects_moving_a_session_to_a_foreign_tenant()
    {
        var repository = new InMemoryFingerprintEnrolmentRepository();
        var session = FingerprintEnrolmentSession.Start("s1", "ten_a", "per_a", EnrolmentMode.SingleFinger, [FingerPosition.RightIndex], false, "t0");
        await repository.CreateAsync(session, CancellationToken.None);

        var foreignTenantCopy = FingerprintEnrolmentSession.Start("s1", "ten_b", "per_a", EnrolmentMode.SingleFinger, [FingerPosition.RightIndex], false, "t0");

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.ReplaceAsync(session, foreignTenantCopy, CancellationToken.None));
    }

    [Fact]
    public async Task ReplaceAsync_rejects_a_stale_expected_version()
    {
        var repository = new InMemoryFingerprintEnrolmentRepository();
        var session = FingerprintEnrolmentSession.Start("s1", "ten_a", "per_a", EnrolmentMode.SingleFinger, [FingerPosition.RightIndex], false, "t0");
        await repository.CreateAsync(session, CancellationToken.None);
        var updatedOnce = session.MarkMissingOrAmputated(FingerPosition.RightIndex, "reason", session.Version, "t1");
        await repository.ReplaceAsync(session, updatedOnce, CancellationToken.None);

        // Second writer still believes it is at version 1 (stale).
        var staleAttempt = session.Cancel(session.Version, "t2");

        await Assert.ThrowsAsync<FingerprintEnrolmentException>(() => repository.ReplaceAsync(session, staleAttempt, CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_cross_tenant_lookup_returns_null_not_an_exception()
    {
        var repository = new InMemoryFingerprintEnrolmentRepository();
        var session = FingerprintEnrolmentSession.Start("s1", "ten_a", "per_a", EnrolmentMode.SingleFinger, [FingerPosition.RightIndex], false, "t0");
        await repository.CreateAsync(session, CancellationToken.None);

        var result = await repository.GetAsync("ten_b", "s1", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_session_not_starting_at_version_1()
    {
        var repository = new InMemoryFingerprintEnrolmentRepository();
        var session = FingerprintEnrolmentSession.Start("s1", "ten_a", "per_a", EnrolmentMode.SingleFinger, [FingerPosition.RightIndex], false, "t0");
        var bumped = session.Cancel(session.Version, "t1"); // version 2

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.CreateAsync(bumped, CancellationToken.None));
    }
}
