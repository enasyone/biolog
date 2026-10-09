# BIO-DOC-011 security review

**Verdict: FAIL / CHANGES REQUIRED**  
**Reviewed:** implementation `ab20b97`, builder evidence `313a546`

## Blocking findings

### ZB-DOC011-01 — Critical — template administration and publication are unauthenticated

The story is explicitly for authorised BTS/tenant administrators and depends on BIO-IDN-006. Every create/edit/test/publish/new-version route instead accepts an arbitrary `X-Actor-Id` and optional `X-Actor-Type`. There is no ADR-0006 provenance verification, authentication, or live tenant permission lookup. Read routes do not even require the asserted actor.

Any caller that reaches a tenant channel can therefore read tenant template definitions, create or alter OCR regions, advance a draft through its lifecycle, and publish it while forging another person's audit identity. Server-resolved tenant context prevents cross-tenant key selection, but it is not authorization inside that tenant.

The module comment says no authentication story has landed, but BIO-INT-001 is already integrated and multiple accepted W12 features use the approved provenance-plus-BIO-IDN-006 permission seam. More importantly, absence of authentication cannot make a privileged mutation safe; it requires the endpoint to remain unexposed or fail closed.

Required: gate reads and mutations with verified identity/provenance and live tenant permissions. Publishing should use a dedicated high-impact permission and, if the approved policy requires it, maker-checker separation. Add missing/forged provenance, unprivileged actor, privilege-withdrawal, forged privileged ID, and cross-tenant negatives.

### ZB-DOC011-02 — High — successful mutations can persist without their audit record

The self-caught ordering fix correctly avoids writing `Allowed` before a failed `TryUpdate`. It does not close audit integrity:

- `MutateAsync` commits with `TryUpdate` and only then writes the `Allowed` audit. If the sink genuinely fails or caller cancellation fires, the changed/published row remains stored while the service returns `audit_unavailable`.
- The resulting error text falsely says the change was not committed.
- `CreateNextVersionAsync` writes `Allowed` before `repository.Add`. Concurrent callers can choose the same next version, both write success audit events, then one insert fails.
- `IDocumentTemplateDraftAuditSink.ExistsAsync` exists specifically to resolve ambiguous writes but is never called.

Required: establish a truthful point-of-no-return protocol. Pre-audit a stable event then commit non-cancellably, or use durable intent/completion/reconciliation semantics. Serialize next-version allocation and do not record `Allowed` for a failed insert. Add genuine sink-failure, cancellation-after-commit, ambiguous-write, and concurrent next-version tests.

## Other checks

- Tenant repository keys and server-resolved tenant context are scoped correctly.
- Corpus outcome is computed internally; empty corpus fails closed.
- Published aggregate mutators are immutable and new versions carry content into a fresh Draft.
- The lost `TryUpdate` race now records Denied rather than Allowed.
- Replayed foundation harness: 36/36 passed; it does not exercise authentication/RBAC or post-commit audit failure.
