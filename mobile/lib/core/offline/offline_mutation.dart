// Mobile-side mirror of `contracts/schemas/offline-mutation.schema.json`
// (ADR-0001 Decisions 3.4/3.5). Every mutation queued while offline is
// modelled and stored in this exact shape so a later sync story can upload
// it without a translation layer.
//
// BIO-MOB-002: `retryCount`/`nextRetryAt` are local-only bookkeeping for the
// bounded exponential retry policy (AC03, `retry_policy.dart`) — not part of
// the frozen wire envelope (`toEnvelopeJson()`), same treatment as
// `payloadJson`. Still no upload/intake path here: this story owns the
// local queue state machine (retry scheduling, conflict marking per
// ADR-0001 §3.5) that a future networking story drives.
library;

import '../identifiers/canonical_id.dart';
import '../identifiers/clock.dart';

/// Sentinel distinguishing "argument not passed" from "explicitly passed
/// null" in [OfflineMutation.copyWith], so `nextRetryAt` can be cleared back
/// to null (a retry succeeded, or none is scheduled) rather than only ever
/// set to a new instant.
const Object _unset = Object();

/// Mirrors `offline-mutation.schema.json#/properties/sync_state`.
enum OfflineSyncState { pending, uploading, acknowledged, synced, conflict, failed }

/// BIO-MOB-003-AC01: what kind of delta this mutation represents. Local-only
/// bookkeeping — same treatment as [OfflineMutation.retryCount] — **not**
/// part of the frozen `offline-mutation.schema.json` envelope, which has no
/// `operation` property and cannot gain one without an amending ADR approved
/// by Kemi (ADR-0001 "Obligations this places on every later story"). It
/// drives two purely client-side decisions this story owns: which delta kind
/// a locally-queued mutation represents (AC01), and revocation-delta
/// priority ordering ahead of other pending mutations (ADR-0001 §3.5,
/// "Revocation deltas ... take a priority path" — the exact gap BIO-MOB-002
/// flagged as deferred because no story yet defined this shape). Whether/how
/// a real wire contract eventually carries this is for whatever story ships
/// the real offline-intake endpoint to decide.
enum OfflineMutationOperation { addition, update, revocation }

String operationToWire(OfflineMutationOperation operation) => switch (operation) {
      OfflineMutationOperation.addition => 'ADDITION',
      OfflineMutationOperation.update => 'UPDATE',
      OfflineMutationOperation.revocation => 'REVOCATION',
    };

OfflineMutationOperation operationFromWire(String value) => switch (value) {
      'ADDITION' => OfflineMutationOperation.addition,
      'UPDATE' => OfflineMutationOperation.update,
      'REVOCATION' => OfflineMutationOperation.revocation,
      _ => throw ArgumentError('unknown offline mutation operation: $value'),
    };

String syncStateToWire(OfflineSyncState state) => switch (state) {
      OfflineSyncState.pending => 'PENDING',
      OfflineSyncState.uploading => 'UPLOADING',
      OfflineSyncState.acknowledged => 'ACKNOWLEDGED',
      OfflineSyncState.synced => 'SYNCED',
      OfflineSyncState.conflict => 'CONFLICT',
      OfflineSyncState.failed => 'FAILED',
    };

OfflineSyncState syncStateFromWire(String value) => switch (value) {
      'PENDING' => OfflineSyncState.pending,
      'UPLOADING' => OfflineSyncState.uploading,
      'ACKNOWLEDGED' => OfflineSyncState.acknowledged,
      'SYNCED' => OfflineSyncState.synced,
      'CONFLICT' => OfflineSyncState.conflict,
      'FAILED' => OfflineSyncState.failed,
      _ => throw ArgumentError('unknown offline mutation sync_state: $value'),
    };

/// A mutation queued locally while offline (or eagerly, before every write is
/// confirmed synced). Field names and shapes match the frozen schema exactly.
class OfflineMutation {
  OfflineMutation({
    required this.mutationId,
    required this.tenantId,
    required this.deviceId,
    required this.aggregateId,
    required this.baseVersion,
    required this.idempotencyKey,
    required this.occurredAt,
    required this.syncState,
    required this.payloadJson,
    this.retryCount = 0,
    this.nextRetryAt,
    this.operation = OfflineMutationOperation.addition,
  }) {
    if (!isCanonicalId(mutationId, CanonicalIdType.event)) {
      throw ArgumentError('mutationId must be a canonical evt_<uuidv7> id');
    }
    if (!isCanonicalId(tenantId, CanonicalIdType.tenant)) {
      throw ArgumentError('tenantId must be a canonical ten_<uuidv7> id');
    }
    if (!isCanonicalId(deviceId, CanonicalIdType.device)) {
      throw ArgumentError('deviceId must be a canonical dev_<uuidv7> id');
    }
    if (baseVersion < 1) {
      throw ArgumentError('baseVersion must be >= 1');
    }
    if (idempotencyKey.isEmpty || idempotencyKey.length > 255) {
      throw ArgumentError('idempotencyKey must be 1-255 characters');
    }
    if (!isUtcInstant(occurredAt)) {
      throw ArgumentError('occurredAt must be a canonical UTC instant');
    }
    if (retryCount < 0) {
      throw ArgumentError('retryCount must be >= 0');
    }
    if (nextRetryAt != null && !isUtcInstant(nextRetryAt!)) {
      throw ArgumentError('nextRetryAt must be a canonical UTC instant');
    }
  }

  /// Compose a new PENDING mutation ready to be queued. [aggregateId] is the
  /// canonical id of the record being mutated; it is opaque to this model
  /// (whatever domain the mutation belongs to owns its own id type).
  factory OfflineMutation.compose({
    required String tenantId,
    required String deviceId,
    required String aggregateId,
    required int baseVersion,
    required String idempotencyKey,
    required Map<String, Object?> payloadJson,
    OfflineMutationOperation operation = OfflineMutationOperation.addition,
  }) {
    return OfflineMutation(
      mutationId: newCanonicalId(CanonicalIdType.event),
      tenantId: tenantId,
      deviceId: deviceId,
      aggregateId: aggregateId,
      baseVersion: baseVersion,
      idempotencyKey: idempotencyKey,
      occurredAt: nowUtc(),
      syncState: OfflineSyncState.pending,
      payloadJson: payloadJson,
      operation: operation,
    );
  }

  final String mutationId;
  final String tenantId;
  final String deviceId;
  final String aggregateId;
  final int baseVersion;
  final String idempotencyKey;
  final String occurredAt;
  final OfflineSyncState syncState;

  /// Domain payload for the mutation. Not part of the frozen envelope schema
  /// (which only governs the envelope fields above); stored alongside it so
  /// the queue is self-contained.
  final Map<String, Object?> payloadJson;

  /// Number of upload attempts that have already failed for this mutation
  /// (BIO-MOB-002-AC03). Local-only; not part of the frozen envelope.
  final int retryCount;

  /// UTC instant before which this mutation should not be attempted again
  /// (BIO-MOB-002-AC03, `RetryPolicy`). `null` means due immediately — either
  /// never attempted, or no backoff currently scheduled. Local-only; not
  /// part of the frozen envelope.
  final String? nextRetryAt;

  /// BIO-MOB-003-AC01: which kind of delta this mutation represents. See the
  /// [OfflineMutationOperation] doc comment — local-only, not part of the
  /// frozen envelope.
  final OfflineMutationOperation operation;

  OfflineMutation copyWith({
    OfflineSyncState? syncState,
    int? retryCount,
    Object? nextRetryAt = _unset,
  }) =>
      OfflineMutation(
        mutationId: mutationId,
        tenantId: tenantId,
        deviceId: deviceId,
        aggregateId: aggregateId,
        baseVersion: baseVersion,
        idempotencyKey: idempotencyKey,
        occurredAt: occurredAt,
        syncState: syncState ?? this.syncState,
        payloadJson: payloadJson,
        retryCount: retryCount ?? this.retryCount,
        nextRetryAt: identical(nextRetryAt, _unset) ? this.nextRetryAt : nextRetryAt as String?,
        operation: operation,
      );

  /// Wire/storage envelope shape matching `offline-mutation.schema.json`
  /// exactly (`payload` is a local-only extension, not part of that schema).
  Map<String, Object?> toEnvelopeJson() => {
        'mutation_id': mutationId,
        'tenant_id': tenantId,
        'device_id': deviceId,
        'aggregate_id': aggregateId,
        'base_version': baseVersion,
        'idempotency_key': idempotencyKey,
        'occurred_at': occurredAt,
        'sync_state': syncStateToWire(syncState),
      };

  /// BIO-MOB-003-AC01: the delta a [SyncTransport] uploads — the frozen
  /// envelope fields plus [operation] and the domain [payloadJson], neither
  /// of which [toEnvelopeJson] carries. Distinct method, not an extension of
  /// [toEnvelopeJson], precisely because that method's shape is frozen and
  /// also doubles as this row's local SQL storage projection
  /// ([SqlCipherAppDatabase.enqueueMutation]) — conflating the two would
  /// risk a future edit to one silently changing the other.
  Map<String, Object?> toSyncDeltaJson() => {
        ...toEnvelopeJson(),
        'operation': operationToWire(operation),
        'payload': payloadJson,
      };
}
