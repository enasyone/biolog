# BIO-UX-015 — Builder evidence

**Owner:** Chioma  
**Implementation commit:** `c276273a1511d52b09091bfd9437c061c8551b34`  
**Decision boundary:** ready for Seyi review; human AC04 remains deliberately pending

## Acceptance-criteria map

| AC | Builder result | Evidence |
|---|---|---|
| AC01 | PASS | New 31-case browser gate opens every `ROUTE_MANIFEST` route and exercises one canonical visible primary action; governance unsandboxed run 31/31 |
| AC02 | PENDING | Requires Seyi's independent review of the final builder commit |
| AC03 | PASS | Eight structural checks bind all manifest/navigation routes, focused browser evidence, BIO-UX-014 representative pages and baseline PNGs |
| AC04 | PENDING HUMAN | `ui-approval.yaml` remains false; only the explicit human `approve-ui` command may satisfy it |
| AC05 | PASS | Worktree has no Git remote; `github_push_enabled: false` |
| AC06 | PASS | Engine is in W01UX.9; its accept path rejects BIO-UX-015 PASS while human approval is false |

## Verification

- Product gate: **31/31 PASS**, run unsandboxed by governance at implementation `c276273`.
- Unit: **52 files / 392 tests PASS**, including BIO-UX-015's 8 traceability/governance checks.
- TypeScript: PASS.
- Next production build: PASS, 34 generated pages.
- Playwright discovery: 31 gate cases found.
- BIO-UX-014 remains the accepted visual baseline: 14 screenshots and distinct light/dark shell hashes.

## Defect found and fixed by the gate

The gate correctly stopped at `/card-designer`: it rendered three level-one headings. The nested
BIO-CRD-003 and BIO-CRD-004 tool headings were changed from `<h1>` to `<h2>` after god explicitly
authorized the component-level semantic correction. The top-level `Card Designer` heading remains
the sole `<h1>`. There are no tag-level CSS selectors on those nested components, so this is a
semantic correction with no approved visual-language change. The final gate then passed all 31
routes/actions.

Earlier gate failures on Credentials, Issue Card and Replace Card were audit-test setup defects, not
product defects: their controls existed and correctly required a selection. The final gate uses
accessible combobox roles and supplies required synthetic selections before invoking actions.

## Human boundary

This evidence does not claim AC04. The human owner must explicitly run:

```sh
BIOLOG_Orchestration_Pack_v2.3/biolog approve-ui \
  --confirm I-APPROVE-BIOLOG-W01UX-PRODUCT-EXPERIENCE
```

After Seyi passes the exact builder commit, god is the story acceptor and will take the approval gate
to the human. No agent may substitute for that decision.
