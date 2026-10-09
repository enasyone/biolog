#!/usr/bin/env bash
# Fails when `dotnet list package --vulnerable` finds anything for a given .NET project,
# or when the audit itself could not run to completion.
#
# `dotnet list package --vulnerable` always exits 0 regardless of what it finds - it is a
# reporting command, not a gate - so nothing previously turned a High/Critical finding into
# a failing quality run (BIO-FND-008-QA-01: pinning a vulnerable Microsoft.OpenApi passed
# scripts/quality.sh --full even though NU1903 was printed). This wrapper is the gate: it
# inspects the command's own output and exit status for its "found something" phrasing and
# fails accordingly, since there is no reliable exit code to depend on for that case.
#
# BIO-FND-008-QA-02: the first version of this wrapper only looked for the vulnerable-
# packages phrase and treated everything else - including a nonzero dotnet exit code, a
# restore failure, or NuGet advisory-source resolution failing partway through - as a
# silent PASS. It now fails closed on two extra conditions: a nonzero exit status from the
# dotnet invocation, and output that contains neither the vulnerable-packages phrase nor
# the only positive-success phrase dotnet prints ("has no vulnerable packages"), which
# covers an advisory-source warning or other unexpected output replacing that line.
#
# BIO-FND-008-QA-03 (Seyi, found via BIO-DEV-001's review): that second condition assumed
# an advisory-source failure (NU1900: "Error occurred while getting package vulnerability
# data") always REPLACES the "has no vulnerable packages" line. In practice `dotnet list`
# can print BOTH in the same run - NU1900 as a warning, then the clean-sounding summary
# line regardless - so the "or unexpected output" fallback above never fired and this
# wrapper accepted an audit that never actually reached the advisory source as a clean
# PASS. NU1900/advisory-source failure is now checked FIRST and fails closed unconditionally,
# whether or not a clean-looking phrase also appears in the same output - "the check ran
# without reaching real advisory data" is never an acceptable substitute for "the check ran
# and found nothing," regardless of what other text surrounds it.
#
# Usage: scripts/check-dotnet-vulnerabilities.sh <csproj> [<csproj> ...]
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

if [ "$#" -eq 0 ]; then
  echo "usage: scripts/check-dotnet-vulnerabilities.sh <csproj> [<csproj> ...]" >&2
  exit 2
fi

fail=0
for csproj in "$@"; do
  echo "-- $csproj --"
  output="$(dotnet list "$csproj" package --vulnerable --include-transitive 2>&1)"
  status=$?
  echo "$output"

  if [ "$status" -ne 0 ]; then
    echo "FAIL  $csproj: dotnet list exited $status (audit did not complete)" >&2
    fail=1
    continue
  fi

  if printf '%s' "$output" | grep -qE "NU1900|Error occurred while getting package vulnerability data"; then
    echo "FAIL  $csproj: advisory-source failure (NU1900) - vulnerability data was not reliably obtained, even if other output looks clean" >&2
    fail=1
    continue
  fi

  if printf '%s' "$output" | grep -q "has the following vulnerable packages"; then
    echo "FAIL  $csproj has one or more vulnerable packages" >&2
    fail=1
    continue
  fi

  if ! printf '%s' "$output" | grep -q "has no vulnerable packages"; then
    echo "FAIL  $csproj: audit did not report a clean result (unexpected output, possible advisory-source failure)" >&2
    fail=1
  fi
done

if [ "$fail" -eq 0 ]; then
  echo "DOTNET VULNERABILITY AUDIT: PASS"
  exit 0
fi
echo "DOTNET VULNERABILITY AUDIT: FAIL" >&2
exit 1
