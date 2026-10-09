# BIO-UX-011 acceptance audit — round 1

## Verdict: FAIL

AC01–AC03 and AC06 have mapped implementation and evidence. Acceptance replay passes 36 unit
files/271 tests, TypeScript, and the 34-page production build; builder browser evidence records
125/125 tests and Seyi reports PASS.

Two approved behaviors are incomplete:

1. **AC04 — feature changes do not alter relevant navigation or controls.** The Feature Flags
   section's own description says it “does not affect real navigation yet.” Its checkboxes only
   change their own local checked state. The AC04 browser block tests tenant and role changes but
   has no feature-change assertion.
2. **AC05 — Reset does not work through a mock adapter.** Save calls `useModuleAction`, but each
   Reset handler only restores local React state and calls `action.reset()`, which clears the local
   status machine without invoking `TenantService`. The browser test asserts only that the input
   value returns locally. This does not satisfy “Settings save/reset actions work through mock
   adapters.”

Reproduction:

```text
rg -n "does not affect real navigation yet|onReset=|action.reset\(\)" web/app/settings/page.tsx
rg -n "AC04|switching tenant|MFA control|reset restores" \
  tests/frontend/playwright/bio-ux-011-admin-journeys.spec.ts
```

## Required remediation

- Make a feature-flag change visibly affect a relevant navigation item or control, with a browser test.
- Route Reset through the TenantService mock adapter and prove both the adapter outcome and restored defaults.
