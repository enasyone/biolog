# BIO-BIO-006 — Implement fingerprint 1:N duplicate/identification search

Implements the BIO-BIO epic's sixth story: 1:N fingerprint search for duplicate detection and
identification, without automatic identity merge (user story, verbatim: "As authorised biometric
workflows, I need 1:N fingerprint search for duplicate detection/identification without automatic
identity merge.").

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Search returns ranked candidates and scores | `Service/FingerprintSearchService.SearchAsync` compares the live probe against every gallery-indexed identity in the tenant, orders by score descending, caps at `maxCandidates` - see `Domain/FingerprintSearchResult.cs`/`Domain/FingerprintSearchCandidate.cs` | `FingerprintSearchServiceTests.AC01_*` |
| **AC02** | Permissions distinguish 1:N from 1:1 | `Domain/IBiometricSearchPermissionGateway.cs`; `SearchAsync` requires the actor to hold `FingerprintSearchService.SearchPermissionKey` ("biometric.fingerprint_search_1n") - a genuinely separate key from whatever a 1:1 flow might require | `FingerprintSearchServiceTests.AC02_*` |
| **AC03** | Possible duplicate routes to human review | Every candidate whose score meets the tenant's policy-resolved duplicate threshold is raised/augmented onto this feature's own `Domain/FingerprintDuplicateReviewEntry.cs` queue; a decided pair is never reopened by a later search; `DecideAsync` records a human decision | `FingerprintSearchServiceTests.AC03_*` |
| **AC04** | No automatic PERSON merge occurs | This project has no `ProjectReference` to BIO-IDN-001 at all - structurally cannot call into `PersonService`; `FingerprintDuplicateReviewStatus` has no `Merged` member | `FingerprintSearchServiceTests.AC04_*` |
| **AC05** | Search scope is tenant constrained | `IFingerprintGalleryStore.GetAllForTenantAsync`/`IFingerprintDuplicateReviewRepository` are both keyed by tenant; `SearchAsync` accepts no parameter that would widen a search beyond the resolved tenant | `FingerprintSearchServiceTests.AC05_*` |

## Design decisions and scope boundaries

**A separate search "gallery", not BIO-BIO-001's per-identity template store.** BIO-BIO-001's own
`Matching.IBiometricTemplateStore` only supports "get every template for ONE (tenant, person)" -
it has no way to enumerate every enrolled identity in a tenant, which a 1:N search fundamentally
needs. Extending that interface would modify BIO-BIO-001's own owned files, out of this story's
write scope (`governance/feature-path-ownership.json`). This story instead defines its own
`Domain/IFingerprintGalleryStore.cs` - the same "search gallery distinct from a canonical
per-identity reference store" architecture a production AFIS/1:N system commonly uses anyway
(a gallery optimized for search is not usually the same store as a per-identity verification
reference), not merely a workaround.

**The gallery is populated by an explicit `IndexAsync` call, not automatically from BIO-BIO-002's
enrolment flow.** BIO-BIO-002 (the approved story's own declared dependency) is in the identical
situation BIO-BIO-004's own README already documents for itself: `FingerprintEnrolmentService`/
`IFingerprintEnrolmentRepository` are internal, Singleton-registered, and expose no
query-by-claimed-identity at all (only get-by-session-id), and captured templates live embedded
in BIO-BIO-002's own session aggregate, never published anywhere queryable. Reaching into
BIO-BIO-002's owned paths to add such a query, or hooking this story automatically into its
enrolment-completion path, is out of this story's write scope. `Service.FingerprintSearchService.IndexAsync`
is the explicit registration step a caller (an operator, or a later orchestration story) invokes
to add an identity to the searchable gallery.

**Duplicate review is this story's own queue, not BIO-IDN-005's.** BIO-IDN-005 ("duplicate review
foundation") already anticipates this exact need - its own `DuplicateSignalSource.Biometric` enum
member is documented as "registered now, deliberately unreachable... so a FUTURE biometric-
matching story can raise a candidate carrying this value through `DuplicateReviewService`'s
existing internal signal-accumulation path." That path, `RaiseOrAugmentAsync`, is `private` - not
even `internal` - so no `ProjectReference`, however direct, could reach it; there is no public
entry point for an external story to raise a `Biometric`-sourced candidate, and adding one would
modify BIO-IDN-005's own owned files, out of this story's write scope. AC03 itself only requires
"possible duplicate routes to human review," not specifically to BIO-IDN-005's own queue, so this
story satisfies it with its own self-contained `FingerprintDuplicateReviewEntry`/
`IFingerprintDuplicateReviewRepository`, deliberately mirroring BIO-IDN-005's own
`DuplicateCandidate` shape (ordered-pair natural key, signals-accumulate-never-reopen-a-decided-
pair discipline, identical wire strings on the status enum) so a later story that DOES get a
public BIO-IDN-005 entry point can migrate/merge the two queues without a vocabulary mismatch.

**BIO-BIO-006-QA-01 (Seyi round 1, High).** AC01's `maxCandidates` cap on the RETURNED ranked list
and AC03's "route every possible duplicate to review" are two independent concerns. An earlier
version computed which candidates were `PossibleDuplicate` and routed them to review AFTER
applying `Take(maxCandidates)` to the score-sorted list, so an above-threshold candidate crowded
out of the top `maxCandidates` (e.g. a caller requesting only the single best match while several
candidates score above the duplicate threshold) never reached the review queue at all - a
possible duplicate silently never routed anywhere, exactly what AC03 exists to prevent. Fixed by
computing review routing from the full, uncapped scored set BEFORE the cap is applied to the
returned list; the two lists (what the caller sees vs. what gets escalated) can now legitimately
differ in size, and do so deliberately.

**BIO-BIO-006-QA-02 (Chioma acceptance, High).** `RaiseOrAugmentReviewAsync`'s own read-check-write
(read the entry, then either create or replace it) was not atomic on its own: two simultaneous
searches that both first observe the SAME (subject, candidate) pair as a new possible duplicate
both read "no existing entry" and both attempt to create one - the repository's own uniqueness
check correctly rejects the second write, but nothing in the service caught and converged that,
so one of two entirely legitimate concurrent callers received an unhandled exception instead of
the same successfully-raised entry every concurrent observer of the same real-world event should
converge on. Fixed with a dedicated `Domain/FingerprintDuplicateReviewLockRegistry.cs`, the same
per-pair `SemaphoreSlim` shape BIO-IDN-005's own `DuplicateCandidateLockRegistry` uses for its
structurally identical aggregate - acquired across the whole read-check-write sequence in both
`RaiseOrAugmentReviewAsync` and `DecideAsync` (the latter's own read-check(version)-write has the
identical shape of race). Two callers acting on two DIFFERENT pairs still proceed fully in
parallel; only operations on the SAME pair are serialized.

**AC02's "distinguish 1:N from 1:1" is satisfied by a genuinely separate permission key, not a
shared one with a flag.** BIO-BIO-004 (1:1 fingerprint verification, already accepted and merged)
enforces no permission check at all today - a pre-existing scope decision from that story, out of
this story's write scope to retrofit. "Distinguish" therefore means: this story's own 1:N search
permission (`biometric.fingerprint_search_1n`) is a key an operator must grant independently,
never implied by any other grant - proven by `FingerprintSearchServiceTests.AC02_holding_an_unrelated_permission_key_does_not_authorise_1n_search`.

**Provider extraction failures are NOT caught into a typed outcome here (unlike BIO-BIO-004/005).**
Neither AC01 nor any other AC in this story asks for a persisted per-attempt evidence record the
way BIO-BIO-004-AC02/BIO-BIO-005-AC01 do, and a search over a population is a fundamentally
different shape of operation from a single 1:1 decision (there is no single "this attempt" record
to attach an acquisition-failure outcome to - a search either has a probe to compare with every
gallery member, or it does not proceed at all). An extraction failure from `IFingerprintProvider.ExtractTemplateAsync`
(e.g. `ArgumentException` for an empty/unusable live sample) is treated as a genuine caller/
integration error and propagates - a deliberate scope decision, not an oversight, and not a
repeat of BIO-BIO-004-F01 (that finding was about a persisted DECISION record silently losing an
attempt; nothing here persists a per-search "outcome" a failure could silently omit).

**No HTTP endpoint is mapped by this story**, the same judgment call every prior BIO-BIO story
made: AC01-AC05 are about the search/review model, not an HTTP surface, and the service takes a
caller-resolved tenant/person/actor id rather than resolving one itself.

**Identifiers are plain strings**, the same explicitly documented choice every prior BIO-BIO story
made; no `identifier-registry.json` prefix is registered for a search result or a review entry
(frozen, ADR-governed contract, out of this story's `read_only_shared_contracts` scope).

**No separate audit sink.** None of this story's five ACs mention auditing (the same explicit
scope call BIO-BIO-003 documents for itself) - the persisted `FingerprintDuplicateReviewEntry`
records themselves (with `Score`/`Status`/`DecidedAtUtc`/`DecidedByActorId`) already constitute
the durable evidence trail AC03's review workflow needs.

**Every sample byte array in this suite is synthetic filler**, never a captured fingerprint image
(this story's own top-level constraint: "Use synthetic data only").
