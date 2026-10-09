# BIO-PHO-003 security review — Zainab round 3

**Verdict: FAIL**  
**Reviewed head:** `12d0a4e`  
**Remediation:** `dd27479`

## Closed from round 2

- Approval intent is now persisted before the external promotion call.
- The marker preserves the stable attempt ID, baseline master version, and immutable initiating
  actor. A retry by another operator retains the initiator in the decision audit.
- Fault tests now cover marker-write failure, process loss immediately after promotion, audit
  failure, and final local-commit failure.

## Blocking finding

### ZB-PHO003-03 — Medium — content equality is not a stable promotion identity

Recovery treats `latest version > baseline` plus exact byte equality as proof that this approval
attempt created that version. Photo content is not unique to an operation. Another workflow can
append the same bytes after the baseline, causing this attempt to claim and audit a master version
it did not create. Conversely, if this attempt promotes successfully and another workflow appends
a different photo afterward, recovery examines only the newest version, reports
`promotion_ambiguous`, and can never find this attempt's matching earlier version. The capture is
then permanently stuck with an in-flight marker despite its promotion already existing.

The new “unrelated concurrent append” test demonstrates the refusal case but treats indefinite
non-convergence as success. Round 2 required every retry to converge on one master and one truthful
terminal audit; refusing forever does not meet that property.

**Required remediation:** carry the stable approval attempt ID into the promoted PhotoMaster (or
an atomic idempotency record owned by the promotion boundary) and query by that identity on retry.
Do not infer operation ownership from reusable content bytes or only the latest version. Add both
interleavings: unrelated identical-content append before this attempt, and this attempt's
successful append followed by an unrelated later append. Each must resolve the correct operation
without duplicate promotion or false audit attribution.

## Verification

- Inspected the complete `958f2fd..12d0a4e` production and test delta.
- Traced intent creation, baseline capture, external state reconciliation, promotion, audit and
  final commit across same-actor and different-actor retries.
- Builder evidence records 39/39 feature tests and 250/250 integration tests; no test assigns a
  stable operation identity to the external PhotoMaster or covers identical-content ambiguity.
