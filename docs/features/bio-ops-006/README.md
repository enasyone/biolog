# BIO-OPS-006 — Security and tenant-isolation hardening

Run the repeatable local gate from the repository root:

```bash
bash tests/features/bio-ops-006/BIO-OPS-006.acceptance.sh
```

- **BIO-OPS-006-AC01:** `THREAT-MODEL.md` covers authentication, authorization, tenant routing,
  biometrics, card lifecycle, document parsing, webhooks and offline caches.
- **BIO-OPS-006-AC02:** the security ledger is checked for explicitly open Critical/High defects;
  accepted future scope boundaries remain disclosed.
- **BIO-OPS-006-AC03:** seven named cross-tenant negative regressions are executed across the host,
  identity, biometric, card, webhook and offline boundaries. A missing runner, failed build, failed
  body or zero-test result fails the gate.
- **BIO-OPS-006-AC04:** gitleaks scans both the current tree and Git history. If gitleaks is absent,
  both gates fail closed and point to `docs/TOOLCHAIN-POLICY.md`. `.gitleaks.toml` allowlists only
  exact known synthetic fixture values; a mutation regression proves an unlisted credential remains
  detectable.
- **BIO-OPS-006-AC05:** direct production logging call sites, console/debug writes and sensitive
  log-template vocabulary are checked.

The gate and its adversarial mutation probes are part of `scripts/quality.sh`. The probes prove a
failing test runner and a deleted historical credential are rejected. This story makes no
shared-contract, migration, API or UI change.
