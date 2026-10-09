#!/usr/bin/env bash
# Developer tests for BIO-ARC-001, mapped to acceptance criteria.
# Run from anywhere: tests/foundation/BIO-ARC-001.test.sh
#
# Two layers: this script checks that the contract artifacts exist and agree with each
# other, then delegates the behavioural assertions to the node:test suite in
# contracts/js/contracts.test.mjs. Both are offline and dependency-free.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

total=0
failed=0

check() {
  local name="$1"; shift
  total=$((total + 1))
  if "$@"; then
    echo "PASS  $name"
  else
    echo "FAIL  $name"
    failed=$((failed + 1))
  fi
}

ADR=docs/adr/ADR-0001-canonical-identifiers-clocks-and-versioning.md

# --- AC01: canonical ID strategy documented for all six entity types -----------------
check "AC01 ADR-0001 exists and is accepted" \
  bash -c "test -f $ADR && grep -q '\*\*Status:\*\* Accepted (BIO-ARC-001' $ADR"
check "AC01 ADR documents every required entity type" \
  bash -c "for t in Tenant Person Credential Device 'Document session' Event; do grep -q \"| \$t |\" $ADR || exit 1; done"
check "AC01 identifier registry lists exactly the six registered types" \
  bash -c "test \"\$(python3 -c 'import json;print(len(json.load(open(\"contracts/schemas/identifier-registry.json\"))[\"types\"]))')\" = 6"
check "AC01 canonical-id schema published" test -f contracts/schemas/canonical-id.schema.json
check "AC01 identifier implementation published" test -f contracts/js/identifiers.mjs
check "AC01 business identifiers excluded from primary keys" \
  bash -c "grep -q 'never its primary key' $ADR"

# --- AC02: UTC internally, explicit local rendering ----------------------------------
check "AC02 UTC instant schema published" test -f contracts/schemas/utc-instant.schema.json
check "AC02 ADR mandates RFC 3339 UTC with literal Z" \
  bash -c "grep -q 'literal \`Z\`' $ADR"
check "AC02 ADR requires an explicit IANA zone for local rendering" \
  bash -c "grep -q 'explicit IANA time zone identifier' $ADR"
check "AC02 ADR forbids MySQL TIMESTAMP in favour of DATETIME(3) UTC" \
  bash -c "grep -q 'DATETIME(3)' $ADR"
check "AC02 clock implementation published" test -f contracts/js/clock.mjs
check "AC02 ADR requires real calendar dates (QA-01)" \
  bash -c "grep -q 'The date must be a real date' $ADR"
check "AC02 ADR states the supported year range (QA-02)" \
  bash -c "grep -q 'supported year range is 1000' $ADR"
check "AC04 QA-02 code and schema agree on every four-digit year" \
  bash -c 'node --input-type=module -e "
    import { isCalendarDate, isUtcInstant } from \"./contracts/js/index.mjs\";
    import { readFileSync } from \"node:fs\";
    const pat = (f) => new RegExp(JSON.parse(readFileSync(\"contracts/schemas/\" + f, \"utf8\")).pattern);
    const dp = pat(\"calendar-date.schema.json\"), ip = pat(\"utc-instant.schema.json\");
    for (let y = 0; y <= 9999; y += 1) {
      const ys = String(y).padStart(4, \"0\");
      for (let m = 1; m <= 12; m += 1) {
        const ms = String(m).padStart(2, \"0\");
        for (let d = 1; d <= 31; d += 1) {
          const date = ys + \"-\" + ms + \"-\" + String(d).padStart(2, \"0\");
          if (dp.test(date) !== isCalendarDate(date)) process.exit(1);
          const inst = date + \"T12:00:00.000Z\";
          if (ip.test(inst) !== isUtcInstant(inst)) process.exit(1);
        }
      }
    }
  "'

check "AC02 QA-01 impossible dates rejected by code and schema" \
  bash -c 'node --input-type=module -e "
    import { isUtcInstant, isCalendarDate } from \"./contracts/js/index.mjs\";
    import { readFileSync } from \"node:fs\";
    const pat = (f) => new RegExp(JSON.parse(readFileSync(\"contracts/schemas/\" + f, \"utf8\")).pattern);
    const instant = pat(\"utc-instant.schema.json\"), date = pat(\"calendar-date.schema.json\");
    for (const d of [\"2026-02-29\", \"2026-02-30\", \"2026-04-31\", \"1900-02-29\"]) {
      if (isCalendarDate(d) || date.test(d)) process.exit(1);
      if (isUtcInstant(d + \"T12:00:00.000Z\") || instant.test(d + \"T12:00:00.000Z\")) process.exit(1);
    }
    for (const d of [\"2028-02-29\", \"2000-02-29\"]) {
      if (!isCalendarDate(d) || !date.test(d)) process.exit(1);
      if (!isUtcInstant(d + \"T12:00:00.000Z\") || !instant.test(d + \"T12:00:00.000Z\")) process.exit(1);
    }
  "'


# --- AC03: optimistic concurrency / version rules ------------------------------------
check "AC03 ADR defines the compare-and-set outcomes" \
  bash -c "grep -q 'concurrency_conflict' $ADR && grep -q 'precondition_required' $ADR"
check "AC03 ADR forbids silent last-write-wins on sync" \
  bash -c "grep -q 'no silent last-write-wins' $ADR"
check "AC03 versioned-resource and offline-mutation schemas published" \
  bash -c "test -f contracts/schemas/versioned-resource.schema.json && test -f contracts/schemas/offline-mutation.schema.json"
check "AC03 concurrency implementation published" test -f contracts/js/concurrency.mjs

# --- AC04: contracts represented in shared code/schema and tests ---------------------
check "AC04 every published schema is valid JSON" \
  bash -c "python3 -c 'import json,glob;[json.load(open(f)) for f in glob.glob(\"contracts/schemas/*.json\")]'"
check "AC04 contracts are documented for consumers" test -f contracts/README.md
check "AC04 ADR index lists ADR-0001" bash -c "grep -q 'ADR-0001' docs/adr/README.md"
check "AC04 fixtures are marked synthetic" \
  bash -c "grep -q SYNTHETIC contracts/js/contracts.test.mjs && grep -q SYNTHETIC contracts/README.md"
check "AC04 contract behaviour suite passes" node --test contracts/js/

echo "=============================="
echo "$((total - failed))/$total checks passed"
[ "$failed" -eq 0 ]
