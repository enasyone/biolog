#!/usr/bin/env bash
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

pnpm --dir tests/frontend exec playwright test \
  --config ../../tests/features/bio-ops-008/playwright.config.mjs
