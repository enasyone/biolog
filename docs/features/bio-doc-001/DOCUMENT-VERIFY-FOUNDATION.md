# BIO-DOC-001 — Document Verify session and evidence foundation

`api/features/bio-doc-001/Biolog.Features.DocumentVerify` is the provider-neutral
session/evidence domain model this story builds, ahead of the MRZ (BIO-DOC-002), barcode
(BIO-DOC-003), OCR (BIO-DOC-004) and NFC (BIO-DOC-007) engines that will populate it. It is a
plain .NET class library with **no ASP.NET Core dependency** and **no persistence backend** -
see "What this story deliberately does not build" below.

## Model

| Type | Kind | Purpose |
|---|---|---|
| `DocumentSession` | mutable, versioned aggregate | AC01: one Document Verify session; may start pre-person (`PersonId == null`) and later link to exactly one person. |
| `DocumentEvidence` | immutable record | AC02: one source-image evidence object - SHA-256 hash, MIME type, page number, side, source, device-claimed vs server-authoritative timestamps, classification. |
| `DocumentFieldEvidence` | immutable record | AC03: one extracted-field result - raw value, normalized value, confidence, producing engine, optional bounding box, classification. |
| `BoundingBox` | immutable value object | Normalized (0-1) location of a field on its source image. |
| `PersonFieldChangeProposal` | immutable record | AC04: a proposed PERSON field change, inert until explicitly `Accept`-ed by a named actor. |
| `DocumentConflict` | mutable, versioned aggregate | AC05: two or more disagreeing `DocumentFieldEvidence` records for one field, with a `ManualReviewState` lifecycle. |

`IDocumentSessionStore` / `InMemoryDocumentSessionStore` (in `Storage/`) is the reference,
in-process orchestration that ties these together: creates/links/closes sessions, appends
evidence and field evidence with auto-incrementing per-session sequence numbers, raises and
resolves conflicts, and proposes (never applies) PERSON field changes.

## Design decisions worth a reader's attention

### No new canonical identifier type was invented (ADR-0001 §1.2)

ADR-0001 registers exactly six identifier prefixes and is explicit that "a feature story may
not invent a prefix locally." Evidence objects therefore have no standalone `evd_`-shaped id.
Identity within a session is `(document_session_id, sequence_number)` - the same shape
ADR-0001 §1.6 already uses for a business sequence like the card number - plus the evidence's
own content hash, which is a stronger tamper-evident anchor than an opaque id would be anyway.
If a later engine genuinely needs a standalone, cross-session evidence identifier, that is the
amending-ADR conversation ADR-0001 §1.2 describes, not a decision this story makes.

### Identifiers and instants are plain, unvalidated strings

ADR-0001's Consequences say not to re-implement identifier parsing or instant formatting
locally, and no .NET binding of `contracts/js/identifiers.mjs` / `clock.mjs` exists anywhere in
this codebase yet - confirmed by inspecting `api/Biolog.Api/Tenancy/TenantContext.cs`, which
makes the identical choice for the identical reason (documented in its own remarks and in
`docs/API-SHELL.md`). A future story that ports those contracts to .NET is the seam this model
accepts without changing shape.

### AC04 is enforced structurally, not by convention

`PersonFieldChangeProposal.Propose` always returns an unapplied instance; the only way to
obtain an applied one is `Accept(acceptedBy, acceptedAt)`, which requires a non-empty actor,
refuses while `RequiresManualReview` is true, and returns a **new** record rather than mutating
the original. No type in this feature has a method that writes to a Person aggregate - there is
no Person domain module in this codebase yet for one to write to. Applying an accepted proposal
is a Person-domain write, owned by whichever future story builds that aggregate.

### No HTTP endpoints yet

`Biolog.Api.Modules.DocumentModule` registers `IDocumentSessionStore` into DI and maps no
routes. Every tenant-plane write must be constructed from a resolved tenant context
(ADR-0002 §1.5), and no real tenant resolution exists anywhere in this codebase yet -
`TenantContextMiddleware` unconditionally clears the context (BIO-FND-008-AC04). Mapping real
endpoints today would mean either fabricating a tenant context (ADR-0002 Decision 2 forbids
exactly this) or shipping untestable dead code. This mirrors BIO-FND-008's own documented
choice to ship `HealthModule` with no checks registered and `TenantModule` with no real
resolver: the module is registered now as the seam a later story (real tenant resolution and/or
real persistence) plugs HTTP endpoints into, without this domain model changing shape.

### No migration proposal was submitted (ADR-0005)

No persistence layer exists anywhere in `api/Biolog.Api` yet - no `DbContext`, no connection
string, no ORM package reference (confirmed by inspection; `appsettings.json` carries no
database configuration). There is therefore no real schema for this story to propose a change
to. `IDocumentSessionStore` is the seam a future, dedicated persistence story implements against
a real tenant database, proposing its MySQL schema through ADR-0005's propose-then-Kemi-assigns
process at that time.

### `evidence/BIO-DOC-001/` rather than `evidence/features/bio-doc-001/`

`governance/feature-path-ownership.json` templates this story's evidence path as
`evidence/features/bio-doc-001/**`, but this story's own dispatched builder packet
(`automation/inbox/Ifeanyi/NEXT.md`) explicitly instructs "Produce builder evidence under
`evidence/BIO-DOC-001/builder/`." Per the source hierarchy (approved story/AC/DoD outranks the
ownership registry's convenience template) and the identical precedent already recorded for
BIO-FND-007/BIO-FND-008 (`evidence/BIO-FND-008/builder/SUMMARY.md`, "Ownership-template gap"),
the explicit packet instruction is followed. `api/`, `tests/` and `docs/` code for this story
still follows the registry's `.../bio-doc-001/**` template, since nothing in the packet
instructs otherwise for those paths.

## Reproducing the tests

```sh
dotnet test tests/features/bio-doc-001/Biolog.Features.DocumentVerify.Tests/Biolog.Features.DocumentVerify.Tests.csproj
bash tests/foundation/BIO-DOC-001.test.sh
```
