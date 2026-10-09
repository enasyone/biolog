// BIO-MOB-002-AC01/AC02/AC03: the queue's retry scheduling, exercised
// through AppDatabase (via FakeAppDatabase — a platform-channel-free double
// of the real SqlCipherAppDatabase, which implements the identical
// contract). AC01 (global/device ids, version, idempotency key) and AC02
// (the six sync states) were already delivered by BIO-MOB-001; these tests
// are about the new AC03 behaviour: bounded exponential retry scheduling.
import 'package:biolog_mobile/core/identifiers/canonical_id.dart';
import 'package:biolog_mobile/core/offline/offline_mutation.dart';
import 'package:biolog_mobile/core/offline/retry_policy.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/fake_app_database.dart';

OfflineMutation _synthetic(FakeAppDatabase database) {
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
  test('a freshly-enqueued mutation is immediately due for retry (never attempted)', () async {
    final database = FakeAppDatabase();
    await database.open();
    final mutation = await database.enqueueMutation(_synthetic(database));

    final due = await database.dueForRetry(DateTime.now());
    expect(due.map((m) => m.mutationId), contains(mutation.mutationId));
    expect(mutation.retryCount, equals(0));
    expect(mutation.nextRetryAt, isNull);
  });

  test('markUploading transitions pending -> uploading (AC02)', () async {
    final database = FakeAppDatabase();
    await database.open();
    final mutation = await database.enqueueMutation(_synthetic(database));

    await database.markUploading(mutation.mutationId);

    expect(await database.countMutationsByState(OfflineSyncState.uploading), equals(1));
    expect(await database.countMutationsByState(OfflineSyncState.pending), equals(0));
  });

  test(
      'markUploading cannot resurrect a conflicted mutation (BIO-MOB-002-QA-01, Seyi round 1 repro)',
      () async {
    final database = FakeAppDatabase();
    await database.open();
    final mutation = await database.enqueueMutation(_synthetic(database));
    await database.markUploading(mutation.mutationId);
    await database.recordConflict(mutation.mutationId);

    // Exactly Seyi's repro: call markUploading on a mutation already
    // resolved to `conflict`.
    await database.markUploading(mutation.mutationId);

    expect(await database.countMutationsByState(OfflineSyncState.conflict), equals(1));
    expect(await database.countMutationsByState(OfflineSyncState.uploading), equals(0));
    final conflicts = await database.conflictedMutations();
    expect(conflicts, hasLength(1),
        reason: 'conflictedMutations must still see it — it was not resurrected to uploading');
  });

  test('markUploading cannot resurrect a failed mutation (BIO-MOB-002-QA-01)', () async {
    const maxAttempts = 1;
    final database = FakeAppDatabase(
      retryPolicy:
          RetryPolicy(baseDelay: const Duration(milliseconds: 1), maxAttempts: maxAttempts),
    );
    await database.open();
    final mutation = await database.enqueueMutation(_synthetic(database));
    // hasAttemptsRemaining(retryCount) checks the count *before* this
    // failure (see retry_policy.dart), so with maxAttempts: 1 it takes two
    // failed attempts to exhaust the bound: the first (retryCount 0) still
    // reschedules, the second (retryCount 1) moves to failed.
    for (var i = 0; i < maxAttempts + 1; i++) {
      await database.markUploading(mutation.mutationId);
      await database.recordUploadFailure(mutation.mutationId);
    }
    expect(await database.countMutationsByState(OfflineSyncState.failed), equals(1));

    await database.markUploading(mutation.mutationId);

    expect(await database.countMutationsByState(OfflineSyncState.failed), equals(1));
    expect(await database.countMutationsByState(OfflineSyncState.uploading), equals(0));
  });

  test('a failed attempt with retries remaining schedules a future retry and returns to pending',
      () async {
    final database = FakeAppDatabase(
      retryPolicy: RetryPolicy(baseDelay: const Duration(minutes: 1), maxAttempts: 5),
    );
    await database.open();
    final mutation = await database.enqueueMutation(_synthetic(database));
    await database.markUploading(mutation.mutationId);

    final now = DateTime.now();
    await database.recordUploadFailure(mutation.mutationId, now: now);

    final due = await database.dueForRetry(now);
    expect(due, isEmpty, reason: 'a scheduled retry is not due immediately');

    final stillPending = await database.pendingMutations();
    expect(stillPending, hasLength(1));
    expect(stillPending.single.retryCount, equals(1));
    expect(stillPending.single.nextRetryAt, isNotNull);

    // Once the scheduled instant passes, it becomes due again.
    final later = now.add(const Duration(hours: 1));
    final dueLater = await database.dueForRetry(later);
    expect(dueLater.map((m) => m.mutationId), contains(mutation.mutationId));
  });

  test('exhausting the bound moves the mutation to failed, not an infinite retry', () async {
    const maxAttempts = 3;
    final database = FakeAppDatabase(
      retryPolicy:
          RetryPolicy(baseDelay: const Duration(milliseconds: 1), maxAttempts: maxAttempts),
    );
    await database.open();
    final mutation = await database.enqueueMutation(_synthetic(database));

    // hasAttemptsRemaining(retryCount) checks the count *before* this
    // failure, so retryCount 0..maxAttempts-1 each still get a scheduled
    // retry; only the (maxAttempts + 1)th failure — retryCount == maxAttempts
    // — is exhausted and moves to failed.
    for (var i = 0; i < maxAttempts + 1; i++) {
      await database.markUploading(mutation.mutationId);
      await database.recordUploadFailure(mutation.mutationId);
    }

    expect(await database.countMutationsByState(OfflineSyncState.failed), equals(1));
    expect(await database.countMutationsByState(OfflineSyncState.pending), equals(0));

    // AC03: bounded — a failed mutation is not retried automatically again.
    final due = await database.dueForRetry(DateTime.now().add(const Duration(days: 1)));
    expect(due, isEmpty);
  });

  test('retryCount increments by exactly one per failed attempt', () async {
    final database = FakeAppDatabase(
      retryPolicy: RetryPolicy(baseDelay: const Duration(milliseconds: 1), maxAttempts: 10),
    );
    await database.open();
    final mutation = await database.enqueueMutation(_synthetic(database));

    await database.markUploading(mutation.mutationId);
    await database.recordUploadFailure(mutation.mutationId);
    var pending = await database.pendingMutations();
    expect(pending.single.retryCount, equals(1));

    await database.markUploading(mutation.mutationId);
    await database.recordUploadFailure(mutation.mutationId);
    pending = await database.pendingMutations();
    expect(pending.single.retryCount, equals(2));
  });

  test(
      'recordUploadFailure is a no-op for a mutation that is not currently uploading (BIO-MOB-002-QA-01)',
      () async {
    final database = FakeAppDatabase();
    await database.open();
    final mutation = await database.enqueueMutation(_synthetic(database));
    // Never called markUploading — the mutation is still `pending`.

    await database.recordUploadFailure(mutation.mutationId);

    final pending = await database.pendingMutations();
    expect(pending.single.retryCount, equals(0),
        reason: 'recordUploadFailure must not apply to a mutation that was never uploading');
  });

  test(
      'a mutation stranded in uploading by a process restart recovers to retryable pending '
      '(BIO-MOB-002-QA-02, Chioma acceptance round 1)', () async {
    // Mirrors evidence/BIO-MOB-002/acceptance/stranded_uploading_restart_probe.dart:
    // markUploading, then simulate the process ending before any response
    // arrives (close/open), standing in for a real app restart.
    final database = FakeAppDatabase();
    await database.open();
    final mutation = await database.enqueueMutation(_synthetic(database));
    await database.markUploading(mutation.mutationId);

    await database.close();
    await database.open();

    expect(await database.countMutationsByState(OfflineSyncState.uploading), equals(0),
        reason: 'a restart must not leave a mutation stranded in uploading forever');
    final pending = await database.pendingMutations();
    expect(pending, hasLength(1));
    expect(pending.single.retryCount, equals(1),
        reason: 'the interrupted attempt counts against the bounded-retry budget, '
            'the same as any other failed attempt');
    // Retryable per the story's "continue through connectivity loss"
    // guarantee: due within a generous window, even though the immediate
    // next attempt is backed off rather than instant.
    final due = await database.dueForRetry(DateTime.now().add(const Duration(days: 365)));
    expect(due.map((m) => m.mutationId), contains(mutation.mutationId));
  });
}
