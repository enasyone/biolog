// Local mirror of the frozen shared contract `contracts/js/identifiers.mjs`
// (ADR-0001 Decision 1, `contracts/schemas/identifier-registry.json`,
// `contracts/schemas/canonical-id.schema.json`). Read-only reference, not a
// fork of behaviour to change independently: the algorithm below must stay
// byte-for-byte equivalent to the shared implementation. Do not add or rename
// prefixes here — that is a contract change owned by Kemi.
library;

import 'dart:math';

/// Registered canonical identifier types this mobile shell mints or reads,
/// mirroring the subset of `identifier-registry.json` it needs.
enum CanonicalIdType {
  tenant('ten'),
  device('dev'),
  event('evt'),
  person('per');

  const CanonicalIdType(this.prefix);

  final String prefix;
}

final RegExp _uuidV7Pattern = RegExp(
  r'^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$',
);

/// Mint a new canonical id `<prefix>_<uuidv7>`. Safe offline: no coordination,
/// no server round trip (mirrors `newId()` in `contracts/js/identifiers.mjs`).
String newCanonicalId(CanonicalIdType type, {int? nowMs, Random? random}) {
  return '${type.prefix}_${uuidV7(nowMs: nowMs, random: random)}';
}

/// True when [value] is a well-formed canonical id of [type].
bool isCanonicalId(String value, CanonicalIdType type) {
  final prefix = '${type.prefix}_';
  if (!value.startsWith(prefix)) return false;
  return _uuidV7Pattern.hasMatch(value.substring(prefix.length));
}

/// RFC 9562 UUIDv7: 48-bit Unix ms, version 7, variant 0b10, remainder random.
/// Mirrors `uuidV7()` in `contracts/js/identifiers.mjs` exactly.
String uuidV7({int? nowMs, Random? random}) {
  final ms = nowMs ?? DateTime.now().toUtc().millisecondsSinceEpoch;
  final rng = random ?? Random.secure();
  final bytes = List<int>.generate(16, (_) => rng.nextInt(256));

  for (var i = 0; i < 6; i += 1) {
    bytes[i] = (ms >> (8 * (5 - i))) & 0xff;
  }
  bytes[6] = (bytes[6] & 0x0f) | 0x70; // version 7
  bytes[8] = (bytes[8] & 0x3f) | 0x80; // variant 0b10

  String hex(int start, int end) => bytes
      .sublist(start, end)
      .map((b) => b.toRadixString(16).padLeft(2, '0'))
      .join();

  return '${hex(0, 4)}-${hex(4, 6)}-${hex(6, 8)}-${hex(8, 10)}-${hex(10, 16)}';
}
