# BIO-OPS-002 builder evidence — blocked

Date: 2026-10-07

The assigned acceptance story cannot truthfully complete on the current accepted product.

## OPS002-F01 — High — tenant selector is not a backend tenant switch

Reproduction by code trace:

1. `web/context/session-context.tsx` calls `startDemoSession(selectedTenant, role)` when the tenant selector changes.
2. `web/lib/session-token.ts` signs the selected `tenantId`; `web/proxy.ts` forwards the signed token.
3. `api/Biolog.Api/Tenancy/DemoSessionTenantResolver.cs` verifies the token but explicitly ignores its `TenantId` claim and always returns the one configured `WebSessionBridge:SyntheticTenantId`.
4. `TenantContextMiddleware` prefers that fixed context over host resolution.

Therefore selecting alpha versus beta changes visible React/mock state but not the backend tenant, binding key, permission tenant, or repository data context. This directly fails AC01 and prevents a genuine AC02 selector-driven isolation journey.

## OPS002-F02 — High — context changes have no audit record

`startDemoSession` only revokes the previous session, issues a replacement token, and writes the `HttpOnly` cookie. Neither it nor `SessionProvider` calls an audit API. The file-backed session revocation record is not an append-only actor/context audit event and contains no old/new tenant or role. AC04 therefore has no implementation to accept.

## Decision needed

These fixes cross the story's owned paths and require architecture/ownership decisions. Route OPS002-F01 to Kemi plus the tenant/session bridge owners, and OPS002-F02 to the session/audit owners. Do not treat existing SIMULATION tests as acceptance proof: they demonstrate cosmetic/local behavior, which is exactly what the story requires us to distinguish from real authorization/data context.
