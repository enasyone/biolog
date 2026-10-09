// BIO-MOB-003-AC05: reconnect storms do not create duplicates.
import 'package:biolog_mobile/core/connectivity/connectivity_signal.dart';
import 'package:biolog_mobile/core/identifiers/canonical_id.dart';
import 'package:biolog_mobile/core/offline/offline_mutation.dart';
import 'package:biolog_mobile/core/sync/sync_engine.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/fake_app_database.dart';
import 'support/fake_connectivity_signal.dart';
import 'support/fake_sync_transport.dart';

OfflineMutation _synthetic() {
  return OfflineMutation.compose(
    tenantId: newCanonicalId(CanonicalIdType.tenant),
    deviceId: newCanonicalId(CanonicalIdType.device),
    aggregateId: newCanonicalId(CanonicalIdType.tenant),
    baseVersion: 1,
    idempotencyKey: 'idem-${DateTime.now().microsecondsSinceEpoch}',
    payloadJson: const {'field': 'SYNTHETIC'},
  );
}

void main() {
  test(
      'two overlapping sync passes over the same due mutations upload each mutation at most once '
      '(direct reconnect-storm repro)', () async {
    final database = FakeAppDatabase();
    await database.open();
    final one = await database.enqueueMutation(_synthetic());
    final two = await database.enqueueMutation(_synthetic());
    final transport = FakeSyncTransport();
    final engine = SyncEngine(database: database, transport: transport);

    // Two "reconnect" callers racing the same due mutations — e.g. a manual
    // "sync now" action firing at the same moment a connectivity listener
    // does. Deliberately not awaited individually: Dart's async functions
    // run synchronously up to their first `await`, so the second call's
    // single-flight check is guaranteed to observe the first call's guard
    // already set, not a timing coincidence.
    final results = await Future.wait([engine.runSyncPass(), engine.runSyncPass()]);

    final totalAcknowledged = results.fold<int>(0, (sum, r) => sum + r.acknowledged);
    expect(totalAcknowledged, 2, reason: 'both mutations are eventually acknowledged...');
    expect(transport.uploadCallCounts[one.mutationId], 1,
        reason: '...but each is uploaded exactly once, not once per overlapping pass');
    expect(transport.uploadCallCounts[two.mutationId], 1);
    expect(results.where((r) => identical(r, SyncPassResult.inFlight)), hasLength(1),
        reason: 'the overlapping call is a deliberate no-op, not a second concurrent pass');
  });

  test('a mutation already uploading (claimed by another pass) is never double-uploaded',
      () async {
    final database = FakeAppDatabase();
    await database.open();
    final mutation = await database.enqueueMutation(_synthetic());
    // Simulate: some other in-flight process already claimed this exact
    // mutation for upload (e.g. a previous pass that has not yet recorded
    // its outcome) — markUploading's own guard is the last line of defence
    // below the engine's own single-flight guard.
    final claimed = await database.markUploading(mutation.mutationId);
    expect(claimed, isTrue);

    final transport = FakeSyncTransport();
    final engine = SyncEngine(database: database, transport: transport);
    final result = await engine.runSyncPass();

    expect(result.skipped, 0,
        reason: 'a mutation already uploading is excluded by dueForRetry (pending-only) before '
            'the engine ever gets a chance to attempt — and therefore to claim — it');
    expect(transport.uploadCallCounts[mutation.mutationId], isNull,
        reason: 'a pass must never upload a mutation it does not itself hold the uploading claim for');
  });

  test('attachTo triggers a sync pass when connectivity transitions to online', () async {
    final database = FakeAppDatabase();
    await database.open();
    await database.enqueueMutation(_synthetic());
    final transport = FakeSyncTransport();
    final engine = SyncEngine(database: database, transport: transport);
    final signal = FakeConnectivitySignal(initial: ConnectivityState.offline);

    engine.attachTo(signal);
    signal.set(ConnectivityState.online);
    // attachTo fires the pass unawaited (fire-and-forget) from the stream
    // listener; give its microtasks a turn to complete before asserting.
    await Future<void>.delayed(const Duration(milliseconds: 20));

    expect(await database.countMutationsByState(OfflineSyncState.acknowledged), 1);
    engine.dispose();
  });
}
