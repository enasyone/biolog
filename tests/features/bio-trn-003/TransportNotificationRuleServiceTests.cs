using Biolog.Features.TransportNotifications;
using Xunit;

namespace Biolog.Features.TransportNotifications.Tests;

/// <summary>Grants every permission by default; add a key to <see cref="DeniedPermissionKeys"/> to simulate an actor who lacks it.</summary>
internal sealed class FakePermissionGateway : IPermissionGateway
{
    public HashSet<string> DeniedPermissionKeys { get; } = [];

    public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(!DeniedPermissionKeys.Contains(permissionKey));
}

/// <summary>BIO-TRN-003-AC01: "Notification rules can select board/alight/missed/exception events."</summary>
public sealed class TransportNotificationRuleServiceTests
{
    private static readonly AuditActor TestActor = new(ActorType.User, "per_test_actor");

    private static TransportNotificationRuleService Build(
        ITransportNotificationRuleRepository? repository = null, IPermissionGateway? permissionGateway = null) =>
        new(repository ?? new InMemoryTransportNotificationRuleRepository(), permissionGateway ?? new FakePermissionGateway());

    [Fact]
    public async Task Creating_a_rule_with_no_event_types_is_rejected()
    {
        var service = Build();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync("ten_a", TestActor, "Board alerts", new HashSet<TransportNotificationEventType>(), CancellationToken.None));
    }

    [Fact]
    public async Task Creating_a_rule_with_a_blank_name_is_rejected()
    {
        var service = Build();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync("ten_a", TestActor, "   ", new HashSet<TransportNotificationEventType> { TransportNotificationEventType.Board }, CancellationToken.None));
    }

    [Fact]
    public async Task Creating_a_rule_selecting_several_event_types_persists_exactly_those_types()
    {
        var service = Build();
        var eventTypes = new HashSet<TransportNotificationEventType> { TransportNotificationEventType.Board, TransportNotificationEventType.MissedAlight };

        var rule = await service.CreateAsync("ten_a", TestActor, "Board and missed-alight alerts", eventTypes, CancellationToken.None);

        Assert.Equal(eventTypes, rule.EventTypes);
        Assert.True(rule.Enabled);
        Assert.Equal(1, rule.Version);
    }

    [Fact]
    public async Task Creating_a_rule_without_the_manage_permission_is_rejected()
    {
        var gateway = new FakePermissionGateway();
        gateway.DeniedPermissionKeys.Add(TransportNotificationRuleService.ManagePermission);
        var service = Build(permissionGateway: gateway);

        await Assert.ThrowsAsync<PermissionRequiredException>(() =>
            service.CreateAsync("ten_a", TestActor, "Board alerts", new HashSet<TransportNotificationEventType> { TransportNotificationEventType.Board }, CancellationToken.None));
    }

    [Fact]
    public async Task Listing_rules_without_the_read_permission_is_rejected()
    {
        var gateway = new FakePermissionGateway();
        gateway.DeniedPermissionKeys.Add(TransportNotificationRuleService.ReadPermission);
        var service = Build(permissionGateway: gateway);

        await Assert.ThrowsAsync<PermissionRequiredException>(() => service.ListAsync("ten_a", TestActor, CancellationToken.None));
    }

    [Fact]
    public async Task Listing_rules_only_returns_rules_for_the_calling_tenant()
    {
        var repository = new InMemoryTransportNotificationRuleRepository();
        var service = Build(repository);

        await service.CreateAsync("ten_a", TestActor, "Tenant A rule", new HashSet<TransportNotificationEventType> { TransportNotificationEventType.Board }, CancellationToken.None);
        await service.CreateAsync("ten_b", TestActor, "Tenant B rule", new HashSet<TransportNotificationEventType> { TransportNotificationEventType.Alight }, CancellationToken.None);

        var rulesForA = await service.ListAsync("ten_a", TestActor, CancellationToken.None);

        var rule = Assert.Single(rulesForA);
        Assert.Equal("Tenant A rule", rule.Name);
    }

    [Fact]
    public async Task Toggling_a_rule_disabled_then_enabled_again_bumps_the_version_each_time()
    {
        var service = Build();
        var rule = await service.CreateAsync("ten_a", TestActor, "Board alerts", new HashSet<TransportNotificationEventType> { TransportNotificationEventType.Board }, CancellationToken.None);

        var disabled = await service.SetEnabledAsync("ten_a", TestActor, rule.RuleId, enabled: false, expectedVersion: rule.Version, CancellationToken.None);
        Assert.False(disabled.Enabled);
        Assert.Equal(2, disabled.Version);

        var reenabled = await service.SetEnabledAsync("ten_a", TestActor, rule.RuleId, enabled: true, expectedVersion: disabled.Version, CancellationToken.None);
        Assert.True(reenabled.Enabled);
        Assert.Equal(3, reenabled.Version);
    }

    [Fact]
    public async Task Toggling_a_rule_with_a_stale_expected_version_is_rejected_as_a_concurrency_conflict()
    {
        var service = Build();
        var rule = await service.CreateAsync("ten_a", TestActor, "Board alerts", new HashSet<TransportNotificationEventType> { TransportNotificationEventType.Board }, CancellationToken.None);
        await service.SetEnabledAsync("ten_a", TestActor, rule.RuleId, enabled: false, expectedVersion: rule.Version, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<RuleConcurrencyConflictException>(() =>
            service.SetEnabledAsync("ten_a", TestActor, rule.RuleId, enabled: true, expectedVersion: rule.Version, CancellationToken.None));
        Assert.Equal(2, ex.CurrentVersion);
    }

    [Fact]
    public async Task Toggling_an_unknown_rule_is_rejected_as_not_found()
    {
        var service = Build();

        await Assert.ThrowsAsync<RuleNotFoundException>(() =>
            service.SetEnabledAsync("ten_a", TestActor, "trn-rule_nonexistent", enabled: false, expectedVersion: 1, CancellationToken.None));
    }

    /// <summary>
    /// SY-TRN003-01 (Seyi, High): two callers racing from the SAME stale read must never both
    /// commit. Barrier-gates <see cref="ITransportNotificationRuleRepository.TryReplaceAsync"/>
    /// itself so both calls' own prior `GetAsync` read has already happened (both hold the
    /// identical version-1 record) before either is released into its own compare-and-swap - the
    /// same barrier-after-the-read, gate-the-write shape this session's own
    /// <c>EnsureTerminalRecordAsync_two_genuinely_concurrent_repairs...</c> test (BIO-INT-004)
    /// already established. Both calls wrapped in <c>Task.Run</c>: <c>Barrier.SignalAndWait</c>
    /// is a synchronous blocking call, so calling both inline would deadlock the test's own
    /// single thread before the second statement is ever reached.
    /// </summary>
    [Fact]
    public async Task Two_genuinely_concurrent_toggles_from_the_same_stale_read_commit_exactly_once()
    {
        var innerRepository = new InMemoryTransportNotificationRuleRepository();
        var service = Build(repository: innerRepository);
        var rule = await service.CreateAsync("ten_a", TestActor, "Board alerts", new HashSet<TransportNotificationEventType> { TransportNotificationEventType.Board }, CancellationToken.None);

        var barrier = new System.Threading.Barrier(2);
        var gatedRepository = new BarrierGatedRuleRepository(innerRepository, barrier);
        var racingService = Build(repository: gatedRepository);

        var first = Task.Run(() => racingService.SetEnabledAsync("ten_a", TestActor, rule.RuleId, enabled: false, expectedVersion: rule.Version, CancellationToken.None));
        var second = Task.Run(() => racingService.SetEnabledAsync("ten_a", TestActor, rule.RuleId, enabled: true, expectedVersion: rule.Version, CancellationToken.None));

        var results = await Task.WhenAll(first.ContinueWith(TaskOutcome), second.ContinueWith(TaskOutcome));

        Assert.Single(results, r => r is "ok");
        Assert.Single(results, r => r is "conflict");

        var stored = await innerRepository.GetAsync("ten_a", rule.RuleId, CancellationToken.None);
        Assert.Equal(2, stored!.Version);
    }

    private static string TaskOutcome(Task<TransportNotificationRule> task)
    {
        if (task.IsFaulted && task.Exception!.GetBaseException() is RuleConcurrencyConflictException)
        {
            return "conflict";
        }

        return task.IsCompletedSuccessfully ? "ok" : throw task.Exception!.GetBaseException();
    }

    /// <summary>Lets both concurrent callers' own prior `GetAsync` read complete before either is released into its own compare-and-swap - never gates `GetAsync` itself, only the write.</summary>
    private sealed class BarrierGatedRuleRepository(ITransportNotificationRuleRepository inner, System.Threading.Barrier barrier) : ITransportNotificationRuleRepository
    {
        public Task AddAsync(TransportNotificationRule rule, CancellationToken cancellationToken = default) => inner.AddAsync(rule, cancellationToken);

        public Task<TransportNotificationRule?> GetAsync(string tenantId, string ruleId, CancellationToken cancellationToken = default) =>
            inner.GetAsync(tenantId, ruleId, cancellationToken);

        public Task<IReadOnlyList<TransportNotificationRule>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken = default) =>
            inner.ListForTenantAsync(tenantId, cancellationToken);

        public Task<bool> TryReplaceAsync(TransportNotificationRule expectedCurrent, TransportNotificationRule updated, CancellationToken cancellationToken = default)
        {
            barrier.SignalAndWait(cancellationToken);
            return inner.TryReplaceAsync(expectedCurrent, updated, cancellationToken);
        }
    }
}
