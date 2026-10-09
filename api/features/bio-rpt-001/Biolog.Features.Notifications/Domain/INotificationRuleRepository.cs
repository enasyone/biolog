namespace Biolog.Features.Notifications;

/// <summary>
/// Tenant-scoped rule storage (BIO-RPT-001-AC01). Every method takes <paramref name="tenantId"/>
/// explicitly rather than resolving an ambient tenant context - this project has no such
/// concept of its own (see the .csproj's own comment); <c>BioRpt001Module</c> resolves the
/// real tenant and passes the string down, exactly like
/// <c>Features.DeviceCertification.DeviceCertificationService</c>'s own tenant-scope methods
/// already do for a value the caller supplies rather than one this layer resolves itself.
///
/// <see cref="GetAsync"/> returns <c>null</c> for a rule that does not exist AND for one that
/// exists in a different tenant (ADR-0002 §4.3's "not found, never forbidden", applied here
/// exactly like <c>IDeviceRepository.GetAsync</c> already documents for devices).
/// </summary>
public interface INotificationRuleRepository
{
    Task<NotificationRule> CreateAsync(string tenantId, NotificationRuleRegistrationRequest request, CancellationToken cancellationToken);

    Task<NotificationRule?> GetAsync(string tenantId, string ruleId, CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationRule>> ListAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>BIO-RPT-001-AC01's actual matching query: every enabled rule for this tenant
    /// whose <see cref="AggregateType"/> matches and whose declared schema-version range
    /// (<see cref="NotificationRule.SupportsSchemaVersion"/>) covers the event being
    /// dispatched. A disabled rule, a rule for a different aggregate type, or a rule that
    /// declares it does not understand this event's schema version are all excluded here,
    /// not filtered later - the dispatcher never sees a rule it should not act on.</summary>
    Task<IReadOnlyList<NotificationRule>> ListMatchingAsync(
        string tenantId, AggregateType aggregateType, int schemaVersion, CancellationToken cancellationToken);

    /// <summary>Throws <see cref="NotificationRuleConcurrencyConflictException"/> if
    /// <paramref name="request"/>'s <c>ExpectedVersion</c> does not match the stored version.
    /// Returns <c>null</c> for a rule the tenant cannot see, preserving the same
    /// not-found-not-forbidden rule as <see cref="GetAsync"/>.</summary>
    Task<NotificationRule?> UpdateStatusAsync(
        string tenantId, string ruleId, NotificationRuleStatusChangeRequest request, CancellationToken cancellationToken);
}

public sealed class InMemoryNotificationRuleRepository : INotificationRuleRepository
{
    private readonly Dictionary<string, NotificationRule> _rulesById = [];
    private readonly Lock _gate = new();

    public Task<NotificationRule> CreateAsync(
        string tenantId, NotificationRuleRegistrationRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var rule = new NotificationRule
        {
            RuleId = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            Name = request.Name,
            AggregateType = request.AggregateType,
            Channel = request.Channel,
            MinSchemaVersion = request.MinSchemaVersion,
            MaxSchemaVersion = request.MaxSchemaVersion,
            Enabled = true,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };

        lock (_gate)
        {
            _rulesById[rule.RuleId] = rule;
        }

        return Task.FromResult(rule);
    }

    public Task<NotificationRule?> GetAsync(string tenantId, string ruleId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_rulesById.TryGetValue(ruleId, out var rule) && rule.TenantId == tenantId ? rule : null);
        }
    }

    public Task<IReadOnlyList<NotificationRule>> ListAsync(string tenantId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<NotificationRule> result = _rulesById.Values.Where(r => r.TenantId == tenantId).ToList();
            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<NotificationRule>> ListMatchingAsync(
        string tenantId, AggregateType aggregateType, int schemaVersion, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<NotificationRule> result = _rulesById.Values
                .Where(r => r.TenantId == tenantId && r.Enabled && r.AggregateType == aggregateType && r.SupportsSchemaVersion(schemaVersion))
                .ToList();
            return Task.FromResult(result);
        }
    }

    public Task<NotificationRule?> UpdateStatusAsync(
        string tenantId, string ruleId, NotificationRuleStatusChangeRequest request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_rulesById.TryGetValue(ruleId, out var current) || current.TenantId != tenantId)
            {
                return Task.FromResult<NotificationRule?>(null);
            }

            if (current.Version != request.ExpectedVersion)
            {
                throw new NotificationRuleConcurrencyConflictException(current.Version);
            }

            var updated = current with { Enabled = request.Enabled, Version = current.Version + 1, UpdatedAt = DateTimeOffset.UtcNow };
            _rulesById[ruleId] = updated;
            return Task.FromResult<NotificationRule?>(updated);
        }
    }
}
