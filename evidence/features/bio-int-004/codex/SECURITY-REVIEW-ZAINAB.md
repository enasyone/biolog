# BIO-INT-004 security review — Zainab

**Verdict: FAIL**  
**Reviewed head:** `1b98b87ffc3e0c25e33dadb552d9fc64b596c1a8`  
**Implementation:** `ad5f854`

## Blocking findings

### ZB-INT004-01 — High — subscription URLs permit server-side request forgery

`BioInt004Module` accepts every absolute HTTP or HTTPS URL (`BioInt004Module.cs:131-149`).
`HttpWebhookSender` then posts the complete signed event envelope to that URL using the host's
network authority (`IWebhookSender.cs:34-48`). There is no HTTPS-only rule, destination allowlist,
DNS/IP resolution check, rejection of loopback/private/link-local/multicast/metadata ranges, port
policy, or redirect revalidation.

An actor holding `webhooks.subscription_manage` can therefore register targets such as loopback,
RFC1918, IPv6 local, or cloud metadata/internal service addresses. A matching tenant event makes
the BIOLOG server issue the request and can expose event payloads classified `personal` or
`sensitive`. Automatic redirects also preserve the attack even if only the initially supplied host
were later checked.

**Required remediation:** implement a single outbound webhook destination policy at creation and
again at every connection/redirect. Require HTTPS in non-development environments; reject userinfo,
unsafe ports, loopback, private, link-local, multicast, unspecified, and platform metadata ranges
for every resolved A/AAAA address; prevent DNS rebinding by binding validation to the actual
connection; disable automatic redirects or validate each hop. Add direct-IP, IPv6, DNS-resolution,
rebinding/redirect, and metadata-address adversarial tests.

### ZB-INT004-02 — High — replay/dedup protection is non-atomic

`HandleAsync` first calls `HasSucceededAsync`, then sends, then records `Delivered`
(`WebhookDispatchService.cs:61-73, 122-128`). The audit contract exposes no atomic claim, and its
lock protects each individual list operation only (`IWebhookDeliveryAuditSink.cs:34-65`). Two
concurrent handlers for the same `(tenant, event, subscription)` can both observe no success and
both deliver. A receiver success followed by audit failure or caller cancellation also leaves no
success marker, so redelivery occurs on retry.

This directly contradicts AC03's claimed delivery-side replay protection. A timestamp-bound HMAC
authenticates a captured request, but does not make the sender's check-then-act sequence single-use.

**Required remediation:** introduce a durable atomic claim/state machine keyed structurally by
`(tenant_id, event_id, subscription_id)`, with one owner allowed to send and losing concurrent
callers observing in-flight/completed state. Define recovery for the unavoidable "receiver accepted,
completion write failed" ambiguity, preferably with a stable delivery/idempotency identifier that
receivers can deduplicate. Do not use the caller cancellation token for post-send completion
recording. Add barrier-gated concurrent-handler and send-success/audit-failure/cancellation tests
that fail the current implementation.

## Checks that passed

- Tenant-scoped subscription lookup and event-type filtering use structural tenant fields.
- Management endpoints require actor provenance and live tenant permission checks.
- Actor provenance fails closed when unconfigured or shorter than 32 characters.
- Signing secrets are cryptographically random and omitted from list responses.
- HMAC covers the timestamp and exact serialized body; comparison guidance is documented.
- Retry count is bounded and permanent failures are dead-lettered.

## Evidence reviewed

- `api/Biolog.Api/Modules/BioInt004Module.cs`
- `api/features/bio-int-004/Biolog.Features.Webhooks/Domain/*`
- `api/features/bio-int-004/Biolog.Features.Webhooks/Api/*`
- `tests/features/bio-int-004/*`
- `docs/features/bio-int-004/README.md`
- Builder evidence at `evidence/features/bio-int-004/builder/`
