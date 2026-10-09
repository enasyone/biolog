// Developer tests for BIO-CRD-006. SYNTHETIC fixtures only.
using Biolog.Features.CardLifecycle.Audit;
using Biolog.Features.CardLifecycle.Domain;
using Xunit;

namespace Biolog.Features.CardLifecycle.Tests;

public sealed class CardLifecycleRepositoryTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string CardClass = "staff";

    private static CardLifecycleRecord NewRecord(int sequenceNumber, CardLockState state, int version = 1) => new(TenantId, PersonId, CardClass, sequenceNumber)
    {
        LockState = state,
        Version = version,
        ReasonCode = "lost",
        ReportedAt = "2026-01-01T00:00:00.000Z",
        ReportedBy = new AuditActor(ActorType.User, "operator-1"),
        CreatedAt = "2026-01-01T00:00:00.000Z",
        UpdatedAt = "2026-01-01T00:00:00.000Z",
    };

    [Fact]
    public void Create_then_Get_round_trips()
    {
        var repository = new InMemoryCardLifecycleRepository();
        var record = NewRecord(1, CardLockState.Locked);

        repository.Create(record);

        Assert.Equal(record, repository.Get(TenantId, PersonId, CardClass, 1));
    }

    [Fact]
    public void Create_throws_on_a_collision_at_an_already_occupied_key()
    {
        var repository = new InMemoryCardLifecycleRepository();
        repository.Create(NewRecord(1, CardLockState.Locked));

        var error = Assert.Throws<CardLifecycleError>(() => repository.Create(NewRecord(1, CardLockState.Locked)));
        Assert.Equal("card_lifecycle_record_collision", error.Code);
    }

    [Fact]
    public void Replace_succeeds_only_when_the_stored_row_still_structurally_equals_expected()
    {
        var repository = new InMemoryCardLifecycleRepository();
        var record = repository.Create(NewRecord(1, CardLockState.Locked));
        var updated = record with { LockState = CardLockState.Recovered, Version = 2 };

        var result = repository.Replace(record, updated);

        Assert.Equal(CardLockState.Recovered, result.LockState);
    }

    [Fact]
    public void Replace_throws_concurrency_conflict_on_a_stale_expected_row()
    {
        var repository = new InMemoryCardLifecycleRepository();
        var record = repository.Create(NewRecord(1, CardLockState.Locked));
        repository.Replace(record, record with { LockState = CardLockState.Recovered, Version = 2 });

        var error = Assert.Throws<CardLifecycleError>(() => repository.Replace(record, record with { LockState = CardLockState.Locked, Version = 2 }));
        Assert.Equal("concurrency_conflict", error.Code);
    }

    [Fact]
    public void Replace_throws_not_found_when_the_row_does_not_exist()
    {
        var repository = new InMemoryCardLifecycleRepository();
        var record = NewRecord(1, CardLockState.Locked);

        var error = Assert.Throws<CardLifecycleError>(() => repository.Replace(record, record with { Version = 2 }));
        Assert.Equal("card_lifecycle_record_not_found", error.Code);
    }

    [Fact]
    public void ListDenyList_includes_Locked_and_PermanentlyRevoked_but_not_Recovered()
    {
        var repository = new InMemoryCardLifecycleRepository();
        repository.Create(NewRecord(1, CardLockState.Locked));
        repository.Create(NewRecord(2, CardLockState.Recovered));
        repository.Create(NewRecord(3, CardLockState.PermanentlyRevoked));

        var denyList = repository.ListDenyList(TenantId, since: null);

        Assert.Equal([1, 3], denyList.Select(r => r.SequenceNumber).OrderBy(n => n));
    }

    [Fact]
    public void ListDenyList_is_scoped_per_tenant()
    {
        var repository = new InMemoryCardLifecycleRepository();
        repository.Create(NewRecord(1, CardLockState.Locked));
        var otherTenant = "ten_018f2e5a-1234-7abc-89ab-0123456789ac";
        repository.Create(new CardLifecycleRecord(otherTenant, PersonId, CardClass, 1)
        {
            LockState = CardLockState.Locked,
            Version = 1,
            ReasonCode = "stolen",
            ReportedAt = "2026-01-01T00:00:00.000Z",
            ReportedBy = new AuditActor(ActorType.User, "operator-1"),
            CreatedAt = "2026-01-01T00:00:00.000Z",
            UpdatedAt = "2026-01-01T00:00:00.000Z",
        });

        var denyList = repository.ListDenyList(TenantId, since: null);

        Assert.Single(denyList);
        Assert.Equal(TenantId, denyList[0].TenantId);
    }

    [Fact]
    public void ListDenyList_filters_by_since()
    {
        var repository = new InMemoryCardLifecycleRepository();
        var early = NewRecord(1, CardLockState.Locked) with { UpdatedAt = "2026-01-01T00:00:00.000Z" };
        var late = NewRecord(2, CardLockState.Locked) with { UpdatedAt = "2026-06-01T00:00:00.000Z" };
        repository.Create(early);
        repository.Create(late);

        var denyList = repository.ListDenyList(TenantId, since: "2026-03-01T00:00:00.000Z");

        Assert.Single(denyList);
        Assert.Equal(2, denyList[0].SequenceNumber);
    }
}
