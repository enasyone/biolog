using Xunit;

namespace Biolog.Features.TransportNotifications.Tests;

public sealed class SeyiRuleConcurrencyProbe
{
    private const string TenantId = "ten_a";
    private static readonly AuditActor Actor = new(ActorType.User, "per_reviewer");

    [Fact]
    public async Task Concurrent_stale_rule_updates_must_not_both_commit()
    {
        var initial = TransportNotificationRule.Create(
            TenantId,
            "Board alerts",
            new HashSet<TransportNotificationEventType> { TransportNotificationEventType.Board });
        var repository = new CoordinatedRepository(initial);
        var service = new TransportNotificationRuleService(repository, new FakePermissionGateway());

        var first = service.SetEnabledAsync(TenantId, Actor, initial.RuleId, false, initial.Version, CancellationToken.None);
        var second = service.SetEnabledAsync(TenantId, Actor, initial.RuleId, true, initial.Version, CancellationToken.None);

        // Post-fix, the loser of the race is expected to THROW RuleConcurrencyConflictException
        // (that is the fix: a rejected stale write, not a silent clobber) - the original probe's
        // own bare `await Task.WhenAll(first, second)` assumed neither call could ever throw,
        // true only under the pre-fix blind-write bug. Tolerate exactly one conflict here; any
        // other exception, or both/neither throwing, still fails this probe.
        var outcomes = await Task.WhenAll(
            first.ContinueWith(t => t.Exception?.GetBaseException()),
            second.ContinueWith(t => t.Exception?.GetBaseException()));
        var conflicts = outcomes.Count(ex => ex is RuleConcurrencyConflictException);
        var unexpected = outcomes.FirstOrDefault(ex => ex is not null and not RuleConcurrencyConflictException);
        Assert.Null(unexpected);
        Assert.Equal(1, conflicts);

        Assert.Equal(1, repository.UpdateCount);
    }

    /// <summary>
    /// Adapted to this story's post-fix <c>ITransportNotificationRuleRepository</c> shape
    /// (SY-TRN003-01's own fix replaced the old blind-write <c>UpdateAsync</c> with an atomic
    /// <c>TryReplaceAsync</c>) - the gating strategy itself is untouched from the original probe:
    /// <see cref="GetAsync"/> still blocks each caller until BOTH have read the identical stale
    /// snapshot before either is released, so this probe still proves the race at the real
    /// contested point (two callers racing from one shared stale read), now against the real
    /// compare-and-swap semantics rather than a blind assignment.
    /// </summary>
    private sealed class CoordinatedRepository(TransportNotificationRule initial) : ITransportNotificationRuleRepository
    {
        private readonly TaskCompletionSource _bothReads = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Lock _gate = new();
        private TransportNotificationRule _current = initial;
        private int _readCount;
        private int _updateCount;

        public int UpdateCount => _updateCount;

        public Task AddAsync(TransportNotificationRule rule, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async Task<TransportNotificationRule?> GetAsync(string tenantId, string ruleId, CancellationToken cancellationToken = default)
        {
            var snapshot = _current;
            if (Interlocked.Increment(ref _readCount) == 2)
            {
                _bothReads.SetResult();
            }

            await _bothReads.Task;
            return snapshot;
        }

        public Task<IReadOnlyList<TransportNotificationRule>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> TryReplaceAsync(TransportNotificationRule expectedCurrent, TransportNotificationRule updated, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (!ReferenceEquals(_current, expectedCurrent) && !Equals(_current, expectedCurrent))
                {
                    return Task.FromResult(false);
                }

                _current = updated;
                Interlocked.Increment(ref _updateCount);
                return Task.FromResult(true);
            }
        }
    }
}
