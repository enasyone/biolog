# BIO-UX-015 — Product Experience approval gate

This story closes the W01.5 interactive frontend only after breadth, traceability, independent
review, and explicit human approval all pass. It does not add or redesign a product surface.

## Executable gate

Run the complete route/action sweep locally with:

```sh
corepack pnpm --dir tests/frontend test:product-gate
```

`bio-ux-015-product-experience-gate.spec.ts` opens all 31 routes in the approved
`ROUTE_MANIFEST`. Each test verifies the route's real heading and then exercises one canonical
visible primary action. The action must either navigate to its intended route, reveal its selected
deterministic record, produce an accessible status, or visibly change the current workflow state.

The shared `product-experience-gate.ts` inventory binds every route to the accepted focused browser
suite that proves the deeper workflow and secondary actions. The unit audit fails when a manifest
route, primary-navigation route, focused evidence file, or BIO-UX-014 major-page baseline becomes
unmapped.

## Acceptance-criteria trace

| Criterion | Evidence |
|---|---|
| AC01 | 31-case `bio-ux-015-product-experience-gate.spec.ts`; accepted focused suites BIO-UX-002 and BIO-UX-005 through BIO-UX-014 |
| AC02 | Pending Seyi's independent review of the builder candidate |
| AC03 | `bio-ux-015-product-experience-gate.test.ts` verifies route, navigation, interaction-evidence, and visual-baseline coverage |
| AC04 | Pending the human owner's `approve-ui` command; no agent may substitute for it |
| AC05 | The feature worktree has no Git remote and `release-state.yaml` keeps `github_push_enabled: false` |
| AC06 | The orchestration engine remains in W01UX.9 and the acceptance command rejects PASS until the human approval flag is true |

## Human approval boundary

The gate must remain unapproved until the human owner explicitly runs:

```sh
BIOLOG_Orchestration_Pack_v2.3/biolog approve-ui \
  --confirm I-APPROVE-BIOLOG-W01UX-PRODUCT-EXPERIENCE
```

Until then, `governance/ui-approval.yaml` must retain
`ui_product_experience_approved: false`. Builder and reviewer evidence can make the story ready for
the human decision, but cannot satisfy AC04 or mark the story DONE.

## Scope boundaries

- All test data is the accepted deterministic synthetic dataset.
- This story changes tests, traceability, and evidence only; it does not change the approved UI.
- The separately tracked Dashboard timezone hydration defect is not silently closed here.

## Gate finding corrected

The first full sweep stopped on `/card-designer` because the composed page exposed three `<h1>`
elements: the page title plus the BIO-CRD-003 typography tool and BIO-CRD-004 object tool headings.
God authorized the minimal component-level semantic correction; the two nested tool headings are now
`<h2>`. No CSS targets those tags, so the approved visual treatment is unchanged. A structural test
and the final 31/31 browser sweep protect the correction.
