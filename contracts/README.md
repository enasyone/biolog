# contracts/

Shared BIOLOG contracts. **Owner: Kemi — Chief Architect & Integrator.**

Everything here is a cross-cutting primitive that feature modules must consume rather
than re-invent. Changing a file in this directory changes every module at once, so it is
governed rather than edited.

## What is here

| Path | Contract | Defined by |
|---|---|---|
| `schemas/identifier-registry.json` | The registered identifier types and their prefixes | ADR-0001 §1.2 |
| `schemas/canonical-id.schema.json` | Wire form of a canonical identifier | ADR-0001 §1.1 |
| `schemas/utc-instant.schema.json` | Wire form of a timestamp | ADR-0001 §2.1 |
| `schemas/calendar-date.schema.json` | A date with no instant (`*_on` fields) | ADR-0001 §2.2 |
| `schemas/versioned-resource.schema.json` | Envelope every mutable aggregate carries | ADR-0001 §3.1 |
| `schemas/event-envelope.schema.json` | Envelope every domain event carries | ADR-0001 §3.6 |
| `schemas/offline-mutation.schema.json` | Envelope every queued offline mutation carries | ADR-0001 §3.5 |
| `js/identifiers.mjs` | Mint, parse, type-check and store identifiers | ADR-0001 §1 |
| `js/clock.mjs` | UTC instants and explicit local rendering | ADR-0001 §2 |
| `js/concurrency.mjs` | Compare-and-set, idempotency, sync conflict classification | ADR-0001 §3 |
| `schemas/tenant-domain.schema.json` | Verified hostname → tenant mapping | ADR-0002 §1.3 |
| `schemas/tenant-data-binding.schema.json` | Tenant → database binding, looked up not supplied | ADR-0002 §4.1 |
| `schemas/tenant-context.schema.json` | The resolved, frozen tenant context | ADR-0002 §1, §3 |
| `schemas/tenant-access-denial-event.schema.json` | Append-only audit for a denied access | ADR-0002 §5 |
| `js/tenant-context.mjs` | Resolve tenancy, route to a database, deny and audit cross-tenant access | ADR-0002 |
| `schemas/data-classification.json` | The five classification levels and the redaction rules | ADR-0003 §3 |
| `schemas/audit-event.schema.json` | One audit envelope for every audited action | ADR-0003 §1 |
| `js/audit.mjs` | Build audit events, redact by allowlist, deduplicate deliveries | ADR-0003 |
| `schemas/problem-response.schema.json` | One HTTP error envelope for the platform | ADR-0004 §2 |
| `schemas/validation-error-item.schema.json` | One entry in a validation failure's `errors[]` | ADR-0004 §5.3 |
| `schemas/paginated-list-response.schema.json` | Cursor-paginated list envelope | ADR-0004 §5.1 |
| `js/api.mjs` | API versioning constants, problem responses, write-endpoint idempotency, pagination, filter allowlisting | ADR-0004 |
| `schemas/migration-proposal.schema.json` | A feature agent's proposed schema change; carries no sequence number | ADR-0005 §1.1 |
| `schemas/migration-record.schema.json` | A proposal Kemi has assigned a sequence number to; immutable | ADR-0005 §1.1, §3.3 |
| `schemas/migration-registry.schema.json` | The single ordered ledger of assigned records | ADR-0005 §2 |
| `js/migrations.mjs` | Propose, assign, and prove fresh-create matches upgrade-from-previous | ADR-0005 |

The JSON Schemas are the language-neutral source of truth. `js/` is the reference
implementation and the executable specification of the rules the schemas cannot express
(clock-skew handling, conflict classification, idempotent replay). Bindings for .NET and
Flutter are generated from the same schemas by the stories that introduce those surfaces;
they must not restate the rules by hand.

## Rules for every other story

1. **Consume, do not copy.** Import from `contracts/js` or a generated binding. A feature
   module that writes its own identifier regex or its own timestamp format has forked the
   contract, and the fork will not be caught until integration.
2. **Do not add an identifier type** without a registry entry, an amending ADR and Kemi's
   recorded approval. Prefixes are permanent once data exists.
3. **New mutable aggregate** → it carries `version` and `updated_at`. **New append-only
   record** → it carries neither and rejects updates.
4. **Denying is asynchronous.** `resolveTenantContext`, `bindingForRepository` and
   `assertRecordInTenant` return promises and must be awaited: they wait for the audit
   write to settle before refusing, so a denial can never reach a caller before its record
   reaches the store. Pass an `audit` sink to anything that can deny — without one, the
   denial fails closed as `audit_unavailable` rather than returning unaudited.
5. **Build audit events through `buildAuditEvent`**, never by hand, and declare an
   allowlist for each audited action — no allowlist means no fields, not all of them. A
   `secret` value never enters an envelope in any form, and hashing a low-entropy personal
   value is not redaction.
6. **Consumers deduplicate on `(tenant_id, event_id)`** and treat a fingerprint mismatch as
   `event_id_reuse` to surface, never a duplicate to drop.
7. **Serialise every HTTP error through `toProblemResponse`/`toValidationProblem`.** They
   duck-type on `.code`/`.status`/`.message`/`.detail`, so an existing `TenantContextError`,
   `ConcurrencyError` or `AuditError` needs no change to conform. A validation error's field
   path is built with `escapeSegment`, never hand-joined with `.` — that is ADR-0003's QA-04
   fix, reused, not re-derived.
8. **Never resolve tenancy yourself.** Take a resolved context, open repositories through
   `bindingForRepository`, and check records with `assertRecordInTenant`. A tenant
   identifier arriving in a request field is not a tenant claim, and every story touching
   tenant data ships a cross-tenant negative test.
9. **Propose a migration; never assign yourself a sequence number.** Write a
   `migration-proposal.schema.json`-shaped proposal and hand it to Kemi. A breaking change
   names the exact target it removes (`forward_safety.breaks`), matching an earlier record's
   declared `deprecation_window.target`; every record carries a `down` script or an explicit
   `reversible: false` plus `repair_procedure`; attach `proofs` from your own
   `verifyFreshCreateMatchesUpgrade` run as a self-check. `assignSequence` runs the real
   fresh-create/upgrade-from-previous check itself, over `applyUp`/`hashState`/`priorState`
   supplied at assignment time, and refuses without them — this proves convergence
   *conditional on that runner faithfully interpreting the migration*, a fact this contract
   cannot itself verify (`up` is opaque by design); choose it with the same care
   `lockHeldBy`/`assigned_by` already demand. Assignment only happens against
   `openMigrationRegistry()` — the one real registry, with no caller-choosable id.
   `createMigrationRegistryForTests` exists for test isolation only; never import it from
   production code.
10. These files **freeze at W01 exit**. After that a change needs a superseding ADR.

## Running the contract tests

No dependencies and no network:

```sh
node --test contracts/js/
```

All fixtures and examples in this directory are SYNTHETIC.
