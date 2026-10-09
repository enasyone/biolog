using Biolog.Features.BiometricRetention.Audit;
using Biolog.Features.BiometricRetention.Domain;
using Biolog.Features.BiometricRetention.Service;
using Xunit;

namespace Biolog.Features.BiometricRetention.Tests;

public sealed class BiometricDeletionServiceTests
{
    private static TemplateDeletionRequest Request(BiometricRecordKind kind = BiometricRecordKind.FingerprintSession) => new()
    {
        TenantId = "ten_a",
        Kind = kind,
        RecordId = "rec_1",
        ActorId = "actor_1",
        Reason = "subject exited the tenant",
    };

    private static BiometricDeletionService CreateService(
        ITemplateDeletionGateway? gateway = null, IBiometricRetentionAuditSink? auditSink = null,
        IPendingGalleryCleanupStore? pendingCleanupStore = null) =>
        new(
            gateway ?? new FakeTemplateDeletionGateway(),
            auditSink ?? new InMemoryBiometricRetentionAuditSink(),
            new BiometricRetentionLockRegistry(),
            pendingCleanupStore ?? new InMemoryPendingGalleryCleanupStore());

    [Fact]
    public async Task AC02_AC04_a_successful_deletion_is_audited_twice_and_propagates_to_the_gallery()
    {
        var gateway = new FakeTemplateDeletionGateway { FingerprintOwnerPersonId = "person_1", FingerprintGalleryRemoved = true };
        var auditSink = new InMemoryBiometricRetentionAuditSink();
        var service = CreateService(gateway, auditSink);

        var result = await service.DeleteAsync(Request(), CancellationToken.None);

        Assert.Equal(DeletionOutcome.Deleted, result.Outcome);
        Assert.True(result.GalleryPropagated);
        Assert.Contains(auditSink.AllEvents, e => e.Action == "biometric_retention.deletion_requested");
        Assert.Contains(auditSink.AllEvents, e => e.Action == "biometric_retention.deletion_completed");
        Assert.Equal("person_1", gateway.FingerprintGalleryRemovalRequestedForPersonId);
    }

    [Fact]
    public async Task AC02_deleting_a_record_that_does_not_exist_is_a_typed_not_found_outcome_not_an_exception()
    {
        var gateway = new FakeTemplateDeletionGateway { FingerprintOwnerPersonId = null };
        var auditSink = new InMemoryBiometricRetentionAuditSink();
        var service = CreateService(gateway, auditSink);

        var result = await service.DeleteAsync(Request(), CancellationToken.None);

        Assert.Equal(DeletionOutcome.NotFound, result.Outcome);
        Assert.Null(result.GalleryPropagated);
        Assert.Contains(auditSink.AllEvents, e => e.Action == "biometric_retention.deletion_not_found");
        Assert.Null(gateway.FingerprintGalleryRemovalRequestedForPersonId);
    }

    [Fact]
    public async Task AC04_face_reference_deletion_routes_through_the_face_gateway_methods_not_the_fingerprint_ones()
    {
        var gateway = new FakeTemplateDeletionGateway { FaceOwnerPersonId = "person_2", FaceGalleryRemoved = true };
        var service = CreateService(gateway);

        var result = await service.DeleteAsync(Request(BiometricRecordKind.FaceReference), CancellationToken.None);

        Assert.Equal(DeletionOutcome.Deleted, result.Outcome);
        Assert.Equal("person_2", gateway.FaceGalleryRemovalRequestedForPersonId);
        Assert.Null(gateway.FingerprintGalleryRemovalRequestedForPersonId);
    }

    /// <summary>ZB-BIO009-06 (round 3): a gallery-propagation failure no longer reports the plain
    /// success outcome - the record was genuinely deleted, but convergence has not happened, so
    /// the caller must be able to tell the two apart.</summary>
    [Fact]
    public async Task AC04_a_cache_that_cannot_be_cleared_yields_a_distinct_pending_outcome_not_plain_success()
    {
        var gateway = new FakeTemplateDeletionGateway { FingerprintOwnerPersonId = "person_1", FingerprintGalleryRemoved = false };
        var service = CreateService(gateway);

        var result = await service.DeleteAsync(Request(), CancellationToken.None);

        Assert.Equal(DeletionOutcome.DeletedPendingGalleryCleanup, result.Outcome);
        Assert.False(result.GalleryPropagated);
    }

    /// <summary>ZA-BIO009-01: the caller never supplies a PersonId (see
    /// <see cref="TemplateDeletionRequest"/>'s own header) - gallery eviction uses only the
    /// identity the gateway itself resolved from the deleted record, proven here by configuring
    /// the gateway to resolve a DIFFERENT person than any value a caller could have guessed or
    /// supplied and confirming eviction still targets exactly that resolved identity.</summary>
    [Fact]
    public async Task AC04_gallery_eviction_uses_only_the_identity_the_gateway_resolved_from_the_deleted_record()
    {
        var gateway = new FakeTemplateDeletionGateway { FingerprintOwnerPersonId = "person_the_record_actually_belongs_to", FingerprintGalleryRemoved = true };
        var service = CreateService(gateway);

        await service.DeleteAsync(Request(), CancellationToken.None);

        Assert.Equal("person_the_record_actually_belongs_to", gateway.FingerprintGalleryRemovalRequestedForPersonId);
    }

    /// <summary>ZB-BIO009-06: a throw from gallery removal (not just a `false` return) must never
    /// lose the resolved person id - the pending-cleanup store still holds it afterward, and the
    /// deletion itself is still reported (as pending, not silently dropped or re-thrown).</summary>
    [Fact]
    public async Task AC04_a_throw_from_gallery_removal_leaves_a_durable_pending_cleanup_record_not_a_lost_person_id()
    {
        var gateway = new FakeTemplateDeletionGateway { FingerprintOwnerPersonId = "person_3", ThrowOnFingerprintGalleryRemoval = true };
        var pendingStore = new InMemoryPendingGalleryCleanupStore();
        var service = CreateService(gateway, pendingCleanupStore: pendingStore);

        var result = await service.DeleteAsync(Request(), CancellationToken.None);

        Assert.Equal(DeletionOutcome.DeletedPendingGalleryCleanup, result.Outcome);
        var pending = await pendingStore.ListAsync("ten_a", CancellationToken.None);
        Assert.Contains(pending, p => p.PersonId == "person_3" && p.Kind == BiometricRecordKind.FingerprintSession);
    }

    /// <summary>ZB-BIO009-06: the recovery path. A pending cleanup obligation left by a prior
    /// failed deletion converges once the gateway starts succeeding, via a later, independent
    /// call - the person id was never lost.</summary>
    [Fact]
    public async Task ReconcilePendingGalleryCleanupAsync_resolves_a_pending_obligation_once_the_gateway_recovers()
    {
        var gateway = new FakeTemplateDeletionGateway { FingerprintOwnerPersonId = "person_4", FingerprintGalleryRemoved = false };
        var pendingStore = new InMemoryPendingGalleryCleanupStore();
        var service = CreateService(gateway, pendingCleanupStore: pendingStore);
        var pendingResult = await service.DeleteAsync(Request(), CancellationToken.None);
        Assert.Equal(DeletionOutcome.DeletedPendingGalleryCleanup, pendingResult.Outcome);

        // The gallery gateway is now healthy.
        gateway.FingerprintGalleryRemoved = true;
        var reconciliation = await service.ReconcilePendingGalleryCleanupAsync("ten_a", CancellationToken.None);

        Assert.Equal(1, reconciliation.Resolved);
        Assert.Equal(0, reconciliation.StillPending);
        Assert.Empty(await pendingStore.ListAsync("ten_a", CancellationToken.None));
    }

    [Fact]
    public async Task ReconcilePendingGalleryCleanupAsync_reports_still_pending_when_the_gateway_is_still_unhealthy()
    {
        var gateway = new FakeTemplateDeletionGateway { FingerprintOwnerPersonId = "person_5", FingerprintGalleryRemoved = false };
        var pendingStore = new InMemoryPendingGalleryCleanupStore();
        var service = CreateService(gateway, pendingCleanupStore: pendingStore);
        await service.DeleteAsync(Request(), CancellationToken.None);

        var reconciliation = await service.ReconcilePendingGalleryCleanupAsync("ten_a", CancellationToken.None);

        Assert.Equal(0, reconciliation.Resolved);
        Assert.Equal(1, reconciliation.StillPending);
        Assert.NotEmpty(await pendingStore.ListAsync("ten_a", CancellationToken.None));
    }

    [Fact]
    public async Task ReconcilePendingGalleryCleanupAsync_is_a_no_op_when_nothing_is_pending()
    {
        var service = CreateService();

        var reconciliation = await service.ReconcilePendingGalleryCleanupAsync("ten_a", CancellationToken.None);

        Assert.Equal(0, reconciliation.Resolved);
        Assert.Equal(0, reconciliation.StillPending);
    }

    /// <summary>ZB-BIO009-06: if the delete call itself throws, nothing was actually deleted, so
    /// the speculative pending-cleanup record recorded just before it must not survive.</summary>
    [Fact]
    public async Task a_throw_from_the_delete_call_itself_removes_the_speculative_pending_record_and_rethrows()
    {
        var gateway = new FakeTemplateDeletionGateway { FingerprintOwnerPersonId = "person_6", ThrowOnFingerprintDelete = true };
        var pendingStore = new InMemoryPendingGalleryCleanupStore();
        var service = CreateService(gateway, pendingCleanupStore: pendingStore);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(Request(), CancellationToken.None));

        Assert.Empty(await pendingStore.ListAsync("ten_a", CancellationToken.None));
    }

    /// <summary>Round 4 (god's dispatch): a delete call that throws but ACTUALLY committed first
    /// must not have its pending-cleanup record discarded - that would strand the gallery entry
    /// with no recovery path at all, exactly what "durable" exists to prevent.</summary>
    [Fact]
    public async Task a_throw_from_delete_that_turns_out_to_have_actually_deleted_the_record_keeps_the_pending_record()
    {
        var gateway = new FakeTemplateDeletionGateway
        {
            FingerprintOwnerPersonId = "person_7",
            ThrowOnFingerprintDelete = true,
            SimulateRecordActuallyDeletedDespiteThrow = true,
        };
        var pendingStore = new InMemoryPendingGalleryCleanupStore();
        var service = CreateService(gateway, pendingCleanupStore: pendingStore);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(Request(), CancellationToken.None));

        var pending = await pendingStore.ListAsync("ten_a", CancellationToken.None);
        Assert.Contains(pending, p => p.PersonId == "person_7");
    }

    /// <summary>Round 4: when verifying whether the delete actually committed is ITSELF
    /// unavailable, that is equally ambiguous - never treated as "confirmed not deleted."</summary>
    [Fact]
    public async Task a_throw_from_delete_whose_own_verification_also_fails_keeps_the_pending_record()
    {
        var gateway = new FakeTemplateDeletionGateway
        {
            FingerprintOwnerPersonId = "person_8",
            ThrowOnFingerprintDelete = true,
            ThrowOnFingerprintResolveAfterFirstCall = true,
        };
        var pendingStore = new InMemoryPendingGalleryCleanupStore();
        var service = CreateService(gateway, pendingCleanupStore: pendingStore);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(Request(), CancellationToken.None));

        var pending = await pendingStore.ListAsync("ten_a", CancellationToken.None);
        Assert.Contains(pending, p => p.PersonId == "person_8");
    }

    private sealed class FakeTemplateDeletionGateway : ITemplateDeletionGateway
    {
        public string? FingerprintOwnerPersonId { get; set; }
        public string? FaceOwnerPersonId { get; set; }
        public bool FingerprintGalleryRemoved { get; set; }
        public bool FaceGalleryRemoved { get; set; }
        public bool ThrowOnFingerprintGalleryRemoval { get; set; }
        public bool ThrowOnFingerprintDelete { get; set; }
        /// <summary>Round 4: the SECOND+ call to <see cref="ResolveFingerprintSessionOwnerAsync"/>
        /// (the catch block's own verification, after a throw from the delete call) returns
        /// <c>null</c> instead of <see cref="FingerprintOwnerPersonId"/> - simulating "the delete
        /// actually committed despite throwing."</summary>
        public bool SimulateRecordActuallyDeletedDespiteThrow { get; set; }
        /// <summary>Round 4: the SECOND+ call to <see cref="ResolveFingerprintSessionOwnerAsync"/>
        /// throws - simulating "verification itself is unavailable," equally ambiguous.</summary>
        public bool ThrowOnFingerprintResolveAfterFirstCall { get; set; }
        public string? FingerprintGalleryRemovalRequestedForPersonId { get; private set; }
        public string? FaceGalleryRemovalRequestedForPersonId { get; private set; }

        private int _fingerprintResolveCalls;

        public Task<string?> ResolveFingerprintSessionOwnerAsync(string tenantId, string sessionId, CancellationToken cancellationToken)
        {
            _fingerprintResolveCalls++;
            if (_fingerprintResolveCalls > 1)
            {
                if (ThrowOnFingerprintResolveAfterFirstCall)
                {
                    throw new InvalidOperationException("simulated resolve outage");
                }

                if (SimulateRecordActuallyDeletedDespiteThrow)
                {
                    return Task.FromResult<string?>(null);
                }
            }

            return Task.FromResult(FingerprintOwnerPersonId);
        }

        public Task<string?> ResolveFaceReferenceOwnerAsync(string tenantId, string referenceId, CancellationToken cancellationToken) =>
            Task.FromResult(FaceOwnerPersonId);

        public Task<(DeletionOutcome Outcome, string? PersonId)> DeleteFingerprintSessionAsync(
            string tenantId, string sessionId, string reason, string actorId, CancellationToken cancellationToken)
        {
            if (ThrowOnFingerprintDelete)
            {
                throw new InvalidOperationException("simulated delete failure");
            }

            return Task.FromResult((DeletionOutcome.Deleted, FingerprintOwnerPersonId));
        }

        public Task<(DeletionOutcome Outcome, string? PersonId)> DeleteFaceReferenceAsync(
            string tenantId, string referenceId, CancellationToken cancellationToken) =>
            Task.FromResult((DeletionOutcome.Deleted, FaceOwnerPersonId));

        public Task<bool> RemoveFromFingerprintGalleryAsync(string tenantId, string personId, CancellationToken cancellationToken)
        {
            FingerprintGalleryRemovalRequestedForPersonId = personId;
            if (ThrowOnFingerprintGalleryRemoval)
            {
                throw new InvalidOperationException("simulated gallery outage");
            }

            return Task.FromResult(FingerprintGalleryRemoved);
        }

        public Task<bool> RemoveFromFaceGalleryAsync(string tenantId, string personId, CancellationToken cancellationToken)
        {
            FaceGalleryRemovalRequestedForPersonId = personId;
            return Task.FromResult(FaceGalleryRemoved);
        }
    }
}
