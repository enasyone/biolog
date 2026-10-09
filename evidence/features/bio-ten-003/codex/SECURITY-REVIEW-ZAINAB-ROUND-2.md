# BIO-TEN-003 security review — round 2

**Verdict: FAIL / CHANGES REQUIRED**  
**Reviewed:** implementation `c6f8e8c`, builder evidence `1f0a7c3`

The round-one authorization, tenant-existence, and Face 1:N parent/child findings are closed. The five currently unenforced module families and browser provenance bridge are accepted only as the explicitly tracked cross-story boundaries approved by the orchestrator. One security blocker remains.

## ZB-TEN003-04 — High — weak provenance secrets are accepted

`HmacActorProvenanceStore.Configure` accepts any string. `ActorProvenanceProvisioning.LoadFromConfiguration` therefore treats a one-character deployment secret as a valid root credential for tenant feature administration. This diverges from the canonical actor-provenance gate now used on the floor, which fails closed unless the configured secret is at least 32 bytes.

An attacker who learns or guesses a weak configured secret can forge any actor's provenance proof and then exercise that actor's live tenant permissions, including enabling Face 1:N.

Required: reject or fail closed for secrets shorter than 32 bytes and add 31/32-byte boundary tests. The integration fixture must also use a compliant secret; its current `test-provenance-secret` is only 22 UTF-8 bytes.

## Verified closures

- GET/PATCH now require actor provenance, distinct tenant-scoped read/manage permissions, and target-tenant existence.
- Effective Face 1:N policy is `Biometrics.Enabled && Face1NIdentificationEnabled` for both async and cached consumers.
- Tenant keys are structural and the existing mutation lock/audit-before-commit protocol remains intact.
- AC05 remains a truthful negative browser proof pending `TD-WEB-PROVENANCE-BRIDGE-REVERIFY`; it is not treated as a story-local blocker.
