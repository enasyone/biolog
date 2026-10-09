#!/usr/bin/env bash
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
dotnet run --project evidence/BIO-DOC-001/codex/DocumentIntegrityProbe/DocumentIntegrityProbe.csproj --nologo
