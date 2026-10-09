// Developer tests for BIO-CRD-007. SYNTHETIC fixtures only.
using Biolog.Features.CardProduction.Audit;
using Biolog.Features.CardProduction.Domain;
using Xunit;

namespace Biolog.Features.CardProduction.Tests;

public sealed class ProductionJobRepositoryTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string CardClass = "staff";

    private static ProductionJob NewJob(int sequenceNumber, int jobSequenceNumber) => new(TenantId, PersonId, CardClass, sequenceNumber, jobSequenceNumber)
    {
        Status = ProductionJobStatus.InProgress,
        Version = 1,
        PrinterRef = "printer-1",
        Operator = new AuditActor(ActorType.User, "operator-1"),
        WorkstationRef = "workstation-1",
        Source = "connected",
        CreatedAt = "2026-01-01T00:00:00.000Z",
        UpdatedAt = "2026-01-01T00:00:00.000Z",
    };

    [Fact]
    public void NextJobSequenceNumber_starts_at_1_and_increments_per_reprint()
    {
        var repository = new InMemoryProductionJobRepository();
        Assert.Equal(1, repository.NextJobSequenceNumber(TenantId, PersonId, CardClass, 1));

        repository.Create(NewJob(1, 1));
        Assert.Equal(2, repository.NextJobSequenceNumber(TenantId, PersonId, CardClass, 1));

        repository.Create(NewJob(1, 2));
        Assert.Equal(3, repository.NextJobSequenceNumber(TenantId, PersonId, CardClass, 1));
    }

    [Fact]
    public void Create_throws_on_a_collision_at_an_already_occupied_key()
    {
        var repository = new InMemoryProductionJobRepository();
        repository.Create(NewJob(1, 1));

        var error = Assert.Throws<ProductionError>(() => repository.Create(NewJob(1, 1)));
        Assert.Equal("production_job_collision", error.Code);
    }

    [Fact]
    public void Replace_succeeds_only_when_the_stored_row_still_structurally_equals_expected()
    {
        var repository = new InMemoryProductionJobRepository();
        var job = repository.Create(NewJob(1, 1));
        var updated = job with { Status = ProductionJobStatus.Passed, Version = 2 };

        var result = repository.Replace(job, updated);

        Assert.Equal(ProductionJobStatus.Passed, result.Status);
    }

    [Fact]
    public void Replace_throws_concurrency_conflict_on_a_stale_expected_row()
    {
        var repository = new InMemoryProductionJobRepository();
        var job = repository.Create(NewJob(1, 1));
        repository.Replace(job, job with { Status = ProductionJobStatus.Failed, Version = 2 });

        var error = Assert.Throws<ProductionError>(() => repository.Replace(job, job with { Status = ProductionJobStatus.Passed, Version = 2 }));
        Assert.Equal("concurrency_conflict", error.Code);
    }

    [Fact]
    public void List_is_ordered_by_job_sequence_number_ascending()
    {
        var repository = new InMemoryProductionJobRepository();
        repository.Create(NewJob(1, 1));
        repository.Create(NewJob(1, 2));

        var jobs = repository.List(TenantId, PersonId, CardClass, 1);

        Assert.Equal([1, 2], jobs.Select(j => j.JobSequenceNumber));
    }

    [Fact]
    public void GetLatest_returns_the_highest_job_sequence_number()
    {
        var repository = new InMemoryProductionJobRepository();
        repository.Create(NewJob(1, 1));
        repository.Create(NewJob(1, 2));

        var latest = repository.GetLatest(TenantId, PersonId, CardClass, 1);

        Assert.Equal(2, latest!.JobSequenceNumber);
    }

    [Fact]
    public void GetLatest_returns_null_when_this_card_has_never_been_produced()
    {
        var repository = new InMemoryProductionJobRepository();

        Assert.Null(repository.GetLatest(TenantId, PersonId, CardClass, 1));
    }

    [Fact]
    public void List_is_scoped_per_tenant()
    {
        var repository = new InMemoryProductionJobRepository();
        repository.Create(NewJob(1, 1));
        const string otherTenant = "ten_018f2e5a-1234-7abc-89ab-0123456789ac";
        repository.Create(new ProductionJob(otherTenant, PersonId, CardClass, 1, 1)
        {
            Status = ProductionJobStatus.InProgress,
            Version = 1,
            PrinterRef = "printer-1",
            Operator = new AuditActor(ActorType.User, "operator-1"),
            WorkstationRef = "workstation-1",
            Source = "connected",
            CreatedAt = "2026-01-01T00:00:00.000Z",
            UpdatedAt = "2026-01-01T00:00:00.000Z",
        });

        var jobs = repository.List(TenantId, PersonId, CardClass, 1);

        Assert.Single(jobs);
        Assert.Equal(TenantId, jobs[0].TenantId);
    }

    [Fact]
    public void BIO_RPT_002_ListForTenant_returns_every_job_for_that_tenant_across_every_person_and_card_class_but_not_another_tenants()
    {
        var repository = new InMemoryProductionJobRepository();
        repository.Create(NewJob(1, 1));
        repository.Create(new ProductionJob(TenantId, "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5c", "visitor", 1, 1)
        {
            Status = ProductionJobStatus.Failed,
            Version = 1,
            PrinterRef = "printer-1",
            Operator = new AuditActor(ActorType.User, "operator-1"),
            WorkstationRef = "workstation-1",
            Source = "connected",
            CreatedAt = "2026-01-01T00:00:00.000Z",
            UpdatedAt = "2026-01-01T00:00:00.000Z",
        });
        const string otherTenant = "ten_018f2e5a-1234-7abc-89ab-0123456789ac";
        repository.Create(new ProductionJob(otherTenant, PersonId, CardClass, 1, 1)
        {
            Status = ProductionJobStatus.InProgress,
            Version = 1,
            PrinterRef = "printer-1",
            Operator = new AuditActor(ActorType.User, "operator-1"),
            WorkstationRef = "workstation-1",
            Source = "connected",
            CreatedAt = "2026-01-01T00:00:00.000Z",
            UpdatedAt = "2026-01-01T00:00:00.000Z",
        });

        var jobs = repository.ListForTenant(TenantId);

        Assert.Equal(2, jobs.Count);
        Assert.All(jobs, j => Assert.Equal(TenantId, j.TenantId));
    }
}
