# BIO-BIO-001 — Biometric service/provider abstraction

Implements the BIO-BIO epic's foundation story: provider-neutral fingerprint/face contracts so
vendor SDKs never leak into core identity logic (user story, verbatim).

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Fingerprint and face provider interfaces are defined | `Providers/IFingerprintProvider.cs`, `Providers/IFaceProvider.cs` — two distinct interfaces (not one interface parameterised by modality), each extending `Providers/IBiometricProvider.cs` | `ProviderContractTests.AC01_fingerprint_and_face_provider_interfaces_are_distinct_types` and the synthetic-implementation tests |
| **AC02** | Provider-specific templates/identifiers are isolated | `Providers/ExtractedBiometricFeatures.cs` and `Domain/BiometricTemplate.cs` carry no vendor/provider-internal identifier field of any kind — a provider is context-free (never sees tenant/person, never mints an id); only `Service/BiometricService.cs` turns extracted features into a tenant/person-scoped canonical template | `BiometricTemplateTests` (reflection over both types' property lists) |
| **AC03** | Biometric service is separate from MySQL matching logic | `Service/BiometricService.cs` depends only on `Matching/IBiometricTemplateStore.cs` and `Matching/IBiometricMatcher.cs` — no connection string, ORM context or SQL statement anywhere in this project (no NuGet package references at all, see the `.csproj` remarks) | `BiometricServiceTests.AC03_BiometricService_has_no_dependency_of_any_kind_on_a_SQL_or_MySQL_client` plus the enrol/verify round-trip tests |
| **AC04** | Provider health and model/version metadata are supported | `Domain/BiometricProviderHealth.cs` (`Status` + `ModelVersion`), returned by `IBiometricProvider.GetHealthAsync` and aggregated by `BiometricService.GetProviderHealthAsync`; `BiometricTemplate.ProviderVersion` records which provider version produced an enrolled template | `ProviderHealthTests` |
| **AC05** | No real biometric data is required for local development | `Synthetic/SyntheticFingerprintProvider.cs`, `Synthetic/SyntheticFaceProvider.cs` (SHA-256 of the sample payload — deterministic, no vendor SDK), `Synthetic/InMemoryBiometricTemplateStore.cs`, `Synthetic/SyntheticBiometricMatcher.cs` (exact-payload-equality — makes no accuracy claim) | `SyntheticDataOnlyTests`, and every other test in this suite runs against these synthetic implementations exclusively |

## Design decisions and scope boundaries

**Providers are context-free.** `IFingerprintProvider`/`IFaceProvider.ExtractTemplateAsync` takes
only a `BiometricSample` and returns `ExtractedBiometricFeatures` (opaque payload + provider
version) — no tenant id, no person id, no template id. Only `BiometricService`, which the caller
gives a tenant/person id, turns extraction output into a tenant/person-scoped
`Domain.BiometricTemplate`. This is what makes AC02's isolation structural rather than a
convention a provider author has to remember: there is no field on the shared contract a
provider could use to smuggle a vendor identifier through even if it wanted to.

**No HTTP endpoint is mapped by this story**, the same judgment call BIO-DOC-001 made for
`DocumentModule`: AC01–AC05 are about the provider/service contract, not an HTTP surface, and
`BiometricService` takes tenant/person ids as parameters rather than resolving them itself, so a
later story (biometric enrolment endpoints) maps real HTTP routes onto it without this feature's
shape changing. `Modules/BioBio001Module.cs` registers the four synthetic implementations and the
service into the host's DI container (`BIO-FND-008-AC05` extension seam) with no mapped routes.

**Identifiers are plain strings**, not a validated implementation of ADR-0001's canonical-id
contract — the same explicitly documented choice `BIO-DOC-001`'s `DocumentSession` made. No
`identifier-registry.json` prefix is registered yet for a biometric template (that registry is a
frozen, ADR-governed contract this story's `read_only_shared_contracts` list forbids touching);
minting one is out of scope here and left as the seam a later, Kemi-approved amendment fills
without this type's shape changing.

**Matching accuracy is explicitly out of scope.** `SyntheticBiometricMatcher` compares templates
by exact payload equality — enough to prove `BiometricService`'s enrol/verify orchestration works
end to end, and nothing more. A later story implements `IBiometricMatcher` with a real similarity
algorithm (and, separately, `IBiometricTemplateStore` against a real datastore) without this
service's orchestration code changing — that is the entire point of AC03's separation.

**No data is written under `tests/features/bio-bio-001/` or `evidence/features/bio-bio-001/`
containing real biometric material.** Every sample byte array in this suite is synthetic filler
(`new byte[] { 1, 2, 3 }` and similar), never a captured fingerprint or face image (AC05).

## BIO-BIO-001-QA-01 (Seyi round 1, High) — mutable payload arrays aliased across the boundary

**Finding.** `BiometricTemplate.Payload`, `ExtractedBiometricFeatures.Payload` and
`BiometricSample.Payload` all exposed the caller's/provider's exact `byte[]` reference, and
`BiometricTemplate.Create` stored the input array without cloning. An independent probe
reproduced: enrol a sample, mutate a byte of the returned `BiometricTemplate.Payload`, then read
the same template back from `InMemoryBiometricTemplateStore` — the stored candidate had changed,
because the returned array and the stored instance's array were literally the same object. That
let a caller silently change what later verification matches against, without a provider or store
operation and without re-enrolment — a data-integrity/tamper violation of AC02's isolation intent.

**Fix.** All three types now hold a private backing array and clone on both write (`Create`/
`init`) and read (the `Payload` getter): `(byte[])value.Clone()` in, `(byte[])_payload.Clone()`
out. A caller mutating an array it passed in, or an array it read back, can no longer reach the
template/features/sample's own internal state. `tests/features/bio-bio-001/BiometricTemplateTests.cs`
and `BiometricServiceTests.cs` reproduce the reviewer's exact enrol-then-mutate-then-read-back
scenario (`QA01_mutating_an_enrolled_templates_Payload_after_the_fact_does_not_alter_the_stored_candidate`)
alongside both directions on `BiometricTemplate.Create`/`.Payload` in isolation.

## BIO-BIO-001-F02 (Zainab round 3, High) — delimiter-joined store key allowed tuple collisions

**Finding.** `Synthetic/InMemoryBiometricTemplateStore` keyed records with a delimiter-joined
string (`tenantId + '\0' + personId + '\0' + modality`). `BiometricTemplate.Create` and
`BiometricService` accept any non-empty string, including one containing the delimiter itself, so
two DIFFERENT identity tuples could join to the SAME string — e.g. `tenantId="a\0b", personId="c"`
and `tenantId="a", personId="b\0c"` both joined to `"a\0b\0c\0Fingerprint"` — crossing the tenant
boundary: a template saved under one tuple would be returned to a lookup under the other.

**Fix.** The dictionary key is now a genuine `ValueTuple<string, string, BiometricModality>`
instead of a joined string — its structural equality compares each component independently, so
there is no delimiter for any input to collide through, regardless of what characters a
tenant/person id contains.
`InMemoryBiometricTemplateStoreTests.F02_embedded_delimiter_characters_in_tenant_or_person_id_never_collide_across_distinct_tuples`
reproduces the reviewer's exact colliding tuples and proves each keeps its own, separate list.
