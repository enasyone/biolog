#!/usr/bin/env bash
# BIO-OPS-006-AC04: scan every reachable Git patch, not only the current tree.
# Gitleaks is mandatory: absence fails closed rather than degrading to a
# structurally incomplete scanner.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

command -v gitleaks >/dev/null 2>&1 || {
  echo 'gitleaks is required and not installed - see docs/TOOLCHAIN-POLICY.md' >&2
  exit 1
}

echo "history-secret-scan: using gitleaks"
exec gitleaks git --no-banner --redact .
