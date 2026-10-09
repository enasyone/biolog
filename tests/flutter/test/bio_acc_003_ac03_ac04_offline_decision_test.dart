// BIO-ACC-003-AC03 (old card presentation logs locally and denies once
// revocation is cached) / AC04 (stale-cache policy is explicit). Every
// assertion here runs against FakeAppDatabase with no network dependency at
// all — proving the decision genuinely needs no connectivity, not just that
// it happens not to use one in this particular test.
import 'package:biolog_mobile/core/access/access_cache_service.dart';
import 'package:biolog_mobile/core/access/presentation_event.dart';
import 'package:biolog_mobile/core/access/revocation_entry.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/fake_app_database.dart';

void main() {
  test('AC03 a presentation for a person NOT in the cache is allowed and logged locally', () async {
    final db = FakeAppDatabase();
    await db.open();
    final service = AccessCacheService(db, now: () => DateTime.utc(2026, 9, 24));

    final event = await service.checkPresentation('per_unknown');

    expect(event.outcome, PresentationOutcome.allowed);
    expect(event.reasonCode, reasonAllowedNotRevoked);
    final pending = await db.pendingPresentationEvents();
    expect(pending, hasLength(1));
    expect(pending.first.eventId, event.eventId);
  });

  test('AC03 a presentation for a person cached as revoked is denied and logged locally, fully offline', () async {
    final db = FakeAppDatabase();
    await db.open();
    await db.cacheRevocations(
      const [RevocationEntry(personId: 'per_revoked', revokedAt: '2026-09-23T00:00:00.000Z')],
      cursor: '2026-09-23T00:00:00.000Z',
      syncedAt: DateTime.utc(2026, 9, 24),
    );
    final service = AccessCacheService(db, now: () => DateTime.utc(2026, 9, 24));

    final event = await service.checkPresentation('per_revoked');

    expect(event.outcome, PresentationOutcome.denied);
    expect(event.reasonCode, reasonDeniedRevokedCached);
    final pending = await db.pendingPresentationEvents();
    expect(pending, hasLength(1));
    expect(pending.first.outcome, PresentationOutcome.denied);
  });

  test('AC04 a decision made against a never-synced cache is flagged stale', () async {
    final db = FakeAppDatabase();
    await db.open();
    final service = AccessCacheService(db, now: () => DateTime.utc(2026, 9, 24));

    final event = await service.checkPresentation('per_unknown');

    expect(event.stale, isTrue);
    expect(event.cacheSnapshotAt, isNull);
  });

  test('AC04 a decision made just after a sync is fresh, not stale', () async {
    final db = FakeAppDatabase();
    await db.open();
    await db.cacheRevocations(const [], cursor: null, syncedAt: DateTime.utc(2026, 9, 24, 0, 0));

    final service = AccessCacheService(db, now: () => DateTime.utc(2026, 9, 24, 0, 5));
    final event = await service.checkPresentation('per_unknown');

    expect(event.stale, isFalse);
  });

  test('AC04 a decision made 25 hours after the last sync is flagged stale', () async {
    final db = FakeAppDatabase();
    await db.open();
    await db.cacheRevocations(const [], cursor: null, syncedAt: DateTime.utc(2026, 9, 23, 0, 0));

    final service = AccessCacheService(db, now: () => DateTime.utc(2026, 9, 24, 1, 0));
    final event = await service.checkPresentation('per_unknown');

    expect(event.stale, isTrue);
  });

  test('AC04 staleness never changes the allow/deny outcome — a stale cache still denies a known revocation', () async {
    final db = FakeAppDatabase();
    await db.open();
    // Cache a revocation, then let it age well past the staleness threshold
    // without ever syncing again — the cached fact must still be honoured.
    await db.cacheRevocations(
      const [RevocationEntry(personId: 'per_revoked', revokedAt: '2026-09-01T00:00:00.000Z')],
      cursor: '2026-09-01T00:00:00.000Z',
      syncedAt: DateTime.utc(2026, 9, 1),
    );

    final service = AccessCacheService(db, now: () => DateTime.utc(2026, 9, 24));
    final event = await service.checkPresentation('per_revoked');

    expect(event.outcome, PresentationOutcome.denied);
    expect(event.stale, isTrue);
  });
}
