// BIO-MOB-002-AC01 (extending BIO-MOB-001's queued-record shape): the new
// local-only retry-tracking fields (`retryCount`, `nextRetryAt`) validate on
// construction the same way every other field already does, and default
// sensibly for a freshly-composed mutation.
import 'package:biolog_mobile/core/identifiers/canonical_id.dart';
import 'package:biolog_mobile/core/identifiers/clock.dart';
import 'package:biolog_mobile/core/offline/offline_mutation.dart';
import 'package:flutter_test/flutter_test.dart';

OfflineMutation _base({int retryCount = 0, String? nextRetryAt}) {
  return OfflineMutation(
    mutationId: newCanonicalId(CanonicalIdType.event),
    tenantId: newCanonicalId(CanonicalIdType.tenant),
    deviceId: newCanonicalId(CanonicalIdType.device),
    aggregateId: newCanonicalId(CanonicalIdType.tenant),
    baseVersion: 1,
    idempotencyKey: 'idem-1',
    occurredAt: nowUtc(),
    syncState: OfflineSyncState.pending,
    payloadJson: const {},
    retryCount: retryCount,
    nextRetryAt: nextRetryAt,
  );
}

void main() {
  test('a freshly-composed mutation starts with retryCount 0 and no scheduled retry', () {
    final mutation = OfflineMutation.compose(
      tenantId: newCanonicalId(CanonicalIdType.tenant),
      deviceId: newCanonicalId(CanonicalIdType.device),
      aggregateId: newCanonicalId(CanonicalIdType.tenant),
      baseVersion: 1,
      idempotencyKey: 'idem-2',
      payloadJson: const {},
    );
    expect(mutation.retryCount, equals(0));
    expect(mutation.nextRetryAt, isNull);
  });

  test('rejects a negative retryCount', () {
    expect(() => _base(retryCount: -1), throwsArgumentError);
  });

  test('rejects a malformed nextRetryAt', () {
    expect(() => _base(nextRetryAt: 'not-an-instant'), throwsArgumentError);
  });

  test('accepts a well-formed nextRetryAt', () {
    final mutation = _base(retryCount: 2, nextRetryAt: nowUtc());
    expect(mutation.retryCount, equals(2));
    expect(mutation.nextRetryAt, isNotNull);
  });

  test('copyWith can explicitly clear nextRetryAt back to null', () {
    final scheduled = _base(retryCount: 1, nextRetryAt: nowUtc());
    final cleared = scheduled.copyWith(nextRetryAt: null);
    expect(cleared.nextRetryAt, isNull);
    // retryCount is untouched by clearing the schedule.
    expect(cleared.retryCount, equals(1));
  });

  test('copyWith without touching nextRetryAt leaves it as-is', () {
    final scheduled = _base(nextRetryAt: nowUtc());
    final bumped = scheduled.copyWith(retryCount: 5);
    expect(bumped.nextRetryAt, equals(scheduled.nextRetryAt));
    expect(bumped.retryCount, equals(5));
  });

  test('toEnvelopeJson does not leak the local-only retry fields into the frozen wire shape', () {
    final mutation = _base(retryCount: 3, nextRetryAt: nowUtc());
    final envelope = mutation.toEnvelopeJson();
    expect(envelope.containsKey('retry_count'), isFalse);
    expect(envelope.containsKey('next_retry_at'), isFalse);
  });
}
