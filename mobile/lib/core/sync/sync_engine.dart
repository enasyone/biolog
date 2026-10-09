// BIO-MOB-003: drives queued offline mutations through the upload/ack cycle
// (AC01-05). Built entirely on `AppDatabase`'s already-guarded state
// transitions (BIO-MOB-002-QA-01/QA-02) and a `SyncTransport` — this class
// adds no new persistence of its own, only orchestration.
library;

import 'dart:async';

import '../connectivity/connectivity_signal.dart';
import '../db/app_database.dart';
import '../offline/offline_mutation.dart';
import 'sync_ack.dart';
import 'sync_transport.dart';

/// Tally of one [SyncEngine.runSyncPass] call, for callers/tests that want
/// to observe what happened without re-querying [AppDatabase] themselves.
class SyncPassResult {
  const SyncPassResult({
    required this.acknowledged,
    required this.conflicted,
    required this.failed,
    required this.skipped,
  });

  /// A pass that found [SyncEngine] already mid-pass (BIO-MOB-003-AC05): a
  /// deliberate no-op, every count zero, distinguishable from "ran and had
  /// nothing due" only by the caller already knowing it called concurrently.
  static const SyncPassResult inFlight =
      SyncPassResult(acknowledged: 0, conflicted: 0, failed: 0, skipped: 0);

  final int acknowledged;
  final int conflicted;
  final int failed;

  /// Mutations this pass saw as due but did not upload because
  /// [AppDatabase.markUploading] reported another caller already claimed
  /// them first (BIO-MOB-003-AC05 defence-in-depth against the in-process
  /// single-flight guard below).
  final int skipped;
}

/// Drives the local offline-mutation queue's upload/acknowledgement cycle.
///
/// - **AC01** (delta sync: additions/updates/revocations): uploads whatever
///   [AppDatabase.dueForRetry] returns, using each mutation's own
///   [OfflineMutation.operation]; no operation kind is special-cased except
///   for priority (below).
/// - **AC02** (server acknowledgements are idempotent): a [SyncAck] with
///   `replayed: true` is handled identically to `replayed: false` — both
///   move the mutation to `acknowledged` exactly once.
/// - **AC03/AC04** (critical conflicts enter the Sync Resolution Queue, no
///   last-write-wins): a conflict ack always calls
///   [AppDatabase.recordConflict], unconditionally — there is no merge
///   branch anywhere in this class.
/// - **AC05** (reconnect storms do not create duplicates): a single-flight
///   guard makes an overlapping [runSyncPass] call a no-op, and
///   [AppDatabase.markUploading]'s guarded return value is checked before
///   ever calling the transport, so even two [SyncEngine] instances racing
///   the same database can upload a given mutation at most once between
///   them.
class SyncEngine {
  SyncEngine({required AppDatabase database, required SyncTransport transport})
      : _database = database,
        _transport = transport;

  final AppDatabase _database;
  final SyncTransport _transport;
  bool _inFlight = false;
  StreamSubscription<ConnectivityState>? _connectivitySubscription;

  /// Runs one pass over currently-due mutations. Safe to call from multiple
  /// call sites (e.g. a manual "sync now" action alongside a connectivity
  /// listener) — a call that arrives while another is still running returns
  /// [SyncPassResult.inFlight] immediately rather than starting a second
  /// concurrent pass (BIO-MOB-003-AC05).
  Future<SyncPassResult> runSyncPass({DateTime? now}) async {
    if (_inFlight) return SyncPassResult.inFlight;
    _inFlight = true;
    try {
      final effectiveNow = now ?? DateTime.now();
      final due = _prioritized(await _database.dueForRetry(effectiveNow));
      var acknowledged = 0;
      var conflicted = 0;
      var failed = 0;
      var skipped = 0;
      for (final mutation in due) {
        final claimed = await _database.markUploading(mutation.mutationId);
        if (!claimed) {
          skipped++;
          continue;
        }
        try {
          final ack = await _transport.upload(mutation);
          if (ack.outcome == SyncAckOutcome.conflict) {
            // BIO-MOB-003-AC03/AC04: unconditional — every conflict enters
            // the Sync Resolution Queue (`AppDatabase.conflictedMutations`),
            // never an automatic merge, regardless of aggregate or
            // operation kind (ADR-0001 §3.5: absent an owning story's
            // written merge rule, the default is CONFLICT; no story has
            // written one for any aggregate this queue handles).
            await _database.recordConflict(mutation.mutationId);
            conflicted++;
          } else {
            // BIO-MOB-003-AC02: the ack's replayed flag is not branched on
            // here — a replayed acknowledgement moves the mutation forward
            // exactly the same way a first-time one does.
            await _database.markAcknowledged(mutation.mutationId);
            acknowledged++;
          }
        } catch (_) {
          // Transport failure (network error, timeout, 5xx): bounded retry,
          // same accounting as BIO-MOB-002-AC03.
          await _database.recordUploadFailure(mutation.mutationId, now: effectiveNow);
          failed++;
        }
      }
      return SyncPassResult(
        acknowledged: acknowledged,
        conflicted: conflicted,
        failed: failed,
        skipped: skipped,
      );
    } finally {
      _inFlight = false;
    }
  }

  /// Revocation deltas take a priority path ahead of every other pending
  /// mutation (ADR-0001 §3.5: "a revocation arriving late is a security
  /// failure while a telemetry batch arriving late is not") — a stable
  /// partition, not a full re-sort, so relative order within each group
  /// (oldest-composed-first, as returned by [AppDatabase.dueForRetry])
  /// is preserved.
  List<OfflineMutation> _prioritized(List<OfflineMutation> due) {
    final revocations = <OfflineMutation>[];
    final rest = <OfflineMutation>[];
    for (final mutation in due) {
      (mutation.operation == OfflineMutationOperation.revocation ? revocations : rest)
          .add(mutation);
    }
    return [...revocations, ...rest];
  }

  /// Starts a pass automatically whenever [signal] transitions to
  /// [ConnectivityState.online] — "reconnect" in BIO-MOB-003-AC05's sense.
  /// Safe against a flapping/rapid signal by construction: each transition
  /// just calls [runSyncPass], and the single-flight guard above absorbs
  /// any overlap into a no-op rather than a duplicate pass. Call [dispose]
  /// to stop listening.
  void attachTo(ConnectivitySignal signal) {
    _connectivitySubscription?.cancel();
    _connectivitySubscription = signal.onChange.listen((state) {
      if (state == ConnectivityState.online) {
        unawaited(runSyncPass());
      }
    });
  }

  void dispose() {
    _connectivitySubscription?.cancel();
    _connectivitySubscription = null;
  }
}
