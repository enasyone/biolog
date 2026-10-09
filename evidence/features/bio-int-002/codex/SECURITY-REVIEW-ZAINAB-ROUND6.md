# BIO-INT-002 security review — Zainab — round 6

**Verdict: FAIL**

Reviewed remediation `8ab0c83` and evidence `ffa6a10`. The structural harness passes 72/72.
Binding recovery provenance to the attempt fingerprint is the correct design, but the fingerprint
encoding permits collisions between different requests.

## ZAINAB-INT-002-F07 — delimiter-based canonicalization is not injective

`ComputeFingerprint` serializes rows and fields by concatenating caller-controlled external keys,
field names, and values with raw `;`, `:`, `,`, and `=` delimiters. These inputs are not escaped or
length-prefixed. Distinct dictionaries can therefore produce the same canonical string and SHA-256
fingerprint. For example, one row whose fields are `{ "a": "b,c=d" }` serializes identically to
one whose fields are `{ "a": "b", "c": "d" }`.

After a failed attempt releases its claim, a differently structured request with the colliding
encoding is treated as the same fingerprint. The new provenance replay branch trusts the stale
Created outcome, repairs/accepts its link, and skips fresh identity conflict classification. This
defeats the F06 remediation and AC05's different-content reuse protection.

Required fix: hash a deterministic, unambiguous serialization such as canonical JSON with explicit
array/object boundaries, or use length-prefixed components. Add adversarial tests using delimiter-
collision payloads and external keys, proving their fingerprints differ and stale provenance is not
replayed.
