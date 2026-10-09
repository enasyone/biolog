// BIO-MOB-001-QA-01 (Chioma, acceptance round 1, High): the restore factory
// (`fromJson` at the time, renamed `fromTrustedJson` in round 3 — see below)
// called the private constructor directly, bypassing every invariant the
// schema requires. A malformed/corrupted persisted record (wrong kind,
// non-canonical tenant id, blank binding key, empty/unknown/duplicate
// channels, malformed correlation id or timestamp) was therefore accepted
// and could bypass the tenant-selection gate. Fixed by routing it through
// one `_validated` factory that enforces the full frozen
// `tenant-context.schema.json` shape and fails closed.
//
// `_validJson` below builds a well-formed record by hand rather than via a
// `resolve()`-style factory: BIO-MOB-001-QA-02 (acceptance round 2) removed
// that factory entirely, since it let raw caller-supplied fields mint an
// apparently-resolved context (see bio_mob_001_ac01_app_boot_test.dart and
// tenant_context.dart's file doc comment). This file only exercises
// `fromTrustedJson`'s validation of an already-well-formed payload — a
// stand-in for what a trusted resolution boundary would deliver, not a claim
// that this app can produce one today.
//
// BIO-MOB-001-QA-03 (acceptance round 3): the factory was renamed from
// `fromJson` to `fromTrustedJson` because it validates shape only, never
// provenance — see `tenant_context.dart`'s file doc comment and
// `bio_mob_001_ac03_session_controller_test.dart`'s legacy-upgrade
// regression for the provenance check itself, which sits one layer out in
// `AppDatabase`.
import 'package:biolog_mobile/core/identifiers/canonical_id.dart';
import 'package:biolog_mobile/core/identifiers/clock.dart';
import 'package:biolog_mobile/core/tenant/tenant_context.dart';
import 'package:flutter_test/flutter_test.dart';

Map<String, Object?> _validJson() {
  return {
    'kind': 'tenant',
    'tenant_id': newCanonicalId(CanonicalIdType.tenant),
    'binding_key': 'gate-3',
    'channels': const ['principal'],
    'correlation_id': newCanonicalId(CanonicalIdType.event),
    'resolved_at': nowUtc(),
  };
}

void main() {
  test('a valid resolved tenant context round-trips through toJson/fromTrustedJson', () {
    final json = _validJson();
    final restored = TenantContext.fromTrustedJson(json);
    expect(restored.toJson(), equals(json));
  });

  test("Chioma's exact acceptance-probe payload is rejected, not accepted", () {
    // evidence/BIO-MOB-001/acceptance/persisted-tenant-validation-probe.dart
    final malformed = <String, Object?>{
      'kind': 'not-tenant',
      'tenant_id': 'attacker-controlled-tenant',
      'binding_key': ' ',
      'channels': <String>[],
      'correlation_id': 'not-an-event-id',
      'resolved_at': 'not-an-instant',
    };
    expect(
      () => TenantContext.fromTrustedJson(malformed),
      throwsA(isA<TenantBindingError>()),
    );
  });

  test('rejects a wrong kind', () {
    final json = _validJson()..['kind'] = 'not-tenant';
    expect(() => TenantContext.fromTrustedJson(json), throwsA(isA<TenantBindingError>()));
  });

  test('rejects a non-canonical tenant_id', () {
    final json = _validJson()..['tenant_id'] = 'attacker-controlled-tenant';
    expect(() => TenantContext.fromTrustedJson(json), throwsA(isA<TenantBindingError>()));
  });

  test('rejects a blank binding_key', () {
    final json = _validJson()..['binding_key'] = '   ';
    expect(() => TenantContext.fromTrustedJson(json), throwsA(isA<TenantBindingError>()));
  });

  test('rejects empty channels', () {
    final json = _validJson()..['channels'] = <String>[];
    expect(() => TenantContext.fromTrustedJson(json), throwsA(isA<TenantBindingError>()));
  });

  test('rejects an unknown channel name', () {
    final json = _validJson()..['channels'] = <String>['not-a-real-channel'];
    expect(() => TenantContext.fromTrustedJson(json), throwsA(isA<TenantBindingError>()));
  });

  test('rejects duplicate channels', () {
    final json = _validJson()..['channels'] = <String>['principal', 'principal'];
    expect(() => TenantContext.fromTrustedJson(json), throwsA(isA<TenantBindingError>()));
  });

  test('rejects a malformed correlation_id', () {
    final json = _validJson()..['correlation_id'] = 'not-an-event-id';
    expect(() => TenantContext.fromTrustedJson(json), throwsA(isA<TenantBindingError>()));
  });

  test('rejects a malformed resolved_at', () {
    final json = _validJson()..['resolved_at'] = 'not-an-instant';
    expect(() => TenantContext.fromTrustedJson(json), throwsA(isA<TenantBindingError>()));
  });

  test('rejects a missing field instead of fabricating a default', () {
    final json = _validJson()..remove('correlation_id');
    expect(() => TenantContext.fromTrustedJson(json), throwsA(isA<TenantBindingError>()));
  });
}
