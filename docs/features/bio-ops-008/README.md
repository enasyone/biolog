# BIO-OPS-008 accessibility and user-experience acceptance

This story adds one executable, synthetic-data acceptance gate for the critical web workflows:
enrolment, card issuance, document verification and card design. It does not redesign the product
or modify shared user-interface code.

## Acceptance mapping

| Criterion | Executable proof |
|---|---|
| AC01 | Axe scans all four critical routes in light and dark themes and rejects every critical-impact violation. |
| AC02 | Keyboard-only tests enter enrolment identity data, issue a card and start document capture by tabbing to controls and activating them from the keyboard. |
| AC03 | Tests exercise empty enrolment, failed document capture and card-issuance prerequisites, checking visible guidance, announcement roles, recovery/gating and axe results. |
| AC04 | The card-designer test switches theme and proves the physical canvas dimensions and design markup are unchanged, semantic success/warning/error colours remain distinct, and the themed result has no critical axe violations. |

## Run

```sh
pnpm install --offline --frozen-lockfile
bash tests/features/bio-ops-008/BIO-OPS-008.acceptance.sh
```

The suite starts the real Next.js application unless `BIOLOG_BASE_URL` names an already-running
instance. It uses only deterministic demo fixtures. No real identity, biometric, card or document
data is read or written.
