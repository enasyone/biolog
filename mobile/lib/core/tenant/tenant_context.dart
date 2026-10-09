// Mobile-side mirror of `contracts/schemas/tenant-context.schema.json`
// (ADR-0002). A tenant id is a claim, never a default, and — per Decision
// 4.1 — an object merely *shaped* like a resolved context is not proof of
// resolution: "checking that an object looks like a context is not enough,
// because field shape is exactly what a forged object imitates."
//
// BIO-MOB-001-QA-02 (Chioma, acceptance round 2): this file previously had a
// `resolve()` factory that took raw operator-typed strings (a tenant id and
// a database binding key, straight from a form) and stamped the result with
// `channels: [principal]` — manufacturing "proof of resolution" from
// unverified local input with no actual resolution boundary involved. That
// is precisely the anti-pattern Decision 4.1 forbids, and ADR-0002 Decision
// 1.1 additionally never allows a mobile client to originate a `principal`
// channel claim itself (a `principal` channel is a token claim, verified by
// a real auth boundary — BIO-INT-001, not shipped yet).
//
// There is now exactly one production construction path: [fromTrustedJson],
// deserializing an already-resolved context. There is no factory that mints
// a resolved context from raw caller-supplied fields. Until a real
// resolution boundary ships, nothing in this app can produce a bound
// session (see `tenant_select_screen.dart`), by design.
//
// BIO-MOB-001-QA-03 (Chioma, acceptance round 3): removing that factory does
// not by itself revoke a context it already wrote to local storage before
// this fix shipped — schema shape alone (what [fromTrustedJson] checks) was
// never proof of resolution provenance, only of well-formedness. Provenance
// itself is tracked one layer out, by `SqlCipherAppDatabase`/`FakeAppDatabase`
// wrapping every write with a `resolution_provenance` marker and refusing
// (invalidating) any stored record that lacks it — see
// `AppDatabase.invalidateLegacyTenantContext`. [fromTrustedJson]'s contract
// is that its caller has already verified that provenance; it never claims
// to establish it itself.
library;

import '../identifiers/canonical_id.dart';
import '../identifiers/clock.dart';

/// The channel(s) that supplied and agreed on a resolved tenant. Mirrors
/// `tenant-context.schema.json#/properties/channels`. `service` and `host`
/// are backend-only resolution paths; a mobile client can only ever supply
/// `principal` (the signed-in/asserted actor's own tenant claim).
enum TenantChannel { host, principal, service }

/// Thrown when tenant binding is attempted without an explicit, well-formed
/// tenant id. There is deliberately no recovery path that invents one.
class TenantBindingError extends Error {
  TenantBindingError(this.message);

  final String message;

  @override
  String toString() => 'TenantBindingError: $message';
}

/// The resolved tenant context for the lifetime of the current device
/// session. Immutable once constructed (ADR-0002 Decision 3): to change
/// tenant, the caller must explicitly discard this instance and resolve a
/// new one — nothing here mutates in place.
class TenantContext {
  TenantContext._({
    required this.tenantId,
    required this.bindingKey,
    required this.channels,
    required this.correlationId,
    required this.resolvedAt,
  });

  /// BIO-MOB-001-QA-01 (Chioma, acceptance round 1): the single place every
  /// frozen `tenant-context.schema.json` invariant is enforced. [fromTrustedJson]
  /// — the only production factory — goes through this; there is no second,
  /// looser construction path.
  factory TenantContext._validated({
    required Object? kindValue,
    required Object? tenantIdValue,
    required Object? bindingKeyValue,
    required Object? channelsValue,
    required Object? correlationIdValue,
    required Object? resolvedAtValue,
  }) {
    if (kindValue != 'tenant') {
      throw TenantBindingError('kind must be exactly "tenant", got "$kindValue"');
    }
    if (tenantIdValue is! String || !isCanonicalId(tenantIdValue, CanonicalIdType.tenant)) {
      throw TenantBindingError(
        'tenantId must be a canonical ten_<uuidv7> identifier, got "$tenantIdValue"',
      );
    }
    if (bindingKeyValue is! String ||
        bindingKeyValue.trim().isEmpty ||
        bindingKeyValue.length > 128) {
      throw TenantBindingError(
        'bindingKey must be 1-128 characters, got "$bindingKeyValue"',
      );
    }
    if (channelsValue is! List || channelsValue.isEmpty) {
      throw TenantBindingError(
        'channels must be a non-empty list, got "$channelsValue"',
      );
    }
    final channels = <TenantChannel>[];
    for (final raw in channelsValue) {
      TenantChannel? channel;
      for (final candidate in TenantChannel.values) {
        if (candidate.name == raw) {
          channel = candidate;
          break;
        }
      }
      if (channel == null) {
        throw TenantBindingError('unknown tenant channel: "$raw"');
      }
      channels.add(channel);
    }
    if (channels.toSet().length != channels.length) {
      throw TenantBindingError('channels must not contain duplicates');
    }
    if (correlationIdValue is! String ||
        !isCanonicalId(correlationIdValue, CanonicalIdType.event)) {
      throw TenantBindingError(
        'correlationId must be a canonical evt_<uuidv7> identifier, got "$correlationIdValue"',
      );
    }
    if (resolvedAtValue is! String || !isUtcInstant(resolvedAtValue)) {
      throw TenantBindingError(
        'resolvedAt must be a canonical UTC instant, got "$resolvedAtValue"',
      );
    }
    return TenantContext._(
      tenantId: tenantIdValue,
      bindingKey: bindingKeyValue,
      channels: List.unmodifiable(channels),
      correlationId: correlationIdValue,
      resolvedAt: resolvedAtValue,
    );
  }

  final String tenantId;
  final String bindingKey;
  final List<TenantChannel> channels;
  final String correlationId;
  final String resolvedAt;

  /// Always `"tenant"` — mirrors `tenant-context.schema.json#/properties/kind`.
  String get kind => 'tenant';

  /// Wire/storage shape matching `tenant-context.schema.json` exactly.
  Map<String, Object?> toJson() => {
        'kind': kind,
        'tenant_id': tenantId,
        'binding_key': bindingKey,
        'channels': channels.map((c) => c.name).toList(growable: false),
        'correlation_id': correlationId,
        'resolved_at': resolvedAt,
      };

  /// Deserializes an already-resolved tenant context. The only production
  /// construction path (BIO-MOB-001-QA-02): there is no way to reach it from
  /// raw, caller-supplied fields. Fails closed: every field is fully
  /// re-validated (BIO-MOB-001-QA-01) — a malformed, corrupted or tampered
  /// record throws [TenantBindingError] rather than being repaired with a
  /// fabricated default or accepted as-is.
  ///
  /// **This validates shape only, not provenance** (ADR-0002 Decision 4.1:
  /// "checking that an object looks like a context is not enough"). Named
  /// `fromTrustedJson`, not `fromJson`, because of BIO-MOB-001-QA-03: the
  /// caller — [SqlCipherAppDatabase], via its `resolution_provenance`
  /// wrapper — is the one responsible for having already established that
  /// [json] came from a trusted source (persisted-and-provenance-checked
  /// local storage, or, once one exists, a real resolution boundary's
  /// response) before calling this. A schema-shape-valid record with no
  /// verified provenance (e.g. one written by the vulnerable local-input
  /// path this app shipped with before QA-02, still sitting in an upgrading
  /// device's local storage) must never reach here at all.
  factory TenantContext.fromTrustedJson(Map<String, Object?> json) {
    return TenantContext._validated(
      kindValue: json['kind'],
      tenantIdValue: json['tenant_id'],
      bindingKeyValue: json['binding_key'],
      channelsValue: json['channels'],
      correlationIdValue: json['correlation_id'],
      resolvedAtValue: json['resolved_at'],
    );
  }
}
