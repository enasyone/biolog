#!/usr/bin/env bash
set -u
cd "$(git rev-parse --show-toplevel)"

dotnet run --project evidence/BIO-FND-009/codex/RedactionProbe/RedactionProbe.csproj --nologo
