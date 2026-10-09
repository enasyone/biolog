// BIO-MOB-003-AC01: delta sync supports additions/updates/revocations.
import 'package:biolog_mobile/core/identifiers/canonical_id.dart';
import 'package:biolog_mobile/core/offline/offline_mutation.dart';
import 'package:biolog_mobile/core/sync/sync_engine.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/fake_app_database.dart';
import 'support/fake_sync_transport.dart';

OfflineMutation _synthetic({
  OfflineMutationOperation operation = OfflineMutationOperation.addition,
  String? idempotencyKey,
}) {
  return OfflineMutation.compose(
    tenantId: newCanonicalId(CanonicalIdType.tenant),
    deviceId: newCanonicalId(CanonicalIdType.device),
    aggregateId: newCanonicalId(CanonicalIdType.tenant),
    baseVersion: 1,
    idempotencyKey: idempotencyKey ?? 'idem-${DateTime.now().microsecondsSinceEpoch}',
    payloadJson: const {'field': 'SYNTHETIC'},
    operation: operation,
  );
}

void main() {
  test('compose defaults to an addition operation', () {
    final mutation = _synthetic();
    expect(mutation.operation, OfflineMutationOperation.addition);
  });

  test('compose accepts an explicit update or revocation operation', () {
    expect(_synthetic(operation: OfflineMutationOperation.update).operation,
        OfflineMutationOperation.update);
    expect(_synthetic(operation: OfflineMutationOperation.revocation).operation,
        OfflineMutationOperation.revocation);
  });

  test('toSyncDeltaJson carries operation and payload on top of the frozen envelope', () {
    final mutation = _synthetic(operation: OfflineMutationOperation.revocation);
    final delta = mutation.toSyncDeltaJson();

    expect(delta['operation'], 'REVOCATION');
    expect(delta['payload'], {'field': 'SYNTHETIC'});
    // Every frozen envelope field is still present.
    for (final key in mutation.toEnvelopeJson().keys) {
      expect(delta[key], mutation.toEnvelopeJson()[key]);
    }
  });

  test('toEnvelopeJson (the frozen wire shape) does not leak operation or payload', () {
    final mutation = _synthetic(operation: OfflineMutationOperation.revocation);
    final envelope = mutation.toEnvelopeJson();

    expect(envelope.containsKey('operation'), isFalse);
    expect(envelope.containsKey('payload'), isFalse);
  });

  test('operation round-trips through storage unchanged', () async {
    final database = FakeAppDatabase();
    await database.open();
    await database.enqueueMutation(_synthetic(operation: OfflineMutationOperation.revocation));

    final pending = await database.pendingMutations();
    expect(pending, hasLength(1));
    expect(pending.single.operation, OfflineMutationOperation.revocation);
  });

  test('operation survives the pending -> uploading -> conflict transition unchanged', () async {
    final database = FakeAppDatabase();
    await database.open();
    final mutation =
        await database.enqueueMutation(_synthetic(operation: OfflineMutationOperation.update));

    await database.markUploading(mutation.mutationId);
    await database.recordConflict(mutation.mutationId);

    final conflicted = await database.conflictedMutations();
    expect(conflicted, hasLength(1));
    expect(conflicted.single.operation, OfflineMutationOperation.update);
  });

  test(
      'a revocation delta takes priority over other pending mutations, even when enqueued later '
      '(ADR-0001 §3.5, BIO-MOB-002 known-gap closed)', () async {
    final database = FakeAppDatabase();
    await database.open();
    final addition = await database.enqueueMutation(_synthetic());
    await Future<void>.delayed(const Duration(milliseconds: 2));
    final update =
        await database.enqueueMutation(_synthetic(operation: OfflineMutationOperation.update));
    await Future<void>.delayed(const Duration(milliseconds: 2));
    final revocation = await database
        .enqueueMutation(_synthetic(operation: OfflineMutationOperation.revocation));

    final transport = FakeSyncTransport();
    final engine = SyncEngine(database: database, transport: transport);

    final result = await engine.runSyncPass();

    expect(result.acknowledged, 3);
    expect(transport.uploadOrder.first, revocation.mutationId,
        reason: 'the revocation must be uploaded first despite being enqueued last');
    expect(transport.uploadOrder.sublist(1), [addition.mutationId, update.mutationId],
        reason: 'non-revocation mutations keep their original due order relative to each other');
  });
}
