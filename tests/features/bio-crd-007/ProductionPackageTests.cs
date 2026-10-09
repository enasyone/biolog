// Developer tests for BIO-CRD-007. SYNTHETIC fixtures only.
using System.Threading;
using Biolog.Features.CardProduction.Audit;
using Biolog.Features.CardProduction.Domain;
using Biolog.Features.CardProduction.Identifiers;
using Xunit;

namespace Biolog.Features.CardProduction.Tests;

/// <summary>AC03: signed, time-limited production packages support permitted offline mode.</summary>
public sealed class ProductionPackageTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string CardClass = "staff";

    private static AuditActor Operator() => new(ActorType.User, "operator-1");

    private static (ProductionService Service, InMemoryProductionJobRepository JobRepository, InMemoryProductionPackageStore PackageStore, InMemoryProductionSigningKeyStore Keys, FakeCardIssueGateway Gateway) Build(string tenantId = TenantId)
    {
        var jobRepository = new InMemoryProductionJobRepository();
        var packageStore = new InMemoryProductionPackageStore();
        var keys = new InMemoryProductionSigningKeyStore();
        var gateway = new FakeCardIssueGateway();
        var service = new ProductionService(
            new FakeCurrentTenantProvider(tenantId), jobRepository, packageStore, keys,
            gateway, new InMemoryProductionAuditSink(), new ProductionJobLockRegistry(), new InMemoryIdempotencyStore(), new FakeCardActivationBarrier());
        return (service, jobRepository, packageStore, keys, gateway);
    }

    [Fact]
    public async Task IssuePackage_returns_a_token_and_a_durable_store_row_not_yet_consumed()
    {
        var (service, _, packageStore, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);

        var (package, token) = await service.IssuePackageAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, ttl: null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.False(package.Consumed);
        var stored = packageStore.Get(TenantId, package.PackageId);
        Assert.NotNull(stored);
    }

    [Fact]
    public async Task IssuePackage_fails_when_the_card_issue_does_not_exist()
    {
        var (service, _, _, _, _) = Build();

        var error = await Assert.ThrowsAsync<ProductionError>(() =>
            service.IssuePackageAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, ttl: null, Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("card_issue_not_found", error.Code);
    }

    [Fact]
    public async Task IssuePackage_rejects_a_ttl_beyond_the_maximum()
    {
        var (service, _, _, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);

        var error = await Assert.ThrowsAsync<ProductionError>(() =>
            service.IssuePackageAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, ttl: TimeSpan.FromDays(2), Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task CompleteOfflineJob_with_all_steps_passing_creates_a_Passed_job_and_consumes_the_package()
    {
        var (service, jobRepository, packageStore, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        var (package, token) = await service.IssuePackageAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, ttl: null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var job = await service.CompleteOfflineJobAsync(token, true, null, true, null, true, null, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        Assert.Equal(ProductionJobStatus.Passed, job.Status);
        Assert.Equal("offline", job.Source);
        Assert.Equal(package.PackageId, job.PackageId);
        Assert.True(packageStore.Get(TenantId, package.PackageId)!.Consumed);
        Assert.Single(jobRepository.List(TenantId, PersonId, CardClass, 1));
    }

    [Fact]
    public async Task CompleteOfflineJob_with_a_failing_step_creates_a_Failed_job()
    {
        var (service, _, _, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        var (_, token) = await service.IssuePackageAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, ttl: null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var job = await service.CompleteOfflineJobAsync(token, true, null, false, "encode mismatch", true, null, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        Assert.Equal(ProductionJobStatus.Failed, job.Status);
    }

    [Fact]
    public async Task CompleteOfflineJob_requires_Idempotency_Key()
    {
        var (service, _, _, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        var (_, token) = await service.IssuePackageAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, ttl: null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var error = await Assert.ThrowsAsync<ProductionError>(() =>
            service.CompleteOfflineJobAsync(token, true, null, true, null, true, null, "", Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("idempotency_key_required", error.Code);
    }

    [Fact]
    public async Task CompleteOfflineJob_rejects_a_tampered_token()
    {
        var (service, _, _, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        var (_, token) = await service.IssuePackageAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, ttl: null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var tampered = token[..^4] + "AAAA"; // corrupt the trailing signature bytes

        var error = await Assert.ThrowsAsync<ProductionError>(() =>
            service.CompleteOfflineJobAsync(tampered, true, null, true, null, true, null, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("production_package_signature_invalid", error.Code);
    }

    [Fact]
    public async Task CompleteOfflineJob_rejects_a_malformed_token()
    {
        var (service, _, _, _, _) = Build();

        var error = await Assert.ThrowsAsync<ProductionError>(() =>
            service.CompleteOfflineJobAsync("not-a-real-token", true, null, true, null, true, null, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("production_package_signature_invalid", error.Code);
    }

    [Fact]
    public async Task CompleteOfflineJob_rejects_a_token_signed_for_a_different_tenant()
    {
        var (issuingService, _, _, _, issuingGateway) = Build(tenantId: TenantId);
        issuingGateway.Seed(PersonId, CardClass, 1);
        var (_, token) = await issuingService.IssuePackageAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, ttl: null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        const string otherTenant = "ten_018f2e5a-1234-7abc-89ab-0123456789ac";
        var (otherService, _, _, _, otherGateway) = Build(tenantId: otherTenant);
        otherGateway.Seed(PersonId, CardClass, 1);

        var error = await Assert.ThrowsAsync<ProductionError>(() =>
            otherService.CompleteOfflineJobAsync(token, true, null, true, null, true, null, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("production_package_signature_invalid", error.Code); // a different tenant's key never produces a matching signature
    }

    [Fact]
    public async Task CompleteOfflineJob_rejects_an_expired_package()
    {
        var (service, _, _, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        var (_, token) = await service.IssuePackageAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, ttl: TimeSpan.FromMilliseconds(1), Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        await Task.Delay(50, CancellationToken.None);

        var error = await Assert.ThrowsAsync<ProductionError>(() =>
            service.CompleteOfflineJobAsync(token, true, null, true, null, true, null, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("production_package_expired", error.Code);
    }

    [Fact]
    public async Task CompleteOfflineJob_rejects_the_same_package_being_submitted_twice_under_different_idempotency_keys()
    {
        var (service, _, _, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        var (_, token) = await service.IssuePackageAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, ttl: null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        await service.CompleteOfflineJobAsync(token, true, null, true, null, true, null, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var error = await Assert.ThrowsAsync<ProductionError>(() =>
            service.CompleteOfflineJobAsync(token, true, null, true, null, true, null, "idem-2", Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("production_package_already_consumed", error.Code);
    }

    [Fact]
    public async Task CompleteOfflineJob_replaying_the_same_idempotency_key_returns_the_cached_result_without_re_consuming()
    {
        var (service, jobRepository, _, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        var (_, token) = await service.IssuePackageAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, ttl: null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var first = await service.CompleteOfflineJobAsync(token, true, null, true, null, true, null, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var second = await service.CompleteOfflineJobAsync(token, true, null, true, null, true, null, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Single(jobRepository.List(TenantId, PersonId, CardClass, 1));
    }

    /// <summary>
    /// Chioma round 1 (evidence/BIO-CRD-007/acceptance/REVIEW-Chioma-round1.md): every existing
    /// double-consumption test submitted the token sequentially (await, then await again), which
    /// proves idempotency-key replay handling but not the actual race this story's own design
    /// notes claim to close - two submissions racing for the SAME per-card lock at the SAME time.
    /// A <see cref="Barrier"/> holds both attempts at the starting line until both have finished
    /// the pure signature/payload parse (which needs no lock) and are about to call
    /// <see cref="ProductionService.CompleteOfflineJobAsync"/>, so they genuinely contend for
    /// <see cref="ProductionJobLockRegistry"/>'s lock rather than merely running one after another.
    /// </summary>
    [Fact]
    public async Task CompleteOfflineJob_two_truly_concurrent_submissions_of_the_same_package_yield_exactly_one_success()
    {
        var (service, jobRepository, packageStore, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        var (package, token) = await service.IssuePackageAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, ttl: null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        using var startLine = new Barrier(2);

        // Task.Run is required here: Barrier.SignalAndWait is a blocking synchronous call, and an
        // async method runs synchronously up to its first await. Calling AttemptAsync(...) directly
        // (without Task.Run) would let the FIRST call run synchronously into SignalAndWait and block
        // the calling thread there - the second call would never even be invoked, and the barrier
        // (needing 2 signals) would deadlock forever. Task.Run schedules each attempt onto its own
        // thread-pool thread immediately, so both genuinely reach the barrier before either proceeds.
        Task<ProductionJob?> AttemptAsync(string idempotencyKey) => Task.Run(async () =>
        {
            startLine.SignalAndWait();
            try
            {
                return await service.CompleteOfflineJobAsync(token, true, null, true, null, true, null, idempotencyKey, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
            }
            catch (ProductionError error) when (error.Code == "production_package_already_consumed")
            {
                return (ProductionJob?)null;
            }
        });

        var attemptA = AttemptAsync("idem-concurrent-a");
        var attemptB = AttemptAsync("idem-concurrent-b");
        var results = await Task.WhenAll(attemptA, attemptB);

        Assert.Single(results, r => r is not null);
        Assert.True(packageStore.Get(TenantId, package.PackageId)!.Consumed);
        Assert.Single(jobRepository.List(TenantId, PersonId, CardClass, 1));
    }

    [Fact]
    public async Task CompleteOfflineJob_on_a_reprint_carries_the_packages_own_reprint_reason_through()
    {
        var (service, _, _, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        var firstJob = await service.CreateJobAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        await service.CancelJobAsync(PersonId, CardClass, 1, 1, firstJob.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var (_, token) = await service.IssuePackageAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", "connected attempt was cancelled", ttl: null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var job = await service.CompleteOfflineJobAsync(token, true, null, true, null, true, null, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        Assert.Equal(2, job.JobSequenceNumber);
        Assert.Equal("connected attempt was cancelled", job.ReprintReason);
    }

    [Fact]
    public async Task CompleteOfflineJob_on_a_reprint_without_a_package_level_reprint_reason_is_refused()
    {
        var (service, _, _, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        var firstJob = await service.CreateJobAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        await service.CancelJobAsync(PersonId, CardClass, 1, 1, firstJob.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var (_, token) = await service.IssuePackageAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", reprintReason: null, ttl: null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var error = await Assert.ThrowsAsync<ProductionError>(() =>
            service.CompleteOfflineJobAsync(token, true, null, true, null, true, null, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("reprint_reason_required", error.Code);
    }
}
