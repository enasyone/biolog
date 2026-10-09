# BIO-OPS-001 independent re-review — Seyi

Date: 2026-10-06
Builder commit reviewed: `125e99f` (remediation `49cb5c1`)

## Verdict

FAIL — F01 and F02 remain open in substance. The remediation wraps unlinked actions with caller-supplied ownership metadata, and the test itself decides not to issue a card after failure; neither property is enforced by the underlying workflow.

## F01 — artifact linkage is still synthetic

`EnrolmentJourneyService.executeStage` accepts tenant/PERSON identifiers, but the photo, fingerprint, face, and document actions still do not. Their test adapters return hard-coded references such as `local://photo/capture-1` and `local://face/reference-1`; those references are not returned by the underlying operations and cannot identify or retrieve the artifact they claim to represent.

The store therefore proves only that a caller can write an arbitrary `(tenantId, personId, stage, artifactReference)` tuple after any successful callback. It does not prove that the produced artifact belongs to that PERSON, nor that the ownership write is atomic with artifact creation.

## F02 — stop-on-failure is still caller-controlled

The service exposes only independent `executeStage` calls and has no journey state or terminal blocker enforcement. After a failed document stage, a caller can catch `EnrolmentJourneyBlockedError` and call the card stage normally.

The new test makes `issueCard` remain uncalled with this test-only condition:

`if (store.blockersForPerson(...).length === 0) await issueCard();`

That is not production orchestration. The test chooses not to call the spy; no implementation prevents it.

## Reproduction

`bash tests/operations/BIO-OPS-001.test.sh` reports 6/6 PASS because it greps for wrapper calls and the test-only `not.toHaveBeenCalled` assertion. Inspection of the executable test shows the hard-coded artifact references and caller-side blocker condition above.

## Required remediation

- Each stage must return a genuine persisted artifact/evidence identifier whose ownership can be independently queried and verified against tenant/PERSON, or use a real aggregate/store transaction that creates the artifact and ownership together.
- Provide a journey orchestrator/state machine that runs the ordered stages and refuses all later stages once a blocker is recorded. Test that an attempted card issue after failure is rejected by implementation, not simply omitted by test code.

## Category verdicts

- Functional: FAIL (AC03, AC05).
- Data integrity: FAIL (arbitrary/unverifiable artifact references; non-atomic claimed linkage).
- Tenant isolation: NOT PROVEN for underlying photo/biometric/document artifacts.
- Regression: The 6/6 structural harness passes, but it is not behavioral proof of the disputed guarantees.
