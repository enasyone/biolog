using System.Collections.Concurrent;

namespace Biolog.Features.CardLifecycle.Domain;

/// <summary>
/// BIO-CRD-006-AC02: "permitted recovery may reactivate ACCORDING TO POLICY." A real,
/// tenant-scoped, per-reason-code setting rather than a hardcoded "always allowed" - defaults
/// to permitted for every reason code until a tenant explicitly disables recovery for one
/// (e.g. an operator may decide a "stolen" report is never recoverable, only "lost"/"missing"
/// are). Own, right-sized store; no BIO-TEN story is a declared dependency, and this setting
/// is entirely this feature's own runtime concern (whether a card THIS story locked may be
/// unlocked again), not a tenant-registry contract.
/// </summary>
public interface ICardRecoveryPolicyStore
{
    bool IsRecoveryPermitted(string tenantId, string reasonCode);

    void SetPolicy(string tenantId, string reasonCode, bool recoveryPermitted);
}

internal sealed class InMemoryCardRecoveryPolicyStore : ICardRecoveryPolicyStore
{
    private readonly ConcurrentDictionary<string, bool> _overrides = new(StringComparer.Ordinal);

    public bool IsRecoveryPermitted(string tenantId, string reasonCode) =>
        !_overrides.TryGetValue(Key(tenantId, reasonCode), out var permitted) || permitted;

    public void SetPolicy(string tenantId, string reasonCode, bool recoveryPermitted) =>
        _overrides[Key(tenantId, reasonCode)] = recoveryPermitted;

    private static string Key(string tenantId, string reasonCode) => $"{tenantId}\u0000{reasonCode.Trim().ToLowerInvariant()}";
}
