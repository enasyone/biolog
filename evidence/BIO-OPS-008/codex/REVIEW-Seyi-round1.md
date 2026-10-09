# BIO-OPS-008 independent review — round 1

**Verdict: FAIL** at implementation commit `1df479f` / evidence commit `18b2417`.

## Finding

### SY-OPS008-01 — High — AC04 does not preserve or test WYSIWYG print colours

The theme test stores only the Front card canvas `innerHTML` and inline width/height. It never reads
the computed canvas or card-element colours. The canvas implementation uses application theme
tokens for its frame/surface, element fill and element text (`--biolog-color-surface`,
`--biolog-color-bg`, `--biolog-color-accent`, `--biolog-color-text`), and those tokens have different
light/dark definitions. Therefore the physical WYSIWYG preview visibly changes colour while the
current assertion still passes because markup and dimensions remain unchanged.

The separate assertion that success/warning/error tokens are mutually distinct within each theme is
not tied to any card-design content and does not prove that a print colour retains its meaning across
themes.

Reproduce:

```sh
python3 evidence/BIO-OPS-008/codex/adversarial-ac04-check.py
```

Required remediation: distinguish editor chrome from physical card artwork. Card surface and design
element colours must be design-state values independent of the application theme. Add a browser
regression that records computed print-surface and representative design-element colours before and
after switching theme and proves they are identical, while separately allowing editor chrome to
change.

## Acceptance criteria

| Criterion | Verdict | Evidence |
|---|---|---|
| AC01 axe baseline | PASS | Eight light/dark scans cover the four named critical routes and reject critical-impact violations; unrestricted gate reports 15/15. |
| AC02 keyboard navigation | PASS | Enrolment and document actions use Tab/Enter; card combobox focus and submit use the keyboard, with native option selection handled by Playwright due headless operating-system popup constraints. |
| AC03 understandable errors | PASS | Enrolment names missing fields/recovery, document failure announces gating, and card issuance exposes its prerequisite. |
| AC04 print-design colour semantics | FAIL | Test does not inspect print colours; actual WYSIWYG canvas colours consume theme-varying application tokens. |

## Category verdicts

- Functional: FAIL (AC04)
- Security: NOT APPLICABLE
- Tenant isolation: NOT APPLICABLE
- Offline: NOT APPLICABLE
- Concurrency: NOT APPLICABLE
- Regression: FAIL (theme/print fidelity)

## Checks

- Builder unrestricted browser gate: 15/15, but the AC04 assertion is non-discriminating for the defect above.
- `python3 evidence/BIO-OPS-008/codex/adversarial-ac04-check.py` — 0 passed, 5 failed.
