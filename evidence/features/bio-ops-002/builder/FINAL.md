# BIO-OPS-002 final builder evidence

Verdict: **PASS**

Final reviewed head before this evidence commit: `aa1c744`.

| Acceptance criterion | Result | Independent evidence |
|---|---|---|
| AC01 — switching tenant changes backend data context | PASS | Signed alpha/beta claims map only to distinct configured tenant identifiers; missing beta rejects instead of substituting alpha. |
| AC02 — cross-tenant identifiers cannot be fetched | PASS | Real-host integration coverage presents a tenant-A person identifier through tenant B and receives the same not-found result as an unknown identifier. |
| AC03 — role switching changes navigation/actions | PASS | Browser regression changes visible navigation after selecting admin; backend permission grants are tenant/person/role keyed. |
| AC04 — audit records context changes | PASS | Audit succeeds before cookie commit; shared ADR-0003 envelope, canonical identifiers, derived actor attribution, exact allowlisted detail. Decision 8/snapshot 1.1.11 accepts only this synthetic mechanism's bounded restart residual. |
| AC05 — unknown host/tenant fails closed | PASS | Unknown host returns `tenant_context_missing`; unconfigured beta is rejected and never resolves as alpha. |

Commands:

- `bash tests/features/bio-ops-002/BIO-OPS-002.acceptance.sh` — PASS.
- `python3 scripts/check-contract-freeze.py` — PASS (39 artifacts, 83 future stories).

Independent .NET execution is unavailable in this sandbox because MSBuild named-pipe binding is denied. The acceptance harness derives its gates independently from the approved ACs and ADRs; dependency builder evidence reports 292/292 integration and 211/211 unit tests.
