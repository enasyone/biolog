#!/usr/bin/env bash
# Fails closed if any feature project's .csproj references Biolog.Api.csproj.
#
# The module-registration pattern (docs/API-SHELL.md AC05) requires the dependency to be
# one-way: Biolog.Api -> feature project, via a thin IBiologModule adapter that lives in
# api/Biolog.Api/Modules/ (see DocumentModule.cs, DeviceRegistryModule.cs,
# BioTen001Module.cs). A feature project that references Biolog.Api.csproj back - typically
# because its own module-adapter class lives inside the feature project and needs
# IBiologModule - creates a circular project reference. `dotnet build` does not report this
# as a normal error: it hangs at "Determining projects to restore..." for several minutes
# and then fails with no diagnostic text at all, which cost BIO-TEN-001's integration two
# blind build attempts before the cause was found by reading the csproj directly
# (hive inbox 2026-09-22T06-07-24-693Z-0d0d3a). This check catches the same defect at
# builder time, before merge.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

fail=0
while IFS= read -r -d '' csproj; do
  # Only the actual MSBuild element matters - a comment mentioning "Biolog.Api.csproj" in
  # prose (e.g. explaining that this project deliberately has no such reference) must not
  # trip the check, so match on the <ProjectReference ...> element itself, not free text.
  if grep -q '<ProjectReference[^>]*Biolog\.Api\.csproj' "$csproj"; then
    echo "FAIL  $csproj references Biolog.Api.csproj - feature projects must never reference the host project (one-way dependency: Biolog.Api -> feature project only; put the IBiologModule adapter in api/Biolog.Api/Modules/ instead)" >&2
    fail=1
  fi
done < <(find api/features -name "*.csproj" -not -path "*/bin/*" -not -path "*/obj/*" -print0 2>/dev/null)

if [ "$fail" -eq 0 ]; then
  echo "MODULE PROJECT REFERENCES: PASS"
  exit 0
fi
echo "MODULE PROJECT REFERENCES: FAIL" >&2
exit 1
