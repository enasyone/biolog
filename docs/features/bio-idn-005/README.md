# BIO-IDN-005 — Duplicate review foundation

As enrolment staff, I need suspected duplicate identities routed to review instead of silently
merged.

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Candidate duplicates can arise from identifiers/contact data and later biometric signals | `DuplicateSignalSource` (`NAME_AND_DATE_OF_BIRTH`, `CONTACT_DATA`, `INSTITUTIONAL_IDENTIFIER_VALUE`, reserved `BIOMETRIC`); `DuplicateReviewService.ScanForPersonAsync` compares a subject against every other person in the resolved tenant on the three signals available today, including a genuinely real `PersonContactPoint` contact-data source (see BIO-IDN-005-QA-01 below) | `DuplicateReviewServiceTests`/`PersonContactPointServiceTests` (AC01 tests), `DuplicateReviewHostChannelTests.cs` (real BIO-IDN-001/BIO-IDN-003-backed adapters and the real contact-point endpoint) |
| **AC02** | Review decisions are human-audited | Every mutating call requires an asserted `X-Actor-Id`; `DecideAsync` requires a non-blank `reason_code` and writes an append-only `DuplicateCandidateAuditEvent` before committing | `DuplicateReviewServiceTests`/`DuplicateCandidateAuditTests` (AC02 tests) |
| **AC03** | Merge is never automatic | No `Merged` status exists; no `ProjectReference` to BIO-IDN-001's project (merge is structurally impossible, not merely undocumented); no method or endpoint anywhere in this feature is named anything like merge | `DuplicateCandidateRepositoryGovernanceTests` (AC03 tests, by reflection and assembly-reference inspection) |
| **AC04** | Cross-tenant duplicate search is prohibited by default | `IPersonAttributeGateway`/`IIdentifierAttributeGateway` wrap already tenant-scoped BIO-IDN-001/BIO-IDN-003 services; `ScanForPersonAsync` accepts no tenant parameter, header or flag that could widen a scan | `DuplicateReviewHostChannelTests.cs` (AC04 real-host cross-tenant proof) |

## Design decisions and scope boundaries

### Applying BIO-IDN-001-QA-01 and BIO-IDN-002-QA-01 from the start

This is the fourth story in a row to apply both of these review-round lessons from the first
commit rather than waiting for a review round to rediscover them:

- `DuplicateCandidate`'s constructor is `internal`; `TenantId`/`PersonIdLow`/`PersonIdHigh` are
  `get`-only (immutable even across a `with` expression).
- `IDuplicateCandidateRepository`/`InMemoryDuplicateCandidateRepository` are `internal`,
  reachable only from this assembly, `Biolog.Api` (DI registration) and this project's own test
  assembly via an explicit `InternalsVisibleTo` grant. `DuplicateReviewService` itself is
  `internal` too.
- The repository independently re-validates identity immutability and initial state at the
  persistence boundary (`InvalidInitialState`, `IdentityImmutable`), not merely trusting
  restricted access - the same dual-boundary discipline every sibling repository in this epic
  applies. `DuplicateCandidateRepositoryGovernanceTests` drives the same attacks BIO-IDN-001's
  probe found (foreign tenant, unrelated identity, a stale-version replace), plus reflection
  tests proving the surface is inaccessible to ordinary production consumers.
- `Domain/IPersonAttributeGateway.cs` and `Domain/IIdentifierAttributeGateway.cs` are the same
  narrow, read-only, host-bridged shape BIO-IDN-002/003/004's own existence gateway established
  - owned by this feature, no `ProjectReference` to BIO-IDN-001's or BIO-IDN-003's project.
  Unlike the existence-only precedent, these two return the actual comparable fields this
  feature's own detection needs (name, date of birth, active identifier values) - never a
  person's lifecycle status or a credential id. `api/Biolog.Api/Modules/BioIdn005Module.cs`
  implements both by wrapping BIO-IDN-001's own already-registered `PersonService.GetAsync`/
  `ListAsync` and BIO-IDN-003's own already-registered `InstitutionalIdentifierService.ListAsync`
  directly - the adapters get tenant scoping "for free" for the identical reason every prior
  gateway's header documents, which is also this story's own AC04 enforcement mechanism (see
  below).

### No canonical id of its own kind

The natural key is `(TenantId, PersonIdLow, PersonIdHigh)` - the two person ids ordered by
`string.CompareOrdinal` (`DuplicateCandidate.OrderPair`) so the unordered pair `{personA,
personB}` always resolves to exactly one record regardless of which order a caller names them
in. This mirrors BIO-TEN-002's `TenantDomain`, BIO-IDN-002's `PersonProfile` and BIO-IDN-003's
`InstitutionalIdentifier` precedent of preferring a natural/composite key over minting a new
ADR-0001 identifier type. Endpoints are nested under the owning person
(`/api/v1/persons/{personId}/duplicate-candidates/...`), matching BIO-IDN-002/003's convention,
and a decision names the SECOND person directly in the path rather than requiring an opaque
candidate id (`.../duplicate-candidates/{otherPersonId}/decisions`).

### AC01: three signals today, one reserved for later

`ScanForPersonAsync` is a staff-triggered comparison (there is no background scheduler anywhere
in this codebase - every sibling story's own README makes the same observation), not an
automatic hook fired on person creation. For the subject person, it compares against every OTHER
person in the resolved tenant:

- **`NAME_AND_DATE_OF_BIRTH`**: an exact, case-insensitive match of trimmed first name, last
  name, and a non-null, equal date of birth. These are identity/demographic attributes, never
  treated as this story's contact-data signal (see BIO-IDN-005-QA-01 below for why an earlier
  attempt to do so was rejected).
- **`CONTACT_DATA`** (BIO-IDN-005-QA-01's fix): a shared, non-blank, normalized email or phone
  value on file in `PersonContactPoint` - this story's own genuinely real contact-data source.
  See BIO-IDN-005-QA-01 below for the full history.
- **`INSTITUTIONAL_IDENTIFIER_VALUE`**: a shared value among the subject's and another person's
  ACTIVE institutional identifiers (same `Type`/`CustomLabel`), read through
  `IIdentifierAttributeGateway`. A RETIRED identifier value is never compared - BIO-IDN-003's own
  "Retire-then-reassign" correction pattern means a retired value being legitimately reused by a
  different person later is that story's own designed behaviour, not a duplicate signal.
- **`BIOMETRIC`** (reserved): no biometric capture story exists anywhere in this codebase yet
  (confirmed by a repo-wide search before designing this feature; the Master Technical
  Specification's own §8 places biometric matching at a separate vendor/engine boundary this
  epic has not built). The enum member and `RaiseOrAugmentAsync`'s signal-accumulation design
  exist so a future story in this same `duplicate-review` conflict group (BIO-BIO-001/002, per
  `governance/feature-path-ownership.json`) can raise a candidate carrying this signal through
  the existing accumulation path without this story needing to guess at that story's own
  matching algorithm - this is the extensibility point, not a stub method or an unused endpoint.

### BIO-IDN-005-QA-01 (Seyi round 1, High) - AC01's contact-data signal was absent

**Finding.** The round 1 submission treated `NAME_AND_DATE_OF_BIRTH` as this story's ENTIRE
answer to AC01's "contact data" requirement, reasoning that PERSON (BIO-IDN-001) has no email/
phone/address field at all. The review correctly rejected this: a name and a date of birth are
identity/demographic attributes, not contact information by any ordinary reading of the term, and
a lower-level implementation note (this story's own README) cannot redefine an approved
acceptance criterion. The offline structural check that was meant to guard AC01 also passed
vacuously - it matched the explanatory doc-comment PROSE arguing for the relabeling, not any
real contact-data field or code path (the same "check matches its own explanatory comment" bug
class documented elsewhere in this codebase's history, this time caught by the reviewer rather
than caught proactively).

**Root cause, in my own words.** Faced with a dependency (BIO-IDN-001) that genuinely lacked the
data AC01 seemed to assume existed, I treated "relabel the closest available data" and "escalate
for a scope decision" as the only two options, and picked the first without seriously
considering a third: build the missing data source myself, inside my own owned scope, exactly
the way BIO-IDN-003 already had to for "institutional identifier" (PERSON has no such field
either, and BIO-IDN-003 didn't ask to add one - it built a sibling aggregate). I had the
precedent already proven in this exact epic and didn't apply it to this story's own gap.

**Fix.** `PersonContactPoint` - a new aggregate owned entirely by BIO-IDN-005, natural key
`(TenantId, PersonId)`, holding the email and/or phone enrolment staff record specifically for
duplicate-matching purposes. It is NOT a communications system of record (never used to actually
contact anyone) and does NOT touch BIO-IDN-001 in any way - no `ProjectReference`, no field added
to `Person`, no reopening of an already-ACCEPTED and merged story. Applied the same governance
discipline as every other aggregate in this project from the start (internal construction,
internal repository, dual-boundary revalidation, `InternalsVisibleTo`). `PersonContactPointService`
applies the identical audit-atomicity discipline (per-person lock, audit-before-commit,
`ExistsAsync` verification, `CancellationToken.None` post-confirm) and the same existence-gateway
check every mutation in this project already uses (`IPersonAttributeGateway.GetAsync` before a
contact point is ever stored, so an orphan or cross-tenant record can never exist). Email is
normalized (trimmed, lower-cased) at write time; phone is normalized (digits-only) at COMPARISON
time in `DuplicateReviewService`, a deliberate simplification that does not attempt
international dialing-code/country-code reconciliation. `ScanForPersonAsync` reads
`IPersonContactPointRepository` directly - no gateway/adapter seam needed, since this aggregate
is owned by the SAME feature project, not a sibling story's store.

Verified with the exact evidence the review's required closure named: positive matching
(`QA01_scan_raises_a_candidate_for_two_people_sharing_an_email_address`), a normalization
boundary (`QA01_scan_raises_a_candidate_for_two_people_sharing_a_phone_number_written_differently`
- same digits, different formatting), nonmatching values
(`QA01_scan_raises_nothing_for_two_people_with_different_contact_data`), cross-tenant exclusion
through the REAL packaged host
(`BIOIDN005_QA01_a_tenant_B_person_sharing_the_same_contact_data_never_surfaces_as_a_candidate_for_a_tenant_A_subject`),
and audit privacy (`Audit_trail_withholds_the_actual_email_and_phone_values` - email/phone are
withheld from `Detail` exactly like every other PII field in this codebase, so
`DuplicateCandidateAuditEventBuilder` still never needs to register a `personal` classification
rank at all).

Signals accumulate on the SAME candidate record rather than each producing its own row: a pair
first raised from a name/date-of-birth match can later also match on an institutional identifier
value without losing the earlier evidence (`SignalSources` is a set,
`A_second_scan_augments_an_existing_pending_candidate_with_a_newly_observed_signal_without_losing_the_first`).
A rescan that finds only an already-recorded signal is a genuine no-op (no version bump, no new
audit event) to avoid audit noise on every routine rescan re-detecting the same evidence.

### AC02: review decisions are human-audited, and so is raising a candidate

Both directions this aggregate ever changes go through the identical audit-atomicity discipline
(ADR-0003 Decision 7) every sibling story in this epic applies: a per-pair lock
(`DuplicateCandidateLockRegistry`), audit-before-commit, `ExistsAsync` verification on an
ambiguous audit-write throw, `CancellationToken.None` on the commit once the audit is confirmed.

- **Raising/augmenting a candidate** is attributed to the staff member who TRIGGERED the scan
  (the match itself is system-computed, but nobody scans without an asserted actor) -
  `duplicate_candidate.raise`/`duplicate_candidate.augment` audit events.
- **Deciding a candidate** (`DecideAsync`) requires a non-blank `reason_code` (mirrors
  BIO-IDN-004's `ReasonRequired` posture for a consequential decision) and an asserted actor;
  `duplicate_candidate.decide` carries the decision's own reason code as the audit event's
  `ReasonCode`. The decision is ALSO recorded directly on the aggregate itself
  (`DecidedAt`/`DecidedByActorId`/`DecisionReasonCode`/`DecisionNotes`), in addition to the
  append-only audit trail, so the current review state is readable without replaying audit
  history.
- A reviewer may call `DecideAsync` again on the same pair to correct an earlier decision (CAS
  via `version`, fully audited either way) - nothing in AC02/AC03 requires a decision to be
  permanently locked once made, and treating it as immutable would make a genuine reviewer
  mistake permanently unrecoverable with no reopen mechanism anywhere in this story's scope.

### Never re-detects a decided pair

Once a candidate has ANY decision (`CONFIRMED_DUPLICATE` or `CONFIRMED_DISTINCT`), a later scan
that would otherwise re-raise or augment it leaves the record entirely untouched instead
(`RaiseOrAugmentAsync` checks `existing.Status != DuplicateReviewStatus.PendingReview` before
doing anything else) - a human already reviewed this pair, and a routine automated rescan must
never silently reopen or relitigate that call. This restriction is only against the SCAN path;
a reviewer can still change a decision by calling `DecideAsync` directly at any time (see above).

### AC03: merge is never automatic - and is structurally impossible, not merely undocumented

The user story's title is "duplicate review **foundation**" - this story routes suspected
duplicates to review and records the review's outcome; it does not execute a merge, automatically
or manually, anywhere. This is enforced at three levels:

- `DuplicateReviewStatus` has exactly three members (`PENDING_REVIEW`, `CONFIRMED_DUPLICATE`,
  `CONFIRMED_DISTINCT`) - no `MERGED` state exists to transition into.
- `api/features/bio-idn-005/Biolog.Features.DuplicateReview.csproj` has no `ProjectReference` to
  BIO-IDN-001's project at all - so nothing in this feature can call into
  `Biolog.Features.PersonRegistry.PersonService` to mutate, archive or delete a PERSON even if
  someone tried; a merge is impossible by construction, not merely absent by choice.
  `This_project_has_no_assembly_reference_to_BIO_IDN_001s_PersonRegistry_project` proves this
  adversarially by inspecting the compiled assembly's own referenced-assembly list.
- No method, endpoint or type anywhere in this project is named anything resembling "merge" -
  proven both by an offline structural grep (`BIO-IDN-005.test.sh`) and a reflection test
  (`No_type_in_this_project_defines_a_method_or_endpoint_named_anything_like_merge`).

A `CONFIRMED_DUPLICATE` decision is routing information for a separate, wholly manual operational
process outside this system's automation - exactly matching the Master Technical Specification
§4's own framing: "Suspected duplicates go to human review; no silent merge."

### AC04: cross-tenant duplicate search is prohibited by default

Every candidate signal this service reads comes through `IPersonAttributeGateway`/
`IIdentifierAttributeGateway`, both of which wrap an already tenant-scoped sibling service
(BIO-IDN-001's/BIO-IDN-003's own `ICurrentTenantProvider`-scoped stores). A person that exists
only in a different tenant is therefore never returned by `ListOthersAsync`, and
`IPersonAttributeGateway.GetAsync` resolves a subject id belonging to a different tenant as
not-found - identical to BIO-IDN-002/003/004's existence-gateway behaviour and ADR-0001 §1.5's
no-enumeration-oracle rule (missing and foreign-tenant subjects both fail as `person_not_found`,
404). `ScanForPersonAsync` accepts no tenant parameter, header or flag that could widen a scan
beyond the resolved tenant - "by default" in the AC's wording leaves room for a FUTURE,
separately-authorised admin capability; this story does not build one.
`DuplicateReviewHostChannelTests.cs` proves this through the REAL packaged host: a tenant-B
person who genuinely shares the same name and date of birth as a tenant-A subject never surfaces
as a candidate when scanning that subject.

### Audit trail privacy

This feature's own audit `Detail` never carries a compared PII value (a name, date of birth,
email or phone) - only person ids (canonical identifiers carry no authority, ADR-0001 §1.5) and
the WIRE LABEL of which signal matched (e.g. `"NAME_AND_DATE_OF_BIRTH"`, `"CONTACT_DATA"`), which
says what KIND of evidence was found without saying what the matching value actually was.
`PersonContactPointService` withholds `"email"`/`"phone"` from every `person_contact_point.set`
event's `Detail` unconditionally, mirroring BIO-IDN-001's identical posture for name/date of
birth. `DuplicateCandidateAuditEventBuilder` (shared by both `DuplicateReviewService` and
`PersonContactPointService` - one unified audit trail for this whole feature) registers only
`public`/`internal` classifications - deliberately NOT `personal`/`sensitive`/`secret` - so an
accidental future attempt to pass an actual PII value into `detail` fails closed with
`invalid_classification` rather than silently accepting it (the same
fail-closed-on-unregistered-classification discipline every sibling audit builder in this epic
applies).

### Lock scope reasoned explicitly, not assumed by analogy (BIO-IDN-003-QA-01's lesson)

BIO-IDN-003-QA-01 found that a per-person lock was the wrong scope for an invariant
("uniqueness") that actually spanned the whole tenant population. Before writing
`DuplicateCandidateLockRegistry`, this story asked the equivalent question up front rather than
defaulting to the narrowest available key: a candidate PAIR's own invariants (at most one record
per pair, CAS on its decision) are scoped to exactly that pair - nothing about raising, augmenting
or deciding one candidate constrains any OTHER pair, so a single per-pair lock
`(TenantId, PersonIdLow, PersonIdHigh)` is the correct and sufficient scope here. This is
documented directly in `DuplicateCandidateLockRegistry`'s own header and verified by
`Two_genuinely_concurrent_scans_of_the_same_pair_never_produce_two_candidate_records`.

### Organisation/campus/branch, RBAC and pagination

Out of scope, matching BIO-IDN-002/003/004's identical choices for the same reasons: no
organisational hierarchy registry exists yet; no RBAC enforcement beyond the asserted
`X-Actor-Id` header, pending BIO-INT-001; no pagination on the list endpoint.

### Tenant context seam

Identical shape to every sibling identity-wave story's own `Tenancy/ICurrentTenantProvider.cs`
(duplicated, not shared). Ships the real `Biolog.Api` wiring in-story
(`api/Biolog.Api/Modules/BioIdn005Module.cs`, `Program.cs` registration, a thin adapter reading
the already-shipped `ITenantContextAccessor`) rather than deferring it, per ADR-0002 Decision 6's
per-channel accommodation-ending rule.

## Deliberately out of scope for this story

- Executing a merge of any kind, manual or automatic (AC03; see above) - a future story, if the
  product ever wants one, would need its own explicit AC and its own `ProjectReference` decision.
- Biometric signal detection itself (BIO-BIO-001/002; `DuplicateSignalSource.Biometric` is
  reserved, not produced, by this story).
- Organisational hierarchy targeting (no registry exists yet).
- RBAC enforcement on who may scan or decide (BIO-IDN-006), pending BIO-INT-001.
- Pagination on the list endpoint.
- A "reopen" endpoint distinct from calling `DecideAsync` again (see AC02 above).
