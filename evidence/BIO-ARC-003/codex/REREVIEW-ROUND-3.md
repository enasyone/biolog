# BIO-ARC-003 independent re-review — round 3

- Reviewer: Seyi
- Primary fix: `82cb6b5`
- Causation alignment: `3da43a3`
- Reviewed head: `581cdac`
- Verdict: **FAIL**

## Closure status

- QA-01 original nested/unclassified probes: closed.
- QA-02 retained-reference mutation: closed.
- QA-03 required audit device and domain-event causation: closed.
- Additional audit `causation_id` alignment: present, nullable, and shape-validated.

The unchanged round-two probes pass. Builder acceptance and regression checks pass 32/32;
the contract suite passes 176/176.

## New finding

### BIO-ARC-003-QA-04 — High — dotted field names alias nested allowlist paths

The recursive projector represents a path by joining caller-controlled field names with
`.`. It does not reject or escape a literal dot in a field name. Consequently the nested
path `profile` → `name` and the top-level field `profile.name` share the same allowlist and
classification key.

If `profile.name` is legitimately allowlisted and classified for a flat source field, a
caller can also supply `{ profile: { name: <sensitive value> } }`; once `profile` is an
allowed container, the sensitive nested value is emitted using the flat field's
classification. The ADR explicitly treats field names as caller-controlled, so the path
representation must be unambiguous or reject ambiguous keys.

Reproduction:

```sh
bash evidence/BIO-ARC-003/codex/independent-review.sh
```

Failing probe: `AC03 literal dotted keys cannot alias nested allowlist paths`.

## Category verdicts

| Category | Verdict | Reason |
|---|---|---|
| Functional | PASS | AC01/AC02 required envelope fields and shapes now align. |
| Security | FAIL | Caller-controlled dotted keys can alias a nested redaction path and disclose a value. |
| Tenant isolation | PASS | ARC-002 tenant denial guarantees remain green. |
| Offline | PASS | Delivery dedupe and conflict behavior remain green. |
| Concurrency | PASS | Projected content is copied and deeply frozen. |
| Regression | PASS | Foundation 32/32 and contract 176/176 pass. |
