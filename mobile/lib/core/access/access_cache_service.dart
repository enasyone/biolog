// BIO-ACC-003-AC03/AC04: the actual offline access decision. Everything
// this class does is local and synchronous-in-spirit — no network call is
// made or awaited anywhere in `checkPresentation` — exactly the "denied
// without cloud connectivity" the user story asks for.
library;

import '../db/app_database.dart';
import '../identifiers/canonical_id.dart';
import '../identifiers/clock.dart';
import 'presentation_event.dart';
import 'stale_cache_policy.dart';

/// Evaluates a card presentation against the locally cached revocation
/// deny-list and durably logs the decision (AC03), flagging it `stale`
/// (AC04) when the cache has not synced recently enough to be current —
/// never changing the allow/deny outcome itself: a stale cache still
/// correctly denies anything it already knows is revoked (see
/// `stale_cache_policy.dart`'s own header for the full reasoning).
class AccessCacheService {
  AccessCacheService(this._db, {DateTime Function()? now}) : _now = now ?? DateTime.now;

  final AppDatabase _db;
  final DateTime Function() _now;

  Future<PresentationEvent> checkPresentation(String personId) async {
    final now = _now().toUtc();
    final revoked = await _db.isRevoked(personId);
    final lastSyncedAt = await _db.revocationCacheLastSyncedAt();
    final stale = isRevocationCacheStale(lastSyncedAt: lastSyncedAt, now: now);

    final event = PresentationEvent(
      eventId: newCanonicalId(CanonicalIdType.event),
      personId: personId,
      outcome: revoked ? PresentationOutcome.denied : PresentationOutcome.allowed,
      reasonCode: revoked ? reasonDeniedRevokedCached : reasonAllowedNotRevoked,
      occurredAt: toInstant(now),
      cacheSnapshotAt: lastSyncedAt == null ? null : toInstant(lastSyncedAt.toUtc()),
      stale: stale,
    );

    // AC03: logged locally regardless of outcome or connectivity — this is
    // the durable record a later `AccessCacheSyncClient.pushPendingEvents`
    // call uploads (AC05), not something this method itself transmits.
    await _db.recordPresentationEvent(event);
    return event;
  }
}
