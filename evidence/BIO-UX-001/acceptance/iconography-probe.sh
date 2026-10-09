#!/usr/bin/env bash
set -u

repo="$(git rev-parse --show-toplevel)"
cd "$repo"

if rg -n 'icon(Size|Stroke|Weight|Optical|Alignment)|iconography\s*=|export const icons' \
  web/design-system tests/frontend/unit/bio-ux-001-* >/dev/null; then
  echo 'PASS AC02 iconography is tokenised and tested'
  exit 0
fi

echo 'FAIL AC02 iconography is tokenised and tested'
exit 1

