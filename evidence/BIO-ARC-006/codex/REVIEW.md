# BIO-ARC-006 independent review

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed implementation `064941a` at builder head `921d2e9` against AC01–AC04.

## Findings

### BIO-ARC-006-QA-01 — High — Snapshot is not actually commit-bound

The manifest records `baseline_parent_commit`, but `check-contract-freeze.py` never reads or validates it. Replacing it with forty zeroes still passes. AC01's versioned snapshot therefore has no verified relationship to the claimed accepted baseline commit.

Required closure: validate that the commit exists and that every frozen artifact hash/size matches that exact commit tree, not only the mutable current working tree.

### BIO-ARC-006-QA-02 — High — Existing snapshot versions can be regenerated in place

Changing a frozen contract and updating its hash/size inside `v1.0.0/manifest.json` makes the checker pass. Nothing binds the immutable version directory to its originally accepted contents. This directly bypasses AC04's required ADR, impact review and new snapshot version while the documentation claims existing versions are never edited.

Required closure: compare an existing snapshot directory with its accepted introduction commit or another immutable trust anchor, and fail if an existing version's manifest changes. A changed contract must require a distinct snapshot version and the full change-control evidence.

### BIO-ARC-006-QA-03 — High — Future stories may rewrite the freeze's own enforcement

Every future feature declares only `contracts/**`, `docs/adr/**`, and `governance/contract-freeze/**` read-only. The authoritative ownership file, checker, and freeze policy document are outside those paths: `governance/feature-path-ownership.json`, `scripts/check-contract-freeze.py`, and `docs/architecture/SHARED-CONTRACT-FREEZE.md`. A feature can therefore alter its own ownership declaration or weaken the checker without violating its declared read-only contract.

Required closure: include the ownership registry, validator, and freeze policy in every feature's read-only shared surface and bind them to the protected freeze/change-control mechanism.

## Reproduction

```sh
python3 evidence/BIO-ARC-006/codex/independent-review.py
bash tests/foundation/BIO-ARC-006.test.sh
node --test contracts/js/
```

## Category verdicts

| Category | Verdict |
|---|---|
| Functional | FAIL — QA-01/02/03 |
| Security | PASS — no secret/material exposure found |
| Tenant isolation | NOT APPLICABLE |
| Offline/recovery | FAIL — immutable baseline recovery is not trustworthy |
| Concurrency | NOT APPLICABLE |
| Regression | PASS — foundation 15/15; contracts 322/322 |
