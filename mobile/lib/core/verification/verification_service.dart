// BIO-MOB-004-AC01/AC02: chooses between the two verification paths this
// story provides - a live, policy-evaluated decision when online (this
// story's own `VerificationSyncClient`) and BIO-ACC-003's already-built
// offline revocation-cache check (`AccessCacheService.checkPresentation`)
// when not. This is "online and permitted offline behavior" from the user
// story: the offline path is a genuinely reduced-capability, existing
// mechanism, not a new one invented for this story - no ProjectReference,
// this class only calls the same public API surface
// `mobile/lib/features/*` screens already use.
library;

import '../access/access_cache_service.dart';
import '../connectivity/connectivity_signal.dart';
import '../db/app_database.dart';
import 'verification_outcome.dart';
import 'verification_sync_client.dart';

class VerificationService {
  VerificationService({
    required AppDatabase database,
    required AccessCacheService accessCacheService,
    required VerificationSyncClient syncClient,
  })  : _database = database,
        _accessCacheService = accessCacheService,
        _syncClient = syncClient;

  final AppDatabase _database;
  final AccessCacheService _accessCacheService;
  final VerificationSyncClient _syncClient;

  /// AC02: [connectivity] decides the path, never a guess or a fallback
  /// hidden inside a try/catch - the caller (the screen) already knows and
  /// displays this same state via the global offline banner, so the two
  /// stay consistent with each other.
  Future<VerificationOutcome> verify({
    required String personId,
    required String credentialClass,
    required String locationId,
    required String policyKey,
    required ConnectivityState connectivity,
  }) async {
    if (connectivity == ConnectivityState.online) {
      return _syncClient.requestDecision(
        _database,
        personId: personId,
        credentialClass: credentialClass,
        locationId: locationId,
        policyKey: policyKey,
      );
    }

    final event = await _accessCacheService.checkPresentation(personId);
    return VerificationOutcome.fromPresentationEvent(event);
  }
}
