// BIO-MOB-003-AC03: critical conflicts enter Sync Resolution Queue.
// BIO-MOB-003-AC04: last-write-wins is not used for security-sensitive data.
import 'package:biolog_mobile/core/identifiers/canonical_id.dart';
import 'package:biolog_mobile/core/offline/offline_mutation.dart';
import 'package:biolog_mobile/core/sync/sync_ack.dart';
import 'package:biolog_mobile/core/sync/sync_engine.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/fake_app_database.dart';
import 'support/fake_sync_transport.dart';

OfflineMutation _synthetic({
  OfflineMutationOperation operation = OfflineMutationOperation.addition,
  Map<String, Object?> payloadJson = const {'field': 'ORIGINAL'},
}) {
  return OfflineMutation.compose(
    tenantId: newCanonicalId(CanonicalIdType.tenant),
    deviceId: newCanonicalId(CanonicalIdType.device),
    aggregateId: newCanonicalId(CanonicalIdType.tenant),
    baseVersion: 1,
    idempotencyKey: 'idem-${DateTime.now().microsecondsSinceEpoch}',
    payloadJson: payloadJson,
    operation: operation,
  );
}

void main() {
  test('a conflict acknowledgement routes the mutation to the Sync Resolution Queue', () async {
    final database = FakeAppDatabase();
    await database.open();
    final mutation = await database.enqueueMutation(_synthetic());
    final transport = FakeSyncTransport()
      ..script(mutation.mutationId, () => const SyncAck.conflict(5));
    final engine = SyncEngine(database: database, transport: transport);

    final result = await engine.runSyncPass();

    expect(result.conflicted, 1);
    expect(result.acknowledged, 0);
    final conflicts = await database.conflictedMutations();
    expect(conflicts.map((m) => m.mutationId), contains(mutation.mutationId));
    expect(await database.countMutationsByState(OfflineSyncState.pending), 0);
    // AC03: removed from the automatic retry cycle entirely.
    final due = await database.dueForRetry(DateTime.now().add(const Duration(days: 365)));
    expect(due, isEmpty);
  });

  test('a conflict never mutates the queued payload — no merge, no last-write-wins', () async {
    final database = FakeAppDatabase();
    await database.open();
    final original = {'field': 'ORIGINAL', 'sensitive': 'value'};
    final mutation = await database.enqueueMutation(_synthetic(payloadJson: original));
    final transport = FakeSyncTransport()
      ..script(mutation.mutationId, () => const SyncAck.conflict(7));
    final engine = SyncEngine(database: database, transport: transport);

    await engine.runSyncPass();

    final conflicts = await database.conflictedMutations();
    expect(conflicts.single.payloadJson, original,
        reason: 'a conflicted mutation keeps its exact original payload — nothing in SyncEngine '
            'ever rewrites it toward a server-reported version');
  });

  test('conflict handling does not special-case operation kind — uniform for every mutation',
      () async {
    final database = FakeAppDatabase();
    await database.open();
    final revocation =
        await database.enqueueMutation(_synthetic(operation: OfflineMutationOperation.revocation));
    final update =
        await database.enqueueMutation(_synthetic(operation: OfflineMutationOperation.update));
    final transport = FakeSyncTransport()
      ..script(revocation.mutationId, () => const SyncAck.conflict(2))
      ..script(update.mutationId, () => const SyncAck.conflict(3));
    final engine = SyncEngine(database: database, transport: transport);

    final result = await engine.runSyncPass();

    expect(result.conflicted, 2,
        reason: 'ADR-0001 §3.5: absent an owning story\'s written merge rule (none exists for '
            'any aggregate here), the default is CONFLICT for every mutation, not just some');
  });
}
