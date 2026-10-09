# BIO-PHO-003 independent review — round 4

**Verdict: FAIL**

Reviewed implementation `dc18486` and evidence head `3090e78` against AC01–AC05 and the promotion-recovery invariants from the prior security rounds.

## Blocking finding

### SY-PHO003-01 — High — pre-commit audit is mistaken for a committed promotion

`PhotoMasterPromotionGatewayAdapter.FindPromotedVersionAsync` treats a matching BIO-PHO-001 `photo.master.append` audit event as conclusive proof that the corresponding `PhotoMaster` version committed. BIO-PHO-001 writes that event **before** `IPhotoMasterRepository.AppendAsync`; the repository append can still fail afterward.

Reproduction sequence:

1. Approval durably records its pending attempt marker.
2. BIO-PHO-001 `AppendAsync` writes `photo.master.append` with the attempt ID and proposed version.
3. The repository append throws before storing the master.
4. Approval retry calls `FindPromotedVersionAsync`, finds the audit event, and returns its version without verifying a committed master exists.
5. BIO-PHO-003 writes its approval audit and commits the capture as Approved with `PromotedMasterVersion` referencing no photo master.

A later unrelated append can then take that same version, making the approved capture point at unrelated content. The new tests model a promotion gateway where a recorded attempt means a successful promotion; they do not model BIO-PHO-001's real audit-before-repository failure window, so all 40 pass without discriminating this defect.

**Required remediation:** promotion identity must be tied to committed BIO-PHO-001 state, not its pre-commit audit intent. Use a durable idempotency/attempt identity at the promotion repository boundary, or otherwise prove both exact attempt ownership and committed master existence atomically. Add a regression where the BIO-PHO-001 audit succeeds, its repository append fails, and retry neither falsely approves nor adopts a later unrelated version.

## Verification

- Structural harness: **55/55 PASS**.
- Independent focused xUnit executable: **40/40 PASS**, confirming the suite gap.
- Traced the real BIO-PHO-003 adapter through BIO-PHO-001 `PhotoMasterService.AppendAsync` and its audit-before-commit ordering.

Functional/data integrity/recovery: **FAIL**. Security, tenant isolation and regression outside this recovery boundary remain PASS. No production code was changed by this review.
