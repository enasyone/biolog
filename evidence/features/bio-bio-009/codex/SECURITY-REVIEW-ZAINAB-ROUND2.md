# BIO-BIO-009 security review — round 2

**Verdict: FAIL / CHANGES REQUIRED**  
**Reviewed:** remediation `321624c`, evidence `69b3c2b`

## Closed from round 1

- Production adapters now genuinely delete fingerprint sessions and face references and call the corresponding gallery stores.
- The deleted record's stored person ID now drives gallery eviction; the caller can no longer provide an unrelated person.
- The endpoint uses a live BIO-IDN-006 permission gateway rather than trusting `X-Actor-Permission`.

## Blocking findings

### ZB-BIO009-03 — High — AC03 remains disconnected from biometric capture

The approved criterion says raw-image retention defaults off unless authorised. BIO-BIO-009 still maintains only its own tenant authorization map. BIO-BIO-002 capture still persists the sample payload solely from `FingerprintEnrolmentSession.RetentionAuthorised`, and no capture path queries BIO-BIO-009's policy. Revoking or never granting the new authorization therefore does not prevent raw biometric storage when the independent session flag is true.

This is an explicit acceptance criterion, not a scope-neutral follow-up. Required: enforce the authoritative tenant policy server-side at every raw-image write boundary, fail closed if unavailable, and prove caller/session input cannot bypass default-off or revocation.

### ZB-BIO009-05 — Critical — provenance gate is forgeable when configuration is absent

The new `HmacActorProvenanceStore` uses a deterministic key derived from the public source literal `no-provenance-secret-configured` whenever `BiometricRetention:ActorProvenanceSecret` is absent. An attacker can calculate the expected HMAC for a real privileged actor ID, impersonate that actor, pass the provenance gate and inherit the victim's live RBAC grant.

Required: unconfigured verification must always fail; do not calculate with a public fallback. Add a composed-host test using the former dummy-key proof for a genuinely privileged actor and require 401 before the permission lookup.

### ZB-BIO009-06 — High — deletion can strand a live gallery entry with no recovery path

Record deletion happens before gallery removal. If gallery removal throws, the record is already gone, no completion event is written, and retry sees `NotFound`, so the service has lost the stored person ID needed to retry eviction. If removal returns `false`, the service still emits `deletion_completed` and returns `Deleted`. There is no durable pending-cleanup record or reconciler. A biometric entry can therefore remain searchable after deletion indefinitely.

Required: durably retain the resolved person/cleanup obligation before deleting, retry/reconcile provider and gallery removal to convergence, and do not report terminal completion while required propagation remains unresolved. Add throw-after-record-delete and false-removal recovery tests.

## Additional result

- Retention expiry and legal hold remain absent. Even if product treats those user-story terms as outside the five enumerated criteria, that does not waive AC03 or the deletion convergence requirement in AC04.

## Verification

- Reviewed the real deletion adapters, sibling repository/gallery extensions, resolved-owner flow, provenance/RBAC path, capture policy references and focused tests.
- Local feature-test replay was again blocked because `vstest` could not bind its loopback socket in the managed sandbox. Builder evidence records the listed passing suites.
