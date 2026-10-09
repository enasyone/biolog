#!/usr/bin/env bash
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

MSBUILDDISABLENODEREUSE=1 dotnet build \
  evidence/features/bio-ten-001/codex/AuditAtomicityProbe/AuditAtomicityProbe.csproj \
  --no-restore --verbosity quiet -m:1
dotnet evidence/features/bio-ten-001/codex/AuditAtomicityProbe/bin/Debug/net10.0/AuditAtomicityProbe.dll

