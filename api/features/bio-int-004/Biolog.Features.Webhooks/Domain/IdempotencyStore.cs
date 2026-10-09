using System.Collections.Concurrent;
using System.Text.Json;

namespace Biolog.Features.Webhooks;

public enum IdempotencyRecordState
{
    InFlight,
    Completed,
}

public sealed record IdempotencyRecord(string RequestFingerprint, IdempotencyRecordState State, object? Result);

/// <summary>
/// BIO-INT-004-ZB-02 (Zainab, High). The result of a <see cref="IIdempotencyStore.Claim"/> call.
/// <see cref="IsNewClaim"/> is true ONLY for the single caller whose attempt actually won the
/// race to create this key's record - every other concurrent caller (whether their own attempt
/// ran and lost, or never ran because the key already existed) gets <c>false</c>, even though
/// <see cref="Record"/> itself is identical content either way for a genuine replay. The caller
/// MUST branch on this, not merely on <see cref="IdempotencyRecord.State"/> - this is the exact
/// shape of the check-then-act race Tunde/Seyi/Zainab found in BIO-INT-002-F01 and that has since
/// been ported into BIO-MOB-003/005, BIO-ACC-003, BIO-CRD-005/006/007 and BIO-PHO-002: a plain
/// "does an audit record already say this succeeded" check (this story's own original
/// <c>HasSucceededAsync</c>) leaves a window between the check and the send where a second,
/// genuinely concurrent delivery attempt for the SAME event+subscription sees the same "not yet
/// succeeded" answer and sends again.
/// </summary>
public readonly record struct IdempotencyClaim(IdempotencyRecord Record, bool IsNewClaim);

/// <summary>Project-local copy of the same shape every sibling feature's own
/// <c>IIdempotencyStore</c> establishes (BIO-MOB-003's own header: "each feature project owns its
/// own small primitives rather than a cross-feature reference" - this project cannot reference
/// any sibling feature project either). Scoped to <c>(tenantId, operation, idempotencyKey)</c>;
/// <see cref="WebhookDispatchService"/> uses the subscription id as <c>operation</c> and the
/// event id as <c>idempotencyKey</c>, giving the natural per-(tenant, subscription, event)
/// delivery key.</summary>
public interface IIdempotencyStore
{
    /// <summary>Atomically claims the key for a new in-flight delivery, or returns the existing
    /// record if one is already present (InFlight or Completed). <see cref="IdempotencyClaim.IsNewClaim"/>
    /// is how the caller tells "I just created this claim" apart from "someone else's claim is
    /// already in flight" - the record's own content is identical either way for a genuine
    /// replay, so content alone cannot distinguish them.</summary>
    IdempotencyClaim Claim(string tenantId, string operation, string idempotencyKey, string requestFingerprint);

    void Complete(string tenantId, string operation, string idempotencyKey, object? result);

    /// <summary>Releases an in-flight claim without completing it - used when an unexpected
    /// exception aborts processing, so a legitimate later delivery attempt for the same key is
    /// never permanently blocked by an abandoned claim.</summary>
    void Release(string tenantId, string operation, string idempotencyKey);
}

internal sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, IdempotencyRecord> _records = new(StringComparer.Ordinal);

    private static string ScopeKey(string tenantId, string operation, string idempotencyKey) =>
        JsonSerializer.Serialize(new[] { tenantId, operation, idempotencyKey });

    public IdempotencyClaim Claim(string tenantId, string operation, string idempotencyKey, string requestFingerprint)
    {
        var key = ScopeKey(tenantId, operation, idempotencyKey);

        // Detect ownership via reference-identity on the factory-created object, NOT by
        // inspecting the stored record's content (identical either way for a genuine replay).
        // ConcurrentDictionary.GetOrAdd's factory overload may invoke the factory on multiple
        // racing threads under real contention, but only ONE thread's result is ever stored and
        // returned to every caller for this key - so ReferenceEquals(stored, created) is true
        // only for the thread whose own factory-created object actually won.
        IdempotencyRecord? created = null;
        var stored = _records.GetOrAdd(key, _ =>
        {
            created = new IdempotencyRecord(requestFingerprint, IdempotencyRecordState.InFlight, Result: null);
            return created;
        });
        return new IdempotencyClaim(stored, IsNewClaim: ReferenceEquals(stored, created));
    }

    public void Complete(string tenantId, string operation, string idempotencyKey, object? result)
    {
        var key = ScopeKey(tenantId, operation, idempotencyKey);
        _records.AddOrUpdate(
            key,
            _ => throw new InvalidOperationException("Complete called without a prior Claim"),
            (_, existing) => existing with { State = IdempotencyRecordState.Completed, Result = result });
    }

    /// <summary>BIO-INT-004-ZB-03 (Zainab, High): a defense-in-depth safety net, not the primary
    /// fix (that is <see cref="WebhookDispatchService.CompleteTerminalAsync"/> calling
    /// <see cref="Complete"/> before any audit write that could throw). Releasing an
    /// already-<see cref="IdempotencyRecordState.Completed"/> record is always a no-op,
    /// regardless of caller intent - a completed delivery decision must never be undone by a
    /// later Release call, since that is exactly what would let a future dispatch win a fresh
    /// claim and resend an event that has already been genuinely delivered. The conditional
    /// remove below is atomic against a concurrent <see cref="Complete"/> racing in between the
    /// read and the remove (the stored record is an immutable <c>record</c>, so
    /// <see cref="ConcurrentDictionary{TKey,TValue}.TryRemove(KeyValuePair{TKey,TValue})"/> only
    /// succeeds if nothing changed it in between).</summary>
    public void Release(string tenantId, string operation, string idempotencyKey)
    {
        var key = ScopeKey(tenantId, operation, idempotencyKey);
        if (_records.TryGetValue(key, out var existing) && existing.State == IdempotencyRecordState.InFlight)
        {
            _records.TryRemove(new KeyValuePair<string, IdempotencyRecord>(key, existing));
        }
    }
}
