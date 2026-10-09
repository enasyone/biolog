// BIO-MOB-001-F01 (Seyi round 1, HIGH): `enqueueMutation` used to insert
// with `ConflictAlgorithm.replace` against the unique `idempotency_key`
// column, so a retry or a second mutation reusing a key silently discarded
// the original queued envelope. Fixed via `resolveEnqueue`: an identical
// replay is a no-op that returns the original untouched; a reused key with
// different content is a rejected conflict, never a silent replace.
import 'package:biolog_mobile/core/identifiers/canonical_id.dart';
import 'package:biolog_mobile/core/offline/idempotent_enqueue.dart';
import 'package:biolog_mobile/core/offline/offline_mutation.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/fake_app_database.dart';

OfflineMutation _mutation({
  required String tenantId,
  required String deviceId,
  String? aggregateId,
  int baseVersion = 1,
  required String idempotencyKey,
  Map<String, Object?> payloadJson = const {'field': 'SYNTHETIC'},
}) {
  return OfflineMutation.compose(
    tenantId: tenantId,
    deviceId: deviceId,
    aggregateId: aggregateId ?? newCanonicalId(CanonicalIdType.tenant),
    baseVersion: baseVersion,
    idempotencyKey: idempotencyKey,
    payloadJson: payloadJson,
  );
}

void main() {
  final tenantId = newCanonicalId(CanonicalIdType.tenant);
  final deviceId = newCanonicalId(CanonicalIdType.device);
  final aggregateId = newCanonicalId(CanonicalIdType.tenant);

  group('resolveEnqueue (pure decision)', () {
    test('no existing entry: the incoming mutation is returned as-is', () {
      final incoming = _mutation(
        tenantId: tenantId,
        deviceId: deviceId,
        aggregateId: aggregateId,
        idempotencyKey: 'k1',
      );
      expect(resolveEnqueue(existing: null, incoming: incoming), same(incoming));
    });

    test('identical replay returns the ORIGINAL instance, not the retry', () {
      final original = _mutation(
        tenantId: tenantId,
        deviceId: deviceId,
        aggregateId: aggregateId,
        idempotencyKey: 'k2',
        payloadJson: const {'field': 'A'},
      );
      final retry = _mutation(
        tenantId: tenantId,
        deviceId: deviceId,
        aggregateId: aggregateId,
        idempotencyKey: 'k2',
        payloadJson: const {'field': 'A'},
      );
      final resolved = resolveEnqueue(existing: original, incoming: retry);
      expect(resolved, same(original));
      expect(resolved.mutationId, equals(original.mutationId));
      expect(resolved.mutationId, isNot(equals(retry.mutationId)));
    });

    test('same key, different payload: throws instead of replacing (BIO-MOB-001-F01)', () {
      final original = _mutation(
        tenantId: tenantId,
        deviceId: deviceId,
        aggregateId: aggregateId,
        idempotencyKey: 'k3',
        payloadJson: const {'field': 'A'},
      );
      final different = _mutation(
        tenantId: tenantId,
        deviceId: deviceId,
        aggregateId: aggregateId,
        idempotencyKey: 'k3',
        payloadJson: const {'field': 'B'},
      );
      expect(
        () => resolveEnqueue(existing: original, incoming: different),
        throwsA(isA<IdempotencyConflictError>()),
      );
    });

    test('same key, different aggregate: throws (aggregate is logical content)', () {
      final original = _mutation(
        tenantId: tenantId,
        deviceId: deviceId,
        aggregateId: aggregateId,
        idempotencyKey: 'k4',
      );
      final different = _mutation(
        tenantId: tenantId,
        deviceId: deviceId,
        aggregateId: newCanonicalId(CanonicalIdType.tenant),
        idempotencyKey: 'k4',
      );
      expect(
        () => resolveEnqueue(existing: original, incoming: different),
        throwsA(isA<IdempotencyConflictError>()),
      );
    });
  });

  group('jsonDeepEquals', () {
    test('nested maps/lists compare structurally, not by identity', () {
      expect(
        jsonDeepEquals({
          'a': [1, 2, {'b': 'x'}],
        }, {
          'a': [1, 2, {'b': 'x'}],
        }),
        isTrue,
      );
      expect(
        jsonDeepEquals({
          'a': [1, 2],
        }, {
          'a': [1, 3],
        }),
        isFalse,
      );
    });
  });

  group('AppDatabase.enqueueMutation (via FakeAppDatabase)', () {
    test('exact Seyi repro: same key + different payload never replaces the queue', () async {
      final db = FakeAppDatabase();
      await db.open();

      final a = _mutation(
        tenantId: tenantId,
        deviceId: deviceId,
        aggregateId: aggregateId,
        idempotencyKey: 'shared-key',
        payloadJson: const {'field': 'A'},
      );
      final b = _mutation(
        tenantId: tenantId,
        deviceId: deviceId,
        aggregateId: aggregateId,
        idempotencyKey: 'shared-key',
        payloadJson: const {'field': 'B'},
      );

      await db.enqueueMutation(a);
      await expectLater(
        () => db.enqueueMutation(b),
        throwsA(isA<IdempotencyConflictError>()),
      );

      final pending = await db.pendingMutations();
      expect(pending, hasLength(1));
      expect(pending.single.mutationId, equals(a.mutationId));
      expect(pending.single.payloadJson, equals(const {'field': 'A'}));
    });

    test('a genuine retry (same logical content) is a no-op, not a duplicate', () async {
      final db = FakeAppDatabase();
      await db.open();

      final first = _mutation(
        tenantId: tenantId,
        deviceId: deviceId,
        aggregateId: aggregateId,
        idempotencyKey: 'retry-key',
      );
      final retry = _mutation(
        tenantId: tenantId,
        deviceId: deviceId,
        aggregateId: aggregateId,
        idempotencyKey: 'retry-key',
      );

      final storedFirst = await db.enqueueMutation(first);
      final storedRetry = await db.enqueueMutation(retry);

      expect(storedRetry.mutationId, equals(storedFirst.mutationId));
      final pending = await db.pendingMutations();
      expect(pending, hasLength(1));
    });
  });
}
