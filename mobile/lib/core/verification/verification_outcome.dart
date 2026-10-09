// BIO-MOB-004-AC01: one shared shape for a verification result regardless of
// which path produced it - the live server decision (`VerificationSyncClient
// .requestDecision`) or the offline cache fallback
// (`AccessCacheService.checkPresentation`, BIO-ACC-003). Deliberately carries
// no raw biometric match score or per-modality comparison outcome (the
// server's own sanitised response has none to carry in the first place;
// the offline cache path never had one at all) - "shows identity/status/
// decision without exposing unnecessary biometric internals" is true by
// construction, not by UI discipline alone.
library;

import '../access/presentation_event.dart';

enum VerificationDecision {
  allow,
  deny;

  static VerificationDecision fromWire(String value) => switch (value.toLowerCase()) {
        'allow' => VerificationDecision.allow,
        'deny' => VerificationDecision.deny,
        _ => throw ArgumentError('unrecognised verification decision: $value'),
      };
}

/// Which path produced this outcome - surfaced in the UI (AC02: offline
/// state must be visible, not just inferable from the absence of a
/// network call).
enum VerificationSource { online, offlineCache }

class VerificationOutcome {
  const VerificationOutcome({
    required this.eventId,
    required this.personId,
    required this.decision,
    required this.reasonCode,
    required this.occurredAt,
    required this.source,
    this.presentedAssuranceLevel,
    this.stale = false,
  });

  final String eventId;
  final String personId;
  final VerificationDecision decision;
  final String reasonCode;
  final String occurredAt;
  final VerificationSource source;

  /// Only populated for [VerificationSource.online] - the offline cache
  /// path never derives an assurance level (BIO-ACC-003's own revocation-
  /// only check has no biometric evidence to derive one from).
  final String? presentedAssuranceLevel;

  /// Only meaningful for [VerificationSource.offlineCache] - the local
  /// revocation cache had not synced recently enough to be current. Never
  /// flips the decision itself (`stale_cache_policy.dart`'s own rule).
  final bool stale;

  factory VerificationOutcome.fromPresentationEvent(PresentationEvent event) => VerificationOutcome(
        eventId: event.eventId,
        personId: event.personId,
        decision: event.outcome == PresentationOutcome.allowed ? VerificationDecision.allow : VerificationDecision.deny,
        reasonCode: event.reasonCode,
        occurredAt: event.occurredAt,
        source: VerificationSource.offlineCache,
        stale: event.stale,
      );

  factory VerificationOutcome.fromJson(Map<String, Object?> json) => VerificationOutcome(
        eventId: json['event_id'] as String,
        personId: json['person_id'] as String,
        decision: VerificationDecision.fromWire(json['decision'] as String),
        reasonCode: json['reason_code'] as String,
        occurredAt: json['occurred_at'] as String,
        source: VerificationSource.online,
        presentedAssuranceLevel: json['presented_assurance_level'] as String?,
      );
}
