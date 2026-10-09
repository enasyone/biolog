// BIO-MOB-003-AC02: server acknowledgements are idempotent.
import 'package:biolog_mobile/core/identifiers/canonical_id.dart';
import 'package:biolog_mobile/core/offline/offline_mutation.dart';
import 'package:biolog_mobile/core/sync/sync_ack.dart';
import 'package:biolog_mobile/core/sync/sync_engine.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/fake_app_database.dart';
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
  test('a fresh accepted acknowledgement moves a mutation to acknowledged', () async {
    final database = FakeAppDatabase();
    await database.open();
    await database.enqueueMutation(_synthetic());
    final transport = FakeSyncTransport();
    final engine = SyncEngine(database: database, transport: transport);

    final result = await engine.runSyncPass();

    expect(result.acknowledged, 1);
    expect(await database.countMutationsByState(OfflineSyncState.acknowledged), 1);
    expect(await database.countMutationsByState(OfflineSyncState.pending), 0);
  });

  test(
      'a replayed acknowledgement (ADR-0001 §3.4: same idempotency key, original stored result) '
      'is handled exactly like a fresh one, not as an error or a second effect', () async {
    final database = FakeAppDatabase();
    await database.open();
    final mutation = await database.enqueueMutation(_synthetic());
    final transport = FakeSyncTransport()
      ..script(mutation.mutationId, () => const SyncAck.accepted(replayed: true));
    final engine = SyncEngine(database: database, transport: transport);

    final result = await engine.runSyncPass();

    expect(result.acknowledged, 1);
    expect(result.failed, 0);
    expect(await database.countMutationsByState(OfflineSyncState.acknowledged), 1);
    expect(transport.uploadCallCounts[mutation.mutationId], 1);
  });

  test('markAcknowledged is a no-op for a mutation that is not currently uploading', () async {
    final database = FakeAppDatabase();
    await database.open();
    final mutation = await database.enqueueMutation(_synthetic());
    // Never called markUploading — the mutation is still `pending`.

    await database.markAcknowledged(mutation.mutationId);

    final pending = await database.pendingMutations();
    expect(pending, hasLength(1),
        reason: 'markAcknowledged must not apply to a mutation that was never uploading');
  });

  test('a mutation already acknowledged is not re-uploaded on a later sync pass', () async {
    final database = FakeAppDatabase();
    await database.open();
    final mutation = await database.enqueueMutation(_synthetic());
    final transport = FakeSyncTransport();
    final engine = SyncEngine(database: database, transport: transport);

    await engine.runSyncPass();
    final second = await engine.runSyncPass();

    expect(second.acknowledged, 0);
    expect(transport.uploadCallCounts[mutation.mutationId], 1,
        reason: 'acknowledged is a terminal-for-sync state, excluded from dueForRetry');
  });
}
