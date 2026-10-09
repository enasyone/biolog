// BIO-MOB-001-AC04 (offline queue support): the local offline-mutation model
// mirrors `contracts/schemas/offline-mutation.schema.json` field-for-field so
// a later sync story can upload it unchanged.
import 'package:biolog_mobile/core/identifiers/canonical_id.dart';
import 'package:biolog_mobile/core/offline/offline_mutation.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  final tenantId = newCanonicalId(CanonicalIdType.tenant);
  final deviceId = newCanonicalId(CanonicalIdType.device);
  final aggregateId = newCanonicalId(CanonicalIdType.tenant);

  test('compose produces a PENDING mutation with a canonical envelope', () {
    final mutation = OfflineMutation.compose(
      tenantId: tenantId,
      deviceId: deviceId,
      aggregateId: aggregateId,
      baseVersion: 1,
      idempotencyKey: 'idem-1',
      payloadJson: {'example': 'SYNTHETIC'},
    );

    expect(mutation.syncState, equals(OfflineSyncState.pending));
    final envelope = mutation.toEnvelopeJson();
    expect(envelope.keys, containsAll([
      'mutation_id',
      'tenant_id',
      'device_id',
      'aggregate_id',
      'base_version',
      'idempotency_key',
      'occurred_at',
      'sync_state',
    ]));
    expect(envelope['sync_state'], equals('PENDING'));
    expect(envelope['tenant_id'], equals(tenantId));
  });

  test('rejects a non-canonical tenant id', () {
    expect(
      () => OfflineMutation.compose(
        tenantId: 'not-a-tenant-id',
        deviceId: deviceId,
        aggregateId: aggregateId,
        baseVersion: 1,
        idempotencyKey: 'idem-2',
        payloadJson: const {},
      ),
      throwsArgumentError,
    );
  });

  test('rejects base_version below 1', () {
    expect(
      () => OfflineMutation(
        mutationId: newCanonicalId(CanonicalIdType.event),
        tenantId: tenantId,
        deviceId: deviceId,
        aggregateId: aggregateId,
        baseVersion: 0,
        idempotencyKey: 'idem-3',
        occurredAt: '2026-09-20T21:05:00.000Z',
        syncState: OfflineSyncState.pending,
        payloadJson: const {},
      ),
      throwsArgumentError,
    );
  });

  test('rejects an empty idempotency key', () {
    expect(
      () => OfflineMutation.compose(
        tenantId: tenantId,
        deviceId: deviceId,
        aggregateId: aggregateId,
        baseVersion: 1,
        idempotencyKey: '',
        payloadJson: const {},
      ),
      throwsArgumentError,
    );
  });

  test('every sync_state round-trips through the wire enum', () {
    for (final state in OfflineSyncState.values) {
      expect(syncStateFromWire(syncStateToWire(state)), equals(state));
    }
  });

  test('copyWith transitions sync_state without touching identity fields', () {
    final mutation = OfflineMutation.compose(
      tenantId: tenantId,
      deviceId: deviceId,
      aggregateId: aggregateId,
      baseVersion: 1,
      idempotencyKey: 'idem-4',
      payloadJson: const {},
    );
    final uploaded = mutation.copyWith(syncState: OfflineSyncState.uploading);
    expect(uploaded.mutationId, equals(mutation.mutationId));
    expect(uploaded.syncState, equals(OfflineSyncState.uploading));
  });
}
