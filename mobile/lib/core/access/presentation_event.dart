// BIO-ACC-003-AC03/AC05: a single card-presentation decision this device
// made against its own locally cached deny-list, logged durably and locally
// (AC03) regardless of connectivity, then uploaded at most once (AC05) once
// connectivity returns.
library;

enum PresentationOutcome {
  allowed,
  denied;

  static PresentationOutcome fromWire(String value) => switch (value) {
        'ALLOWED' => PresentationOutcome.allowed,
        'DENIED' => PresentationOutcome.denied,
        _ => throw ArgumentError('unrecognised presentation outcome: $value'),
      };

  String toWire() => switch (this) {
        PresentationOutcome.allowed => 'ALLOWED',
        PresentationOutcome.denied => 'DENIED',
      };
}

const String reasonDeniedRevokedCached = 'DENIED_REVOKED_CACHED';
const String reasonAllowedNotRevoked = 'ALLOWED_NOT_REVOKED';

/// A presentation event as this device's own local log records it.
/// [tenantId]/[deviceId] are deliberately NOT stored per-row (AC01, minimum
/// necessary locally cached/logged data) — this device already knows both
/// ambiently (`TenantContext`, `AppDatabase.deviceId()`); the sync client
/// attaches them only at upload time, from those same sources, never a
/// second stored copy that could drift.
class PresentationEvent {
  const PresentationEvent({
    required this.eventId,
    required this.personId,
    required this.outcome,
    required this.reasonCode,
    required this.occurredAt,
    required this.cacheSnapshotAt,
    required this.stale,
    this.synced = false,
  });

  final String eventId;
  final String personId;
  final PresentationOutcome outcome;
  final String reasonCode;
  final String occurredAt;

  /// The revocation cache's own `last_synced_at` at the moment this decision
  /// was made — `null` if the cache had never synced at all. Distinct from
  /// [stale]: this is the raw timestamp evidence; [stale] is that timestamp
  /// already evaluated against [staleAfter] (`stale_cache_policy.dart`).
  final String? cacheSnapshotAt;

  final bool stale;
  final bool synced;

  PresentationEvent copyWith({bool? synced}) => PresentationEvent(
        eventId: eventId,
        personId: personId,
        outcome: outcome,
        reasonCode: reasonCode,
        occurredAt: occurredAt,
        cacheSnapshotAt: cacheSnapshotAt,
        stale: stale,
        synced: synced ?? this.synced,
      );

  /// The upload envelope `POST /api/v1/access-cache/presentation-events`
  /// expects (`PresentationEventIntakeRequest` in
  /// `api/features/bio-acc-003/Api/AccessCacheDtos.cs`) — [tenantId]/
  /// [deviceId] are supplied by the caller (the sync client), read from this
  /// device's own ambient tenant context/device id, never stored on this
  /// event itself.
  Map<String, Object?> toIntakeJson({required String tenantId, required String deviceId}) => {
        'event_id': eventId,
        'tenant_id': tenantId,
        'device_id': deviceId,
        'person_id': personId,
        'outcome': outcome.toWire(),
        'reason_code': reasonCode,
        'occurred_at': occurredAt,
        'cache_snapshot_at': cacheSnapshotAt,
        'stale': stale,
      };
}
