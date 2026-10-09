#!/usr/bin/env bash
# Developer tests for BIO-ARC-005, mapped to acceptance criteria.
# Run from anywhere: tests/foundation/BIO-ARC-005.test.sh
#
# Artifact and consistency checks here; behaviour is asserted by the node:test suite in
# contracts/js/migrations.test.mjs. Both are offline and dependency-free.
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

ADR=docs/adr/ADR-0005-migration-ownership-and-sequencing.md
# A trivial, always-converging verification setup (ADR Decision 4.5: assignSequence now
# RUNS the check itself) — applyUp never touches state, so fresh-create and
# upgrade-from-previous always agree as long as emptyState and priorState both hash the
# same. Reused across these inline scripts wherever a check is not specifically probing
# AC04/QA-02's real-migration semantics.
TRIVIAL_VERIFY='applyUp: (s) => s, hashState: hashSchemaState, priorState: {}'
TRIVIAL_PROOFS='{ fresh_create_state_hash: hashSchemaState({}), upgrade_from_previous_state_hash: hashSchemaState({}) }'

# --- AC01: feature agents propose; Kemi assigns sequence numbers ---------------------
check "AC01 ADR-0005 exists and is accepted" \
  bash -c "test -f $ADR && grep -q '\*\*Status:\*\* Accepted (BIO-ARC-005' $ADR"
check "AC01 proposal and record schemas published" \
  bash -c "test -f contracts/schemas/migration-proposal.schema.json && test -f contracts/schemas/migration-record.schema.json"
check "AC01 migrations.mjs implementation published" test -f contracts/js/migrations.mjs
check "AC01 a proposal carries no sequence number; only assignSequence adds one" \
  bash -c "node --input-type=module -e '
    import { proposeMigration, assignSequence, createMigrationRegistryForTests, hashSchemaState } from \"./contracts/js/index.mjs\";
    const p = proposeMigration({ storyId: \"BIO-IDN-001\", domainConflictGroup: \"identity\",
      description: \"d\", up: \"u\", reversibility: { down: \"d\" },
      forwardSafety: { classification: \"additive\" },
      proofs: $TRIVIAL_PROOFS });
    if (\"sequence\" in p) process.exit(1);
    const { record } = assignSequence(createMigrationRegistryForTests(\"r1\"), p, { lockHeldBy: \"Kemi\", $TRIVIAL_VERIFY });
    if (record.sequence !== 1 || record.assigned_by !== \"Kemi\") process.exit(1);
  '"

# --- AC02: only one migration-core conflict group active at a time -------------------
check "AC02 ADR states the rule precisely and pre-empts the three rationalisations" \
  bash -c "grep -q 'No two migration records may ever be assigned sequence numbers' $ADR \
    && grep -q 'touch the same tables, so they can be assigned in parallel' $ADR \
    && grep -q 'Split the assignment work across two trusted helpers' $ADR"
check "AC02 assignment is refused without the lock explicitly held by Kemi" \
  bash -c "node --input-type=module -e '
    import { proposeMigration, assignSequence, createMigrationRegistryForTests, hashSchemaState } from \"./contracts/js/index.mjs\";
    const p = proposeMigration({ storyId: \"BIO-IDN-001\", domainConflictGroup: \"identity\",
      description: \"d\", up: \"u\", reversibility: { down: \"d\" },
      forwardSafety: { classification: \"additive\" },
      proofs: $TRIVIAL_PROOFS });
    try { assignSequence(createMigrationRegistryForTests(\"r2\"), p, { lockHeldBy: \"Seyi\", $TRIVIAL_VERIFY }); process.exit(1); }
    catch (e) { if (e.code !== \"lock_not_held\") process.exit(1); }
  '"

# --- QA-01: exactly one production registry authority, with no caller-choosable id ---
check "QA-01 assignSequence refuses a plain object that never went through openMigrationRegistry" \
  bash -c "node --input-type=module -e '
    import { proposeMigration, assignSequence, hashSchemaState } from \"./contracts/js/index.mjs\";
    const p = proposeMigration({ storyId: \"BIO-IDN-001\", domainConflictGroup: \"identity\",
      description: \"d\", up: \"u\", reversibility: { down: \"d\" },
      forwardSafety: { classification: \"additive\" },
      proofs: $TRIVIAL_PROOFS });
    try { assignSequence({ records: [] }, p, { lockHeldBy: \"Kemi\", $TRIVIAL_VERIFY }); process.exit(1); }
    catch (e) { if (e.code !== \"invalid_registry_handle\") process.exit(1); }
  '"
check "QA-01 assignSequence refuses a JSON round-trip of a real handle" \
  bash -c "node --input-type=module -e '
    import { proposeMigration, assignSequence, createMigrationRegistryForTests, hashSchemaState } from \"./contracts/js/index.mjs\";
    const mk = (storyId) => proposeMigration({ storyId, domainConflictGroup: \"identity\",
      description: \"d\", up: \"u\", reversibility: { down: \"d\" },
      forwardSafety: { classification: \"additive\" }, proofs: $TRIVIAL_PROOFS });
    const real = createMigrationRegistryForTests(\"r3\");
    assignSequence(real, mk(\"BIO-IDN-001\"), { lockHeldBy: \"Kemi\", $TRIVIAL_VERIFY });
    const persisted = JSON.parse(JSON.stringify(real));
    try { assignSequence(persisted, mk(\"BIO-IDN-002\"), { lockHeldBy: \"Kemi\", $TRIVIAL_VERIFY }); process.exit(1); }
    catch (e) { if (e.code !== \"invalid_registry_handle\") process.exit(1); }
  '"
check "QA-01 openMigrationRegistry() takes no id — there is no parameter left to alias" \
  bash -c "node --input-type=module -e '
    import { openMigrationRegistry } from \"./contracts/js/index.mjs\";
    if (openMigrationRegistry.length !== 0) process.exit(1);
  '"
check "QA-01 two openMigrationRegistry() calls always return the SAME live handle" \
  bash -c "node --input-type=module -e '
    import { proposeMigration, assignSequence, openMigrationRegistry, hashSchemaState } from \"./contracts/js/index.mjs\";
    const mk = (storyId) => proposeMigration({ storyId, domainConflictGroup: \"identity\",
      description: \"d\", up: \"u\", reversibility: { down: \"d\" },
      forwardSafety: { classification: \"additive\" }, proofs: $TRIVIAL_PROOFS });
    const first = assignSequence(openMigrationRegistry(), mk(\"BIO-IDN-001\"), { lockHeldBy: \"Kemi\", $TRIVIAL_VERIFY });
    const second = assignSequence(openMigrationRegistry(), mk(\"BIO-IDN-002\"), { lockHeldBy: \"Kemi\", $TRIVIAL_VERIFY });
    if (first.registry !== second.registry) process.exit(1);
    if (first.record.sequence === second.record.sequence) process.exit(1);
  '"
check "QA-01 the codex round-4 alias attack cannot reach the real registry, even under the SAME id text, via the test-only factory" \
  bash -c "node --input-type=module -e '
    import { proposeMigration, assignSequence, openMigrationRegistry, createMigrationRegistryForTests, hashSchemaState } from \"./contracts/js/index.mjs\";
    const mk = (storyId) => proposeMigration({ storyId, domainConflictGroup: \"identity\",
      description: \"d\", up: \"u\", reversibility: { down: \"d\" },
      forwardSafety: { classification: \"additive\" }, proofs: $TRIVIAL_PROOFS });
    const alias1 = createMigrationRegistryForTests(\"migration-core\");
    const alias2 = createMigrationRegistryForTests(\"migration-core-alias\");
    if (alias1 === alias2) process.exit(1);
    if (alias1 === openMigrationRegistry()) process.exit(1);
    if (alias2 === openMigrationRegistry()) process.exit(1);
  '"

# --- AC03: forward-safe and rollback/repair rules -------------------------------------
check "AC03 ADR reuses ADR-0004's additive-vs-breaking split explicitly" \
  bash -c "grep -q 'additive-vs-breaking split, one layer down' $ADR"
check "AC03 a breaking migration without a named deprecation dependency is refused" \
  bash -c "node --input-type=module -e '
    import { proposeMigration } from \"./contracts/js/index.mjs\";
    try {
      proposeMigration({ storyId: \"BIO-IDN-005\", domainConflictGroup: \"identity\",
        description: \"drop old\", up: \"u\", reversibility: { reversible: false, repair_procedure: \"r\" },
        forwardSafety: { classification: \"breaking\" },
        proofs: { fresh_create_state_hash: \"h\", upgrade_from_previous_state_hash: \"h\" } });
      process.exit(1);
    } catch (e) { if (e.code !== \"missing_deprecation_dependency\") process.exit(1); }
  '"
check "AC03 reversibility must be exactly one of down or reversible:false+repair_procedure" \
  bash -c "node --input-type=module -e '
    import { proposeMigration } from \"./contracts/js/index.mjs\";
    const base = { storyId: \"BIO-IDN-001\", domainConflictGroup: \"identity\", description: \"d\", up: \"u\",
      forwardSafety: { classification: \"additive\" },
      proofs: { fresh_create_state_hash: \"h\", upgrade_from_previous_state_hash: \"h\" } };
    for (const reversibility of [{}, { down: \"x\", reversible: false, repair_procedure: \"y\" }]) {
      try { proposeMigration({ ...base, reversibility }); process.exit(1); }
      catch (e) { if (e.code !== \"invalid_reversibility\") process.exit(1); }
    }
  '"
check "AC03 an assigned record is immutable — editing it in place is refused" \
  bash -c "node --input-type=module -e '
    import { proposeMigration, assignSequence, assertNotAnEdit, createMigrationRegistryForTests, hashSchemaState } from \"./contracts/js/index.mjs\";
    const p = proposeMigration({ storyId: \"BIO-IDN-001\", domainConflictGroup: \"identity\",
      description: \"d\", up: \"u\", reversibility: { down: \"d\" },
      forwardSafety: { classification: \"additive\" },
      proofs: $TRIVIAL_PROOFS });
    const { record, registry } = assignSequence(createMigrationRegistryForTests(\"r6\"), p, { lockHeldBy: \"Kemi\", $TRIVIAL_VERIFY });
    try { assertNotAnEdit(registry, { ...record, up: \"CHANGED\" }); process.exit(1); }
    catch (e) { if (e.code !== \"immutable_record\") process.exit(1); }
  '"

# --- QA-03: a deprecation dependency must be a real, matching-target window ----------
check "QA-03 a breaking migration cannot depend on an unrelated record that never declared a deprecation window" \
  bash -c "node --input-type=module -e '
    import { proposeMigration, assignSequence, createMigrationRegistryForTests, hashSchemaState } from \"./contracts/js/index.mjs\";
    const unrelatedProposal = proposeMigration({ storyId: \"BIO-IDN-001\", domainConflictGroup: \"identity\",
      description: \"d\", up: \"u\", reversibility: { down: \"d\" },
      forwardSafety: { classification: \"additive\" },
      proofs: $TRIVIAL_PROOFS });
    const { registry } = assignSequence(createMigrationRegistryForTests(\"r7\"), unrelatedProposal, { lockHeldBy: \"Kemi\", $TRIVIAL_VERIFY });
    const breaking = proposeMigration({ storyId: \"BIO-IDN-005\", domainConflictGroup: \"identity\",
      description: \"drop legacy\", up: \"u\", reversibility: { reversible: false, repair_procedure: \"r\" },
      forwardSafety: { classification: \"breaking\", depends_on_deprecation_sequence: 1, breaks: \"person.legacy_email\" },
      proofs: $TRIVIAL_PROOFS });
    try { assignSequence(registry, breaking, { lockHeldBy: \"Kemi\", $TRIVIAL_VERIFY }); process.exit(1); }
    catch (e) { if (e.code !== \"missing_deprecation_dependency\") process.exit(1); }
  '"
check "QA-03 a deprecation window for one target does not authorise dropping a different one" \
  bash -c "node --input-type=module -e '
    import { proposeMigration, assignSequence, createMigrationRegistryForTests, hashSchemaState } from \"./contracts/js/index.mjs\";
    const deprecating = proposeMigration({ storyId: \"BIO-IDN-004\", domainConflictGroup: \"identity\",
      description: \"d\", up: \"u\", reversibility: { down: \"d\" },
      forwardSafety: { classification: \"additive\", deprecation_window: { target: \"person.legacy_email\" } },
      proofs: $TRIVIAL_PROOFS });
    const { registry } = assignSequence(createMigrationRegistryForTests(\"r8\"), deprecating, { lockHeldBy: \"Kemi\", $TRIVIAL_VERIFY });
    const breaking = proposeMigration({ storyId: \"BIO-IDN-006\", domainConflictGroup: \"identity\",
      description: \"drop credential.secret\", up: \"u\", reversibility: { reversible: false, repair_procedure: \"r\" },
      forwardSafety: { classification: \"breaking\", depends_on_deprecation_sequence: 1, breaks: \"credential.secret\" },
      proofs: $TRIVIAL_PROOFS });
    try { assignSequence(registry, breaking, { lockHeldBy: \"Kemi\", $TRIVIAL_VERIFY }); process.exit(1); }
    catch (e) { if (e.code !== \"missing_deprecation_dependency\") process.exit(1); }
  '"
check "QA-03 a breaking migration assigns cleanly once its target genuinely matches the declared window" \
  bash -c "node --input-type=module -e '
    import { proposeMigration, assignSequence, createMigrationRegistryForTests, hashSchemaState } from \"./contracts/js/index.mjs\";
    const deprecating = proposeMigration({ storyId: \"BIO-IDN-004\", domainConflictGroup: \"identity\",
      description: \"d\", up: \"u\", reversibility: { down: \"d\" },
      forwardSafety: { classification: \"additive\", deprecation_window: { target: \"person.legacy_email\" } },
      proofs: $TRIVIAL_PROOFS });
    const { registry } = assignSequence(createMigrationRegistryForTests(\"r9\"), deprecating, { lockHeldBy: \"Kemi\", $TRIVIAL_VERIFY });
    const breaking = proposeMigration({ storyId: \"BIO-IDN-005\", domainConflictGroup: \"identity\",
      description: \"drop legacy\", up: \"u\", reversibility: { reversible: false, repair_procedure: \"r\" },
      forwardSafety: { classification: \"breaking\", depends_on_deprecation_sequence: 1, breaks: \"person.legacy_email\" },
      proofs: $TRIVIAL_PROOFS });
    const { record } = assignSequence(registry, breaking, { lockHeldBy: \"Kemi\", $TRIVIAL_VERIFY });
    if (record.sequence !== 2) process.exit(1);
  '"

# --- AC04: fresh-create and upgrade-from-previous tests are mandatory ----------------
check "AC04 ADR requires a real priorState, refusing the tautological default" \
  bash -c "grep -q 'tautology wearing one' $ADR && grep -q 'missing_prior_state' $ADR"
check "AC04 verifyFreshCreateMatchesUpgrade refuses a missing priorState" \
  bash -c "node --input-type=module -e '
    import { proposeMigration, assignSequence, verifyFreshCreateMatchesUpgrade, hashSchemaState, createMigrationRegistryForTests } from \"./contracts/js/index.mjs\";
    const p = proposeMigration({ storyId: \"BIO-IDN-001\", domainConflictGroup: \"identity\",
      description: \"d\", up: \"u\", reversibility: { down: \"d\" },
      forwardSafety: { classification: \"additive\" },
      proofs: $TRIVIAL_PROOFS });
    const { registry } = assignSequence(createMigrationRegistryForTests(\"r10\"), p, { lockHeldBy: \"Kemi\", $TRIVIAL_VERIFY });
    try { verifyFreshCreateMatchesUpgrade(registry.records, { applyUp: (s) => s, hashState: hashSchemaState }); process.exit(1); }
    catch (e) { if (e.code !== \"missing_prior_state\") process.exit(1); }
  '"
check "AC04/QA-02 verifyFreshCreateMatchesUpgrade itself refuses a migration reading accumulated data, called directly" \
  bash -c "node --input-type=module -e '
    import { proposeMigration, assignSequence, verifyFreshCreateMatchesUpgrade, hashSchemaState, createMigrationRegistryForTests } from \"./contracts/js/index.mjs\";
    const proposals = [
      { description: \"create widget\", up: \"CREATE widget\" },
      { description: \"add computed_count\", up: \"ADD computed_count DEFAULT count(*)\" },
    ];
    let registry = createMigrationRegistryForTests(\"r11\");
    for (const over of proposals) {
      const p = proposeMigration({ storyId: \"BIO-IDN-001\", domainConflictGroup: \"identity\",
        reversibility: { down: \"d\" }, forwardSafety: { classification: \"additive\" },
        proofs: $TRIVIAL_PROOFS, ...over });
      registry = assignSequence(registry, p, { lockHeldBy: \"Kemi\", $TRIVIAL_VERIFY }).registry;
    }
    const applyUp = (state, record) => {
      if (record.description === \"create widget\") return { rowCount: 0 };
      if (record.description === \"add computed_count\") return { ...state, computed_count: state.rowCount };
      return state;
    };
    try {
      verifyFreshCreateMatchesUpgrade(registry.records, { applyUp, hashState: hashSchemaState, priorState: { rowCount: 42 } });
      process.exit(1);
    } catch (e) { if (e.code !== \"proof_mismatch\") process.exit(1); }
  '"

# --- QA-02: the module runs the check itself; it does not accept a claim to have run it --
check "QA-02 assignSequence refuses to assign without applyUp/hashState" \
  bash -c "node --input-type=module -e '
    import { proposeMigration, assignSequence, createMigrationRegistryForTests } from \"./contracts/js/index.mjs\";
    const p = proposeMigration({ storyId: \"BIO-IDN-001\", domainConflictGroup: \"identity\",
      description: \"d\", up: \"u\", reversibility: { down: \"d\" },
      forwardSafety: { classification: \"additive\" },
      proofs: { fresh_create_state_hash: \"h\", upgrade_from_previous_state_hash: \"h\" } });
    try { assignSequence(createMigrationRegistryForTests(\"r12\"), p, { lockHeldBy: \"Kemi\", priorState: {} }); process.exit(1); }
    catch (e) { if (e.code !== \"verification_required\") process.exit(1); }
  '"
check "QA-02 a caller-forged, perfectly verifier-shaped proof is irrelevant to assignment" \
  bash -c "node --input-type=module -e '
    import { proposeMigration, assignSequence, createMigrationRegistryForTests, hashSchemaState } from \"./contracts/js/index.mjs\";
    const p = proposeMigration({ storyId: \"BIO-IDN-001\", domainConflictGroup: \"identity\",
      description: \"d\", up: \"u\", reversibility: { down: \"d\" },
      forwardSafety: { classification: \"additive\" },
      proofs: { fresh_create_state_hash: \"a\".repeat(64), upgrade_from_previous_state_hash: \"a\".repeat(64) } });
    try { assignSequence(createMigrationRegistryForTests(\"r13\"), p, { lockHeldBy: \"Kemi\", $TRIVIAL_VERIFY }); process.exit(1); }
    catch (e) { if (e.code !== \"proof_declaration_mismatch\") process.exit(1); }
  '"
check "QA-02 assignSequence computes the proof itself; a genuine match assigns cleanly" \
  bash -c "node --input-type=module -e '
    import { proposeMigration, assignSequence, createMigrationRegistryForTests, hashSchemaState } from \"./contracts/js/index.mjs\";
    const p = proposeMigration({ storyId: \"BIO-IDN-001\", domainConflictGroup: \"identity\",
      description: \"d\", up: \"u\", reversibility: { down: \"d\" },
      forwardSafety: { classification: \"additive\" },
      proofs: $TRIVIAL_PROOFS });
    const { record } = assignSequence(createMigrationRegistryForTests(\"r14\"), p, { lockHeldBy: \"Kemi\", $TRIVIAL_VERIFY });
    if (record.sequence !== 1) process.exit(1);
  '"
check "QA-02 the ADR documents the honest, narrower guarantee as an explicit accepted scope boundary" \
  bash -c "grep -q 'What this module can and cannot prove about a supplied runner' $ADR \
    && grep -q 'accepted, explicit scope boundary' $ADR"

# --- shared-contract hygiene ---------------------------------------------------------
check "every published schema is valid JSON" \
  bash -c "python3 -c 'import json,glob;[json.load(open(f)) for f in glob.glob(\"contracts/schemas/*.json\")]'"
check "ADR index lists ADR-0005" bash -c "grep -q 'ADR-0005' docs/adr/README.md"
check "contracts README documents the migration contract" bash -c "grep -q 'migration-proposal' contracts/README.md"
check "fixtures are marked synthetic" bash -c "grep -q SYNTHETIC contracts/js/migrations.test.mjs"
check "BIO-ARC-001 contracts still green (no regression)" bash tests/foundation/BIO-ARC-001.test.sh
check "BIO-ARC-002 contracts still green (no regression)" bash tests/foundation/BIO-ARC-002.test.sh
check "BIO-ARC-003 contracts still green (no regression)" bash tests/foundation/BIO-ARC-003.test.sh
check "BIO-ARC-004 contracts still green (no regression)" bash tests/foundation/BIO-ARC-004.test.sh
check "contract behaviour suite passes" node --test contracts/js/

echo "=============================="
echo "$((total - failed))/$total checks passed"
[ "$failed" -eq 0 ]
