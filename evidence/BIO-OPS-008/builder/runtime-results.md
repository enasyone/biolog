# BIO-OPS-008 unrestricted browser result

- Tested commit: `8dfe7cc69d38cb50bd958cfce2768a2fdfd15fac`
- Environment: unrestricted local Chromium against the real Next.js development server on loopback
- Data: synthetic demo fixtures only
- Command: `bash tests/features/bio-ops-008/BIO-OPS-008.acceptance.sh`
- Result: **PASS — 15 passed, 0 failed, 1.7 minutes**
- Runner: Chinedu/god, reported to Chioma on 2026-10-08

The managed builder sandbox cannot bind the required loopback listener (`listen EPERM`), so the
orchestrator ran the exact gate unrestricted. The passing run includes eight light/dark critical-flow
axe checks, three workflow checks, three error/prerequisite checks and one theme/print-semantics check.
That final check compares computed canvas background colour/image and representative element
fill/text/border/outline across themes for all four composed canvases, while also proving the
surrounding editor control changes.

For the native Person `<select>`, Chromium exposes the correct `combobox`/`Person` accessibility
name and sequential Tab focus. Headless Chromium does not drive the operating-system dropdown with
raw ArrowDown events, so the test selects the real eligible option through Playwright's
`selectOption`, then continues the application-owned keyboard path: Tab focuses the enabled Issue
card button and Enter submits it.
