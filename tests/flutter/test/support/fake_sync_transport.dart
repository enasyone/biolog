// Test double for SyncTransport. Lets a test script a specific outcome (or
// sequence of outcomes) per mutation id, and observes call counts/order —
// needed to prove BIO-MOB-003-AC02 (idempotent-replay handling), AC03/AC04
// (conflict routing) and AC05 (no duplicate uploads under a reconnect
// storm) without a real network dependency.
library;

import 'package:biolog_mobile/core/offline/offline_mutation.dart';
import 'package:biolog_mobile/core/sync/sync_ack.dart';
import 'package:biolog_mobile/core/sync/sync_transport.dart';

class FakeSyncTransport implements SyncTransport {
  /// Number of times [upload] was called per mutation id.
  final Map<String, int> uploadCallCounts = {};

  /// Mutation ids in the order [upload] was called for them (duplicates
  /// included) — used to assert revocation-priority ordering (AC01) and
  /// that no mutation is uploaded twice in one storm (AC05).
  final List<String> uploadOrder = [];

  final Map<String, List<SyncAck Function()>> _scripted = {};

  /// Fallback used when a mutation has no (remaining) scripted outcome.
  SyncAck Function(OfflineMutation mutation) defaultOutcome = (_) => const SyncAck.accepted();

  /// Queues [outcome] to be returned (or thrown, if the closure throws) the
  /// next time [mutationId] is uploaded. Scripted outcomes are consumed in
  /// order; once exhausted, [defaultOutcome] applies.
  void script(String mutationId, SyncAck Function() outcome) {
    _scripted.putIfAbsent(mutationId, () => []).add(outcome);
  }

  @override
  Future<SyncAck> upload(OfflineMutation mutation) async {
    uploadCallCounts.update(mutation.mutationId, (n) => n + 1, ifAbsent: () => 1);
    uploadOrder.add(mutation.mutationId);
    final queued = _scripted[mutation.mutationId];
    if (queued != null && queued.isNotEmpty) {
      return queued.removeAt(0)();
    }
    return defaultOutcome(mutation);
  }
}
