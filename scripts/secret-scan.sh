#!/usr/bin/env bash
# BIOLOG secret scan (BIO-FND-001 security criterion: "Secret scanning runs
# before the first commit"). Gitleaks is mandatory: absence fails closed.
#
# Usage: scripts/secret-scan.sh [--staged|--all]
#   --staged (default when there are staged changes): scan staged files.
#   --all:    scan every git-tracked file.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

mode="${1:-auto}"

command -v gitleaks >/dev/null 2>&1 || {
  echo 'gitleaks is required and not installed - see docs/TOOLCHAIN-POLICY.md' >&2
  exit 1
}

echo "secret-scan: using gitleaks"
if [ "$mode" = "--staged" ]; then
  exec gitleaks protect --no-banner --staged --redact
fi
exec gitleaks detect --no-banner --redact --source .
