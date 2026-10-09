using System.Collections.Concurrent;
using Biolog.Features.Biometrics.Domain;
using Biolog.Features.Biometrics.Matching;
using Biolog.Features.Biometrics.Providers;
using Biolog.Features.Biometrics.Synthetic;
using Biolog.Features.FingerprintSearch.Domain;
using Biolog.Features.FingerprintSearch.Service;

namespace Biolog.Features.FingerprintSearch.Tests;

internal static class Program
{
    private const string TenantId = "ten_acceptance";
    private const string SubjectId = "per_subject";
    private const string CandidateId = "per_candidate";
    private const string ActorId = "per_actor";
    private const string NowUtc = "2026-09-23T02:55:00Z";

    private static async Task<int> Main()
    {
        var repository = new ControlledReviewRepository();
        var service = new FingerprintSearchService(
            new InMemoryFingerprintGalleryStore(),
            new SyntheticFingerprintProvider(),
            new AlwaysMatchMatcher(),
            new SyntheticFingerprintDuplicateThresholdPolicy(),
            new AllowSearchPermissionGateway(),
            repository,
            new FingerprintDuplicateReviewLockRegistry(),
            newId: () => Guid.NewGuid().ToString("N"),
            nowUtc: () => NowUtc);

        await service.IndexAsync(TenantId, CandidateId, Sample([1, 2, 3]), CancellationToken.None);

        var firstSearch = Task.Run(
            () => service.SearchAsync(TenantId, SubjectId, ActorId, Sample([9]), 10, CancellationToken.None));
        await repository.FirstReadEntered.WaitAsync(TimeSpan.FromSeconds(5));

        var secondSearch = Task.Run(
            () => service.SearchAsync(TenantId, SubjectId, ActorId, Sample([9]), 10, CancellationToken.None));
        var secondCompletedBeforeRelease =
            await Task.WhenAny(secondSearch, Task.Delay(150)) == secondSearch;

        repository.ReleaseFirstRead();
        await Task.WhenAll(firstSearch, secondSearch).WaitAsync(TimeSpan.FromSeconds(5));

        if (secondCompletedBeforeRelease || repository.Count != 1 || repository.CreateAttempts != 1)
        {
            Console.Error.WriteLine(
                $"FAIL: blocked={(!secondCompletedBeforeRelease).ToString().ToLowerInvariant()}, entries={repository.Count}, creates={repository.CreateAttempts}.");
            return 1;
        }

        Console.WriteLine("PASS: the second same-pair search waited while the first review read-check-write was in progress.");
        Console.WriteLine("PASS: both searches completed and converged on one review entry with one create attempt.");
        return 0;
    }

    private static BiometricSample Sample(byte[] payload) => new()
    {
        Payload = payload,
        ContentType = "application/octet-stream",
        CapturedAtUtc = NowUtc,
    };

    private sealed class AllowSearchPermissionGateway : IBiometricSearchPermissionGateway
    {
        public Task<bool> HasPermissionAsync(
            string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class AlwaysMatchMatcher : IBiometricMatcher
    {
        public string MatcherId => "acceptance-always-match";

        public Task<BiometricMatchResult> CompareAsync(
            BiometricTemplate probe, BiometricTemplate candidate, CancellationToken cancellationToken) =>
            Task.FromResult(new BiometricMatchResult
            {
                IsMatch = true,
                Score = 1.0,
                MatcherId = MatcherId,
                MatchedTemplateId = candidate.Id,
            });
    }

    private sealed class ControlledReviewRepository : IFingerprintDuplicateReviewRepository
    {
        private readonly ConcurrentDictionary<(string TenantId, string Low, string High), FingerprintDuplicateReviewEntry> _entries = new();
        private readonly TaskCompletionSource _firstReadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirstRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _readCount;
        private int _createAttempts;

        public Task FirstReadEntered => _firstReadEntered.Task;
        public int Count => _entries.Count;
        public int CreateAttempts => Volatile.Read(ref _createAttempts);

        public void ReleaseFirstRead() => _releaseFirstRead.TrySetResult();

        public async Task<FingerprintDuplicateReviewEntry?> GetAsync(
            string tenantId, string personIdLow, string personIdHigh, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _readCount) == 1)
            {
                _firstReadEntered.TrySetResult();
                await _releaseFirstRead.Task.WaitAsync(cancellationToken);
            }

            _entries.TryGetValue((tenantId, personIdLow, personIdHigh), out var entry);
            return entry;
        }

        public Task<FingerprintDuplicateReviewEntry> CreateAsync(
            FingerprintDuplicateReviewEntry entry, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _createAttempts);
            if (!_entries.TryAdd((entry.TenantId, entry.PersonIdLow, entry.PersonIdHigh), entry))
            {
                throw new InvalidOperationException("fingerprint_duplicate_review_entry_already_exists");
            }
            return Task.FromResult(entry);
        }

        public Task<FingerprintDuplicateReviewEntry> ReplaceAsync(
            FingerprintDuplicateReviewEntry expected, FingerprintDuplicateReviewEntry updated, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<FingerprintDuplicateReviewEntry>> ListForTenantAsync(
            string tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FingerprintDuplicateReviewEntry>>(
                _entries.Values.Where(entry => entry.TenantId == tenantId).ToList());
    }
}
