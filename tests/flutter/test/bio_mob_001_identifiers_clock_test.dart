// BIO-MOB-001-AC02 / AC03 support: verifies the on-device identifier and
// clock helpers stay byte-for-byte compatible with the frozen shared
// contracts (`contracts/js/identifiers.mjs`, `contracts/js/clock.mjs`).
import 'package:biolog_mobile/core/identifiers/canonical_id.dart';
import 'package:biolog_mobile/core/identifiers/clock.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('canonical_id', () {
    test('newCanonicalId mints a well-formed ten_<uuidv7> id', () {
      final id = newCanonicalId(CanonicalIdType.tenant);
      expect(id, startsWith('ten_'));
      expect(isCanonicalId(id, CanonicalIdType.tenant), isTrue);
      expect(isCanonicalId(id, CanonicalIdType.device), isFalse);
    });

    test('uuidV7 embeds the supplied 48-bit millisecond timestamp', () {
      const nowMs = 1795305600000; // 2026-11-21T08:00:00.000Z
      final uuid = uuidV7(nowMs: nowMs);
      final hex = uuid.replaceAll('-', '');
      final embeddedMs = int.parse(hex.substring(0, 12), radix: 16);
      expect(embeddedMs, equals(nowMs));
    });

    test('uuidV7 pins version 7 and RFC 9562 variant bits', () {
      final uuid = uuidV7();
      final parts = uuid.split('-');
      expect(parts[2][0], equals('7'));
      expect(['8', '9', 'a', 'b'], contains(parts[3][0]));
    });

    test('isCanonicalId rejects a different prefix and a malformed uuid', () {
      final tenantId = newCanonicalId(CanonicalIdType.tenant);
      final deviceId = newCanonicalId(CanonicalIdType.device);
      expect(isCanonicalId(deviceId, CanonicalIdType.tenant), isFalse);
      expect(isCanonicalId(tenantId, CanonicalIdType.tenant), isTrue);
      expect(isCanonicalId('ten_not-a-uuid', CanonicalIdType.tenant), isFalse);
    });
  });

  group('clock', () {
    test('nowUtc matches the canonical 24-character wire instant shape', () {
      final instant = nowUtc();
      expect(instant.length, equals(24));
      expect(instant, endsWith('Z'));
      expect(isUtcInstant(instant), isTrue);
    });

    test('rejects a calendar date that does not exist', () {
      expect(isUtcInstant('2026-02-30T00:00:00.000Z'), isFalse);
    });

    test('accepts a real leap day and rejects a non-leap-century Feb 29', () {
      expect(isUtcInstant('2028-02-29T00:00:00.000Z'), isTrue);
      expect(isUtcInstant('1900-02-29T00:00:00.000Z'), isFalse);
    });

    test('rejects a naive value and a non-Z offset', () {
      expect(isUtcInstant('2026-09-20T21:05:00.000'), isFalse);
      expect(isUtcInstant('2026-09-20T21:05:00.000+01:00'), isFalse);
    });

    test('toInstant round-trips through isUtcInstant', () {
      final at = DateTime.utc(2026, 9, 20, 21, 5, 0, 7);
      expect(isUtcInstant(toInstant(at)), isTrue);
      expect(toInstant(at), equals('2026-09-20T21:05:00.007Z'));
    });
  });
}
