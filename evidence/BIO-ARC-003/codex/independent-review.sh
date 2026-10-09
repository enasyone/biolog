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

check "builder acceptance and regression suite" bash tests/foundation/BIO-ARC-003.test.sh

check "AC01 audit schema requires the device/workstation slot" python3 -c '
import json
s=json.load(open("contracts/schemas/audit-event.schema.json"))
assert "device" in s["required"]
'

check "AC02 aggregate version is required and bare event version is forbidden" python3 -c '
import json
s=json.load(open("contracts/schemas/event-envelope.schema.json"))
assert "aggregate_version" in s["required"]
assert s["properties"]["aggregate_version"]["minimum"] == 1
assert s["not"]["required"] == ["version"]
'

check "AC02 domain event schema requires causation" python3 -c '
import json
s=json.load(open("contracts/schemas/event-envelope.schema.json"))
assert "causation_id" in s["required"]
'

check "AC03 a nested unclassified secret cannot cross an allowlist boundary" node --input-type=module -e '
  import { redact } from "./contracts/js/index.mjs";
  const secret = "synthetic-sensitive-value";
  const result = redact(
    { metadata: { display: "ok", newly_added_secret: secret } },
    { allow: ["metadata"], classifications: { metadata: "internal" } },
  );
  if (JSON.stringify(result.detail).includes(secret)) process.exit(1);
'

check "AC03 an audit event cannot change after it is built" node --input-type=module -e '
  import { buildAuditEvent, newId } from "./contracts/js/index.mjs";
  const metadata = { state: "APPROVED" };
  const event = buildAuditEvent({
    tenant_id: "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77",
    actor: { type: "system", id: "qa" }, action: "qa.verify",
    target: { type: "person", id: null }, outcome: "ALLOWED", reason_code: "verified",
    correlation_id: newId("event"), source: { metadata }, allow: ["metadata"],
    classifications: { metadata: "internal" },
  });
  const before = JSON.stringify(event);
  metadata.state = "REVOKED";
  metadata.injected = "synthetic-sensitive-value";
  if (JSON.stringify(event) !== before) process.exit(1);
'

check "AC03 unknown classification cannot produce an invalid envelope level" node --input-type=module -e '
  import { redact, ENVELOPE_CLASSIFICATIONS } from "./contracts/js/index.mjs";
  const result = redact({ field: "value" }, {
    allow: ["field"], classifications: { field: "unregistered_level" },
  });
  if (!ENVELOPE_CLASSIFICATIONS.includes(result.classification)) process.exit(1);
'

check "AC03 literal dotted keys cannot alias nested allowlist paths" node --input-type=module -e '
  import { redact } from "./contracts/js/index.mjs";
  const secret = "synthetic-sensitive-value";
  const result = redact(
    { profile: { name: secret }, "profile.name": "approved display value" },
    { allow: ["profile", "profile.name"], classifications: { "profile.name": "personal" } },
  );
  if (result.detail.profile?.name === secret) process.exit(1);
'

check "AC03 escaped path encoding is injective for dots and backslashes" node --input-type=module -e '
  import { escapeSegment } from "./contracts/js/index.mjs";
  const keys = ["a", ".", "\\", "a.b", "a\\b", ".\\", "\\."];
  const paths = [];
  for (const a of keys) {
    paths.push({ raw: [a], encoded: escapeSegment(a) });
    for (const b of keys) {
      paths.push({ raw: [a, b], encoded: `${escapeSegment(a)}.${escapeSegment(b)}` });
      for (const c of keys) paths.push({ raw: [a, b, c], encoded: `${escapeSegment(a)}.${escapeSegment(b)}.${escapeSegment(c)}` });
    }
  }
  const seen = new Map();
  for (const path of paths) {
    const prior = seen.get(path.encoded);
    if (prior !== undefined && JSON.stringify(prior) !== JSON.stringify(path.raw)) process.exit(1);
    seen.set(path.encoded, path.raw);
  }
'

check "AC03 low-entropy hashes are refused as correlatable references" node --input-type=module -e '
  import { correlatableReference } from "./contracts/js/index.mjs";
  for (const value of ["1990-01-01", "12345678901", "STAFF-00042"]) {
    try { correlatableReference(value); process.exit(1); }
    catch (e) { if (e.code !== "invalid_reference") process.exit(1); }
  }
'

check "AC04 content change under a reused event id is surfaced" node --input-type=module -e '
  import { newId, recordDelivery } from "./contracts/js/index.mjs";
  const seen = new Map();
  const event = {
    event_id: newId("event"), tenant_id: "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77",
    aggregate_version: 8, payload: { state: "ACTIVE" },
    received_at: "2026-09-21T00:00:00.000Z",
  };
  if (recordDelivery(seen, event).outcome !== "FIRST") process.exit(1);
  const conflict = recordDelivery(seen, { ...event, payload: { state: "REVOKED" } });
  if (conflict.outcome !== "CONFLICT" || conflict.reason_code !== "event_id_reuse") process.exit(1);
'

check "AC04 received_at alone does not change the fingerprint" node --input-type=module -e '
  import { newId, eventFingerprint } from "./contracts/js/index.mjs";
  const event = {
    event_id: newId("event"), tenant_id: "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77",
    payload: { state: "ACTIVE" }, received_at: "2026-09-21T00:00:00.000Z",
  };
  if (eventFingerprint(event) !== eventFingerprint({ ...event, received_at: "2026-09-21T00:01:00.000Z" })) process.exit(1);
'

exit "$([ "$failed" -eq 0 ] && echo 0 || echo 1)"
