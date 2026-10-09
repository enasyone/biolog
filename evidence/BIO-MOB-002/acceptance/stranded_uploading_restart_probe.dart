import 'package:biolog_mobile/core/identifiers/canonical_id.dart';
import 'package:biolog_mobile/core/offline/offline_mutation.dart';

import '../../../tests/flutter/test/support/fake_app_database.dart';

Future<void> main() async {
  final database = FakeAppDatabase();
  await database.open();
  final mutation = await database.enqueueMutation(
    OfflineMutation.compose(
      tenantId: newCanonicalId(CanonicalIdType.tenant),
      deviceId: newCanonicalId(CanonicalIdType.device),
      aggregateId: newCanonicalId(CanonicalIdType.tenant),
      baseVersion: 1,
      idempotencyKey: 'acceptance-restart-probe',
      payloadJson: const {'value': 'SYNTHETIC'},
    ),
  );

  await database.markUploading(mutation.mutationId);

  // Simulate the process ending after the durable state changes to UPLOADING
  // but before a response can call recordUploadFailure/recordConflict.
  await database.close();
  await database.open();

  final due = await database.dueForRetry(
    DateTime.now().toUtc().add(const Duration(days: 365)),
  );
  final uploading =
      await database.countMutationsByState(OfflineSyncState.uploading);

  print('uploading_after_restart=$uploading');
  print('due_after_restart=${due.length}');

  if (uploading != 0 ||
      !due.any((item) => item.mutationId == mutation.mutationId)) {
    throw StateError(
      'expected restart recovery to make the interrupted mutation retryable',
    );
  }
}
