using Biolog.Features.BiometricRetention.Domain;
using Xunit;

namespace Biolog.Features.BiometricRetention.Tests;

/// <summary>Round 4 (god's dispatch): the whole point of this store is surviving a process
/// restart - proven here by creating TWO separate instances over the same file path and
/// confirming the second one sees what the first one wrote, nothing shared in memory between
/// them.</summary>
public sealed class FileBackedPendingGalleryCleanupStoreTests
{
    private static string NewTempPath() =>
        Path.Combine(Path.GetTempPath(), $"bio-bio-009-pending-gallery-cleanup-tests-{Guid.NewGuid():N}.json");

    private static PendingGalleryCleanup Entry(string tenantId, string personId, string recordId) => new()
    {
        TenantId = tenantId,
        Kind = BiometricRecordKind.FingerprintSession,
        PersonId = personId,
        RecordId = recordId,
        CreatedAtUtc = "2026-01-01T00:00:00Z",
    };

    [Fact]
    public async Task a_recorded_entry_survives_a_simulated_process_restart()
    {
        var path = NewTempPath();
        try
        {
            var first = new FileBackedPendingGalleryCleanupStore(path);
            await first.RecordAsync(Entry("ten_a", "person_1", "rec_1"), CancellationToken.None);

            // A fresh instance over the SAME file path - nothing is shared in memory with
            // `first`, the same shape a real process restart has.
            var second = new FileBackedPendingGalleryCleanupStore(path);
            var pending = await second.ListAsync("ten_a", CancellationToken.None);

            Assert.Contains(pending, p => p.PersonId == "person_1" && p.RecordId == "rec_1");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RemoveAsync_removes_the_entry_durably_too()
    {
        var path = NewTempPath();
        try
        {
            var first = new FileBackedPendingGalleryCleanupStore(path);
            await first.RecordAsync(Entry("ten_a", "person_2", "rec_2"), CancellationToken.None);
            await first.RemoveAsync("ten_a", BiometricRecordKind.FingerprintSession, "person_2", CancellationToken.None);

            var second = new FileBackedPendingGalleryCleanupStore(path);
            var pending = await second.ListAsync("ten_a", CancellationToken.None);

            Assert.DoesNotContain(pending, p => p.PersonId == "person_2");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ListAsync_returns_empty_for_a_tenant_when_no_file_exists_yet()
    {
        var path = NewTempPath();
        var store = new FileBackedPendingGalleryCleanupStore(path);

        var pending = await store.ListAsync("ten_nonexistent", CancellationToken.None);

        Assert.Empty(pending);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ListAsync_only_returns_entries_for_the_requested_tenant()
    {
        var path = NewTempPath();
        try
        {
            var store = new FileBackedPendingGalleryCleanupStore(path);
            await store.RecordAsync(Entry("ten_a", "person_3", "rec_3"), CancellationToken.None);
            await store.RecordAsync(Entry("ten_b", "person_4", "rec_4"), CancellationToken.None);

            var pendingForTenantA = await store.ListAsync("ten_a", CancellationToken.None);

            Assert.Contains(pendingForTenantA, p => p.PersonId == "person_3");
            Assert.DoesNotContain(pendingForTenantA, p => p.PersonId == "person_4");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
