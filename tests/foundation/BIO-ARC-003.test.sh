#!/usr/bin/env bash
# Developer tests for BIO-ARC-003, mapped to acceptance criteria.
# Run from anywhere: tests/foundation/BIO-ARC-003.test.sh
#
# Artifact and consistency checks here; behaviour is asserted by the node:test suite in
# contracts/js/audit.test.mjs. Both are offline and dependency-free.
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

ADR=docs/adr/ADR-0003-audit-evidence-and-event-envelopes.md

# --- AC01: audit envelope ------------------------------------------------------------
check "AC01 ADR-0003 exists and is accepted" \
  bash -c "test -f $ADR && grep -q '\*\*Status:\*\* Accepted (BIO-ARC-003' $ADR"
check "AC01 audit-event schema published" test -f contracts/schemas/audit-event.schema.json
check "AC01 audit envelope requires every field the criterion names" \
  bash -c 'python3 -c "
import json
r = json.load(open(\"contracts/schemas/audit-event.schema.json\"))[\"required\"]
need = [\"tenant_id\",\"actor\",\"action\",\"target\",\"reason_code\",\"correlation_id\",\"occurred_at\",\"received_at\",\"outcome\"]
raise SystemExit(0 if all(f in r for f in need) else 1)
"'
check "AC01 ADR requires a structured actor" \
  bash -c "grep -q 'structured value, not a free string' $ADR"
check "AC01 ADR requires outcome and reason on success too" \
  bash -c "grep -q 'including on success' $ADR"
check "AC01 the ARC-002 denial event is a profile of the audit envelope" \
  bash -c 'python3 -c "
import json
d = json.load(open(\"contracts/schemas/tenant-access-denial-event.schema.json\"))
raise SystemExit(0 if d.get(\"allOf\") == [{\"\$ref\": \"audit-event.schema.json\"}]
                 and d[\"properties\"][\"outcome\"][\"const\"] == \"DENIED\"
                 and \"attempted_tenant_id\" in d[\"required\"] else 1)
"'
check "AC01 audit implementation published" test -f contracts/js/audit.mjs

# --- AC02: domain event envelope -----------------------------------------------------
check "AC02 event envelope requires id, aggregate, version, tenant, correlation, time" \
  bash -c 'python3 -c "
import json
r = json.load(open(\"contracts/schemas/event-envelope.schema.json\"))[\"required\"]
need = [\"event_id\",\"schema_version\",\"tenant_id\",\"aggregate_type\",\"aggregate_id\",\"aggregate_version\",\"correlation_id\",\"occurred_at\"]
raise SystemExit(0 if all(f in r for f in need) else 1)
"'
check "AC02 a bare version on an event is still refused" \
  bash -c 'python3 -c "
import json
s = json.load(open(\"contracts/schemas/event-envelope.schema.json\"))
raise SystemExit(0 if s[\"not\"][\"required\"] == [\"version\"] else 1)
"'
check "AC02 ADR explains version vs schema version" \
  bash -c "grep -q 'not a version \*of the event\*' $ADR"

# --- AC03: classification and redaction ----------------------------------------------
check "AC03 classification registry published with five levels" \
  bash -c 'python3 -c "
import json
r = json.load(open(\"contracts/schemas/data-classification.json\"))
levels = [l[\"level\"] for l in r[\"levels\"]]
secret = [l for l in r[\"levels\"] if l[\"level\"] == \"secret\"][0]
raise SystemExit(0 if levels == [\"public\",\"internal\",\"personal\",\"sensitive\",\"secret\"]
                 and secret[\"envelope\"] == \"never\" else 1)
"'
check "AC03 ADR mandates an allowlist, not a denylist" \
  bash -c "grep -q 'allowlist, never a denylist' $ADR"
check "AC03 ADR forbids hashing low-entropy personal data as redaction" \
  bash -c "grep -q 'Hashing is not redaction for low-entropy personal data' $ADR"
check "AC03 a secret is never emitted even when allowlisted" \
  bash -c 'node --input-type=module -e "
    import { redact } from \"./contracts/js/index.mjs\";
    const { detail, withheld } = redact({ template: \"AAECAwQ=\" },
      { allow: [\"template\"], classifications: { template: \"secret\" } });
    if (JSON.stringify(detail) !== \"{}\") process.exit(1);
    if (withheld[0] !== \"template\") process.exit(1);
  "'
check "AC03 a field added later is withheld by default" \
  bash -c 'node --input-type=module -e "
    import { redact } from \"./contracts/js/index.mjs\";
    const allow = [\"surname\"], classifications = { surname: \"personal\" };
    const after = redact({ surname: \"Okafor\", national_id: \"12345678901\" }, { allow, classifications });
    if (JSON.stringify(after.detail) !== JSON.stringify({ surname: \"Okafor\" })) process.exit(1);
    if (!after.withheld.includes(\"national_id\")) process.exit(1);
  "'

check "AC03 QA-01 ADR requires a recursive path allowlist" \
  bash -c "grep -q 'list of paths, and it projects recursively' $ADR"
check "AC03 QA-01 a nested secret cannot cross an allowlist boundary" \
  bash -c 'node --input-type=module -e "
    import { redact } from \"./contracts/js/index.mjs\";
    const r = redact({ card: { sequence: \"001\", template: \"SECRET-TEMPLATE\" } },
      { allow: [\"card\", \"card.sequence\"],
        classifications: { \"card.sequence\": \"internal\", \"card.template\": \"secret\" } });
    if (JSON.stringify(r.detail).includes(\"SECRET-TEMPLATE\")) process.exit(1);
    if (!r.withheld.includes(\"card.template\")) process.exit(1);
  "'
check "AC03 QA-01 an unregistered classification is withheld and never becomes the level" \
  bash -c 'node --input-type=module -e "
    import { redact, ENVELOPE_CLASSIFICATIONS } from \"./contracts/js/index.mjs\";
    const r = redact({ f: \"v\" }, { allow: [\"f\"], classifications: { f: \"made_up\" } });
    if (Object.keys(r.detail).length !== 0) process.exit(1);
    if (!ENVELOPE_CLASSIFICATIONS.includes(r.classification)) process.exit(1);
  "'
check "AC03 QA-04 ADR requires the dotted-key collision rule" \
  bash -c "grep -q 'A dot-joined path is a caller-controlled string, and strings can be forged to collide' $ADR"
check "AC03 QA-04 a flat dotted key aliasing a real nested value is withheld on both sides" \
  bash -c 'node --input-type=module -e "
    import { redact } from \"./contracts/js/index.mjs\";
    const secret = \"synthetic-sensitive-value\";
    const result = redact(
      { profile: { name: secret }, \"profile.name\": \"approved display value\" },
      { allow: [\"profile\", \"profile.name\"], classifications: { \"profile.name\": \"personal\" } },
    );
    if (result.detail.profile?.name === secret) process.exit(1);
    if (\"profile.name\" in result.detail) process.exit(1);
  "'
check "AC03 QA-02 ADR requires deep immutability" \
  bash -c "grep -q 'An audit event is immutable all the way down' $ADR"
check "AC03 QA-02 mutating the source cannot change a built event" \
  bash -c 'node --input-type=module -e "
    import { buildAuditEvent, newId } from \"./contracts/js/index.mjs\";
    const source = { card: { sequence: \"001\" } };
    const e = buildAuditEvent({
      tenant_id: \"ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77\", actor: { type: \"user\", id: \"a\" },
      action: \"x.y\", target: { type: \"t\", id: null }, outcome: \"ALLOWED\", reason_code: \"ok\",
      correlation_id: newId(\"event\"), source, allow: [\"card\", \"card.sequence\"],
      classifications: { \"card.sequence\": \"internal\" } });
    const before = JSON.stringify(e);
    source.card.sequence = \"999\"; source.card.injected = \"LEAK\";
    if (JSON.stringify(e) !== before) process.exit(1);
    if (e.detail.card.sequence !== \"001\" || e.detail.card.injected !== undefined) process.exit(1);
  "'
check "AC01/AC02 QA-03 device and causation_id are schema-required" \
  bash -c 'python3 -c "
import json
a = json.load(open(\"contracts/schemas/audit-event.schema.json\"))
e = json.load(open(\"contracts/schemas/event-envelope.schema.json\"))
raise SystemExit(0 if \"device\" in a[\"required\"] and \"causation_id\" in e[\"required\"] else 1)
"'
check "AC01 audit envelope's own causation_id is also required and nullable" \
  bash -c 'python3 -c "
import json
a = json.load(open(\"contracts/schemas/audit-event.schema.json\"))
raise SystemExit(0 if \"causation_id\" in a[\"required\"]
                 and \"null\" in a[\"properties\"][\"causation_id\"][\"type\"] else 1)
"'
check "AC01 causation_id shape is validated at construction, like correlation_id" \
  bash -c 'node --input-type=module -e "
    import { buildAuditEvent, newId } from \"./contracts/js/index.mjs\";
    const base = {
      tenant_id: \"ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77\", actor: { type: \"user\", id: \"a\" },
      action: \"x.y\", target: { type: \"t\", id: null }, outcome: \"ALLOWED\", reason_code: \"ok\",
      correlation_id: newId(\"event\") };
    buildAuditEvent({ ...base, causation_id: null });
    buildAuditEvent({ ...base, causation_id: newId(\"event\") });
    try { buildAuditEvent({ ...base, causation_id: \"not-an-id\" }); process.exit(1); }
    catch (e) { if (e.code !== \"invalid_causation_id\") process.exit(1); }
  "'

# --- AC04: idempotency ---------------------------------------------------------------
check "AC04 ADR defines the dedupe key and the conflict rule" \
  bash -c "grep -q 'dedupe key is' $ADR && grep -q 'event_id_reuse' $ADR"
check "AC04 duplicate is ignored and a reused id with new content conflicts" \
  bash -c 'node --input-type=module -e "
    import { recordDelivery, newId } from \"./contracts/js/index.mjs\";
    const seen = new Map();
    const e = { event_id: newId(\"event\"), tenant_id: \"ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77\",
                payload: { state: \"REVOKED\" }, received_at: \"2026-09-21T00:00:00.000Z\" };
    if (recordDelivery(seen, e).outcome !== \"FIRST\") process.exit(1);
    if (recordDelivery(seen, { ...e, received_at: \"2026-09-21T00:00:30.000Z\" }).outcome !== \"DUPLICATE\") process.exit(1);
    const c = recordDelivery(seen, { ...e, payload: { state: \"ACTIVE\" } });
    if (c.outcome !== \"CONFLICT\" || c.reason_code !== \"event_id_reuse\") process.exit(1);
  "'

# --- shared-contract hygiene ---------------------------------------------------------
check "every published schema is valid JSON" \
  bash -c "python3 -c 'import json,glob;[json.load(open(f)) for f in glob.glob(\"contracts/schemas/*.json\")]'"
check "ADR index lists ADR-0003" bash -c "grep -q 'ADR-0003' docs/adr/README.md"
check "contracts README documents the audit contract" bash -c "grep -q 'audit-event' contracts/README.md"
check "fixtures are marked synthetic" bash -c "grep -q SYNTHETIC contracts/js/audit.test.mjs"
check "BIO-ARC-001 contracts still green (no regression)" bash tests/foundation/BIO-ARC-001.test.sh
check "BIO-ARC-002 contracts still green (no regression)" bash tests/foundation/BIO-ARC-002.test.sh
check "contract behaviour suite passes" node --test contracts/js/

echo "=============================="
echo "$((total - failed))/$total checks passed"
[ "$failed" -eq 0 ]
