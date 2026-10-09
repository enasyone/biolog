// Test double for AppDatabase. sqflite_sqlcipher needs a real platform
// channel that only exists on a device/emulator, so widget/unit tests boot
// the real app against this in-memory fake instead of the real store.
library;

import 'package:biolog_mobile/core/access/presentation_event.dart';
import 'package:biolog_mobile/core/access/revocation_entry.dart';
import 'package:biolog_mobile/core/db/app_database.dart';
import 'package:biolog_mobile/core/enrolment/enrolment_capture_policy.dart';
import 'package:biolog_mobile/core/enrolment/enrolment_package.dart';
import 'package:biolog_mobile/core/identifiers/canonical_id.dart';
import 'package:biolog_mobile/core/identifiers/clock.dart';
import 'package:biolog_mobile/core/offline/idempotent_enqueue.dart';
import 'package:biolog_mobile/core/offline/offline_mutation.dart';
import 'package:biolog_mobile/core/offline/retry_policy.dart';
import 'package:biolog_mobile/core/tenant/tenant_context.dart';

class FakeAppDatabase implements AppDatabase {
  FakeAppDatabase({String? deviceId, RetryPolicy? retryPolicy})
      : _deviceId = deviceId ?? newCanonicalId(CanonicalIdType.device),
        _retryPolicy = retryPolicy ?? RetryPolicy();

  static const String _trustedResolutionProvenance = 'trusted_resolution_provenance';

  final String _deviceId;
  final RetryPolicy _retryPolicy;
  bool opened = false;
  // Stored as the same {provenance, context} envelope the real SQLCipher
  // store writes (BIO-MOB-001-QA-03), not the TenantContext object itself,
  // so this fake round-trips through the real provenance check and
  // TenantContext.fromTrustedJson's validation instead of trivially
  // bypassing either (BIO-MOB-001-QA-01/QA-03: a fake that can't reproduce a
  // corrupted or unprovenanced record can't verify the fix for one).
  Map<String, Object?>? _storedTenantContextRow;
  final Map<String, OfflineMutation> _mutations = {};
  final Map<String, String> _revocationCache = {};
  String? _revocationCursor;
  DateTime? _revocationLastSyncedAt;
  final Map<String, PresentationEvent> _presentationEvents = {};
  EnrolmentCapturePolicy? _cachedCapturePolicy;
  final Map<String, EnrolmentPackage> _enrolmentPackages = {};

  @override
  Future<void> open() async {
    opened = true;
    // BIO-MOB-002-QA-02 (Chioma, acceptance round 1): mirrors the real
    // store's stranded-upload recovery on every open — a mutation still
    // `uploading` from before a simulated `close()`/`open()` (standing in
    // for a real process restart) is treated as a failed attempt, not left
    // permanently stuck outside `dueForRetry`'s `pending`-only selection.
    for (final current in _mutations.values.toList()) {
      if (current.syncState == OfflineSyncState.uploading) {
        _applyFailedAttempt(current);
      }
    }
  }

  @override
  Future<void> close() async {
    opened = false;
  }

  @override
  Future<String> deviceId() async => _deviceId;

  @override
  Future<TenantContext?> loadTrustedTenantContext() async {
    final row = _storedTenantContextRow;
    if (row == null) return null;
    if (row['provenance'] != _trustedResolutionProvenance) {
      await invalidateLegacyTenantContext();
      return null;
    }
    return TenantContext.fromTrustedJson(row['context'] as Map<String, Object?>);
  }

  @override
  Future<void> saveTenantContext(TenantContext context) async {
    _storedTenantContextRow = {
      'provenance': _trustedResolutionProvenance,
      'context': context.toJson(),
    };
  }

  @override
  Future<void> clearTenantContext() async {
    _storedTenantContextRow = null;
  }

  @override
  Future<void> invalidateLegacyTenantContext() async {
    _storedTenantContextRow = null;
  }

  /// Test-only: injects the raw content of the `tenant_context` storage row,
  /// bypassing [saveTenantContext]'s provenance wrapping and
  /// [TenantContext]'s validated constructors entirely. Used to simulate
  /// either a corrupted/tampered record (BIO-MOB-001-QA-01) or a
  /// schema-valid-but-unprovenanced legacy record surviving an app upgrade
  /// (BIO-MOB-001-QA-03) — both are exactly "whatever raw bytes happen to be
  /// in that row", which is the point.
  void debugInjectRawTenantContextJson(Map<String, Object?> json) {
    _storedTenantContextRow = json;
  }

  @override
  Future<OfflineMutation> enqueueMutation(OfflineMutation mutation) async {
    OfflineMutation? existing;
    for (final m in _mutations.values) {
      if (m.idempotencyKey == mutation.idempotencyKey) {
        existing = m;
        break;
      }
    }
    final resolved = resolveEnqueue(existing: existing, incoming: mutation);
    _mutations[resolved.mutationId] = resolved;
    return resolved;
  }

  @override
  Future<List<OfflineMutation>> pendingMutations() async {
    return _mutations.values
        .where((m) => m.syncState == OfflineSyncState.pending)
        .toList(growable: false);
  }

  @override
  Future<int> countMutationsByState(OfflineSyncState state) async {
    return _mutations.values.where((m) => m.syncState == state).length;
  }

  @override
  Future<bool> markUploading(String mutationId) async {
    // BIO-MOB-002-QA-01: guarded by current state, mirroring the real
    // store — a mutation already `failed`/`conflict` must never be
    // resurrected to `uploading`.
    final current = _mutations[mutationId];
    if (current == null || current.syncState != OfflineSyncState.pending) return false;
    _mutations[mutationId] = current.copyWith(syncState: OfflineSyncState.uploading);
    return true;
  }

  @override
  Future<void> markAcknowledged(String mutationId) async {
    // BIO-MOB-003-AC02: mirrors the real store's guard — only a mutation
    // currently `uploading` can move to `acknowledged`.
    final current = _mutations[mutationId];
    if (current == null || current.syncState != OfflineSyncState.uploading) return;
    _mutations[mutationId] = current.copyWith(syncState: OfflineSyncState.acknowledged);
  }

  @override
  Future<void> recordUploadFailure(String mutationId, {DateTime? now}) async {
    // BIO-MOB-002-QA-01: only applies to a mutation currently `uploading` —
    // mirrors the real store's guard (single-threaded Dart makes the
    // read-then-write here inherently atomic; the guard itself is what
    // fixes the state-machine-integrity bug, not transactionality, for
    // this in-memory double).
    final current = _mutations[mutationId];
    if (current == null || current.syncState != OfflineSyncState.uploading) return;
    _applyFailedAttempt(current, now: now);
  }

  /// Shared accounting for "this upload attempt did not complete
  /// successfully" — used by both an explicit [recordUploadFailure] call
  /// and [open]'s stranded-upload recovery (BIO-MOB-002-QA-02), so a
  /// simulated restart counts against the same bounded-retry budget as any
  /// other failure, mirroring the real store.
  void _applyFailedAttempt(OfflineMutation current, {DateTime? now}) {
    final failedAttempt = current.retryCount + 1;
    if (_retryPolicy.hasAttemptsRemaining(current.retryCount)) {
      final delay = _retryPolicy.nextDelay(current.retryCount);
      final scheduledAt = (now ?? DateTime.now()).toUtc().add(delay);
      _mutations[current.mutationId] = current.copyWith(
        syncState: OfflineSyncState.pending,
        retryCount: failedAttempt,
        nextRetryAt: toInstant(scheduledAt),
      );
    } else {
      _mutations[current.mutationId] = current.copyWith(
        syncState: OfflineSyncState.failed,
        retryCount: failedAttempt,
        nextRetryAt: null,
      );
    }
  }

  @override
  Future<void> recordConflict(String mutationId) async {
    // BIO-MOB-002-QA-01: only applies to a mutation currently `uploading`,
    // mirroring the real store's guard.
    final current = _mutations[mutationId];
    if (current == null || current.syncState != OfflineSyncState.uploading) return;
    _mutations[mutationId] = current.copyWith(
      syncState: OfflineSyncState.conflict,
      nextRetryAt: null,
    );
  }

  @override
  Future<List<OfflineMutation>> dueForRetry(DateTime now) async {
    final nowInstant = toInstant(now.toUtc());
    final due = _mutations.values.where((m) {
      if (m.syncState != OfflineSyncState.pending) return false;
      final nextRetryAt = m.nextRetryAt;
      return nextRetryAt == null || nextRetryAt.compareTo(nowInstant) <= 0;
    }).toList();
    due.sort((a, b) => a.occurredAt.compareTo(b.occurredAt));
    return due;
  }

  @override
  Future<List<OfflineMutation>> conflictedMutations() async {
    final conflicted = _mutations.values
        .where((m) => m.syncState == OfflineSyncState.conflict)
        .toList();
    conflicted.sort((a, b) => a.occurredAt.compareTo(b.occurredAt));
    return conflicted;
  }

  @override
  Future<void> cacheRevocations(
    List<RevocationEntry> entries, {
    String? cursor,
    required DateTime syncedAt,
  }) async {
    for (final entry in entries) {
      _revocationCache[entry.personId] = entry.revokedAt;
    }
    _revocationCursor = cursor;
    _revocationLastSyncedAt = syncedAt;
  }

  @override
  Future<bool> isRevoked(String personId) async => _revocationCache.containsKey(personId);

  @override
  Future<String?> revocationCacheCursor() async => _revocationCursor;

  @override
  Future<DateTime?> revocationCacheLastSyncedAt() async => _revocationLastSyncedAt;

  @override
  Future<void> recordPresentationEvent(PresentationEvent event) async {
    _presentationEvents[event.eventId] = event;
  }

  @override
  Future<List<PresentationEvent>> pendingPresentationEvents() async {
    final pending = _presentationEvents.values.where((e) => !e.synced).toList();
    pending.sort((a, b) => a.occurredAt.compareTo(b.occurredAt));
    return pending;
  }

  @override
  Future<void> markPresentationEventSynced(String eventId) async {
    final current = _presentationEvents[eventId];
    if (current == null) return;
    _presentationEvents[eventId] = current.copyWith(synced: true);
  }

  @override
  Future<void> cacheCapturePolicy(EnrolmentCapturePolicy policy) async {
    _cachedCapturePolicy = policy;
  }

  @override
  Future<EnrolmentCapturePolicy?> loadCapturePolicy() async => _cachedCapturePolicy;

  @override
  Future<void> enqueueEnrolmentPackage(EnrolmentPackage package) async {
    _enrolmentPackages[package.packageId] = package;
  }

  @override
  Future<List<EnrolmentPackage>> pendingEnrolmentPackages() async {
    final pending = _enrolmentPackages.values
        .where((p) => p.status == EnrolmentPackageStatus.submitted)
        .toList();
    pending.sort((a, b) => a.submittedAt.compareTo(b.submittedAt));
    return pending;
  }

  @override
  Future<void> markEnrolmentPackageSynced(String packageId, EnrolmentPackageStatus status) async {
    final current = _enrolmentPackages[packageId];
    if (current == null) return;
    _enrolmentPackages[packageId] = current.copyWith(status: status);
  }

  @override
  Future<List<EnrolmentPackage>> allEnrolmentPackages() async {
    final all = _enrolmentPackages.values.toList();
    all.sort((a, b) => b.submittedAt.compareTo(a.submittedAt));
    return all;
  }
}
