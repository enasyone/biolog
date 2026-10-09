# BIO-FND-010 independent re-review — round 2

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed remediation `6775a04` at evidence head `6559b14`.

QA-01 is closed: evidence metadata is now bound to the story and locally resolvable reviewed commit, requires a PASS result, command, reviewer, and prevents owner-authored non-builder stages. QA-02 is closed: `--all` inventories all 13 implemented foundation harnesses and reports omitted story specs.

## Finding

### BIO-FND-010-QA-03 — High — Story spec can remove its own independent-review gate

`required_acceptance_stages` is read from the same mutable story spec being accepted. The checker has no independent policy source or minimum required stages. A story can change that list to `["builder"]`, remove the adversarial evidence declaration, and then builder-only evidence authorizes a PASS acceptance record.

The independent reproduction creates valid builder metadata bound to the reviewed commit, declares only the builder stage, and records acceptance successfully without any independent evidence. This contradicts AC04 and the Definition of Done requirement that independent Codex categories pass.

Required closure: derive mandatory review stages from a protected, authoritative policy/routing source rather than the story under acceptance, or enforce a non-removable minimum independent stage. Test deletion and weakening of required stages.

## Reproduction

```sh
python3 evidence/BIO-FND-010/codex/independent-review.py
bash tests/foundation/BIO-FND-010.test.sh
python3 scripts/check-traceability.py --all
```

Independent probes: original three pass; policy-weakening probe fails. Builder harness: 13/13 pass. Inventory: 13/13 stories pass.

## Category verdicts

- Functional: FAIL — AC04 remains bypassable through QA-03.
- Security: FAIL — a mutable input controls its own review requirement.
- Tenant isolation: not applicable.
- Offline/recovery: PASS.
- Concurrency: not applicable; exclusive record creation remains correct.
- Regression: PASS — original QA-01/QA-02 probes and supplied checks pass.
