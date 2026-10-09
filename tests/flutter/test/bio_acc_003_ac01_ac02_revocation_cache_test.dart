// BIO-ACC-003-AC01 (edge cache is minimum necessary data) / AC02 (revocation
// deltas have priority — modelled here as additive, monotonic merge: once a
// person is cached as revoked, a later cache round can only add more
// entries, never remove or downgrade one, which is how "priority" survives
// an arbitrary number of partial/interrupted syncs).
import 'package:biolog_mobile/core/access/revocation_entry.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/fake_app_database.dart';

void main() {
  test('AC01 a cached revocation entry carries only person id and revoked-at, nothing else', () {
    const entry = RevocationEntry(personId: 'per_test', revokedAt: '2026-09-24T00:00:00.000Z');
    expect(entry.personId, 'per_test');
    expect(entry.revokedAt, '2026-09-24T00:00:00.000Z');
  });

  test('AC01/AC02 caching a delta makes isRevoked true only for the cached person', () async {
    final db = FakeAppDatabase();
    await db.open();

    expect(await db.isRevoked('per_revoked'), isFalse);

    await db.cacheRevocations(
      const [RevocationEntry(personId: 'per_revoked', revokedAt: '2026-09-24T00:00:00.000Z')],
      cursor: '2026-09-24T00:00:00.000Z',
      syncedAt: DateTime.utc(2026, 9, 24),
    );

    expect(await db.isRevoked('per_revoked'), isTrue);
    expect(await db.isRevoked('per_untouched'), isFalse);
  });

  test('AC02 a second, later delta round adds to the cache without losing earlier entries', () async {
    final db = FakeAppDatabase();
    await db.open();

    await db.cacheRevocations(
      const [RevocationEntry(personId: 'per_first', revokedAt: '2026-09-24T00:00:00.000Z')],
      cursor: '2026-09-24T00:00:00.000Z',
      syncedAt: DateTime.utc(2026, 9, 24),
    );
    await db.cacheRevocations(
      const [RevocationEntry(personId: 'per_second', revokedAt: '2026-09-24T01:00:00.000Z')],
      cursor: '2026-09-24T01:00:00.000Z',
      syncedAt: DateTime.utc(2026, 9, 24, 1),
    );

    expect(await db.isRevoked('per_first'), isTrue);
    expect(await db.isRevoked('per_second'), isTrue);
    expect(await db.revocationCacheCursor(), '2026-09-24T01:00:00.000Z');
  });

  test('AC04 an empty successful poll still advances last-synced-at (proves the cache is current, not stale)', () async {
    final db = FakeAppDatabase();
    await db.open();
    expect(await db.revocationCacheLastSyncedAt(), isNull);

    final syncedAt = DateTime.utc(2026, 9, 24, 6);
    await db.cacheRevocations(const [], cursor: null, syncedAt: syncedAt);

    expect(await db.revocationCacheLastSyncedAt(), syncedAt);
  });
}
