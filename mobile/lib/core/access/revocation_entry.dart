// BIO-ACC-003-AC01/AC02: the wire shape this device pulls from
// `GET /api/v1/access-cache/revocation-deltas` (api/features/bio-acc-003/
// Api/AccessCacheDtos.cs's `RevocationEntryResponse`) — minimum necessary by
// construction: nothing beyond the revoked person's id and the instant it
// was revoked ever reaches this device.
library;

/// One revoked person, as cached locally. [revokedAt] mirrors the server's
/// `Person.UpdatedAt` — used only as this device's own delta cursor, never
/// displayed or otherwise interpreted.
class RevocationEntry {
  const RevocationEntry({required this.personId, required this.revokedAt});

  final String personId;
  final String revokedAt;

  factory RevocationEntry.fromJson(Map<String, Object?> json) => RevocationEntry(
        personId: json['person_id'] as String,
        revokedAt: json['revoked_at'] as String,
      );
}
