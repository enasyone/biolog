# BIO-OPS-004 — offline/reconnect local acceptance

Status: **PASS — access-cache synchronization is wired into the shipped reconnect path.**

The dependency components work independently:

- connectivity state selects the cached verification path;
- cached revocations deny and presentation events are stored in the encrypted database;
- access-cache uploads use the event identifier as the idempotency key;
- generic offline mutations surface conflicts.

`SessionController` now composes `AccessCacheSyncClient` in production and subscribes it to the
live connectivity signal. An online transition pulls the tenant's revocation delta and drains
pending presentation events. Overlapping reconnect signals are coalesced into a single follow-up
pass, so a recovery signal received while a failing request is still in flight is not lost.

The deterministic Flutter regression covers offline denial persistence, reconnect pull and queue
drain, idempotent repeat reconnects, conflict visibility, and the in-flight failure/recovery race.
See `evidence/BIO-OPS-004/manifest.json` and
`evidence/BIO-OPS-004/codex/SECURITY-REVIEW-ZAINAB-ROUND2.md`.
