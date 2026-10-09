# BIO-OPS-008 independent review — round 2

**Verdict: FAIL** at remediation commit `52c659b` / evidence commit `9bc246e`.

## Remediation result

SY-OPS008-01 is closed for BIO-CRD-002's base `Front card canvas`: its paper/grid/element colours
are fixed design values, and the browser test now compares computed print colours while proving
editor chrome changes.

### SY-OPS008-02 — High — three other composed print canvases still change with theme

`/card-designer` composes four independent canvas applications: base design, typography/watermark,
dynamic objects, and photo. The remediation changes and tests only the base canvas. The typography,
object and photo canvas styles still use `--biolog-color-surface`, `--biolog-color-bg`,
`--biolog-color-border`, `--biolog-color-brand-navy` and `--biolog-color-accent` in their
frame/surface/element paths. Those values change between light and dark mode, so three physical
print previews still change with the administrator theme. None is inspected by the AC04 browser
test.

Reproduce:

```sh
python3 evidence/BIO-OPS-008/codex/adversarial-ac04-all-canvases-check.py
```

Required remediation: apply the same design-owned colour boundary to every print canvas composed on
the route, ideally via shared card-design variables, and extend the computed-style regression to
typography, objects, and front/back photo canvases. Editor chrome may remain theme-aware.

## Acceptance criteria

| Criterion | Verdict |
|---|---|
| AC01 axe baseline | PASS |
| AC02 keyboard navigation | PASS |
| AC03 understandable errors | PASS |
| AC04 print-design colour semantics | FAIL — base canvas fixed; three other live print canvases remain theme-dependent and untested. |

## Category verdicts

- Functional: FAIL (AC04)
- Security: NOT APPLICABLE
- Tenant isolation: NOT APPLICABLE
- Offline: NOT APPLICABLE
- Concurrency: NOT APPLICABLE
- Regression: FAIL (partial route-level theme/print fidelity)

The unrestricted 15/15 result is consistent with this finding because the sole AC04 test locates
only `[aria-label="Front card canvas"]`.
