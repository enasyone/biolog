# BIO-BIO-001 security review — Zainab (round 3)

## Verdict: FAIL (High)

### BIO-BIO-001-F02 — tenant/person key delimiter collision breaks isolation

`Synthetic/InMemoryBiometricTemplateStore` keys records as the concatenation
`tenantId + NUL + personId + NUL + modality`. `BiometricTemplate.Create` and
`BiometricService` accept arbitrary non-empty strings; they do not reject NUL
characters. Therefore distinct identity tuples collide: `(tenantId="a\\0b",
personId="c", modality=Fingerprint)` and `(tenantId="a", personId="b\\0c",
modality=Fingerprint)` produce the same dictionary key `a\\0b\\0c\\0Fingerprint`.
A template saved under one tuple is returned to a lookup under the other tuple,
crossing the tenant boundary. This is a direct tenant-isolation failure, even
though ordinary canonical IDs do not contain NUL.

**Required fix:** use a collision-free structured key (for example a value tuple
or length-prefixed encoding), and add an adversarial test covering embedded NUL
characters. Alternatively reject NUL/control characters at the service/domain
boundary and document that contract, but a structured key remains safer.

## Scope checks

- Payload aliasing remediation from Seyi round 2 remains present: template,
  sample, and extracted-feature payloads clone on input and output.
- Provider contracts are context-free; no provider receives tenant/person IDs or
  vendor identifiers. No SQL/MySQL dependency or real biometric fixture found.
- No logging or secret material was found in the reviewed feature.
- The service intentionally accepts caller-resolved tenant/person IDs and has no
  HTTP/authentication boundary in this story; that deferred boundary is not
  treated as a closure for the key-collision finding.

## Evidence

- Source: `Synthetic/InMemoryBiometricTemplateStore.cs`, `Domain/BiometricTemplate.cs`.
- Existing tests cover ordinary tenant separation but not delimiter collisions.
- Build/test evidence from the packet: structural 38/38 and build 0 errors;
  xUnit execution is blocked by the environment socket permission.
