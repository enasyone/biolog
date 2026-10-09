# BIO-INT-001 security review — Zainab

## Verdict: FAIL (High)

### BIO-INT-001-F01 — MFA challenge code is derivable from public challenge ID

The host registers `InMemoryMfaChallengeProvider` for the live `/api/v1/auth/{providerId}/authenticate` and `/api/v1/auth/mfa/{challengeId}/verify` routes. Its public `ExpectedCodeFor` derives the six-character code as a deterministic SHA-256 prefix of the challenge ID; the challenge ID is returned to the caller when MFA is required. Any caller can therefore compute the correct code and complete required MFA without possessing a second factor. The demo bridge using this helper is acceptable only as an explicitly isolated simulation path; it must not back a real authentication endpoint.

**Required fix:** use a real out-of-band MFA provider for live routes, or make the deterministic provider explicitly demo-only and prevent it from being registered on the production authentication surface. Add an integration test proving an attacker who only knows the challenge ID cannot verify MFA.

Other checks passed: tenant-scoped provider repositories and claim mappings, exact role allow-list, single-use challenge state, audited failures/linking, and no JWT dependency.
