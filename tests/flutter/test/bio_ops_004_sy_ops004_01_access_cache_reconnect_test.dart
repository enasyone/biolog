// BIO-OPS-004 (SY-OPS004-01, Seyi, High): the end-to-end session test
// evidence/BIO-OPS-004/builder/BLOCKED.md required. The shipped reconnect
// path never wired BIO-ACC-003's access-cache sync into
// SessionController.bootstrap() - a live app never learned server-side
// revocations and queued presentation events never drained. This exercises
// the real SessionController, its real ConnectivitySignal wiring and the
// real AccessCacheService/AccessCacheSyncClient (fake transport/HTTP, no
// real network), proving the fix end to end rather than only at the
// component level the isolated dependency tests already covered.
//
// BIO-OPS-004-ZA-01 (Zainab, High, round 2): round 1's reconnect-storm test
// only proved suppression of an overlapping signal on top of a
// *successful* first pass - it never exercised the lost-wakeup race where
// the overlapping signal is the only one that arrives while the first pass
// is in flight and that first pass then fails. The last test in this file
// is the deterministic repro Zainab's review required for that case.
import 'dart:async';
import 'dart:convert';

import 'package:biolog_mobile/core/access/access_cache_service.dart';
import 'package:biolog_mobile/core/access/access_cache_sync_client.dart';
import 'package:biolog_mobile/core/access/presentation_event.dart';
import 'package:biolog_mobile/core/access/revocation_entry.dart';
import 'package:biolog_mobile/core/connectivity/connectivity_signal.dart';
import 'package:biolog_mobile/core/identifiers/canonical_id.dart';
import 'package:biolog_mobile/core/identifiers/clock.dart';
import 'package:biolog_mobile/core/offline/offline_mutation.dart';
import 'package:biolog_mobile/core/security/secure_key_value_store.dart';
import 'package:biolog_mobile/core/session/session_controller.dart';
import 'package:biolog_mobile/core/sync/device_credential_store.dart';
import 'package:biolog_mobile/core/sync/sync_ack.dart';
import 'package:biolog_mobile/core/sync/sync_engine.dart';
import 'package:biolog_mobile/core/tenant/tenant_context.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

import 'support/fake_app_database.dart';
import 'support/fake_connectivity_signal.dart';
import 'support/fake_sync_transport.dart';

class _InMemorySecureStore implements SecureKeyValueStore {
  final Map<String, String> _values = {};

  @override
  Future<String?> read(String key) async => _values[key];

  @override
  Future<void> write(String key, String value) async {
    _values[key] = value;
  }
}

const _syntheticDeviceSecret = 'synthetic-access-cache-device-secret';

Future<DeviceCredentialStore> _provisioned() async {
  final store = DeviceCredentialStore(
    store: _InMemorySecureStore(),
    storageKey: 'test.bio_ops_004.access_cache_device_secret',
  );
  await store.writeSecret(_syntheticDeviceSecret);
  return store;
}

/// A bound tenant is the only production state
/// [SessionController._syncAccessCache] requires before it will pull/push -
/// see `tenant_select_screen.dart`/`session_controller.dart`'s own header
/// for why there is still no production path that binds one. Mirrors how
/// every other test needing a bound tenant seeds one directly, sanctioned
/// for test doubles by BIO-MOB-001-QA-03 (Decision 6).
Future<String> _bindTenant(FakeAppDatabase db) async {
  final tenantId = newCanonicalId(CanonicalIdType.tenant);
  final context = TenantContext.fromTrustedJson({
    'kind': 'tenant',
    'tenant_id': tenantId,
    'binding_key': 'binding-$tenantId',
    'channels': ['principal'],
    'correlation_id': newCanonicalId(CanonicalIdType.event),
    'resolved_at': toInstant(DateTime.utc(2026, 9, 24)),
  });
  await db.saveTenantContext(context);
  return tenantId;
}

OfflineMutation _syntheticGenericMutation(String tenantId) {
  return OfflineMutation.compose(
    tenantId: tenantId,
    deviceId: newCanonicalId(CanonicalIdType.device),
    aggregateId: newCanonicalId(CanonicalIdType.tenant),
    baseVersion: 1,
    idempotencyKey: 'idem-${DateTime.now().microsecondsSinceEpoch}',
    payloadJson: const {'field': 'SYNTHETIC'},
  );
}

void main() {
  test(
      'offline deny survives close/open, then two reconnects drain the event '
      'and keep the generic conflict visible, pulling/pushing access-cache '
      'state only once per reconnect', () async {
    // -- Arrange: a device that already has a revocation cached from some
    // earlier, now-closed session (BIO-ACC-003's own AC01/AC02), and a
    // generic BIO-MOB-003 mutation still pending from the same earlier
    // session - both must survive the close/open below exactly as a real
    // app restart would leave them.
    final seedDb = FakeAppDatabase();
    await seedDb.open();
    await seedDb.cacheRevocations(
      const [RevocationEntry(personId: 'per_revoked', revokedAt: '2026-09-23T00:00:00.000Z')],
      cursor: 'cursor-before-restart',
      syncedAt: DateTime.utc(2026, 9, 23),
    );
    final tenantId = await _bindTenant(seedDb);
    final genericMutation = await seedDb.enqueueMutation(_syntheticGenericMutation(tenantId));
    await seedDb.close();

    // -- The live app launches (bootstrap() itself reopens this same
    // database) already offline.
    final connectivity = FakeConnectivitySignal(initial: ConnectivityState.offline);
    final genericTransport = FakeSyncTransport()
      ..script(genericMutation.mutationId, () => const SyncAck.conflict(2));
    final syncEngine = SyncEngine(database: seedDb, transport: genericTransport);

    var revocationPulls = 0;
    final pushedIdempotencyKeys = <String>[];
    final accessCacheClient = AccessCacheSyncClient(
      baseUrl: Uri.parse('https://access-cache.example.test'),
      credentialStore: await _provisioned(),
      client: MockClient((request) async {
        if (request.method == 'GET') {
          revocationPulls++;
          // Nothing new server-side this pass - the denial below is decided
          // from the cache this device already carried across the restart.
          return http.Response(jsonEncode({'items': const [], 'cursor': 'cursor-before-restart'}), 200);
        }
        pushedIdempotencyKeys.add(request.headers['Idempotency-Key']!);
        final body = jsonDecode(request.body) as Map<String, Object?>;
        return http.Response(jsonEncode({'event_id': body['event_id'], 'received_at': '2026-09-24T00:00:01.000Z'}), 201);
      }),
    );

    final session = SessionController(
      database: seedDb,
      connectivity: connectivity,
      syncEngine: syncEngine,
      accessCacheSyncClient: accessCacheClient,
    );

    await session.bootstrap();
    expect(session.hasBoundTenant, isTrue, reason: 'the seeded tenant context must survive close/open too');

    // -- Act 1: while still offline, a presentation of the person this
    // device already knows is revoked (cached before the restart) is denied
    // and logged locally - no network involved, exactly AC03's contract.
    final accessCacheService = AccessCacheService(session.database, now: () => DateTime.utc(2026, 9, 24));
    final deniedEvent = await accessCacheService.checkPresentation('per_revoked');
    expect(deniedEvent.outcome, PresentationOutcome.denied,
        reason: 'the cached revocation from before the restart must still deny, proving the cache itself survived close/open');
    expect(await seedDb.pendingPresentationEvents(), hasLength(1));

    // -- Act 2: first reconnect.
    connectivity.set(ConnectivityState.online);
    await Future<void>.delayed(const Duration(milliseconds: 50));

    expect(revocationPulls, 1, reason: 'reconnect must pull revocation deltas, not just drive the generic SyncEngine');
    expect(pushedIdempotencyKeys, [deniedEvent.eventId],
        reason: 'the queued presentation event must drain to the server using its own event id as the idempotency key');
    expect(await seedDb.pendingPresentationEvents(), isEmpty, reason: 'one server event has drained');
    final conflictedAfterFirstReconnect = await seedDb.conflictedMutations();
    expect(conflictedAfterFirstReconnect.map((m) => m.mutationId), [genericMutation.mutationId],
        reason: 'the generic mutation conflict must be surfaced, never silently resolved');

    // -- Act 3: second reconnect (flap offline, then online again). Must not
    // re-push the already-synced event, must not lose or auto-resolve the
    // still-visible conflict, and must still pull again (a live cache keeps
    // refreshing on every reconnect, not just the first one).
    connectivity.set(ConnectivityState.offline);
    connectivity.set(ConnectivityState.online);
    await Future<void>.delayed(const Duration(milliseconds: 50));

    expect(revocationPulls, 2, reason: 'a second reconnect pulls again - the live cache keeps refreshing');
    expect(pushedIdempotencyKeys, [deniedEvent.eventId],
        reason: 'no new pending event exists, so the second reconnect must not push anything again');
    final conflictedAfterSecondReconnect = await seedDb.conflictedMutations();
    expect(conflictedAfterSecondReconnect.map((m) => m.mutationId), [genericMutation.mutationId],
        reason: 'the conflict is still visible after a second reconnect, not silently cleared');
  });

  test(
      'two reconnect signals arriving back to back coalesce into exactly one rerun - never zero (dropped, '
      'ZA-OPS004-01) and never unbounded (one extra pass per signal)', () async {
    final db = FakeAppDatabase();
    await db.open();
    await _bindTenant(db);
    final connectivity = FakeConnectivitySignal(initial: ConnectivityState.offline);

    var pulls = 0;
    final accessCacheClient = AccessCacheSyncClient(
      baseUrl: Uri.parse('https://access-cache.example.test'),
      credentialStore: await _provisioned(),
      client: MockClient((request) async {
        if (request.method == 'GET') pulls++;
        return http.Response(jsonEncode({'items': const [], 'cursor': null}), 200);
      }),
    );
    final session = SessionController(database: db, connectivity: connectivity, accessCacheSyncClient: accessCacheClient);
    await session.bootstrap();

    // A flapping radio reporting "online" twice in quick succession - the
    // exact BIO-MOB-003-AC05 reconnect-storm shape, now for the
    // access-cache coordinator too. The second signal arrives while the
    // first pass is still active (synchronously, before its first network
    // await yields control back - see bio_mob_003_ac05_reconnect_storm_test
    // .dart's own header for why that ordering is guaranteed, not a timing
    // coincidence), so it must be coalesced into exactly one rerun after
    // the active pass finishes - not silently dropped (ZA-OPS004-01), and
    // not fanned out into a second signal's own independent pass on top.
    connectivity.set(ConnectivityState.online);
    connectivity.set(ConnectivityState.online);
    await Future<void>.delayed(const Duration(milliseconds: 50));

    expect(pulls, 2,
        reason: 'one pass for the active signal, plus exactly one coalesced rerun for the overlapping second '
            'signal - never dropped, never one extra pass per overlapping signal');
  });

  test(
      'ZA-OPS004-01: a reconnect signal that arrives while the first pull is still in flight is coalesced into '
      'a rerun, not dropped, when that first pull then fails', () async {
    final db = FakeAppDatabase();
    await db.open();
    await _bindTenant(db);
    final connectivity = FakeConnectivitySignal(initial: ConnectivityState.offline);

    var pullAttempts = 0;
    // Holds the FIRST pull's HTTP response open, so the test controls
    // exactly when that request finally fails - mirroring a mid-request
    // network drop whose recovery is the only new connectivity signal
    // before the original request gives up.
    final firstPullHeld = Completer<void>();
    final accessCacheClient = AccessCacheSyncClient(
      baseUrl: Uri.parse('https://access-cache.example.test'),
      credentialStore: await _provisioned(),
      client: MockClient((request) async {
        if (request.method == 'GET') {
          pullAttempts++;
          if (pullAttempts == 1) {
            await firstPullHeld.future;
            throw Exception('simulated mid-request network drop');
          }
          return http.Response(jsonEncode({'items': const [], 'cursor': null}), 200);
        }
        return http.Response(jsonEncode({'event_id': 'unused', 'received_at': '2026-09-24T00:00:01.000Z'}), 201);
      }),
    );
    final session = SessionController(database: db, connectivity: connectivity, accessCacheSyncClient: accessCacheClient);
    await session.bootstrap();

    // Reconnect 1: starts the held-open pull.
    connectivity.set(ConnectivityState.online);
    await Future<void>.delayed(const Duration(milliseconds: 20));
    expect(pullAttempts, 1);

    // The network's return - the only new signal while the first pull is
    // still in flight - must be coalesced (recorded as a rerun owed), not
    // dropped, even though the single-flight guard correctly absorbs it as
    // a no-op right now.
    connectivity.set(ConnectivityState.offline);
    connectivity.set(ConnectivityState.online);
    await Future<void>.delayed(const Duration(milliseconds: 20));
    expect(pullAttempts, 1,
        reason: 'single-flight is still preserved - this signal must not start a second CONCURRENT pull');

    // Now let the original, now-stale request fail.
    firstPullHeld.complete();
    await Future<void>.delayed(const Duration(milliseconds: 50));

    expect(pullAttempts, 2,
        reason: 'the coalesced rerun must fire once the first pass finishes, with no third connectivity '
            'transition required - this is exactly the lost-wakeup ZA-OPS004-01 flagged');
  });
}
