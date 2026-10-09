// BIO-MOB-003-AC02/AC03: the shape of a server's response to an uploaded
// delta (`SyncTransport.upload`). Deliberately small — this story's own
// scope is the client-side sync engine, not a new wire contract; a real
// transport implementation, once a real offline-intake endpoint exists, maps
// whatever that endpoint actually returns onto this shape.
library;

/// What the server decided about an uploaded mutation, per ADR-0001 §3.4
/// (idempotency) and §3.5 (conflict handling). There is no "merged"
/// outcome: per §3.5, absent an owning story's written merge rule (none
/// exists for any aggregate in this codebase yet), a `base_version`
/// mismatch is always [conflict] — see [OfflineMutationOperation] and
/// `SyncEngine` for where that unconditional rule is enforced.
enum SyncAckOutcome { accepted, conflict }

/// A server acknowledgement for one uploaded mutation.
class SyncAck {
  /// The server accepted the delta. [replayed] is true when this
  /// acknowledgement is the server returning the result of an **earlier**
  /// execution of the same `idempotency_key` (ADR-0001 §3.4: "same key, same
  /// request fingerprint -> return the original stored result; do not
  /// execute again") rather than a fresh one. `SyncEngine` handles both
  /// identically — the field exists for observability/testing (BIO-MOB-003-
  /// AC02), not because the two cases need different local handling.
  const SyncAck.accepted({this.replayed = false})
      : outcome = SyncAckOutcome.accepted,
        serverVersion = null;

  /// The server rejected the delta because its `base_version` no longer
  /// matches the server's current version (ADR-0001 §3.5). [serverVersion]
  /// is the server's current version, surfaced for the human Sync
  /// Resolution Queue (BIO-MOB-003-AC03) — never used to auto-resolve.
  const SyncAck.conflict(this.serverVersion) : outcome = SyncAckOutcome.conflict, replayed = false;

  final SyncAckOutcome outcome;
  final bool replayed;
  final int? serverVersion;
}
