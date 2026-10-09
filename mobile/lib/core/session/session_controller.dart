// App-wide session state: the explicit tenant context (AC03) and the live
// connectivity signal that drives the offline UI (AC04). This is the single
// source of truth the router and screens read from.
//
// BIO-MOB-001-QA-02 (Chioma, acceptance round 2): this class previously had
// a `bindTenant(tenantId, bindingKey, ...)` method that took raw operator
// input and called the (now-removed) `TenantContext.resolve()` to mint a
// "resolved" context locally — exactly the caller-constructed-provenance
// anti-pattern ADR-0002 Decision 4.1 forbids. There is no such method now.
// A tenant context only ever enters this class via
// `AppDatabase.loadTrustedTenantContext()` (`bootstrap()`), which itself
// only returns a context that both deserialized via
// `TenantContext.fromTrustedJson` AND carries verified resolution
// provenance (BIO-MOB-001-QA-03) — a schema-valid record with no provenance
// marker (e.g. one left over from before this fix) is invalidated, not
// bound. There is no production path to mint provenance today; until a
// trusted resolution boundary exists, this app cannot reach a bound state —
// intentional (see `tenant_select_screen.dart`), not an omission.
library;

import 'dart:async';

import 'package:flutter/foundation.dart';

import '../access/access_cache_sync_client.dart';
import '../connectivity/connectivity_signal.dart';
import '../db/app_database.dart';
import '../sync/sync_engine.dart';
import '../sync/sync_transport.dart';
import '../tenant/tenant_context.dart';

class SessionController extends ChangeNotifier {
  // BIO-MOB-003-FEMI-01 (Femi, round 1): a [SyncEngine] that only ever
  // existed in isolated component tests was not a delivered capability —
  // the live app must actually construct one and attach it to the real
  // connectivity signal, or reconnects never attempt synchronization at
  // all. [syncEngine] is injectable (a fake transport in tests,
  // [HttpSyncTransport] by default in the real app) so this remains
  // testable without a real network dependency, the same pattern
  // [database]/[connectivity] already use.
  //
  // BIO-OPS-004 (SY-OPS004-01, Seyi, High): the exact same gap existed for
  // BIO-ACC-003's own [AccessCacheSyncClient] — it only ever existed in
  // isolated component tests too. [accessCacheSyncClient] is injectable for
  // the identical reason.
  SessionController({
    required AppDatabase database,
    required ConnectivitySignal connectivity,
    SyncEngine? syncEngine,
    AccessCacheSyncClient? accessCacheSyncClient,
  })  : _database = database,
        _connectivity = connectivity,
        _syncEngine = syncEngine ??
            SyncEngine(
              database: database,
              transport: HttpSyncTransport(baseUrl: defaultSyncBaseUrl),
            ),
        _accessCacheSyncClient =
            accessCacheSyncClient ?? AccessCacheSyncClient(baseUrl: defaultAccessCacheBaseUrl);

  final AppDatabase _database;
  final ConnectivitySignal _connectivity;
  final SyncEngine _syncEngine;
  final AccessCacheSyncClient _accessCacheSyncClient;

  // BIO-OPS-004 (SY-OPS004-01): mirrors SyncEngine.runSyncPass's own
  // single-flight guard (BIO-MOB-003-AC05) — only one pull/push round ever
  // runs at a time.
  bool _accessCacheSyncInFlight = false;
  // BIO-OPS-004-ZA-01 (Zainab, High): an overlapping signal sets this rather
  // than being dropped — see _syncAccessCache's own header.
  bool _accessCacheSyncRerunRequested = false;
  StreamSubscription<ConnectivityState>? _accessCacheSyncSubscription;

  bool _ready = false;
  TenantContext? _tenantContext;
  String? _deviceId;
  ConnectivityState _connectivityState = ConnectivityState.unknown;
  int _pendingMutationCount = 0;

  bool get ready => _ready;

  /// BIO-MOB-005-AC05: the enrolment screen (and any future feature screen
  /// needing direct local-store access) reads through this getter rather
  /// than each constructing/holding its own `AppDatabase` instance - one
  /// open connection for the whole app, the same single-source-of-truth
  /// this class's own header already establishes for tenant/connectivity
  /// state.
  AppDatabase get database => _database;

  TenantContext? get tenantContext => _tenantContext;
  String? get deviceId => _deviceId;
  ConnectivityState get connectivityState => _connectivityState;
  int get pendingMutationCount => _pendingMutationCount;

  /// AC03: navigation must not proceed until an explicit tenant is bound.
  bool get hasBoundTenant => _tenantContext != null;

  /// Boots the local store, restores any previously bound tenant, and starts
  /// watching connectivity. Must complete before the router makes its first
  /// redirect decision.
  Future<void> bootstrap() async {
    await _database.open();
    _deviceId = await _database.deviceId();
    // BIO-MOB-001-QA-01/QA-03: a persisted record that does not satisfy
    // every tenant-context invariant, or lacks verified resolution
    // provenance (loadTrustedTenantContext already invalidates that case
    // itself and returns null), must fail closed to "no tenant bound" — not
    // crash the app on every launch, not be silently accepted by shape
    // alone. Either way the bad record is cleared so the app self-heals
    // instead of getting stuck.
    try {
      _tenantContext = await _database.loadTrustedTenantContext();
    } on TenantBindingError {
      await _database.clearTenantContext();
      _tenantContext = null;
    }
    // BIO-MOB-003-FEMI-02 (Femi, round 2): both listeners MUST subscribe to
    // `onChange` before `start()` is called, not after. `start()` fires the
    // signal's very first reading (ADR-none, but `ConnectivitySignal`'s own
    // contract: "the instant before the first real reading arrives" is
    // never assumed `online`) through the same broadcast stream `onChange`
    // exposes — a broadcast stream does not replay a missed event to a
    // listener that subscribes after it fired. Subscribing `attachTo` (and
    // the connectivity-state tracker below) only after `start()` meant an
    // app that boots already online, with mutations already queued from a
    // previous offline session, silently missed its very first sync
    // opportunity and would not sync again until some *later* transition —
    // which might not happen for a long time on a device that stays
    // connected. Reordered: both listeners are live before `start()` ever
    // emits anything, so the first reading reaches both, exactly like every
    // later one does.
    _connectivity.onChange.listen((state) {
      _connectivityState = state;
      notifyListeners();
    });
    // BIO-MOB-003-AC05: a real reconnect now actually attempts to drain the
    // offline queue — SyncEngine's own single-flight guard (and
    // AppDatabase.markUploading's claim guard beneath it) makes this safe
    // against a flapping connectivity signal.
    _syncEngine.attachTo(_connectivity);
    // BIO-OPS-004 (SY-OPS004-01): the equivalent reconnect trigger for
    // BIO-ACC-003's access-cache sync. A separate listener (not folded into
    // SyncEngine.attachTo) because this pulls/pushes a distinct store
    // through a distinct client with its own credential namespace — see
    // AccessCacheSyncClient's own header. Only runs once a tenant is bound
    // (AC03): there is nothing to pull/push for the trusted-tenant-scoped
    // routes before that, and today no production path binds one at all
    // (see this class's own header).
    _accessCacheSyncSubscription = _connectivity.onChange.listen((state) {
      if (state == ConnectivityState.online) {
        unawaited(_syncAccessCache());
      }
    });
    await _connectivity.start();
    _connectivityState = _connectivity.current;
    await _refreshPendingCount();
    _ready = true;
    notifyListeners();
  }

  @override
  void dispose() {
    _syncEngine.dispose();
    _accessCacheSyncSubscription?.cancel();
    super.dispose();
  }

  /// Clears any persisted tenant context. Defensive/forward-looking: today
  /// nothing in this app can bind one in the first place, but a future
  /// resolution boundary's sign-out flow will need exactly this.
  Future<void> signOut() async {
    await _database.clearTenantContext();
    _tenantContext = null;
    notifyListeners();
  }

  Future<void> refreshPendingMutationCount() => _refreshPendingCount();

  Future<void> _refreshPendingCount() async {
    // Local queue depth surfaced in the offline UI (AC04); real enqueue/sync
    // wiring for feature mutations lands in BIO-MOB-002/003.
    _pendingMutationCount = (await _database.pendingMutations()).length;
  }

  /// BIO-OPS-004 (SY-OPS004-01, Seyi, High): pulls BIO-ACC-003's revocation
  /// deltas and drains its queued presentation events on every reconnect —
  /// the live wiring `AccessCacheSyncClient`'s own component tests always
  /// assumed existed but nothing in the shipped app ever provided.
  ///
  /// Single-flight (mirrors [SyncEngine.runSyncPass]): only one pass ever
  /// runs at a time. Pull and push are each isolated in their own
  /// `try`/`catch` — one failing must not prevent the other from being
  /// attempted, the same per-operation isolation
  /// `AccessCacheSyncClient.pushPendingPresentationEvents` already applies
  /// per-event. Neither failure needs any cleanup here to "retain it for
  /// retry": both methods leave their underlying state (the revocation
  /// cursor; which events are still pending) exactly as it was on failure.
  ///
  /// BIO-OPS-004-ZA-01 (Zainab, High): an overlapping signal is *coalesced*,
  /// not dropped — [_accessCacheSyncRerunRequested] records that one more
  /// pass is owed, and the active pass loops to run it once it finishes.
  /// Without this, a signal arriving while this device's own pull/push is
  /// still in flight (e.g. a brief mid-request network drop whose recovery
  /// is the only new signal before the original request fails or times out)
  /// was silently lost: the in-flight pass still fails on its now-stale
  /// connection, clears the flag, and nothing ever reruns even though the
  /// device is online again — a stale revocation cursor and undrained
  /// queue could then persist indefinitely.
  Future<void> _syncAccessCache() async {
    if (_accessCacheSyncInFlight) {
      _accessCacheSyncRerunRequested = true;
      return;
    }
    if (_tenantContext == null) return;
    _accessCacheSyncInFlight = true;
    try {
      do {
        _accessCacheSyncRerunRequested = false;
        final tenant = _tenantContext;
        if (tenant == null) break;
        try {
          await _accessCacheSyncClient.pullRevocationDelta(_database);
        } catch (_) {
          // Retained for retry - see header.
        }
        try {
          await _accessCacheSyncClient.pushPendingPresentationEvents(_database, tenantId: tenant.tenantId);
        } catch (_) {
          // Retained for retry - see header.
        }
      } while (_accessCacheSyncRerunRequested && _connectivityState == ConnectivityState.online);
    } finally {
      _accessCacheSyncInFlight = false;
    }
  }
}
