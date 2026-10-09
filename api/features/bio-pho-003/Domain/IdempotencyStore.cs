using System.Collections.Concurrent;
using System.Text.Json;

namespace Biolog.Features.PhotoQuality.Domain;

/// <summary>
/// ZB-PHO003-04 (Kemi's ruling, 2026-10-07): the same fix category as BIO-PHO-002's own
/// TD-PHO-002-IDEMPOTENCY-RACE remediation. Three rounds of trying to RECONSTRUCT "did this
/// approval attempt already promote" from BIO-PHO-001's own post-hoc state (content equality,
/// then an exact attempt-id audit lookup, then a repository-existence confirmation) kept finding
/// a new way for that reconstruction to misattribute someone else's side effect - because
/// <c>PhotoMaster</c> genuinely has no caller-supplied identity field, and inferring ownership
/// from an external system's own observable state can never be fully disambiguated from the
/// outside. The real fix is for THIS feature to REMEMBER its own attempt locally instead -
/// exactly what BIO-PHO-002's own idempotency store already does for an unrelated operation.
/// </summary>
public enum IdempotencyRecordState
{
    InFlight,
    Completed,
}

public sealed record IdempotencyRecord(string RequestFingerprint, IdempotencyRecordState State, object? Result);

/// <summary>
/// <see cref="IsNewClaim"/> is true ONLY for the single caller whose attempt actually won the
/// race to create this key's record - every other concurrent caller (whether their own attempt
/// ran and lost, or never ran because the key already existed) gets <c>false</c>, even though
/// <see cref="Record"/> itself is identical content either way for a genuine replay. The caller
/// MUST branch on this, not merely on <see cref="IdempotencyRecord.State"/>, or two concurrent
/// identical requests can both believe they own the claim and both execute.
/// </summary>
public readonly record struct IdempotencyClaim(IdempotencyRecord Record, bool IsNewClaim);

/// <summary>Fresh, project-local copy of the same shape <c>IIdempotencyStore</c> (BIO-CRD-001/BIO-PHO-002) established - each feature project owns its own small primitives rather than a cross-feature reference, the established convention in this repository.</summary>
public interface IIdempotencyStore
{
    /// <summary>Atomically claims the key for a new in-flight operation, or returns the existing record if one is already present (InFlight or Completed). <see cref="IdempotencyClaim.IsNewClaim"/> is how the caller tells "I just created this claim" apart from "someone else's claim is already in flight" - the record's own content is identical either way for a genuine replay, so content alone cannot distinguish them.</summary>
    IdempotencyClaim Claim(string tenantId, string operation, string idempotencyKey, string requestFingerprint);

    /// <summary>Marks an in-flight claim as completed with its result, so a later replay returns the original outcome rather than re-executing.</summary>
    void Complete(string tenantId, string operation, string idempotencyKey, object? result);

    /// <summary>Releases an in-flight claim that failed before producing a result, so a genuine retry (not a replay) can proceed.</summary>
    void Release(string tenantId, string operation, string idempotencyKey);

    IdempotencyRecord? Get(string tenantId, string operation, string idempotencyKey);
}

/// <summary>
/// In-memory, process-lifetime idempotency store. Scope key is a JSON array of the three scoping
/// values - injective, unlike a delimiter-joined string, which a value containing the delimiter
/// could otherwise collide against.
///
/// <b>Named residual (Kemi's own ruling, not a new gap - "crash residual inert with volatile
/// singletons" boundary Seyi already confirmed genuinely irreducible elsewhere this session):</b>
/// a crash strictly between <c>PromoteAsync</c> succeeding and <see cref="PhotoQualityService"/>'s
/// own <see cref="Complete"/> call leaves that claim stuck <see cref="IdempotencyRecordState.InFlight"/>
/// forever in this volatile, process-lifetime store. A retry's own bounded poll-wait then times
/// out and throws, rather than silently reusing wrong data - fails LOUD, not silently wrong.
/// Disclosed, not fixed further; the same boundary every in-memory placeholder in this codebase
/// carries pending ADR-0005's real persistence.
/// </summary>
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
        // only for the thread whose own factory-created object actually won, false for every
        // other caller.
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

    public void Release(string tenantId, string operation, string idempotencyKey)
    {
        _records.TryRemove(ScopeKey(tenantId, operation, idempotencyKey), out _);
    }

    public IdempotencyRecord? Get(string tenantId, string operation, string idempotencyKey)
    {
        return _records.TryGetValue(ScopeKey(tenantId, operation, idempotencyKey), out var record) ? record : null;
    }
}
