# BIO-TEN-002 independent adversarial review

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed implementation `ee09e07aabe52611c2044121898576559e6b1c97` at builder evidence head `a6ecae6`.

## BIO-TEN-002-QA-01 — High — the shipped host channel is neither a Host channel nor shipped

ADR-0002 Decision 1.1 permits the `host` channel only from a verified `TenantDomain` matching the request hostname and explicitly says a query parameter is not a tenant channel. Decision 6 then names BIO-TEN-002 as the story that ends the interim accommodation by shipping the real production host resolver.

The implementation does the opposite at its only HTTP resolution boundary:

- `TenantDomainEndpoints.ResolveAsync` reads `Request.Query["host"]`, a caller-controlled query parameter, instead of `Request.Host`.
- `BioTen002Module` is not registered in `api/Biolog.Api`, and `TenantDomainService.ResolveAsync` is not connected to the shipped `TenantContextMiddleware`.
- The integration tests self-host the feature module directly and explicitly do not exercise `Biolog.Api/Program.cs` or the real tenant-context pipeline.

Therefore a request to the packaged API under a verified custom hostname still cannot resolve a tenant, while the endpoint described as the “real host channel” accepts a source the governing ADR expressly excludes. This blocks the user story and the real shipped-host positive path required at BIO-TEN-002.

### Reproduction

```sh
rg -n 'Request.Query\["host"\]' api/features/bio-ten-002/Api/TenantDomainEndpoints.cs
rg -n 'BioTen002Module|TenantDomainService' api/Biolog.Api --glob '*.cs'
```

The first command finds the query-param resolver. The second finds no production registration or host-channel integration.

Required closure: resolve the host claim from the real request authority, register the module in the shipped host through the approved shared-code integration process, connect it to tenant-context resolution, and prove a verified hostname produces a resolved context while unknown/unverified hostnames fail closed. A diagnostic query endpoint may exist, but it is not the ADR host channel and must not be described or consumed as one.

## BIO-TEN-002-QA-02 — High — malformed authority suffixes alias a verified hostname

`HostNormalizer.Normalize` truncates at the first colon without validating that the removed suffix is a legal terminal numeric port. Consequently all of these normalize to `portal.example.edu.ng` and resolve its verified tenant:

- `portal.example.edu.ng:evil`
- `portal.example.edu.ng:`
- `portal.example.edu.ng:443.attacker.invalid`

This violates AC02's exact matching and ADR-0002's rule that hostname normalization is a strict security boundary. The parser must not turn malformed or attacker-controlled suffixes into a different verified authority.

### Reproduction

```sh
dotnet run --project evidence/features/bio-ten-002/codex/HostExactnessProbe/HostExactnessProbe.csproj --nologo
```

The independent probe registers and verifies `portal.example.edu.ng`, then expects each malformed input to return `host_not_recognised`; current source accepts all three. The local .NET command stalled in this reviewer sandbox after emitting the platform CSSM diagnostic, so no runtime PASS is claimed; the alias is also source-deterministic at `host = host[..colonIndex]` before exact dictionary lookup.

Required closure: remove only a syntactically valid terminal port (or consume an already-validated host component from the HTTP stack) and reject every other colon-bearing form. Add negative tests for empty, nonnumeric, signed, overflow, and suffix-bearing ports.

## Passing evidence

- AC01 record shape: PASS.
- AC03 status gating and indistinguishable unknown/unverified error: PASS at the service boundary.
- AC04 atomic hostname-keyed duplicate prevention: PASS for the current singleton in-memory tier.
- AC05 DNS deployment fields absent from domain/request/response shapes: PASS.
- Offline structural suite: 29/29 PASS.
- Repository traceability inventory: PASS, 18 implemented stories.
- Builder evidence records 74/74 feature tests; independent .NET execution was unavailable as described above.

Functional FAIL; Security FAIL; Tenant Isolation FAIL; Offline/Recovery NOT APPLICABLE; Concurrency PASS; Regression FAIL.
