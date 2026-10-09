#!/usr/bin/env bash
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

failed=0
check() {
  local name="$1"
  shift
  if "$@"; then
    printf 'PASS %s\n' "$name"
  else
    printf 'FAIL %s\n' "$name"
    failed=$((failed + 1))
  fi
}

check "builder AC suite" bash tests/foundation/BIO-ARC-001.test.sh

check "AC01 exact canonical type registry" node --input-type=module -e '
  import { TYPES } from "./contracts/js/index.mjs";
  const expected = ["credential", "device", "document_session", "event", "person", "tenant"];
  if (JSON.stringify(Object.keys(TYPES).sort()) !== JSON.stringify(expected)) process.exit(1);
'

check "AC02 impossible calendar instants fail closed" node --input-type=module -e '
  import { isUtcInstant, parseInstant, ClockError } from "./contracts/js/index.mjs";
  const invalid = [
    "2026-02-29T12:00:00.000Z",
    "2026-02-30T12:00:00.000Z",
    "2026-04-31T12:00:00.000Z",
  ];
  for (const value of invalid) {
    if (isUtcInstant(value)) process.exit(1);
    try { parseInstant(value); process.exit(1); } catch (error) {
      if (!(error instanceof ClockError)) process.exit(1);
    }
  }
'

check "AC02 impossible calendar dates fail closed" node --input-type=module -e '
  import { isCalendarDate } from "./contracts/js/index.mjs";
  for (const value of ["2026-02-29", "2026-02-30", "2026-04-31"]) {
    if (isCalendarDate(value)) process.exit(1);
  }
  if (!isCalendarDate("2024-02-29")) process.exit(1);
'

check "AC04 clock code and schemas agree at the four-digit year boundary" node --input-type=module -e '
  import { readFileSync } from "node:fs";
  import { isCalendarDate, isUtcInstant } from "./contracts/js/index.mjs";
  const datePattern = new RegExp(JSON.parse(readFileSync("contracts/schemas/calendar-date.schema.json", "utf8")).pattern);
  const instantPattern = new RegExp(JSON.parse(readFileSync("contracts/schemas/utc-instant.schema.json", "utf8")).pattern);
  for (const date of ["0000-02-28", "0000-02-29", "0000-03-01", "0004-02-29", "0400-02-29", "9999-12-31"]) {
    if (datePattern.test(date) !== isCalendarDate(date)) process.exit(1);
    const instant = `${date}T12:00:00.000Z`;
    if (instantPattern.test(instant) !== isUtcInstant(instant)) process.exit(1);
  }
'

check "AC03 stale and missing versions fail closed" node --input-type=module -e '
  import { checkPrecondition } from "./contracts/js/index.mjs";
  try { checkPrecondition({ storedVersion: 2 }); process.exit(1); } catch (e) {
    if (e.status !== 428 || e.code !== "precondition_required") process.exit(1);
  }
  try { checkPrecondition({ storedVersion: 2, expectedVersion: 1 }); process.exit(1); } catch (e) {
    if (e.status !== 409 || e.code !== "concurrency_conflict") process.exit(1);
  }
'

exit "$([ "$failed" -eq 0 ] && echo 0 || echo 1)"
