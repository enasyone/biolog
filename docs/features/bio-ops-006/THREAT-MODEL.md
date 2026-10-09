# BIO-OPS-006 whole-system threat model

This W14 gate reviews the composed local system at the accepted `develop` baseline. It does not
reinterpret owner-approved future boundaries as delivered controls. Synthetic data only was used.

## Assets and trust boundaries

Protected assets are tenant routing authority, authenticated principals and role grants, PERSON
records, biometric templates/photos, physical and logical card state, document evidence, webhook
secrets/deliveries, offline deny lists and queued mutations, audit evidence, and provisioning
secrets. Boundaries are the browser/session bridge, API host tenant middleware, device-authenticated
mobile APIs, feature services/repositories, external providers/transports, encrypted mobile store,
and redacting log provider.

## Domain analysis

### BIO-OPS-006-THREAT:authentication

- Attack: forge an actor header, replay a session token, substitute a device identifier, or use a
  malformed/expired credential.
- Controls: authenticated browser session bridge; exact actor-provenance proof where asserted actor
  identity feeds authorization; tenant-bound device credentials; expiry/signature validation;
  constant-time secret comparison; generic authentication errors.
- Residual boundary: shared HMAC actor provenance is the accepted interim mechanism from ADR-0006,
  not a claim of real person authentication or separation of duties.

### BIO-OPS-006-THREAT:authorization

- Attack: use a valid low-privilege principal for a privileged mutation, self-approve a
  maker-checker action, reuse approval, or reach an intentionally unmapped service method.
- Controls: tenant/person/permission-scoped gateways, verified actor provenance before role lookup,
  distinct authorized maker/checker roles, single-use approvals, and no HTTP mapping for deferred
  consequential surfaces such as authenticity override/read.

### BIO-OPS-006-THREAT:tenant-routing

- Attack: spoof Host, query/body/header tenant fields, reuse an identifier from another tenant, or
  exploit not-found/error differences as an oracle.
- Controls: verified Host-channel resolution, immutable set-once ambient context, canonical tenant
  identifiers, tenant-scoped repository keys, server/device-resolved tenant binding, and foreign
  tenant not-found equivalence. The AC03 suite requires representative negative regressions across
  routing, identity, biometrics, cards, webhooks, offline sync, and documents.

### BIO-OPS-006-THREAT:biometrics

- Attack: mutate aliased template bytes, cross-tenant search, disclose biometric/photo content,
  bypass liveness or retention, or turn provider failure into an unaudited allow.
- Controls: defensive byte copies, structural tenant keys, permission/provenance gates, bounded
  provider failure handling, liveness policy, retention/deletion workflow, minimum response data,
  and redacted logging. No production log call carries biometric content.

### BIO-OPS-006-THREAT:card-lifecycle

- Attack: activate revoked/failed production, race replacement against production mutation,
  duplicate issuance, replay offline completion, or cross tenant/card substitution.
- Controls: tenant-scoped repositories, audit-before-commit issuance, idempotency, one-active-card
  enforcement, shared activation barrier across lifecycle and production, signed tenant-bound
  offline completion, expiry/single-use validation, and conflict-preserving retry.

### BIO-OPS-006-THREAT:document-parsing

- Attack: catastrophic regular expression input, oversized/malformed evidence, mutable result
  aliasing, fabricated NFC/authenticity, raw evidence rewrite, or silent PERSON overwrite.
- Controls: bounded regex execution, strict parsers and confidence/provenance, immutable copied
  evidence, unavailable NFC when no transport exists, evidence-linked manual review, append-only
  reads, and proposal-only PERSON changes. The disclosed synthetic-raster boundary remains explicit.

### BIO-OPS-006-THREAT:webhooks

- Attack: cross-tenant subscription access, forged privileged actor, secret disclosure, insecure
  destination, duplicate delivery, signature confusion, or server-side request forgery.
- Controls: ambient tenant plus permission/provenance gates, constrained secret references,
  HTTPS destination policy, tenant-scoped repositories, signed deliveries, idempotent delivery
  state, and response/error minimization. Secrets are neither returned nor logged.

### BIO-OPS-006-THREAT:offline-caches

- Attack: use stale revocation data to allow a revoked credential, forge device/tenant claims,
  replay queued events, lose reconnect wakeups, silently merge conflicts, or leak plaintext local
  data.
- Controls: encrypted local database, trusted persisted tenant context, device-authenticated
  revocation pulls, deny/stale policy, event-id idempotency, monotonic conflict handling,
  single-flight plus coalesced reconnect rerun, request deadlines, and retain-on-failure queues.

## Secrets and logs

`scripts/secret-scan.sh --all` covers the current tree through mandatory gitleaks.
`scripts/history-secret-scan.sh` covers Git history through mandatory gitleaks. Both fail closed with
the toolchain-policy installation reference when gitleaks is unavailable. Production application logs
have only two direct sink call sites; both wrap every emitted field in `SafeLogValue`. The host
clears default providers and installs the redacting provider, unknown values default to withheld,
and exception messages/custom formatters cannot bypass classification.

## Verdict

No unresolved Critical or High defect was found in the current local baseline. Two explicitly
accepted future boundaries remain visible rather than misreported as fixes: real principal binding
replaces shared actor-provenance HMAC under BIO-INT-001, and real device enrolment provisions mobile
device secrets. Both current interim paths fail closed within their accepted scope.
