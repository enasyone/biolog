# BIO-INT-005 security review — Zainab round 2

**Verdict: PASS**  
**Reviewed head:** `ed5f0d3`  
**Remediation:** `a44c778`

## Findings closed

- **ZB-INT005-01:** scan now verifies the actor's HMAC provenance proof at the endpoint and
  requires the tenant-scoped `integrations.conflict_review_read` permission in the service before
  querying import outcomes, mutating review state, or writing audit events.
- **ZB-INT005-02:** list applies the same provenance and read-permission gates before accessing or
  returning person identifiers, external keys, exact existing/incoming identity values, decision
  notes or other queue data.

The existing decide path remains protected by verified provenance plus the separate
`integrations.conflict_review_decide` permission. Permission lookups use the resolved tenant and
actor, while repositories and the import gateway remain tenant-scoped.

## Verification

- Inspected the complete `f36f66b..ed5f0d3` production and test delta.
- Traced scan, list and decide from actor resolution through provenance, tenant resolution,
  permission lookup, gateway/repository access and response/audit creation.
- Host tests prove missing provenance and real unprivileged grants fail closed on scan and list;
  service tests prove permission denial causes no scan mutation and no list disclosure.
- Builder evidence records 22/22 feature tests and 255/255 integration tests passing.

No remaining security blocker was found in the reviewed scope.
