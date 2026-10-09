using System.Collections.Concurrent;
using System.Text.Json;

namespace Biolog.Features.TransportNotifications;

public enum LedgerEntryState
{
    InFlight,
    Completed,
}

public sealed record LedgerEntry(string RequestFingerprint, LedgerEntryState State);

/// <summary>
/// The result of a <see cref="ITransportNotificationDispatchLedger.Claim"/> call.
/// <see cref="IsNewClaim"/> is true ONLY for the single caller whose attempt actually won the race
/// to create this key's entry - the caller MUST branch on this, not on <see cref="LedgerEntry.State"/>
/// alone, exactly the check-then-act race BIO-INT-004's own <c>IdempotencyClaim</c> already closed
/// for webhook delivery - this project cannot reference that sibling feature project, so this is
/// its own small, project-local copy of the same primitive, scoped to
/// <c>(tenantId, ruleId, sourceEventId)</c>: one rule only ever delivers once per underlying
/// source event, even if that event independently matches more than one of the rule's selected
/// event types (e.g. a Board event with unverified evidence also qualifies as Exception) - the
/// first classification to be processed wins the claim, and a rule selecting both never delivers
/// the same physical happening twice.
/// </summary>
public readonly record struct LedgerClaim(LedgerEntry Entry, bool IsNewClaim);

internal interface ITransportNotificationDispatchLedger
{
    LedgerClaim Claim(string tenantId, string ruleId, string sourceEventId);

    void Complete(string tenantId, string ruleId, string sourceEventId);

    /// <summary>Releases an in-flight claim without completing it - used when an unexpected exception aborts processing, so a later dispatch pass for the same key is never permanently blocked by an abandoned claim.</summary>
    void Release(string tenantId, string ruleId, string sourceEventId);
}

internal sealed class InMemoryTransportNotificationDispatchLedger : ITransportNotificationDispatchLedger
{
    private readonly ConcurrentDictionary<string, LedgerEntry> _entries = new(StringComparer.Ordinal);

    private static string ScopeKey(string tenantId, string ruleId, string sourceEventId) =>
        JsonSerializer.Serialize(new[] { tenantId, ruleId, sourceEventId });

    public LedgerClaim Claim(string tenantId, string ruleId, string sourceEventId)
    {
        var key = ScopeKey(tenantId, ruleId, sourceEventId);

        // Ownership via reference-identity on the factory-created object, NOT by inspecting the
        // stored entry's content - the same discipline BIO-INT-004's own IIdempotencyStore uses.
        LedgerEntry? created = null;
        var stored = _entries.GetOrAdd(key, _ =>
        {
            created = new LedgerEntry(sourceEventId, LedgerEntryState.InFlight);
            return created;
        });
        return new LedgerClaim(stored, IsNewClaim: ReferenceEquals(stored, created));
    }

    public void Complete(string tenantId, string ruleId, string sourceEventId)
    {
        var key = ScopeKey(tenantId, ruleId, sourceEventId);
        _entries.AddOrUpdate(
            key,
            _ => throw new InvalidOperationException("Complete called without a prior Claim"),
            (_, existing) => existing with { State = LedgerEntryState.Completed });
    }

    public void Release(string tenantId, string ruleId, string sourceEventId)
    {
        var key = ScopeKey(tenantId, ruleId, sourceEventId);
        if (_entries.TryGetValue(key, out var existing) && existing.State == LedgerEntryState.InFlight)
        {
            _entries.TryRemove(new KeyValuePair<string, LedgerEntry>(key, existing));
        }
    }
}
