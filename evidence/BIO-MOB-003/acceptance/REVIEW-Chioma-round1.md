# BIO-MOB-003 acceptance audit — round 1

## Verdict: FAIL

The local queue and client orchestration are well covered: the feature harness, both independent
Femi probes, traceability check, module-boundary check, and secret scan pass. The builder transcript
also records 104 Flutter tests and clean static analysis. The two prior Femi findings concerning
initial-online startup and an unbounded HTTP wait are closed.

Acceptance is blocked because the evidence does not prove the approved central synchronization
capability or server acknowledgement idempotency:

- `HttpSyncTransport` posts to an invented `/api/v1/offline-mutations` route, but no matching
  central intake endpoint exists in the repository and no approved contract defines that route.
- The production default is `https://sync.biolog.invalid`, which is deliberately non-resolving.
- Every HTTP behavior test uses `MockClient`; there is no client-to-central integration test or
  transcript proving an addition, update, or revocation is accepted centrally.
- AC02 is server-side behavior. The client sends an idempotency key and trusts a replay response,
  but no executable evidence proves that the server stores the original result, rejects key reuse
  with a changed fingerprint, or returns the stored result on replay.

This is not a missing screenshot or deployment-only detail. The story is titled “central/offline
synchronization,” its user story requires deterministic synchronization, and AC02 explicitly names
server acknowledgements. Component mocks cannot establish those system-boundary claims.

## Required remediation

1. Implement or bind to an approved central offline-intake contract and executable endpoint.
2. Add end-to-end evidence for addition, update, revocation, first acknowledgement, replay, changed-
   fingerprint key reuse, conflict routing, and reconnect duplicate prevention across that boundary.
3. Replace or demonstrably configure the non-resolving production default in the tested app path.
4. Update the traceability evidence to reference the central integration tests and reviewed commit.

