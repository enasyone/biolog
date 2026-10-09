# BIO-VIS-002 security re-review — Zainab round 3

## Verdict: FAIL (High)

Commit `b49a46e` closes cross-visit mis-adoption, but does not neutralize a stranded Active card.

### F05 — stranded-card ledger prevents reuse but not access

The forced triple-failure test explicitly leaves the backing card `active`, the credential Pending with no sequence number, and only a stranded-ledger entry. That ledger is consulted solely during future grant selection. No checkout/revoke path, access-decision gate, recovery worker, or operator endpoint consumes it to deny or deactivate the physical credential.

The failed grant can therefore leave usable access indefinitely with no Granted credential history and no visitor-lifecycle path capable of reaching the card. Preventing a later visit from adopting it fixes ownership aliasing but not the original active-access hazard.

**Required fix:** make the ledger drive fail-closed access rejection or a retryable reconciliation process that deactivates the card and records a terminal/audited state. Add a test proving the forced attach + compensation + fallback failure cannot leave usable access, and that recovery converges when the card service becomes available.

Structural checks pass 60/60, but the new regression test itself asserts that the stranded card remains Active. This agrees with Seyi’s concurrent round-4 finding.
