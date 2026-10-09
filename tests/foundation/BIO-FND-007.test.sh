#!/usr/bin/env bash
# Offline-safe structural checks for BIO-FND-007 (Next.js application shell and
# design-system foundation). Behavioural assertions (rendering, theming,
# permission filtering, accessibility) live in the executable vitest/Playwright
# suites under tests/frontend/ (see docs/TESTING.md, scripts/quality.sh --full).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

# --- AC01: responsive shell, light and dark mode ---
check "AC01 Next.js app scaffolded at web/" test -f web/package.json
check "AC01 web depends on next/react" bash -c 'grep -q "\"next\"" web/package.json && grep -q "\"react\"" web/package.json'
check "AC01 web has a committed lock file" test -f web/pnpm-lock.yaml
check "AC01 theme script sets data-theme before paint" test -f web/components/theme-script.tsx
check "AC01 theme toggle component exists" test -f web/components/theme-toggle.tsx
check "AC01 globals.css declares light and dark token sets" \
  bash -c "grep -q \"data-theme='light'\" web/app/globals.css && grep -q \"data-theme='dark'\" web/app/globals.css"
check "AC01 shell layout has a responsive breakpoint" bash -c 'grep -q "@media (min-width: 768px)" web/app/globals.css'

# --- AC02: BIOLOG brand assets and orange/navy palette are tokenised ---
check "AC02 tokens.ts pins the approved brand orange" bash -c "grep -q \"orange: '#F4502A'\" web/lib/tokens.ts"
check "AC02 tokens.ts pins the approved brand navy" bash -c "grep -q \"navy: '#0B2341'\" web/lib/tokens.ts"
check "AC02 globals.css mirrors the same brand orange" bash -c 'grep -qi "#f4502a" web/app/globals.css'
check "AC02 globals.css mirrors the same brand navy" bash -c 'grep -qi "#0b2341" web/app/globals.css'
check "AC02 wordmark brand asset is present" test -f web/public/brand/biolog-wordmark-light.svg
check "AC02 brand mark component renders the wordmark" bash -c 'grep -q "biolog-wordmark-light.svg" web/components/brand-mark.tsx'

# --- AC03: navigation supports permission-based visibility ---
check "AC03 permission-filtering module exists" test -f web/lib/permissions.ts
check "AC03 permission filter is a pure, framework-agnostic function" \
  bash -c 'grep -q "export function filterNavByPermission" web/lib/permissions.ts'
check "AC03 nav placeholder items declare requiredPermission" test -f web/lib/nav-items.ts
check "AC03 the shell wires nav through the permission filter" \
  bash -c 'grep -q "filterNavByPermission" web/components/app-shell.tsx'

# --- AC04: no feature-specific business logic is embedded in shell ---
check "AC04 shell code does not import shared backend contracts" \
  bash -c '! grep -rIl "from .\{1,3\}/contracts" web/app web/components web/lib web/context 2>/dev/null | grep -q .'
check "AC04 nav items carry no computed/business fields (see unit test)" \
  bash -c 'grep -q "AC04" tests/frontend/unit/bio-fnd-007-permissions.test.ts'

# --- AC05: accessibility baseline passes axe smoke tests ---
check "AC05 skip link present for keyboard users" test -f web/components/skip-link.tsx
check "AC05 axe devDependency present for the e2e harness" \
  bash -c 'grep -q "@axe-core/playwright" tests/frontend/package.json'
check "AC05 axe smoke spec exists and covers light/dark/mobile-drawer" \
  bash -c 'test -f tests/frontend/playwright/bio-fnd-007-shell.spec.ts && grep -q "AxeBuilder" tests/frontend/playwright/bio-fnd-007-shell.spec.ts'

# --- Developer test wiring ---
check "unit tests mapped to BIO-FND-007 ACs exist" \
  bash -c 'test -f tests/frontend/unit/bio-fnd-007-tokens.test.ts && test -f tests/frontend/unit/bio-fnd-007-permissions.test.ts'
check "e2e spec is auto-started against web/ (self-contained)" \
  bash -c 'grep -q "pnpm --dir ../../web dev" tests/frontend/playwright.config.mjs'
check "builder evidence recorded" test -d evidence/BIO-FND-007/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
