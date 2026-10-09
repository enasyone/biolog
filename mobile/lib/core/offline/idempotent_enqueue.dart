// BIO-MOB-001-F01 (Seyi, round 1): the offline queue must treat
// `idempotency_key` as a real idempotency guarantee, not a plain unique
// column to overwrite on conflict. A retry that resubmits the exact same
// logical mutation must return the original queued envelope untouched; a
// second, different mutation reusing the same key is a conflict and must be
// rejected, never silently replace the original. This is the single place
// that decision is made, so both the real store and the fake test double
// enforce it identically.
library;

import 'offline_mutation.dart';

/// Thrown when a mutation reuses an already-queued `idempotency_key` with
/// different logical content. The caller decides how to surface this
/// (e.g. ask the operator, or treat as a bug) — the queue itself never
/// discards [existing] to make room for [incoming].
class IdempotencyConflictError extends Error {
  IdempotencyConflictError({required this.existing, required this.incoming});

  final OfflineMutation existing;
  final OfflineMutation incoming;

  @override
  String toString() =>
      'IdempotencyConflictError: idempotency_key "${incoming.idempotencyKey}" '
      'is already queued with different content '
      '(existing mutation_id=${existing.mutationId}, incoming mutation_id=${incoming.mutationId})';
}

/// Decides what a store should persist for [incoming] given whatever is
/// already queued under the same `idempotency_key` ([existing], or null if
/// none). Never mutates either argument.
///
/// - No existing entry: [incoming] is new, return it as-is to be inserted.
/// - An existing entry with the same logical content (same tenant, device,
///   aggregate, base version and payload): this is a replay — return
///   [existing] unchanged so the caller performs no write at all.
/// - An existing entry with different content: throws
///   [IdempotencyConflictError]. The original is never replaced.
OfflineMutation resolveEnqueue({
  required OfflineMutation? existing,
  required OfflineMutation incoming,
}) {
  if (existing == null) return incoming;
  if (_sameLogicalContent(existing, incoming)) return existing;
  throw IdempotencyConflictError(existing: existing, incoming: incoming);
}

bool _sameLogicalContent(OfflineMutation a, OfflineMutation b) {
  return a.tenantId == b.tenantId &&
      a.deviceId == b.deviceId &&
      a.aggregateId == b.aggregateId &&
      a.baseVersion == b.baseVersion &&
      jsonDeepEquals(a.payloadJson, b.payloadJson);
}

/// Structural equality for decoded JSON values (`Map`/`List`/primitives).
/// Local helper so this file has no extra package dependency for a single
/// comparison used only here.
bool jsonDeepEquals(Object? a, Object? b) {
  if (identical(a, b)) return true;
  if (a is Map && b is Map) {
    if (a.length != b.length) return false;
    for (final key in a.keys) {
      if (!b.containsKey(key) || !jsonDeepEquals(a[key], b[key])) return false;
    }
    return true;
  }
  if (a is List && b is List) {
    if (a.length != b.length) return false;
    for (var i = 0; i < a.length; i += 1) {
      if (!jsonDeepEquals(a[i], b[i])) return false;
    }
    return true;
  }
  return a == b;
}
