# BIO-DOC-007 independent review — round 1

## Verdict: FAIL

### Finding QA-01 — passive authentication ignores declared-but-unread data groups

`PassiveAuthentication.Evaluate` iterates only `readDataGroups` and marks `dataGroupHashesMatch` true when every read group matches. It never checks that every data group declared in the Document Security Object (SOD) hash map was actually read. A chip read that omits a declared group can therefore report matching hashes and a passing passive-authentication result despite incomplete evidence.

Reproduction: pass one read group (DG1) and `sodDeclaredHashes` containing matching DG1 plus a valid-looking DG2 hash. The method returns `DataGroupHashesMatch == true`; DG2 is neither listed as mismatched nor represented as missing. This is a false authenticity signal whenever a requested/unavailable group is omitted.

Category verdicts: Functional FAIL; Security FAIL; Tenant isolation NOT APPLICABLE; Offline NOT APPLICABLE; Concurrency NOT APPLICABLE; Regression FAIL (AC04).

Foundation checks: 30 passed, 0 failed; they do not cover declared hashes for unread groups.
