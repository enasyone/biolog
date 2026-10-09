# BIO-MOB-003 acceptance audit — round 3

## Verdict: FAIL

QA-02 is closed at `01044b0`: the central service rejects a body `device_id` that differs from
the authenticated device and binds tenant/device claims into the idempotency fingerprint. The
corrected boundary probe passes both checks.

QA-01 remains open. `HttpSyncTransport` can now read a securely stored secret and send both
authentication headers, but `DeviceCredentialStore.writeSecret` has no production caller. Only
tests provision it. The implementation and Seyi round-six report explicitly defer real device-
secret provisioning because no enrolment story exists. Production `SessionController` therefore
constructs a credential store that is always empty, `upload()` throws
`DeviceNotProvisionedException` before any network request, and every queued mutation follows the
failure/retry path indefinitely.

This moves the gap from “client never sends credentials” to “client can send credentials that the
shipped app can never acquire.” It does not deliver AC01 central synchronization or AC05 reconnect
behavior. The approved scope note requires the real endpoint in this story, and acceptance round two
explicitly required provisioning the production client; deferring that terminal capability is not a
closure.

Reproduction: `bash evidence/BIO-MOB-003/acceptance/round2-boundary-probe.sh` — required headers,
device cross-validation and fingerprint binding pass; production provisioning fails.

## Required remediation

Add a trusted production provisioning/enrolment path that securely writes the server-recognized
device secret, and prove the actual default `SessionController` → `HttpSyncTransport` → packaged
central host flow succeeds without a test-only credential injection. If this capability belongs to
a different story, the approved dependency/scope must be changed by the owner; an implementation
comment cannot defer an acceptance criterion.

