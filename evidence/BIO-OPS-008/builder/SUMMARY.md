# BIO-OPS-008 builder evidence

## Result

PASS. The story-owned acceptance gate passes 15/15 against the real web application using synthetic
fixtures. No critical-impact axe violations remain on the four critical flows in either theme.

## Acceptance criteria

- AC01: enrolment, card issuance, document verification and card design pass the axe critical baseline in light and dark mode.
- AC02: keyboard focus reaches the core workflow controls; application-owned submit/capture actions are activated by Enter without traps.
- AC03: empty enrolment names the missing fields and recovery step; failed document capture explains gating; card issuance explains its prerequisite.
- AC04: all four composed card canvases use fixed physical-design paper/grid/ink/accent/border values. The gate adds a representative element to each, proves every computed surface and element colour stays identical across themes while editor chrome changes, and remains axe-clean.

## Commands

- `bash tests/features/bio-ops-008/BIO-OPS-008.acceptance.sh`: PASS, 15/15 unrestricted.
- `pnpm --dir web exec tsc --noEmit`: PASS.
- Focused enrolment journey and live-port unit tests: PASS, 8/8.
- `python3 scripts/check-traceability.py --all`: PASS, 36 implemented stories.
- `bash scripts/check-module-project-references.sh`: PASS.

Acceptance found and closed two product defects: generic enrolment failure copy now names each
missing identity field and gives a recovery instruction; physical card artwork no longer inherits
the administrator's light/dark application theme.
