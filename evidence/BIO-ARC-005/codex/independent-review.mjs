import assert from 'node:assert/strict';
import {
  proposeMigration,
  assignSequence,
  openMigrationRegistry,
  createMigrationRegistryForTests,
  verifyFreshCreateMatchesUpgrade,
  hashSchemaState,
} from '../../../contracts/js/migrations.mjs';

const proposal = (overrides = {}) => proposeMigration({
  storyId: 'BIO-IDN-001',
  domainConflictGroup: 'identity',
  description: 'synthetic migration',
  up: 'synthetic up',
  reversibility: { down: 'synthetic down' },
  forwardSafety: { classification: 'additive' },
  proofs: {
    fresh_create_state_hash: hashSchemaState({}),
    upgrade_from_previous_state_hash: hashSchemaState({}),
  },
  ...overrides,
});

const assignmentOptions = {
  lockHeldBy: 'Kemi',
  applyUp: (state) => state,
  hashState: hashSchemaState,
  priorState: {},
};

let failures = 0;
function probe(name, fn) {
  try {
    fn();
    console.log(`PASS ${name}`);
  } catch (error) {
    failures += 1;
    console.error(`FAIL ${name}: ${error.message}`);
  }
}

probe('AC02 production registry cannot be aliased and serializes repeated opens', () => {
  const first = assignSequence(openMigrationRegistry([]), proposal(), assignmentOptions);
  const second = assignSequence(openMigrationRegistry([]), proposal({ storyId: 'BIO-IDN-002' }), assignmentOptions);
  assert.notEqual(first.record.sequence, second.record.sequence,
    'repeated production opens created competing sequence authorities');
  assert.equal(first.registry, second.registry, 'production opens did not return one authority');
});

probe('AC04 documented runner-fidelity boundary remains reproducible', () => {
  const { record } = assignSequence(createMigrationRegistryForTests('noop-proof-registry'), proposal({
    up: 'THIS MIGRATION BODY IS NEVER EXECUTED',
  }), assignmentOptions);
  assert.equal(record.sequence, 1, 'scope-boundary reproduction unexpectedly stopped assigning');
});

probe('AC04 verifier fails closed rather than returning an assignable mismatch', () => {
  const seed = assignSequence(createMigrationRegistryForTests('verifier-registry'), proposal(), assignmentOptions).record;
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

probe('AC03 breaking dependency must match the declared deprecation target', () => {
  const unrelated = assignSequence(createMigrationRegistryForTests('target-registry'), proposal({
    forwardSafety: { classification: 'additive', deprecation_window: { target: 'person.legacy_email' } },
  }), assignmentOptions).registry;
  const breaking = proposal({
    storyId: 'BIO-IDN-003',
    description: 'drop credential.secret',
    up: 'DROP COLUMN credential.secret',
    forwardSafety: { classification: 'breaking', depends_on_deprecation_sequence: 1, breaks: 'credential.secret' },
    reversibility: { reversible: false, repair_procedure: 'synthetic forward repair' },
  });
  assert.throws(
    () => assignSequence(unrelated, breaking, assignmentOptions),
    (error) => error?.code === 'missing_deprecation_dependency',
    'a deprecation window for person.legacy_email authorized dropping credential.secret',
  );
});

if (failures) process.exitCode = 1;
