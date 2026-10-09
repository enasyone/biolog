# BIO-PHO-001 independent review — Seyi

Date: 2026-09-22
Verdict: FAIL

Structural suite: `bash tests/features/bio-pho-001/BIO-PHO-001.test.sh` — 32 passed, 0 failed.

## Finding BIO-PHO-001-F01 — immutable photo content is mutable through exposed byte arrays (High)

`PhotoMaster.Content` and `PhotoDerivative.Content` expose the constructor's mutable `byte[]`
directly. The internal repository stores the same object. A caller can mutate a returned master
after append, changing the supposedly immutable source content and all future derivative inputs;
the same issue allows a regenerated derivative's bytes to be changed without regeneration. This
violates AC01 immutable master, AC02/AC03 master-reference integrity, and the story's data
integrity requirement.

### Reproduction

1. Append a synthetic `PhotoMaster` with content `{ 1, 2, 3 }`.
2. Mutate `master.Content[0]` to `255`.
3. Read the same version back from `InMemoryPhotoMasterRepository`.
4. The repository returns `{ 255, 2, 3 }`, because it retained the same array reference.

The constructor also does not clone its input, so mutation of the upload buffer after construction
has the same effect. `PhotoDerivative` has identical aliasing on both input and output.

## Category verdicts

- Functional: FAIL (AC01/AC02/AC03 data integrity)
- Security: FAIL (post-upload tampering)
- Tenant Isolation: PASS by keyed repository inspection
- Offline: PASS (synthetic deterministic fixtures)
- Concurrency: PASS for version-keyed append; content aliasing remains unsafe
- Regression: FAIL pending independent unit execution

No production code was changed by this review.
