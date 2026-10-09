namespace Biolog.Features.TransportNotifications;

public sealed class RuleNotFoundException(string ruleId) : Exception($"no transport notification rule '{ruleId}' exists for this tenant");

public sealed class RuleConcurrencyConflictException(int currentVersion)
    : Exception($"transport notification rule version conflict - current version is {currentVersion}")
{
    public int CurrentVersion { get; } = currentVersion;
}

/// <summary>
/// AC01 - a tenant-scoped rule selecting which <see cref="TransportNotificationEventType"/>
/// categories should be delivered. CAS-versioned, same shape every sibling mutable config in this
/// codebase uses (<c>AttendancePolicy</c>, BIO-INT-004's own <c>WebhookSubscription</c>).
/// </summary>
public sealed record TransportNotificationRule
{
    public required string RuleId { get; init; }
    public required string TenantId { get; init; }
    public required string Name { get; init; }
    public required IReadOnlySet<TransportNotificationEventType> EventTypes { get; init; }
    public required bool Enabled { get; init; }
    public required int Version { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The only sanctioned way to construct one - AC01 requires at least one selected event type; a rule selecting none would be a silently-dead configuration, never created.</summary>
    public static TransportNotificationRule Create(string tenantId, string name, IReadOnlySet<TransportNotificationEventType> eventTypes)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
        {
            throw new ArgumentException("name must be non-empty and at most 200 characters", nameof(name));
        }

        if (eventTypes.Count == 0)
        {
            throw new ArgumentException("at least one event type is required", nameof(eventTypes));
        }

        return new TransportNotificationRule
        {
            RuleId = $"trn-rule_{Guid.NewGuid():N}",
            TenantId = tenantId,
            Name = name,
            EventTypes = eventTypes,
            Enabled = true,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public TransportNotificationRule WithEnabled(bool enabled, int version) => this with { Enabled = enabled, Version = version };
}

/// <summary>Tenant-scoped storage for <see cref="TransportNotificationRule"/>. <c>internal</c> (BIO-IDN-001-QA-01 discipline) - restricted to this assembly plus <c>Biolog.Api</c> and this project's own test assembly via <c>InternalsVisibleTo</c>.</summary>
internal interface ITransportNotificationRuleRepository
{
    Task AddAsync(TransportNotificationRule rule, CancellationToken cancellationToken = default);

    Task<TransportNotificationRule?> GetAsync(string tenantId, string ruleId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TransportNotificationRule>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// SY-TRN003-01 (Seyi, High): atomic compare-and-swap, never a blind assignment. Replaces
    /// the stored rule with <paramref name="updated"/> ONLY if the currently-stored rule is still
    /// exactly <paramref name="expectedCurrent"/> - the object <see cref="GetAsync"/> itself
    /// returned to the caller, never a freshly-reconstructed-but-equal value. Two callers racing
    /// from the SAME stale read (both pass their own version check, both compute their own
    /// "version + 1") can therefore never both win: whichever one's write lands first changes
    /// what is stored, so the second one's compare-and-swap genuinely fails (its own
    /// <see cref="TransportNotificationRule.Version"/> differs from what is now stored) instead
    /// of silently clobbering the first - the identical "no window between check and act" guarantee
    /// this session's <c>IIdempotencyStore.Claim</c>/<c>IWebhookDeliveryAuditSink.TryRecordOnceAsync</c>
    /// already establish, applied here to a record REPLACE rather than a create.
    /// </summary>
    Task<bool> TryReplaceAsync(TransportNotificationRule expectedCurrent, TransportNotificationRule updated, CancellationToken cancellationToken = default);
}

internal sealed class InMemoryTransportNotificationRuleRepository : ITransportNotificationRuleRepository
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, TransportNotificationRule> _rules = new();

    public Task AddAsync(TransportNotificationRule rule, CancellationToken cancellationToken = default)
    {
        _rules[Key(rule.TenantId, rule.RuleId)] = rule;
        return Task.CompletedTask;
    }

    public Task<TransportNotificationRule?> GetAsync(string tenantId, string ruleId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_rules.TryGetValue(Key(tenantId, ruleId), out var rule) ? rule : null);

    public Task<IReadOnlyList<TransportNotificationRule>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TransportNotificationRule>>([.. _rules.Values.Where(r => r.TenantId == tenantId).OrderBy(r => r.CreatedAt)]);

    /// <summary>
    /// <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}.TryUpdate(TKey,TValue,TValue)"/>
    /// is itself the atomic compare-and-swap - it only replaces the stored value when it still
    /// equals <paramref name="expectedCurrent"/> at that exact instant, and reports whether THIS
    /// caller's swap was the one that won. Safe to rely on record equality rather than a raw
    /// `ReferenceEquals` wrapper here specifically because <see cref="GetAsync"/> always returns
    /// the dictionary's own live stored reference (never a copy), so the no-conflict case
    /// short-circuits on reference equality inside the record's own generated `Equals`, and the
    /// conflict case always differs on <see cref="TransportNotificationRule.Version"/> alone (it
    /// is bumped on every genuine write) regardless of any other field's own equality semantics.
    /// </summary>
    public Task<bool> TryReplaceAsync(TransportNotificationRule expectedCurrent, TransportNotificationRule updated, CancellationToken cancellationToken = default)
    {
        var key = Key(expectedCurrent.TenantId, expectedCurrent.RuleId);
        return Task.FromResult(_rules.TryUpdate(key, updated, expectedCurrent));
    }

    private static string Key(string tenantId, string ruleId) => $"{tenantId}::{ruleId}";
}
