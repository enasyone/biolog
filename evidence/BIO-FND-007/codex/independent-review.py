#!/usr/bin/env python3
"""Independent source-level boundary probes for BIO-FND-007."""

from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
shell = (ROOT / "web/components/app-shell.tsx").read_text(encoding="utf-8")
css = (ROOT / "web/app/globals.css").read_text(encoding="utf-8")
e2e = (ROOT / "tests/frontend/playwright/bio-fnd-007-shell.spec.ts").read_text(encoding="utf-8")

checks = []

checks.append((
    "permission-filtered navigation is wired into the shell",
    "filterNavByPermission(PRIMARY_NAV_ITEMS, session.permissions)" in shell,
))
checks.append((
    "light and dark token selectors exist",
    ":root[data-theme='light']" in css and ":root[data-theme='dark']" in css,
))

# A translated off-canvas element remains in sequential keyboard navigation.
# Closed content needs an actual interaction/accessibility-tree guard such as
# hidden/inert (or equivalent conditional rendering), not transform alone.
nav_tag = shell[shell.index("<nav"):shell.index("</nav>")]
closed_guard = (
    "inert=" in nav_tag
    or "aria-hidden=" in nav_tag
    or "hidden=" in nav_tag
    or "{navOpen &&" in shell
    or (
        ".biolog-primary-nav" in css
        and "visibility: hidden" in css
        and ".biolog-shell[data-nav-open='true'] .biolog-primary-nav" in css
        and "visibility: visible" in css
    )
)
checks.append(("closed mobile navigation is removed from keyboard/accessibility interaction", closed_guard))

# An axe pass normally means no violations. Severity filtering silently accepts
# moderate/minor violations and cannot establish that the scan itself passed.
axe_full = (
    "assertNoViolations(results.violations)" in e2e
    and "v.impact === 'serious'" not in e2e
    and "v.impact === 'critical'" not in e2e
)
checks.append(("axe smoke tests reject every reported violation", axe_full))

failed = 0
for label, ok in checks:
    print(("PASS" if ok else "FAIL"), label)
    failed += not ok

raise SystemExit(1 if failed else 0)
