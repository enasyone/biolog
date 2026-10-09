#!/usr/bin/env python3
"""Prove BIO-OPS-008 AC04 does not inspect theme-dependent WYSIWYG colours."""

from pathlib import Path

root = Path(__file__).resolve().parents[3]
spec = (root / "tests/features/bio-ops-008/bio-ops-008.spec.ts").read_text()
css = (root / "web/features/bio-crd-002/card-designer.module.css").read_text()
globals_css = (root / "web/app/globals.css").read_text()

checks = [
    (
        "AC04 compares computed canvas colours before and after theme switch",
        "getComputedStyle(canvas" in spec or "getComputedStyle(element" in spec,
        "test compares only innerHTML/inline dimensions; computed canvas colours are never read",
    ),
    (
        "physical canvas surface avoids application theme background tokens",
        "var(--biolog-color-surface)" not in css and "var(--biolog-color-bg)" not in css,
        "canvas frame/surface use --biolog-color-surface and --biolog-color-bg",
    ),
    (
        "card element fill/text avoid application theme tokens",
        "var(--biolog-color-accent)" not in css and "var(--biolog-color-text)" not in css,
        "card element fill/text use --biolog-color-accent, --biolog-color-surface and --biolog-color-text",
    ),
    (
        "light and dark theme values are identical for canvas-driving tokens",
        globals_css.count("--biolog-color-text:") < 2
        and globals_css.count("--biolog-color-surface:") < 2,
        "globals.css defines different light/dark values for the tokens consumed by the canvas",
    ),
    (
        "AC04 semantic colour assertion proves stable meaning across themes",
        "expect(darkTokens).toEqual(lightTokens)" in spec,
        "test only proves three tokens are mutually distinct inside each theme; it does not compare mappings across themes or inspect print content",
    ),
]

failed = 0
for label, ok, detail in checks:
    print(("PASS" if ok else "FAIL"), label)
    if not ok:
        failed += 1
        print("     ", detail)

print(f"{len(checks) - failed} passed, {failed} failed")
raise SystemExit(1 if failed else 0)
