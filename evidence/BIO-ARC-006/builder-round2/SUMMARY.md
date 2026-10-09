# BIO-ARC-006 — builder remediation evidence

- **Remediation commit:** `f63220a1967bc52b78395f298bc289845617d15a`
- **Review finding source:** `evidence/BIO-ARC-006/codex/REVIEW.md`

## Findings closed

| Finding | Result | Closure |
|---|---|---|
| QA-01 baseline commit unvalidated | PASS | The checker requires the accepted baseline commit, resolves it as a Git commit, requires it to be an ancestor, and compares every snapshot entry to that commit tree. |
| QA-02 v1.0.0 regenerable in place | PASS | A frozen-file rewrite plus regenerated hash/size now differs from the accepted baseline tree and fails closed. |
| QA-03 enforcement files writable | PASS | All 83 future stories declare the ownership registry, validator and freeze policy read-only, in addition to the frozen contracts, ADRs and snapshot directory. |

## Verification

- `python3 evidence/BIO-ARC-006/codex/independent-review.py` — 4/4 PASS
- `python3 scripts/check-contract-freeze.py` — PASS, 38 artifacts and 83 future stories
- `bash tests/foundation/BIO-ARC-006.test.sh` — 17/17 PASS
- Embedded contract behavior suite — 322/322 PASS
- Embedded tracked secret scan — clean
- `bash scripts/quality.sh` — QUALITY: PASS

The reviewer-owned evidence files were not changed.
