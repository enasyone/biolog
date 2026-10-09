// BIO-ACC-003-AC04: "stale-cache policy is explicit." Neither the master
// spec nor any accepted ADR names a concrete staleness threshold for an
// offline revocation cache (checked ADR-0001/ADR-0002,
// `02-ARCHITECTURE/OFFLINE-EDGE-ARCHITECTURE.md`, `MASTER-TECHNICAL-
// SPECIFICATION.md` §6/§11 — only the *requirement* that offline decisions
// "support safe degraded operation", never a number) — the same position
// BIO-MOB-002's own `RetryPolicy` was in for its backoff constants. The
// constant below is this story's own documented judgment call, not a value
// read from a contract.
//
// **What "explicit" means here, precisely.** Staleness never changes an
// access DECISION: a stale cache still correctly denies anything it already
// knows is revoked — BIO-CRD-006's own deny-list feed documents exactly this
// reasoning ("a stale local deny list is exactly what a genuinely revoked
// card is supposed to still be denied against"), and this story's own scope
// decision (identity-level revocation only) inherits it. What staleness
// changes is VISIBILITY: every `PresentationEvent` this device logs while
// its cache is stale carries `stale: true` (see `presentation_event.dart`),
// so a human reviewing the access-events log later can tell "allowed with a
// possibly-outdated cache" apart from "allowed with a cache synced minutes
// ago" — the "explicit" AC04 asks for, as a durable, auditable flag rather
// than a silent internal state no one downstream can see.
library;

/// 24 hours: this story's own choice. A site fully offline for less than a
/// day still denies known-revoked presentations with full confidence and no
/// stale flag; beyond that, decisions are still made (never blocked outright
/// — the whole point of a cached deny-list is continued operation without
/// connectivity) but every one is flagged `stale` until the next successful
/// sync.
const Duration staleAfter = Duration(hours: 24);

/// True when the local revocation cache was last synced too long ago (or
/// never) to be treated as current, as of [now].
bool isRevocationCacheStale({required DateTime? lastSyncedAt, required DateTime now}) {
  if (lastSyncedAt == null) return true;
  return now.difference(lastSyncedAt) > staleAfter;
}
