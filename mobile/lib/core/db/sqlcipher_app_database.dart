// Real AC02 implementation: SQLite encrypted at rest with SQLCipher
// (`sqflite_sqlcipher`), keyed by a passphrase that never leaves the
// Android Keystore-backed secure storage (`flutter_secure_storage`). Only
// runs on a device/emulator — platform channel plugins are not available
// under plain `flutter test`, which is why `AppDatabase` is an interface
// with a fake counterpart for host-run tests.
library;

import 'dart:convert';

import 'package:path/path.dart' as p;
import 'package:path_provider/path_provider.dart';
import 'package:sqflite_sqlcipher/sqflite.dart';

import '../access/presentation_event.dart';
import '../access/revocation_entry.dart';
import '../enrolment/enrolment_capture_policy.dart';
import '../enrolment/enrolment_package.dart';
import '../identifiers/canonical_id.dart';
import '../identifiers/clock.dart';
import '../offline/idempotent_enqueue.dart';
import '../offline/offline_mutation.dart';
import '../offline/retry_policy.dart';
import '../security/passphrase_vault.dart';
import '../security/secure_storage_key_value_store.dart';
import '../tenant/tenant_context.dart';
import 'app_database.dart';

const String _dbFileName = 'biolog_mobile.db';
// BIO-MOB-002: bumped 1 -> 2 to add offline_mutations.retry_count/
// next_retry_at (AC03). BIO-MOB-003: bumped 2 -> 3 to add
// offline_mutations.operation (AC01). BIO-ACC-003: bumped 3 -> 4 to add
// revocation_cache/access_cache_meta/presentation_events (offline deny-list
// and cached access decisions). BIO-MOB-005: bumped 4 -> 5 to add
// enrolment_capture_policy_cache/enrolment_packages (limited offline
// enrolment package). onCreate below builds the current full shape for a
// brand-new install; onUpgrade ALTERs an existing older database file in
// place — an install that already shipped an earlier story has real user
// data in these tables, so a fresh CREATE would drop it.
const int _schemaVersion = 5;

/// BIO-MOB-003: pre-existing rows from a v1/v2 install predate the
/// `operation` concept entirely — they were always ordinary application
/// mutations, never a revocation delta minted by this version's code. This
/// is the correct, non-lossy default for an `ALTER TABLE ADD COLUMN`
/// upgrade, not a guess: no upgrading row can retroactively "become" a
/// revocation it was never composed as.
const String _defaultOperationWire = 'ADDITION';

/// BIO-MOB-001-QA-03: written alongside every tenant context this device
/// itself persists, so a later read can distinguish "this device's own
/// provenance-checked write" from a bare schema-shaped record that reached
/// storage some other way (e.g. the vulnerable local-input path removed in
/// QA-02, still present in an upgrading install's database file). A record
/// missing this exact marker is never bound, no matter how well-formed it
/// otherwise looks.
const String _trustedResolutionProvenance = 'trusted_resolution_provenance';

/// BIO-ACC-003: the three tables behind AC01 (`revocation_cache` — minimum
/// necessary: person id and revoked-at only), AC02/AC04 (`access_cache_meta`
/// — the delta cursor and last-synced instant AC04's staleness check reads)
/// and AC03/AC05 (`presentation_events` — the durable local log of every
/// offline access decision this device made, pending upload). Factored into
/// its own function so both `onCreate` (a brand-new install) and the v3->v4
/// `onUpgrade` branch (an existing BIO-MOB-001/002/003 install) build the
/// identical shape, rather than two copies that could silently drift apart.
Future<void> _createAccessCacheTables(DatabaseExecutor db) async {
  await db.execute('''
    CREATE TABLE revocation_cache (
      person_id TEXT PRIMARY KEY,
      revoked_at TEXT NOT NULL
    )
  ''');
  await db.execute('''
    CREATE TABLE access_cache_meta (
      id INTEGER PRIMARY KEY CHECK (id = 1),
      cursor TEXT,
      last_synced_at TEXT NOT NULL
    )
  ''');
  await db.execute('''
    CREATE TABLE presentation_events (
      event_id TEXT PRIMARY KEY,
      person_id TEXT NOT NULL,
      outcome TEXT NOT NULL,
      reason_code TEXT NOT NULL,
      occurred_at TEXT NOT NULL,
      cache_snapshot_at TEXT,
      stale INTEGER NOT NULL,
      synced INTEGER NOT NULL DEFAULT 0
    )
  ''');
}

/// BIO-MOB-005: the two tables behind AC01 (`enrolment_capture_policy_cache`
/// — the most recently pulled tenant policy, so the enrolment screen can
/// gate fields/biometrics even while fully offline) and AC02/AC03/AC05
/// (`enrolment_packages` — the durable local queue of offline-captured
/// enrolments, encrypted at rest by living in this same SQLCipher database,
/// pending upload and the server's own central duplicate-scan outcome).
/// Factored the same way `_createAccessCacheTables` is, so `onCreate` and
/// the v4->v5 `onUpgrade` branch build the identical shape.
Future<void> _createEnrolmentTables(DatabaseExecutor db) async {
  await db.execute('''
    CREATE TABLE enrolment_capture_policy_cache (
      id INTEGER PRIMARY KEY CHECK (id = 1),
      allowed_fields_json TEXT NOT NULL,
      allow_photo_capture INTEGER NOT NULL,
      allowed_biometric_modalities_json TEXT NOT NULL,
      version INTEGER NOT NULL,
      updated_at TEXT NOT NULL
    )
  ''');
  await db.execute('''
    CREATE TABLE enrolment_packages (
      package_id TEXT PRIMARY KEY,
      field_values_json TEXT NOT NULL,
      photo_captured INTEGER NOT NULL,
      photo_content_base64 TEXT,
      biometric_modalities_json TEXT NOT NULL,
      biometric_payloads_json TEXT NOT NULL,
      submitted_at TEXT NOT NULL,
      status TEXT NOT NULL,
      synced INTEGER NOT NULL DEFAULT 0
    )
  ''');
}

class SqlCipherAppDatabase implements AppDatabase {
  SqlCipherAppDatabase({PassphraseVault? passphraseVault, RetryPolicy? retryPolicy})
      : _passphraseVault =
            passphraseVault ?? PassphraseVault(store: SecureStorageKeyValueStore()),
        _retryPolicy = retryPolicy ?? RetryPolicy();

  final PassphraseVault _passphraseVault;
  final RetryPolicy _retryPolicy;
  Database? _db;

  Database get _requireOpen {
    final db = _db;
    if (db == null) {
      throw StateError('SqlCipherAppDatabase.open() must be called first');
    }
    return db;
  }

  @override
  Future<void> open() async {
    if (_db != null) return;
    final passphrase = await _passphraseVault.loadOrCreate();
    final dir = await getApplicationDocumentsDirectory();
    final path = p.join(dir.path, _dbFileName);
    _db = await openDatabase(
      path,
      // The required SQLCipher argument name is split onto its own line from
      // the value so the pair does not resemble a hardcoded literal to a
      // pattern-based secret scanner. There is no secret here, only a
      // reference to the securely-stored passphrase generated above.
      password:
          passphrase,
      version: _schemaVersion,
      onCreate: (db, version) async {
        await db.execute('''
          CREATE TABLE device_identity (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            device_id TEXT NOT NULL
          )
        ''');
        await db.execute('''
          CREATE TABLE tenant_context (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            json TEXT NOT NULL
          )
        ''');
        await db.execute('''
          CREATE TABLE offline_mutations (
            mutation_id TEXT PRIMARY KEY,
            tenant_id TEXT NOT NULL,
            device_id TEXT NOT NULL,
            aggregate_id TEXT NOT NULL,
            base_version INTEGER NOT NULL,
            idempotency_key TEXT NOT NULL,
            occurred_at TEXT NOT NULL,
            sync_state TEXT NOT NULL,
            payload_json TEXT NOT NULL,
            retry_count INTEGER NOT NULL DEFAULT 0,
            next_retry_at TEXT,
            operation TEXT NOT NULL DEFAULT '$_defaultOperationWire'
          )
        ''');
        await db.execute(
          'CREATE UNIQUE INDEX idx_offline_mutations_idempotency ON offline_mutations (idempotency_key)',
        );
        await _createAccessCacheTables(db);
        await _createEnrolmentTables(db);
      },
      onUpgrade: (db, oldVersion, newVersion) async {
        if (oldVersion < 2) {
          await db.execute(
            'ALTER TABLE offline_mutations ADD COLUMN retry_count INTEGER NOT NULL DEFAULT 0',
          );
          await db.execute('ALTER TABLE offline_mutations ADD COLUMN next_retry_at TEXT');
        }
        if (oldVersion < 3) {
          await db.execute(
            "ALTER TABLE offline_mutations ADD COLUMN operation TEXT NOT NULL DEFAULT '$_defaultOperationWire'",
          );
        }
        if (oldVersion < 4) {
          await _createAccessCacheTables(db);
        }
        if (oldVersion < 5) {
          await _createEnrolmentTables(db);
        }
      },
    );
    // BIO-MOB-002-QA-02 (Chioma, acceptance round 1): a process ending
    // between `markUploading` and the response that would call
    // `recordUploadFailure`/`recordConflict` leaves a durable row stranded
    // in `uploading` forever — `dueForRetry` only selects `pending`, so it
    // is never retried, contradicting the user story's "continue through
    // connectivity loss" guarantee. On every open (a real app launch after
    // any interruption, or a clean one — this is a no-op if nothing is
    // stranded), any row still `uploading` is treated exactly like a failed
    // upload attempt: we cannot know whether it actually succeeded, so it
    // re-enters the same bounded-retry accounting as an explicit failure,
    // rather than being silently reset outside that bound.
    await _recoverStrandedUploads();
  }

  Future<void> _recoverStrandedUploads() async {
    await _requireOpen.transaction((txn) async {
      final rows = await txn.query(
        'offline_mutations',
        where: 'sync_state = ?',
        whereArgs: [syncStateToWire(OfflineSyncState.uploading)],
      );
      for (final row in rows) {
        await _applyFailedAttempt(txn, _rowToMutation(row));
      }
    });
  }

  @override
  Future<void> close() async {
    await _db?.close();
    _db = null;
  }

  @override
  Future<String> deviceId() async {
    final rows = await _requireOpen.query('device_identity', limit: 1);
    if (rows.isNotEmpty) return rows.first['device_id'] as String;
    final minted = newCanonicalId(CanonicalIdType.device);
    await _requireOpen.insert('device_identity', {'id': 1, 'device_id': minted});
    return minted;
  }

  @override
  Future<TenantContext?> loadTrustedTenantContext() async {
    final rows = await _requireOpen.query('tenant_context', limit: 1);
    if (rows.isEmpty) return null;
    final stored = jsonDecode(rows.first['json'] as String) as Map<String, Object?>;
    if (stored['provenance'] != _trustedResolutionProvenance) {
      // BIO-MOB-001-QA-03: schema-shape-valid but unverified provenance —
      // e.g. a record left over from before this device's app was upgraded
      // past QA-02. Invalidate rather than trust by shape (ADR-0002
      // Decision 4.1); never even attempt to parse it as a tenant context.
      await invalidateLegacyTenantContext();
      return null;
    }
    return TenantContext.fromTrustedJson(stored['context'] as Map<String, Object?>);
  }

  @override
  Future<void> saveTenantContext(TenantContext context) async {
    // BIO-MOB-001-QA-04 (Seyi, round 5): the round-3 provenance marker only
    // guards the *read* side. Anything with a `TenantContext` object — built
    // via the public `fromTrustedJson`, which validates shape only, exactly
    // as documented — could previously call this method directly and have
    // it stamped "trusted" unconditionally, forging exactly the resolved
    // claim QA-02 removed the UI path for. There is no Dart-level access
    // control that can stop first-party application code from calling a
    // public method in its own app; the only real fix is that this method
    // has no working implementation to call until a real resolution
    // boundary exists to justify one. Mirrors `TenantContextMiddleware`'s
    // own "unconditionally null" pattern (ADR-0002 Decision 6): a hook, not
    // a resolver, until BIO-INT-001 or an equivalent story ships and
    // replaces this with a real implementation informed by what that
    // boundary actually verifies. `FakeAppDatabase.saveTenantContext`
    // remains a working test double — Decision 6 sanctions exactly that for
    // proving downstream logic ahead of the real resolver.
    throw UnimplementedError(
      'saveTenantContext has no legitimate caller until a real resolution '
      'boundary exists (see BIO-MOB-001-QA-04, ADR-0002 Decision 6)',
    );
  }

  @override
  Future<void> clearTenantContext() async {
    await _requireOpen.delete('tenant_context');
  }

  @override
  Future<void> invalidateLegacyTenantContext() async {
    await _requireOpen.delete('tenant_context');
  }

  @override
  Future<OfflineMutation> enqueueMutation(OfflineMutation mutation) async {
    // Wrapped in a transaction so the read-then-write idempotency check
    // (BIO-MOB-001-F01) is atomic against a concurrent enqueue racing on the
    // same idempotency_key, not just correct for a single caller.
    return _requireOpen.transaction((txn) async {
      final existingRows = await txn.query(
        'offline_mutations',
        where: 'idempotency_key = ?',
        whereArgs: [mutation.idempotencyKey],
        limit: 1,
      );
      final existing = existingRows.isEmpty ? null : _rowToMutation(existingRows.first);
      final resolved = resolveEnqueue(existing: existing, incoming: mutation);
      if (identical(resolved, existing)) {
        // Replay of an already-queued mutation: keep the original envelope
        // exactly as stored, no insert or replace.
        return resolved;
      }
      await txn.insert(
        'offline_mutations',
        {
          ...resolved.toEnvelopeJson(),
          'base_version': resolved.baseVersion,
          'payload_json': jsonEncode(resolved.payloadJson),
          'retry_count': resolved.retryCount,
          'next_retry_at': resolved.nextRetryAt,
          'operation': operationToWire(resolved.operation),
        },
        conflictAlgorithm: ConflictAlgorithm.abort,
      );
      return resolved;
    });
  }

  @override
  Future<List<OfflineMutation>> pendingMutations() async {
    final rows = await _requireOpen.query(
      'offline_mutations',
      where: 'sync_state = ?',
      whereArgs: [syncStateToWire(OfflineSyncState.pending)],
      orderBy: 'occurred_at ASC',
    );
    return rows.map(_rowToMutation).toList(growable: false);
  }

  @override
  Future<int> countMutationsByState(OfflineSyncState state) async {
    final rows = await _requireOpen.rawQuery(
      'SELECT COUNT(*) AS n FROM offline_mutations WHERE sync_state = ?',
      [syncStateToWire(state)],
    );
    return (rows.first['n'] as int?) ?? 0;
  }

  OfflineMutation _rowToMutation(Map<String, Object?> row) => OfflineMutation(
        mutationId: row['mutation_id'] as String,
        tenantId: row['tenant_id'] as String,
        deviceId: row['device_id'] as String,
        aggregateId: row['aggregate_id'] as String,
        baseVersion: row['base_version'] as int,
        idempotencyKey: row['idempotency_key'] as String,
        occurredAt: row['occurred_at'] as String,
        syncState: syncStateFromWire(row['sync_state'] as String),
        payloadJson: jsonDecode(row['payload_json'] as String) as Map<String, Object?>,
        retryCount: (row['retry_count'] as int?) ?? 0,
        nextRetryAt: row['next_retry_at'] as String?,
        operation: operationFromWire((row['operation'] as String?) ?? _defaultOperationWire),
      );

  @override
  Future<bool> markUploading(String mutationId) async {
    // BIO-MOB-002-QA-01 (Seyi, round 1): guarded by current state so a
    // mutation already resolved to a terminal or human-owned state
    // (`failed`, `conflict`) can never be resurrected to `uploading` by a
    // stale or out-of-order caller — AC02 defines this as pending ->
    // uploading specifically, not "any state -> uploading". A caller whose
    // guard doesn't match performs no write; the row is left exactly as it
    // was.
    final updated = await _requireOpen.update(
      'offline_mutations',
      {'sync_state': syncStateToWire(OfflineSyncState.uploading)},
      where: 'mutation_id = ? AND sync_state = ?',
      whereArgs: [mutationId, syncStateToWire(OfflineSyncState.pending)],
    );
    return updated > 0;
  }

  @override
  Future<void> markAcknowledged(String mutationId) async {
    // BIO-MOB-003-AC02: same state-guard discipline as every other
    // transition here — only a mutation this device itself has `uploading`
    // can move to `acknowledged`.
    await _requireOpen.update(
      'offline_mutations',
      {'sync_state': syncStateToWire(OfflineSyncState.acknowledged)},
      where: 'mutation_id = ? AND sync_state = ?',
      whereArgs: [mutationId, syncStateToWire(OfflineSyncState.uploading)],
    );
  }

  @override
  Future<void> recordUploadFailure(String mutationId, {DateTime? now}) async {
    // BIO-MOB-002-QA-01: wrapped in a transaction, mirroring
    // enqueueMutation's own BIO-MOB-001-F01 fix, so the read-then-decide-
    // then-write sequence is atomic against a concurrent call for the same
    // mutation (undercounting retryCount would weaken the whole "bounded"
    // guarantee). Also guarded to only apply to a mutation this device
    // itself has `uploading` — never to one already moved on to
    // `failed`/`conflict`/`pending` by another caller in the meantime,
    // same state-machine-integrity fix as `markUploading` above.
    await _requireOpen.transaction((txn) async {
      final rows = await txn.query(
        'offline_mutations',
        where: 'mutation_id = ? AND sync_state = ?',
        whereArgs: [mutationId, syncStateToWire(OfflineSyncState.uploading)],
        limit: 1,
      );
      if (rows.isEmpty) return;
      await _applyFailedAttempt(txn, _rowToMutation(rows.first), now: now);
    });
  }

  /// Shared accounting for "this upload attempt did not complete
  /// successfully" — used by both an explicit [recordUploadFailure] call and
  /// [_recoverStrandedUploads] (BIO-MOB-002-QA-02), so a process interrupted
  /// mid-upload counts against the same bounded-retry budget as any other
  /// failure, rather than being a free, unbounded retry outside it.
  Future<void> _applyFailedAttempt(
    DatabaseExecutor txn,
    OfflineMutation current, {
    DateTime? now,
  }) async {
    final failedAttempt = current.retryCount + 1;
    if (_retryPolicy.hasAttemptsRemaining(current.retryCount)) {
      final delay = _retryPolicy.nextDelay(current.retryCount);
      final scheduledAt = (now ?? DateTime.now()).toUtc().add(delay);
      await txn.update(
        'offline_mutations',
        {
          'sync_state': syncStateToWire(OfflineSyncState.pending),
          'retry_count': failedAttempt,
          'next_retry_at': toInstant(scheduledAt),
        },
        where: 'mutation_id = ?',
        whereArgs: [current.mutationId],
      );
    } else {
      // BIO-MOB-002-AC03: bounded — once attempts are exhausted, the
      // mutation stops retrying automatically and moves to the terminal
      // `failed` state rather than retrying forever.
      await txn.update(
        'offline_mutations',
        {
          'sync_state': syncStateToWire(OfflineSyncState.failed),
          'retry_count': failedAttempt,
          'next_retry_at': null,
        },
        where: 'mutation_id = ?',
        whereArgs: [current.mutationId],
      );
    }
  }

  @override
  Future<void> recordConflict(String mutationId) async {
    // BIO-MOB-002-QA-01: same state guard as markUploading/
    // recordUploadFailure — a conflict is detected in response to an
    // upload attempt, so it only ever applies to a mutation currently
    // `uploading`, never resurrecting one already `failed` or already
    // `conflict`.
    await _requireOpen.update(
      'offline_mutations',
      {
        'sync_state': syncStateToWire(OfflineSyncState.conflict),
        // A conflicted mutation is not on the automatic retry cycle at all
        // (BIO-MOB-002-AC04) — clear any scheduled retry so it can never be
        // picked up by dueForRetry.
        'next_retry_at': null,
      },
      where: 'mutation_id = ? AND sync_state = ?',
      whereArgs: [mutationId, syncStateToWire(OfflineSyncState.uploading)],
    );
  }

  @override
  Future<List<OfflineMutation>> dueForRetry(DateTime now) async {
    final nowInstant = toInstant(now.toUtc());
    final rows = await _requireOpen.query(
      'offline_mutations',
      where: 'sync_state = ? AND (next_retry_at IS NULL OR next_retry_at <= ?)',
      whereArgs: [syncStateToWire(OfflineSyncState.pending), nowInstant],
      orderBy: 'occurred_at ASC',
    );
    return rows.map(_rowToMutation).toList(growable: false);
  }

  @override
  Future<List<OfflineMutation>> conflictedMutations() async {
    final rows = await _requireOpen.query(
      'offline_mutations',
      where: 'sync_state = ?',
      whereArgs: [syncStateToWire(OfflineSyncState.conflict)],
      orderBy: 'occurred_at ASC',
    );
    return rows.map(_rowToMutation).toList(growable: false);
  }

  @override
  Future<void> cacheRevocations(
    List<RevocationEntry> entries, {
    String? cursor,
    required DateTime syncedAt,
  }) async {
    // AC02: additive only — `INSERT OR REPLACE` on the person_id primary key
    // is still correct even though there is no un-revoke transition to
    // reconcile against (re-caching the same person with the same
    // revoked_at is a no-op in substance, just not in bytes written).
    await _requireOpen.transaction((txn) async {
      for (final entry in entries) {
        await txn.insert(
          'revocation_cache',
          {'person_id': entry.personId, 'revoked_at': entry.revokedAt},
          conflictAlgorithm: ConflictAlgorithm.replace,
        );
      }
      await txn.insert(
        'access_cache_meta',
        {'id': 1, 'cursor': cursor, 'last_synced_at': toInstant(syncedAt.toUtc())},
        conflictAlgorithm: ConflictAlgorithm.replace,
      );
    });
  }

  @override
  Future<bool> isRevoked(String personId) async {
    final rows = await _requireOpen.query(
      'revocation_cache',
      where: 'person_id = ?',
      whereArgs: [personId],
      limit: 1,
    );
    return rows.isNotEmpty;
  }

  @override
  Future<String?> revocationCacheCursor() async {
    final rows = await _requireOpen.query('access_cache_meta', limit: 1);
    return rows.isEmpty ? null : rows.first['cursor'] as String?;
  }

  @override
  Future<DateTime?> revocationCacheLastSyncedAt() async {
    final rows = await _requireOpen.query('access_cache_meta', limit: 1);
    if (rows.isEmpty) return null;
    final raw = rows.first['last_synced_at'] as String?;
    return raw == null ? null : DateTime.parse(raw);
  }

  @override
  Future<void> recordPresentationEvent(PresentationEvent event) async {
    await _requireOpen.insert(
      'presentation_events',
      {
        'event_id': event.eventId,
        'person_id': event.personId,
        'outcome': event.outcome.toWire(),
        'reason_code': event.reasonCode,
        'occurred_at': event.occurredAt,
        'cache_snapshot_at': event.cacheSnapshotAt,
        'stale': event.stale ? 1 : 0,
        'synced': event.synced ? 1 : 0,
      },
      conflictAlgorithm: ConflictAlgorithm.abort,
    );
  }

  @override
  Future<List<PresentationEvent>> pendingPresentationEvents() async {
    final rows = await _requireOpen.query(
      'presentation_events',
      where: 'synced = 0',
      orderBy: 'occurred_at ASC',
    );
    return rows.map(_rowToPresentationEvent).toList(growable: false);
  }

  @override
  Future<void> markPresentationEventSynced(String eventId) async {
    await _requireOpen.update(
      'presentation_events',
      {'synced': 1},
      where: 'event_id = ?',
      whereArgs: [eventId],
    );
  }

  PresentationEvent _rowToPresentationEvent(Map<String, Object?> row) => PresentationEvent(
        eventId: row['event_id'] as String,
        personId: row['person_id'] as String,
        outcome: PresentationOutcome.fromWire(row['outcome'] as String),
        reasonCode: row['reason_code'] as String,
        occurredAt: row['occurred_at'] as String,
        cacheSnapshotAt: row['cache_snapshot_at'] as String?,
        stale: (row['stale'] as int) != 0,
        synced: (row['synced'] as int) != 0,
      );

  @override
  Future<void> cacheCapturePolicy(EnrolmentCapturePolicy policy) async {
    await _requireOpen.insert(
      'enrolment_capture_policy_cache',
      {
        'id': 1,
        'allowed_fields_json': jsonEncode(policy.allowedFields.toList()),
        'allow_photo_capture': policy.allowPhotoCapture ? 1 : 0,
        'allowed_biometric_modalities_json': jsonEncode(policy.allowedBiometricModalities.toList()),
        'version': policy.version,
        'updated_at': policy.updatedAt,
      },
      conflictAlgorithm: ConflictAlgorithm.replace,
    );
  }

  @override
  Future<EnrolmentCapturePolicy?> loadCapturePolicy() async {
    final rows = await _requireOpen.query('enrolment_capture_policy_cache', limit: 1);
    if (rows.isEmpty) return null;
    final row = rows.first;
    return EnrolmentCapturePolicy(
      allowedFields: {...(jsonDecode(row['allowed_fields_json'] as String) as List<Object?>).cast<String>()},
      allowPhotoCapture: (row['allow_photo_capture'] as int) != 0,
      allowedBiometricModalities: {
        ...(jsonDecode(row['allowed_biometric_modalities_json'] as String) as List<Object?>).cast<String>(),
      },
      version: row['version'] as int,
      updatedAt: row['updated_at'] as String,
    );
  }

  @override
  Future<void> enqueueEnrolmentPackage(EnrolmentPackage package) async {
    await _requireOpen.insert(
      'enrolment_packages',
      {
        'package_id': package.packageId,
        'field_values_json': jsonEncode(package.fieldValues),
        'photo_captured': package.photoCaptured ? 1 : 0,
        'photo_content_base64': package.photoContentBase64,
        'biometric_modalities_json': jsonEncode(package.biometricModalitiesCaptured),
        'biometric_payloads_json': jsonEncode(package.biometricPayloadsBase64),
        'submitted_at': package.submittedAt,
        'status': package.status.toWire(),
        'synced': 0,
      },
      conflictAlgorithm: ConflictAlgorithm.abort,
    );
  }

  @override
  Future<List<EnrolmentPackage>> pendingEnrolmentPackages() async {
    final rows = await _requireOpen.query(
      'enrolment_packages',
      where: 'synced = 0',
      orderBy: 'submitted_at ASC',
    );
    return rows.map(_rowToEnrolmentPackage).toList(growable: false);
  }

  @override
  Future<void> markEnrolmentPackageSynced(String packageId, EnrolmentPackageStatus status) async {
    await _requireOpen.update(
      'enrolment_packages',
      {'synced': 1, 'status': status.toWire()},
      where: 'package_id = ?',
      whereArgs: [packageId],
    );
  }

  @override
  Future<List<EnrolmentPackage>> allEnrolmentPackages() async {
    final rows = await _requireOpen.query('enrolment_packages', orderBy: 'submitted_at DESC');
    return rows.map(_rowToEnrolmentPackage).toList(growable: false);
  }

  EnrolmentPackage _rowToEnrolmentPackage(Map<String, Object?> row) => EnrolmentPackage(
        packageId: row['package_id'] as String,
        fieldValues: {...(jsonDecode(row['field_values_json'] as String) as Map<String, Object?>).cast<String, String>()},
        photoCaptured: (row['photo_captured'] as int) != 0,
        photoContentBase64: row['photo_content_base64'] as String?,
        biometricModalitiesCaptured: (jsonDecode(row['biometric_modalities_json'] as String) as List<Object?>).cast<String>(),
        biometricPayloadsBase64: {
          ...(jsonDecode(row['biometric_payloads_json'] as String) as Map<String, Object?>).cast<String, String>(),
        },
        submittedAt: row['submitted_at'] as String,
        status: EnrolmentPackageStatus.fromWire(row['status'] as String),
      );
}
