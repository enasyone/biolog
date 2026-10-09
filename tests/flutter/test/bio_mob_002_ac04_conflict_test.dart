// BIO-MOB-002-AC04: security-sensitive conflicts are never silently
// overwritten. Per ADR-0001 §3.5 ("Offline sync conflicts"): on a
// base_version mismatch, identity/credential/biometric aggregates always
// move to CONFLICT and enter the human Sync Resolution Queue — no silent
// last-write-wins. For any other aggregate, a deterministic merge is
// permitted only where the owning feature story defines the merge rule in
// writing; absent one, the default is also CONFLICT. No story anywhere yet
// defines such a rule, so this foundation's `recordConflict` is
// unconditional: every conflict goes to CONFLICT, uniformly, with no
// automatic resolution path at all.
import 'package:biolog_mobile/core/identifiers/canonical_id.dart';
import 'package:biolog_mobile/core/offline/offline_mutation.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/fake_app_database.dart';

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
  test('recordConflict moves a mutation to conflict, out of the automatic retry cycle',
      () async {
    final database = FakeAppDatabase();
    await database.open();
    final mutation = await database.enqueueMutation(_synthetic());
    // A conflict is detected responding to an upload attempt (BIO-MOB-002-QA-01:
    // recordConflict only applies to a mutation currently `uploading`).
    await database.markUploading(mutation.mutationId);

    await database.recordConflict(mutation.mutationId);

    expect(await database.countMutationsByState(OfflineSyncState.conflict), equals(1));
    expect(await database.countMutationsByState(OfflineSyncState.pending), equals(0));

    // Never picked up by the automatic retry path again — a conflict is not
    // an upload failure to retry, it needs a human.
    final due = await database.dueForRetry(DateTime.now().add(const Duration(days: 365)));
    expect(due, isEmpty);
    final stillPending = await database.pendingMutations();
    expect(stillPending, isEmpty);
  });

  test('a conflicted mutation is surfaced via conflictedMutations for the Sync Resolution Queue',
      () async {
    final database = FakeAppDatabase();
    await database.open();
    final mutation = await database.enqueueMutation(_synthetic());
    await database.markUploading(mutation.mutationId);
    await database.recordConflict(mutation.mutationId);

    final conflicts = await database.conflictedMutations();
    expect(conflicts, hasLength(1));
    expect(conflicts.single.mutationId, equals(mutation.mutationId));
    // The original payload is preserved unchanged — never silently
    // overwritten with a merged or "winning" value.
    expect(conflicts.single.payloadJson, equals(const {'field': 'SYNTHETIC'}));
  });

  test('a mutation already scheduled for retry is removed from the retry cycle on conflict',
      () async {
    final database = FakeAppDatabase();
    await database.open();
    final mutation = await database.enqueueMutation(_synthetic());
    await database.markUploading(mutation.mutationId);
    await database.recordUploadFailure(mutation.mutationId); // schedules a future retry
    await database.markUploading(mutation.mutationId); // the next attempt begins

    await database.recordConflict(mutation.mutationId);

    final conflicted = await database.conflictedMutations();
    expect(conflicted.single.nextRetryAt, isNull,
        reason: 'a conflicted mutation has nothing scheduled — it is not retried automatically');
  });

  test('conflict is never silently resolved — no resolution path exists in this story', () async {
    final database = FakeAppDatabase();
    await database.open();
    final mutation = await database.enqueueMutation(_synthetic());
    await database.markUploading(mutation.mutationId);
    await database.recordConflict(mutation.mutationId);

    // Nothing in this story's queue API can move a mutation back out of
    // conflict automatically. It stays there — visible, unresolved — until
    // a future human Sync Resolution Queue story acts on it.
    final conflicts = await database.conflictedMutations();
    expect(conflicts, hasLength(1));
    expect(conflicts.single.syncState, equals(OfflineSyncState.conflict));
  });
}
