# BIO-FND-005 Builder Evidence — Configure Munder office operating rules

Owner: Chinedu. Commit: see git log. Synthetic/no PII.

## Artifacts
- `governance/munder-agents.json` — 12 agents with role, engine/model, permissions; office rules block.
- `governance/MUNDER-OFFICE-RULES.md` — human-readable operating rules.
- `tests/foundation/BIO-FND-005.test.sh` — 5 tests mapped to AC IDs (5/5 pass, see test-output.txt).

## AC → evidence
- AC01 (12 agents w/ prompts, engine/model, permissions): munder-agents.json + test AC01.
- AC02 (Chinedu assigns only eligible stories, deps DONE): rules doc + json rule + test AC02.
- AC03 (Kemi alone approves shared contracts/migrations): json permissions + test AC03.
- AC04 (builders cannot self-accept): json permissions + rule + test AC04.
- AC05 (no GitHub remote/push before W15): rule + repo pre-push guard (.githooks/pre-push from BIO-FND-001) + test AC05.

## Local-first
No remote, no push. Commits local only.

## CHANGES_REQUIRED round 1 (Seyi QA-01..03) — FIXED
Root cause: original tests grep'd prose instead of enforcing rules. Fix:
- Added `scripts/validate-governance.py` — strict, machine-enforced AC01-AC05.
- Restructured `governance/munder-agents.json` rules into typed booleans (dispatch_eligibility.require_dependencies_done/respect_conflict_locks/allow_incomplete_dependencies; no_self_accept.self_accept_allowed; local_first.git_push_allowed/remote_allowed).
- AC02: contradictory eligibility now FAILs. AC04: self_accept must be boolean false (rejects "allowed"). AC05: git_push must be boolean false AND no git remote (detected via `git remote`) + pre-push guard.
- Rewrote `tests/foundation/BIO-FND-005.test.sh`: 8/8 incl. adversarial regressions for QA-01/02/03, 13th-agent, second-approver, plus a harness-sanity check. Remote detection tested via a git PATH-shim (sandbox blocks throwaway git repos).

## CHANGES_REQUIRED round 2 (Seyi QA-04/05/06) — FIXED
- QA-04: self_accept/git_push now REQUIRED present and boolean false (missing key => FAIL); normalized every agent's permissions to include both explicitly.
- QA-05: AC01 now asserts the EXACT canonical 12 names + roles (not just count/uniqueness); a replaced/renamed agent FAILs.
- QA-06: AC03 now asserts Kemi's write_scope includes 'migrations'.
- Tests: 12/12 incl. regressions for missing keys, agent replacement, Kemi migrations loss, plus prior QA-01/02/03 and harness sanity.
