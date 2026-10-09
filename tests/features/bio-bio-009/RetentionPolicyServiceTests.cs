using Biolog.Features.BiometricRetention.Audit;
using Biolog.Features.BiometricRetention.Domain;
using Biolog.Features.BiometricRetention.Service;
using Xunit;

namespace Biolog.Features.BiometricRetention.Tests;

public sealed class RetentionPolicyServiceTests
{
    private static RetentionPolicyService CreateService(
        IRetentionPolicy? retentionPolicy = null,
        IRawImageRetentionPolicy? rawImagePolicy = null,
        IBiometricRetentionAuditSink? auditSink = null) =>
        new(
            retentionPolicy ?? new InMemoryRetentionPolicy(),
            rawImagePolicy ?? new InMemoryRawImageRetentionPolicy(),
            auditSink ?? new InMemoryBiometricRetentionAuditSink(),
            new BiometricRetentionLockRegistry());

    [Fact]
    public async Task AC01_setting_retention_policy_succeeds_and_is_audited()
    {
        var auditSink = new InMemoryBiometricRetentionAuditSink();
        var service = CreateService(auditSink: auditSink);

        await service.SetRetentionPolicyAsync("ten_a", RetentionCategory.FaceTemplate, 120, "actor_1", CancellationToken.None);

        var setting = await service.GetRetentionPolicyAsync("ten_a", RetentionCategory.FaceTemplate, CancellationToken.None);
        Assert.Equal(120, setting.RetentionDays);
        Assert.Contains(auditSink.AllEvents, e => e.Action == "biometric_retention.policy_updated");
    }

    [Fact]
    public async Task AC01_a_non_positive_retention_day_count_is_rejected()
    {
        var auditSink = new InMemoryBiometricRetentionAuditSink();
        var service = CreateService(auditSink: auditSink);

        await Assert.ThrowsAsync<BiometricRetentionException>(
            () => service.SetRetentionPolicyAsync("ten_a", RetentionCategory.FaceTemplate, 0, "actor_1", CancellationToken.None));

        Assert.Empty(auditSink.AllEvents);
    }

    [Fact]
    public async Task AC03_authorising_raw_image_retention_succeeds_and_is_audited()
    {
        var auditSink = new InMemoryBiometricRetentionAuditSink();
        var service = CreateService(auditSink: auditSink);

        await service.SetRawImageRetentionAuthorisedAsync("ten_a", authorised: true, "actor_1", CancellationToken.None);

        var authorised = await service.IsRawImageRetentionAuthorisedAsync("ten_a", CancellationToken.None);
        Assert.True(authorised);
        Assert.Contains(auditSink.AllEvents, e => e.Action == "biometric_retention.raw_image_authorised");
    }

    [Fact]
    public async Task AC01_an_audit_write_that_throws_but_genuinely_lands_does_not_fail_the_mutation()
    {
        var auditSink = new AmbiguousThrowThenLandAuditSink();
        var service = CreateService(auditSink: auditSink);

        await service.SetRetentionPolicyAsync("ten_a", RetentionCategory.FaceTemplate, 120, "actor_1", CancellationToken.None);

        var setting = await service.GetRetentionPolicyAsync("ten_a", RetentionCategory.FaceTemplate, CancellationToken.None);
        Assert.Equal(120, setting.RetentionDays);
    }

    [Fact]
    public async Task AC01_an_audit_write_that_throws_and_genuinely_never_lands_fails_the_mutation_closed()
    {
        var auditSink = new AlwaysThrowsAndNeverLandsAuditSink();
        var service = CreateService(auditSink: auditSink);

        await Assert.ThrowsAsync<BiometricRetentionException>(
            () => service.SetRetentionPolicyAsync("ten_a", RetentionCategory.FaceTemplate, 120, "actor_1", CancellationToken.None));

        var setting = await service.GetRetentionPolicyAsync("ten_a", RetentionCategory.FaceTemplate, CancellationToken.None);
        Assert.False(setting.IsExplicitlyConfigured);
    }

    /// <summary>ADR-0003 Decision 7, element 4: a write that throws on acknowledgement but
    /// actually committed - <see cref="IBiometricRetentionAuditSink.ExistsAsync"/> must confirm
    /// it landed, so the caller proceeds rather than failing a genuinely successful write.</summary>
    private sealed class AmbiguousThrowThenLandAuditSink : IBiometricRetentionAuditSink
    {
        private readonly InMemoryBiometricRetentionAuditSink _inner = new();

        public async Task WriteAsync(BiometricRetentionAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            await _inner.WriteAsync(auditEvent, cancellationToken);
            throw new InvalidOperationException("simulated ambiguous acknowledgement failure");
        }

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            _inner.ExistsAsync(eventId, cancellationToken);
    }

    /// <summary>The write never lands and verification correctly reports that - the mutation must
    /// fail closed, not proceed on an unverifiable audit claim.</summary>
    private sealed class AlwaysThrowsAndNeverLandsAuditSink : IBiometricRetentionAuditSink
    {
        public Task WriteAsync(BiometricRetentionAuditEvent auditEvent, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("simulated genuine failure");

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
