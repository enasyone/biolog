// SUPERSEDED by evidence/BIO-ARC-005/builder/adapted-review-round5.mjs — kept as the
// historical record of what Round 4's fix closed at the time. Seyi's round-4 re-review
// found the registryId-keyed Map still insufficient (a caller could open two DIFFERENT
// valid ids over the same persisted content) and the fused-verification design's QA-02
// claim disproved outright (a no-op applyUp still converges). This file's own probes now
// fail against the Round 5 implementation: createMigrationRegistry no longer exists at all
// (retired in favour of openMigrationRegistry()/createMigrationRegistryForTests). Left
// unmodified, not deleted, as the evidence trail; do not treat it as a current-state
// reproduction.
//
// Adapted transcription of evidence/BIO-ARC-005/codex/independent-review.mjs's round-3
// re-review probes, for Round 4 (god's pattern-naming message, QA-01 registryId-singleton
// fix, QA-02 fused-verification fix). Seyi's own script is UNCHANGED and re-run as-is (see
// git-state.txt / SUMMARY.md Round 4 for its output).
//
// Both Round 4 fixes changed assignSequence's/createMigrationRegistry's calling convention
// again: createMigrationRegistry now REQUIRES a registryId as its first argument (the
// script's `createMigrationRegistry(persistedRecords)` and `createMigrationRegistry()`
// calls pass an array or nothing where a registryId string is now required), and
// assignSequence now REQUIRES applyUp/hashState/priorState to run the real check itself
// (the script never supplies them, since Round 3's API didn't need them). Re-running the
// script unmodified: three probes hit the registryId requirement, and the fourth (which
// wants a caller-forged verifier-shaped proof rejected) gets a different, but still
// correct, rejection reason for the same underlying cause. This file transcribes all four
// probes plus a fifth positive case, using the new sanctioned API, to confirm the
// underlying guarantees independently of this interaction. All identifiers, targets and
// bodies are SYNTHETIC.
import assert from 'node:assert/strict';
import {
  proposeMigration, assignSequence, createMigrationRegistry,
  verifyFreshCreateMatchesUpgrade, hashSchemaState,
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

let failures = 0;
function probe(name, fn) {
  try { fn(); console.log(`PASS ${name}`); }
  catch (error) { failures += 1; console.error(`FAIL ${name}: ${error.message}`); }
}

probe('QA-01 (adapted) rejects two authoritative handles minted for the SAME registryId', () => {
  const id = 'shared-persisted-registry';
  const first = assignSequence(createMigrationRegistry(id), proposal(), {
    lockHeldBy: 'Kemi', applyUp: trivialApplyUp, hashState: hashSchemaState, priorState: {},
  });
  const second = assignSequence(createMigrationRegistry(id), proposal({ storyId: 'BIO-IDN-002' }), {
    lockHeldBy: 'Kemi', applyUp: trivialApplyUp, hashState: hashSchemaState, priorState: {},
  });
  assert.notEqual(first.record.sequence, second.record.sequence,
    'two distinct records were both assigned sequence 1');
});

probe('QA-02 (adapted) refuses a caller-forged, verifier-shaped proof that was never actually computed', () => {
  const forged = proposal({
    proofs: { fresh_create_state_hash: 'a'.repeat(64), upgrade_from_previous_state_hash: 'a'.repeat(64) },
  });
  assert.throws(
    () => assignSequence(createMigrationRegistry('qa02-forged'), forged, {
      lockHeldBy: 'Kemi', applyUp: trivialApplyUp, hashState: hashSchemaState, priorState: {},
    }),
    (error) => error?.code === 'proof_declaration_mismatch',
    'assignment accepted a caller-fabricated copy of the verifier output shape',
  );
});

probe('QA-02 (adapted) verifier fails closed rather than returning an assignable mismatch', () => {
  const seed = assignSequence(createMigrationRegistry('qa02-failclosed'), proposal(), {
    lockHeldBy: 'Kemi', applyUp: trivialApplyUp, hashState: hashSchemaState, priorState: {},
  }).record;
  assert.throws(
    () => verifyFreshCreateMatchesUpgrade([seed], {
      emptyState: { rows: 0 },
      priorState: { rows: 7 },
      applyUp: (state) => ({ ...state, marker: state.rows }),
      hashState: hashSchemaState,
    }),
    (error) => error?.code === 'proof_mismatch',
    'verifier returned matches:false instead of refusing the failed proof',
  );
});

probe('QA-03 (adapted) breaking dependency must match the declared deprecation target', () => {
  const unrelated = assignSequence(createMigrationRegistry('qa03-target'), proposal({
    forwardSafety: { classification: 'additive', deprecation_window: { target: 'person.legacy_email' } },
  }), { lockHeldBy: 'Kemi', applyUp: trivialApplyUp, hashState: hashSchemaState, priorState: {} }).registry;
  const breaking = proposal({
    storyId: 'BIO-IDN-003',
    description: 'drop credential.secret',
    up: 'DROP COLUMN credential.secret',
    forwardSafety: { classification: 'breaking', depends_on_deprecation_sequence: 1, breaks: 'credential.secret' },
    reversibility: { reversible: false, repair_procedure: 'synthetic forward repair' },
  });
  assert.throws(
    () => assignSequence(unrelated, breaking, { lockHeldBy: 'Kemi', applyUp: trivialApplyUp, hashState: hashSchemaState, priorState: {} }),
    (error) => error?.code === 'missing_deprecation_dependency',
    'a deprecation window for person.legacy_email authorized dropping credential.secret',
  );
});

probe('QA-02 (adapted) a genuinely matching, real verifier-computed proof assigns cleanly', () => {
  const { record } = assignSequence(createMigrationRegistry('qa02-positive'), proposal(), {
    lockHeldBy: 'Kemi', applyUp: trivialApplyUp, hashState: hashSchemaState, priorState: {},
  });
  assert.equal(record.sequence, 1);
});

if (failures) process.exitCode = 1;
