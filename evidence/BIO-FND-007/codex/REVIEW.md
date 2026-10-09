# BIO-FND-007 independent review

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed implementation and builder evidence at `908ac7f`.

The structural harness passes 25/25, focused unit tests pass 11/11, TypeScript passes, and the production Next.js build succeeds. The builder's recorded browser suite passes 15/15. Independent review found one accessibility blocker that those checks do not cover.

## Finding

### BIO-FND-007-QA-01 — Medium — Closed mobile navigation remains keyboard-accessible

At mobile widths, the closed navigation is moved off screen only with `transform: translateX(-100%)`. It remains rendered with ordinary links and has no `hidden`, `inert`, `aria-hidden`, conditional rendering, or equivalent focus guard. Keyboard focus can therefore move into links that the user cannot see while the drawer reports closed. This breaks the keyboard/focus-order accessibility baseline.

The axe tests do not cover this boundary. They also filter results to `serious` and `critical`, so any `moderate` or `minor` axe violation is silently treated as a passing scan rather than requiring `results.violations` to be empty.

Required closure: make the closed drawer non-interactive and absent from the accessibility tree while preserving the open/desktop behavior; add a keyboard test proving closed links cannot receive focus; make the axe smoke assertion reject every reported violation or record an explicit reviewed waiver.

## Reproduction

```sh
python3 evidence/BIO-FND-007/codex/independent-review.py
bash tests/foundation/BIO-FND-007.test.sh
cd tests/frontend && ./node_modules/.bin/vitest run unit/bio-fnd-007-permissions.test.ts unit/bio-fnd-007-tokens.test.ts
cd ../../web && ./node_modules/.bin/tsc --noEmit && ./node_modules/.bin/next build
```

Browser re-execution was unavailable in the reviewer sandbox: local port binding and Chromium Mach service registration were denied. The committed builder transcript records 15/15 browser tests passing; the finding is source-deterministic and outside their tested assertions.

## Category verdicts

- Functional: PASS — shell, themes, brand tokens, and permission filtering are present.
- Security: PASS — visibility is treated as presentation, not authorization; demo profiles are synthetic.
- Tenant isolation: not applicable; no tenant data access exists.
- Offline/recovery: PASS — structural/unit/type/build checks run locally.
- Concurrency: not applicable.
- Accessibility: FAIL — QA-01.
- Regression: PASS — structural, unit, type, and production-build checks pass.
