// Adapted transcription of evidence/BIO-ARC-005/codex/independent-review.mjs's round-4
// re-review probes, for Round 5. Seyi's own script is UNCHANGED and re-run as-is; it now
// fails at IMPORT time (SyntaxError: no export named 'createMigrationRegistry') because
// Round 5 retires that export from the production surface entirely (ADR-0005 Decision 2.4).
// QA-02 is NOT re-probed here as a "closing" demonstration -- it is a documented, accepted
// scope boundary (ADR-0005 Decision 4.6), not a fixed finding. This file only re-confirms
// QA-01's actual closure.
import assert from 'node:assert/strict';
import {
  proposeMigration, assignSequence, openMigrationRegistry, createMigrationRegistryForTests,
  hashSchemaState,
} from '../../../contracts/js/migrations.mjs';

const trivialApplyUp = (state) => state;
const TRIVIAL_HASH = hashSchemaState({});

const proposal = (overrides = {}) => proposeMigration({
  storyId: 'BIO-IDN-001',
  domainConflictGroup: 'identity',
  description: 'synthetic migration',
  up: 'synthetic up',
  reversibility: { down: 'synthetic down' },
  forwardSafety: { classification: 'additive' },
  proofs: { fresh_create_state_hash: TRIVIAL_HASH, upgrade_from_previous_state_hash: TRIVIAL_HASH },
  ...overrides,
});
const opts = { lockHeldBy: 'Kemi', applyUp: trivialApplyUp, hashState: hashSchemaState, priorState: {} };

let failures = 0;
function probe(name, fn) {
  try { fn(); console.log(`PASS ${name}`); }
  catch (error) { failures += 1; console.error(`FAIL ${name}: ${error.message}`); }
}

probe('QA-01 (adapted) the exact codex round-4 attack: two caller-named ids over one persisted history — no longer even expressible on the production path', () => {
  // codex's probe called createMigrationRegistry('migration-core', persistedRecords) and
  // createMigrationRegistry('migration-core-alias', persistedRecords). That call shape no
  // longer exists: openMigrationRegistry() takes no id at all.
  assert.equal(openMigrationRegistry.length, 0, 'the production factory must not accept an id argument at all');
  const first = assignSequence(openMigrationRegistry(), proposal({ storyId: 'BIO-IDN-001' }), opts);
  const second = assignSequence(openMigrationRegistry(), proposal({ storyId: 'BIO-IDN-002' }), opts);
  assert.equal(first.registry, second.registry, 'every call must resolve to the one real authority');
  assert.notEqual(first.record.sequence, second.record.sequence);
});

probe('QA-01 (adapted) the test-only factory cannot alias the real registry even when given the production name as text', () => {
  const alias1 = createMigrationRegistryForTests('migration-core');
  const alias2 = createMigrationRegistryForTests('migration-core-alias');
  assert.notEqual(alias1, alias2, 'two distinct test ids are legitimately distinct test-scoped authorities');
  assert.notEqual(alias1, openMigrationRegistry(), 'a test id spelled "migration-core" is never the real production registry');
});

if (failures) process.exitCode = 1;
