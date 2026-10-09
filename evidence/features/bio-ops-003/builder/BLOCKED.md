# BIO-OPS-003 builder evidence — blocked

Date: 2026-10-07

Verdict: **FAIL / BLOCKED**

## SY-OPS003-01 — High — production and replacement activation are not composed

The approved journey requires one transaction boundary to preserve all of these facts:

1. Card 001 is active.
2. Card 001 is reported and Card 002 is issued as the next sequence.
3. Card 002 cannot activate unless production passed.
4. Activating Card 002 permanently prevents Card 001 from reactivation.
5. No boundary exposes two active cards.

The shipped API offers two incompatible activation paths:

- `POST .../{sequence}/production/activate` → BIO-CRD-007 verifies the latest job is `Passed`, then calls BIO-CRD-005 `ActivateAsync`. BIO-CRD-005 refuses because Card 001 is still active. BIO-CRD-007's `ICardIssueGateway` deliberately exposes no lifecycle/replacement operation.
- `POST .../{sequence}/activate-replacement` → BIO-CRD-006 sweeps earlier cards to permanent revocation and activates Card 002. BIO-CRD-006 never queries a production job, and its `ICardIssueGateway` deliberately exposes no production status. Therefore this route activates a failed or wholly unproduced Card 002.

This is not a defect BIO-OPS-003 can safely patch inside acceptance-only paths. It requires a Kemi-approved ownership/composition design between BIO-CRD-006 and BIO-CRD-007: either production-gated replacement activation, or a coordinator that atomically applies both preconditions and preserves the existing compensation protocol.

## Acceptance results

| Criterion | Result |
|---|---|
| AC01 — issue Card 001 and activate | PASS at dependency level |
| AC02 — report and replace through next sequence | PASS at dependency level |
| AC03 — previous card cannot reactivate | FAIL in the production activation path |
| AC04 — production failure cannot activate | FAIL in the replacement activation path |
| AC05 — one active card at every boundary | No single composed path can complete the journey while enforcing AC03 and AC04 |

## Reproduction

`bash tests/features/bio-ops-003/BIO-OPS-003.acceptance.sh` proves both individual guards exist and both cross-feature composition links are absent; it exits 1 with two failed integrated invariants.

Existing dependency tests cannot catch this: BIO-CRD-007 tests start with no active predecessor, while BIO-CRD-006 replacement tests do not create or fail a production job.
