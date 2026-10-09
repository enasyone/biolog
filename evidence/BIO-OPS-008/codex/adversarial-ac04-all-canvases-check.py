#!/usr/bin/env python3
"""Check every print canvas composed into /card-designer for theme leakage."""

import re
from pathlib import Path

root = Path(__file__).resolve().parents[3]
page = (root / "web/app/card-designer/page.tsx").read_text()
spec = (root / "tests/features/bio-ops-008/bio-ops-008.spec.ts").read_text()

canvases = {
    "base": ("CardDesignerApp", "web/features/bio-crd-002/card-designer.module.css", "Front card canvas"),
    "typography": ("CardTypographyApp", "web/features/bio-crd-003/card-typography.module.css", "Card typography canvas"),
    "objects": ("CardObjectsApp", "web/features/bio-crd-004/card-objects.module.css", "Card objects canvas"),
    "photo": ("CardPhotoApp", "web/features/bio-crd-009/card-photo.module.css", "Front card photo canvas"),
}

failures = 0
for name, (component, css_path, accessible_name) in canvases.items():
    css = (root / css_path).read_text()
    # The outer frame border is editor chrome. The printable surface and its
    # elements must not consume application-theme tokens.
    blocks = "\n".join(
        match.group(1)
        for selector in ("canvasSurface", "element")
        for match in [re.search(rf"\.{selector}\s*\{{(.*?)\}}", css, re.S)]
        if match
    )
    composed = f"<{component}" in page
    theme_leak = "var(--biolog-color-" in blocks
    tested = accessible_name in spec
    ok = composed and not theme_leak and tested
    print(("PASS" if ok else "FAIL"), name, "print canvas is composed, theme-independent and browser-tested")
    if not ok:
        failures += 1
        print("     ", f"composed={composed}, theme_token_in_canvas={theme_leak}, covered_by_AC04={tested}")

print(f"{len(canvases) - failures} passed, {failures} failed")
raise SystemExit(1 if failures else 0)
