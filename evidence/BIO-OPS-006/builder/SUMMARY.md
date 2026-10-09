# BIO-OPS-006 builder evidence

## Result

PASS with genuine gitleaks-backed current-tree/history scans and an unrestricted AC03 integration
run supplied by god. No production behavior, shared contract, migration, API or UI surface changed.

## Acceptance criteria

- **AC01:** whole-system threat model covers all eight named domains with assets, attacks,
  controls and honest residual boundaries.
- **AC02:** the executable ledger check finds no entry explicitly marked open at Critical/High.
  ADR-0006's future real-principal replacement and future device-secret provisioning remain
  disclosed accepted boundaries, not falsely described as completed.
- **AC03:** the gate executes seven named negative regressions across tenant middleware, identity,
  biometrics, card lifecycle, webhooks and offline sync. It fails closed on unavailable execution,
  build/test failure or a zero-test result.
- **AC04:** current-tree and full-history scanning hard-require gitleaks. An unavailable scanner
  fails closed with an actionable reference to `docs/TOOLCHAIN-POLICY.md`; no heuristic fallback,
  content filter or path exclusion remains.
- **AC05:** all direct production application log calls were enumerated. Only the two audit sinks
  emit directly, and all 12 values cross `SafeLogValue`. No production console/debug writes or
  sensitive-content log templates were found.

## Commands and results

- `bash tests/features/bio-ops-006/BIO-OPS-006.acceptance.sh --cross-tenant-only` — **7/7 PASS,
  exit 0** on frozen commit `e50d3f6` with sandbox restrictions disabled. Build succeeded, the TRX
  was written, and the exact runner summary was: `Passed! - Failed: 0, Passed: 7, Skipped: 0,
  Total: 7, Duration: 1 s`.
- `bash tests/features/bio-ops-006/BIO-OPS-006.acceptance.sh --controls-only` — **14/14 PASS** with
  gitleaks 8.30.1 genuinely executing both scans; the final rerun scanned 1,270 commits / about 134 MB and
  reported no leaks.
- `bash tests/features/bio-ops-006/BIO-OPS-006.mutation.sh` — **4/4 PASS**: a failing .NET runner
  fails AC03, both secret scanners fail clearly when gitleaks is unavailable, and an unallowlisted
  Slack-token-shaped credential is still rejected by the real gitleaks engine.
- `bash tests/features/bio-ops-006/BIO-OPS-006.adversarial.sh` — **PASS**: AC03 executes the suite
  and both AC04 scanners fail closed with the required toolchain-policy error.
- `bash scripts/secret-scan.sh --all` — **PASS**, gitleaks executed and found no leaks.
- `bash scripts/history-secret-scan.sh` — **PASS**, gitleaks executed and found no leaks.
- `bash scripts/quality.sh` — started and exercised the existing foundation suites successfully,
  but was stopped because multiple feature harnesses recursively replay the 322-case ADR-0001
  suite, producing more than 150,000 lines before reaching the remaining gates. The new focused
  story gate above completed independently and is wired into the quality command.
- Runtime `.NET` integration execution remains unavailable in the builder/reviewer sandbox because
  MSBuild cannot create its named pipe (`SocketException (13): Permission denied`). God replayed the
  exact fail-closed command through the unrestricted path on `e50d3f6`, producing the genuine 7/7
  result above; no source-inspection substitute was used.

## Secret-scanner design decision

Per Kemi's round-4 ruling, both scripts require gitleaks and have no hand-rolled fallback. Three
review rounds showed that string matching an unbounded attacker-controlled stream could not avoid
both false negatives and unsafe exemptions: narrower patterns missed secrets, content exclusions
were attacker-controlled, and path exclusions hid production files. Failing closed while gitleaks
is absent is intentional. Pinning/installing gitleaks belongs to the separate BIO-FND-002 toolchain
story; this feature story does not edit `toolchain/versions.json`. That toolchain task has now landed,
and the local runtime is gitleaks 8.30.1. `.gitleaks.toml` uses gitleaks' native allowlist against
exact known synthetic fixture values only; it contains no path-wide exemption. The mutation suite
builds an unlisted Slack-token-shaped credential from non-matching source fragments at runtime and
proves the real scanner still fails. Exact historical mutation values are anchored in the native
allowlist because earlier review commits necessarily retain them.

## Sensitive-log review

Reviewed `InMemoryAuditSink` and `InMemoryCertificationAuditSink` directly. They emit only audit
action, target identifiers, actor type/id and outcome; each value is explicitly classified through
`SafeLogValue`. BIO-FND-009's composed-host evidence clears default providers, leaving the redacting
provider as the sole output path. No raw biometrics, photos, document evidence, credentials, tokens,
cookies or secrets are logged.
