# TD-PROVENANCE-DUMMYKEY-SWEEP security review

**Targeted sweep verdict: PASS**  
**Separate new finding: BrandAssets tenant dummy hash is unsafe**

## Thirteen-file sweep

Reviewed all commits supplied by Busola:

`b509cef`, `d7a6ead`, `91e6336`, `ed120e1`, `6c1c979`, `f104cde`, `3a2f74d`, `e847e85`, `7e1e8ce`, `06a74c0`, `7921c7d`, `b50ea11`, `390b8ef`.

Each commit changes only the unconfigured fallback initializer (plus two now-correct comments):

```csharp
RandomNumberGenerator.GetBytes(32)
```

The twelve `HmacActorProvenanceStore` copies and BIO-INT-001 `HmacSessionTokenCodec` now use a process-random unknown fallback. Configured behavior, interfaces and constant-time comparisons are unchanged. A full-tree scan found no remaining `no-provenance-secret-configured`, `no-session-token-secret-configured`, or `DummyKey = SHA256(...)` occurrence. The original public deterministic HMAC/session-token forgery class is closed on `develop`.

## Separately flagged dummy credential hashes

The six device credential stores are safe against the public dummy literal: although the presented secret `dummy-hash-for-timing-defense-only` makes the constant-time hash comparison true for an unknown device, each implementation returns `matches && _hashesByDeviceId.ContainsKey(deviceId)`. An unregistered device therefore still fails authentication. These are comparison timing targets, not signing/authentication keys.

### New finding — High — BrandAssets unknown tenant can match its deterministic dummy

`api/Biolog.Api/Features/BrandAssets/TenantCredentialStore.cs` differs from the device stores. For an unknown tenant it compares the presented secret hash to:

```text
SHA256("\0no-such-tenant\0" + tenantId)
```

and returns the comparison result without also requiring tenant existence. Supplying that exact public preimage makes `Validate(tenantId, secret)` return true for an unregistered canonical tenant. Current HTTP header parsing may reject embedded NUL characters, but the credential boundary itself is fail-open and its “never-matching” comment is false; security must not depend on incidental transport character rejection.

Required: retain the constant-time comparison but return `matches && _secretHashes.ContainsKey(tenantId)` (or use an unknowable random dummy). Add a direct store/authenticator regression using the published former dummy preimage for an unknown canonical tenant.

## Verification

- Inspected every commit diff and enumerated all twelve provenance-store definitions plus the session-token codec.
- Full-tree searches for old fallback literals and deterministic `DummyKey` initializers returned no matches.
- Inspected all six device credential dummy-hash implementations and BrandAssets tenant credential validation through their return conditions.
