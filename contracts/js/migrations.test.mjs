// Developer tests for BIO-ARC-005. Every test name carries the AC it proves.
// Run: node --test contracts/js/   (no dependencies, offline-safe)
// All story ids, table names and migration bodies here are SYNTHETIC.

import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import {
  SEQUENCING_CONFLICT_GROUP, FORWARD_SAFETY_CLASSES, SOLE_ASSIGNER,
  MigrationError, proposeMigration, assignSequence,
  openMigrationRegistry, createMigrationRegistryForTests,
  isGaplessSequence, assertNotAnEdit,
  verifyFreshCreateMatchesUpgrade, hashSchemaState,
  newId, isUtcInstant,
} from './index.mjs';

const schema = (name) =>
  JSON.parse(readFileSync(fileURLToPath(new URL(`../schemas/${name}`, import.meta.url)), 'utf8'));
const ADR = readFileSync(
  fileURLToPath(new URL('../../docs/adr/ADR-0005-migration-ownership-and-sequencing.md', import.meta.url)), 'utf8',
);

// Every independent test scenario needs its OWN registry authority. `createMigrationRegistryForTests`
// is TEST-ONLY (ADR-0005 Decision 2.4, Round 5) — production code uses `openMigrationRegistry()`,
// which takes no id and always resolves to the one real migration-core authority; tests need
// distinct ids so unrelated scenarios in this one process do not collide with each other.
let registryCounter = 0;
const freshRegistry = (initialRecords = []) => createMigrationRegistryForTests(`test-registry-${registryCounter += 1}`, initialRecords);

// ADR-0005 Decision 4.5: assignSequence now runs fresh-create/upgrade-from-previous itself.
// A trivial, state-blind applyUp converges for ANY set of records as long as priorState and
// emptyState both hash the same — used by every test that is not specifically exercising
// AC04/QA-02's real-migration semantics, so those tests do not need their own schema model.
const trivialApplyUp = (state) => state;
const TRIVIAL_HASH = hashSchemaState({});
const assignTrivially = (registry, proposal, extra = {}) => assignSequence(registry, proposal, {
  lockHeldBy: 'Kemi', applyUp: trivialApplyUp, hashState: hashSchemaState, priorState: {}, ...extra,
});

const additive = (over = {}) => proposeMigration({
  storyId: 'BIO-IDN-001', domainConflictGroup: 'identity', description: 'add person.middle_name',
  up: 'ALTER TABLE person ADD COLUMN middle_name TEXT NULL',
  reversibility: { down: 'ALTER TABLE person DROP COLUMN middle_name' },
  forwardSafety: { classification: 'additive' },
  proofs: { fresh_create_state_hash: TRIVIAL_HASH, upgrade_from_previous_state_hash: TRIVIAL_HASH },
  ...over,
});

// ---------------------------------------------------------------------------
// BIO-ARC-005-AC01 — feature agents propose; Kemi assigns sequence numbers.
// ---------------------------------------------------------------------------

test('AC01: a proposal carries no sequence number', () => {
  const proposal = additive();
  assert.equal('sequence' in proposal, false);
  assert.ok(newId('event').startsWith('evt_'));
  assert.match(proposal.proposal_id, /^evt_/);
});

test('AC01: proposal and record are validated against distinct schemas', () => {
  const proposalSchema = schema('migration-proposal.schema.json');
  const recordSchema = schema('migration-record.schema.json');
  assert.deepEqual(proposalSchema.not, { required: ['sequence'] });
  assert.ok(recordSchema.required.includes('sequence'));
  assert.ok(recordSchema.required.includes('assigned_by'));
});

test('AC01: assignSequence is the only path to a sequence number', () => {
  const proposal = additive();
  const { record, registry } = assignTrivially(freshRegistry(), proposal);
  assert.equal(record.sequence, 1);
  assert.equal(record.assigned_by, 'Kemi');
  assert.equal(SOLE_ASSIGNER, 'Kemi');
  assert.ok(isUtcInstant(record.assigned_at));
  assert.equal(registry.records.length, 1);
});

test('AC01/hostile: a caller cannot compute their own "next" number by pre-setting sequence', () => {
  const proposal = additive();
  const forged = { ...proposal, sequence: 1 };
  assert.throws(() => assignTrivially(freshRegistry(), forged),
    (e) => e.code === 'already_assigned');
});

test('AC01/hostile: a domain change cannot name itself the sequencing conflict group', () => {
  assert.throws(() => additive({ domainConflictGroup: SEQUENCING_CONFLICT_GROUP }),
    (e) => e.code === 'reserved_conflict_group');
  assert.equal(SEQUENCING_CONFLICT_GROUP, 'migration-core');
});

test('AC01: the story id must be a real Jira-shaped identifier', () => {
  for (const storyId of ['', 'idn-001', 'BIO-IDN', 'BIO-idn-001', 'random text']) {
    assert.throws(() => additive({ storyId }), (e) => e.code === 'invalid_story_id', `${JSON.stringify(storyId)} rejected`);
  }
  assert.equal(additive({ storyId: 'BIO-IDN-001' }).story_id, 'BIO-IDN-001');
});

// ---------------------------------------------------------------------------
// BIO-ARC-005-AC02 — exactly one migration-core conflict group active at a time.
// ---------------------------------------------------------------------------

test('AC02: assignment refuses to proceed without the lock explicitly held by Kemi', () => {
  const proposal = additive();
  for (const lockHeldBy of [undefined, null, '', 'Seyi', 'kemi', 'KEMI']) {
    assert.throws(() => assignTrivially(freshRegistry(), proposal, { lockHeldBy }),
      (e) => e.code === 'lock_not_held', `${JSON.stringify(lockHeldBy)} must be refused`);
  }
});

test('AC02: sequential assignment against one held handle is strictly monotonic and gapless', () => {
  let registry = freshRegistry();
  for (let i = 1; i <= 10; i += 1) {
    const proposal = additive({ storyId: 'BIO-IDN-001', description: `step ${i}` });
    registry = assignTrivially(registry, proposal).registry;
  }
  assert.deepEqual(registry.records.map((r) => r.sequence), Array.from({ length: 10 }, (_v, i) => i + 1));
  assert.ok(isGaplessSequence(registry.records));
});

test('AC02: a proposal already present in the registry cannot be assigned a second time', () => {
  const proposal = additive();
  const first = assignTrivially(freshRegistry(), proposal);
  assert.throws(() => assignTrivially(first.registry, proposal),
    (e) => e.code === 'already_assigned' && e.detail.proposal_id === proposal.proposal_id);
});

test('AC02: the ADR states the rule with no implicit carve-outs, and pre-empts the three rationalisations', () => {
  assert.match(ADR, /No two migration records may ever be assigned sequence numbers without one fully/);
  assert.match(ADR, /Our migrations don't touch the same tables, so they can be assigned in parallel/);
  assert.match(ADR, /Batch several proposals through in one sitting to save time/);
  assert.match(ADR, /Split the assignment work across two trusted helpers to go faster/);
  assert.match(ADR, /does not introduce a new locking mechanism/);
});

// ---------------------------------------------------------------------------
// BIO-ARC-005-QA-01 — there is exactly one production registry authority, with no
// caller-choosable name for it at all. Round 4's registryId-keyed Map closed "two calls for
// the SAME id mint two authorities" but not "two DIFFERENT ids, each individually valid,
// minted over the identical persisted state" — Round 5 retires the id parameter from the
// production surface entirely.
// ---------------------------------------------------------------------------

test('QA-01: assignSequence refuses a plain object that never went through openMigrationRegistry', () => {
  assert.throws(
    () => assignTrivially({ records: [] }, additive()),
    (e) => e.code === 'invalid_registry_handle',
  );
});

test('QA-01: assignSequence refuses a JSON round-trip of a real handle', () => {
  const real = freshRegistry();
  assignTrivially(real, additive({ storyId: 'BIO-IDN-001' }));
  const persisted = JSON.parse(JSON.stringify(real));
  assert.throws(
    () => assignTrivially(persisted, additive({ storyId: 'BIO-IDN-002' })),
    (e) => e.code === 'invalid_registry_handle',
    'a JSON round-trip of a real handle was silently accepted as a second authoritative view',
  );
});

test('QA-01 (Round 5): openMigrationRegistry() takes no id — there is no parameter left to alias', () => {
  assert.equal(openMigrationRegistry.length, 0, 'the production factory must not accept an id argument');
});

test('QA-01 (Round 5): two openMigrationRegistry() calls always return the SAME live handle, and serialize correctly', () => {
  const first = assignSequence(openMigrationRegistry(), additive({ storyId: 'BIO-IDN-001' }), {
    lockHeldBy: 'Kemi', applyUp: trivialApplyUp, hashState: hashSchemaState, priorState: {},
  });
  const second = assignSequence(openMigrationRegistry(), additive({ storyId: 'BIO-IDN-002' }), {
    lockHeldBy: 'Kemi', applyUp: trivialApplyUp, hashState: hashSchemaState, priorState: {},
  });
  assert.equal(first.registry, second.registry, 'every openMigrationRegistry() call must return the identical live handle');
  assert.notEqual(first.record.sequence, second.record.sequence);
});

test('QA-01 (Round 5): createMigrationRegistryForTests requires an explicit registryId — the SAME two-different-ids attack applied to the test-only factory still cannot alias the production registry', () => {
  // codex/independent-review.mjs's round-4 re-review probe named this exact shape:
  // `createMigrationRegistry('migration-core', ...)` and
  // `createMigrationRegistry('migration-core-alias', ...)` each becoming an authority. The
  // test-only factory still takes an id (test isolation needs it), but nothing about it can
  // ever be reached from `openMigrationRegistry()`'s SEQUENCING_CONFLICT_GROUP-only path —
  // there is no shared parameter for a caller-chosen alias to collide through.
  const alias1 = createMigrationRegistryForTests('migration-core');
  const alias2 = createMigrationRegistryForTests('migration-core-alias');
  assert.notEqual(alias1, alias2, 'two distinct test ids are legitimately two distinct test-scoped authorities');
  assert.notEqual(alias1, openMigrationRegistry(), 'a test-scoped id, even one that reuses the production name as text, is never the real registry');
});

test('QA-01: createMigrationRegistryForTests refuses a missing or empty registryId', () => {
  for (const bad of [undefined, null, '', 42, {}]) {
    assert.throws(() => createMigrationRegistryForTests(bad), (e) => e.code === 'invalid_registry', `${JSON.stringify(bad)} must be refused`);
  }
});

test('QA-01: createMigrationRegistryForTests refuses a non-array seed on first open', () => {
  for (const bad of [null, {}, 'records', 42]) {
    assert.throws(() => createMigrationRegistryForTests(`bad-seed-${JSON.stringify(bad)}`, bad),
      (e) => e.code === 'invalid_registry', `${JSON.stringify(bad)} must be refused`);
  }
  assert.equal(createMigrationRegistryForTests('empty-default-seed').records.length, 0, 'omitting the seed uses the default empty history');
});

test('QA-01 (Round 5): a second createMigrationRegistryForTests() call for a live id ignores initialRecords', () => {
  const id = 'reseed-test';
  const registry = createMigrationRegistryForTests(id, []);
  assignTrivially(registry, additive());
  const reopened = createMigrationRegistryForTests(id, []);
  assert.equal(reopened, registry, 'a re-open must return the SAME live handle');
  assert.equal(reopened.records.length, 1, 'the fabricated empty seed must not have overridden the real authoritative history');
});

test('QA-01 (Round 5): the ADR documents the retired-id fix and corrects its own Round 4 overclaim', () => {
  assert.match(ADR, /That scope claim was overclaimed a second time anyway/);
  assert.match(ADR, /The only closure that does not repeat this: remove the caller's ability to choose at all/);
  assert.match(ADR, /`createMigrationRegistry` is retired from the production surface/);
});

// ---------------------------------------------------------------------------
// BIO-ARC-005-AC03 — forward-safe and rollback/repair rules.
// ---------------------------------------------------------------------------

test('AC03: an additive migration needs no deprecation dependency', () => {
  const proposal = additive({ forwardSafety: { classification: 'additive' } });
  assert.equal(proposal.forward_safety.classification, 'additive');
  assert.equal('depends_on_deprecation_sequence' in proposal.forward_safety, false);
});

test('AC03: a breaking migration must name its deprecation dependency to even be proposed', () => {
  assert.throws(
    () => additive({ forwardSafety: { classification: 'breaking' } }),
    (e) => e.code === 'missing_deprecation_dependency',
  );
  const proposal = additive({ forwardSafety: { classification: 'breaking', depends_on_deprecation_sequence: 3, breaks: 'x' } });
  assert.equal(proposal.forward_safety.depends_on_deprecation_sequence, 3);
});

test('AC03/hostile: a breaking migration cannot be assigned if its named dependency is not actually in the registry', () => {
  const breaking = additive({
    storyId: 'BIO-IDN-005', description: 'drop legacy_email',
    up: 'ALTER TABLE person DROP COLUMN legacy_email',
    reversibility: { reversible: false, repair_procedure: 'restore legacy_email from the last verified backup' },
    forwardSafety: { classification: 'breaking', depends_on_deprecation_sequence: 99, breaks: 'person.legacy_email' },
  });
  assert.throws(() => assignTrivially(freshRegistry(), breaking),
    (e) => e.code === 'missing_deprecation_dependency' && e.detail.depends_on === 99);
});

test('AC03: a breaking migration assigns cleanly once its deprecation dependency genuinely matches', () => {
  const deprecating = additive({
    storyId: 'BIO-IDN-004', description: 'add person.email (replaces legacy_email)',
    forwardSafety: { classification: 'additive', deprecation_window: { target: 'person.legacy_email' } },
  });
  const afterDeprecating = assignTrivially(freshRegistry(), deprecating);
  const breaking = additive({
    storyId: 'BIO-IDN-005', description: 'drop legacy_email',
    up: 'ALTER TABLE person DROP COLUMN legacy_email',
    reversibility: { reversible: false, repair_procedure: 'restore legacy_email from the last verified backup' },
    forwardSafety: {
      classification: 'breaking',
      depends_on_deprecation_sequence: afterDeprecating.record.sequence,
      breaks: 'person.legacy_email',
    },
  });
  const afterBreaking = assignTrivially(afterDeprecating.registry, breaking);
  assert.equal(afterBreaking.record.sequence, 2);
  assert.equal(afterBreaking.record.forward_safety.depends_on_deprecation_sequence, 1);
});

test('AC03/QA-03: a breaking migration cannot be assigned against a record at the right sequence that never declared a deprecation window', () => {
  const unrelated = additive({ storyId: 'BIO-IDN-001', description: 'add person.middle_name' });
  const afterUnrelated = assignTrivially(freshRegistry(), unrelated);
  assert.equal('deprecation_window' in afterUnrelated.record.forward_safety, false, 'the fixture record must NOT self-declare a deprecation window');
  const breaking = additive({
    storyId: 'BIO-IDN-005', description: 'drop legacy_email',
    up: 'ALTER TABLE person DROP COLUMN legacy_email',
    reversibility: { reversible: false, repair_procedure: 'restore legacy_email from the last verified backup' },
    forwardSafety: {
      classification: 'breaking',
      depends_on_deprecation_sequence: afterUnrelated.record.sequence,
      breaks: 'person.legacy_email',
    },
  });
  assert.throws(
    () => assignTrivially(afterUnrelated.registry, breaking),
    (e) => e.code === 'missing_deprecation_dependency' && e.detail.depends_on === afterUnrelated.record.sequence,
    'an unrelated additive record was accepted as the required deprecation window',
  );
});

test('AC03/QA-03: deprecation_window.target must be a real, non-empty string when present', () => {
  for (const deprecation_window of [{}, { target: '' }, { target: 42 }]) {
    assert.throws(
      () => additive({ forwardSafety: { classification: 'additive', deprecation_window } }),
      (e) => e.code === 'invalid_forward_safety',
      `${JSON.stringify(deprecation_window)} must be refused`,
    );
  }
  const ok = additive({ forwardSafety: { classification: 'additive', deprecation_window: { target: 'person.legacy_email' } } });
  assert.equal(ok.forward_safety.deprecation_window.target, 'person.legacy_email');
});

test('QA-03 (Round 3): a deprecation window for one target does not authorise dropping a different one', () => {
  const deprecating = additive({
    storyId: 'BIO-IDN-004', description: 'add person.email (replaces legacy_email)',
    forwardSafety: { classification: 'additive', deprecation_window: { target: 'person.legacy_email' } },
  });
  const afterDeprecating = assignTrivially(freshRegistry(), deprecating);
  const breaking = additive({
    storyId: 'BIO-IDN-006', description: 'drop credential.secret',
    up: 'ALTER TABLE credential DROP COLUMN secret',
    reversibility: { reversible: false, repair_procedure: 'synthetic forward repair' },
    forwardSafety: {
      classification: 'breaking',
      depends_on_deprecation_sequence: afterDeprecating.record.sequence,
      breaks: 'credential.secret',
    },
  });
  assert.throws(
    () => assignTrivially(afterDeprecating.registry, breaking),
    (e) => e.code === 'missing_deprecation_dependency',
    'a deprecation window for person.legacy_email authorised dropping credential.secret',
  );
});

test('QA-03 (Round 3): an omitted breaks can never match a real target', () => {
  const deprecating = additive({
    storyId: 'BIO-IDN-004', description: 'add person.email (replaces legacy_email)',
    forwardSafety: { classification: 'additive', deprecation_window: { target: 'person.legacy_email' } },
  });
  const afterDeprecating = assignTrivially(freshRegistry(), deprecating);
  const breaking = additive({
    storyId: 'BIO-IDN-005', description: 'drop legacy_email',
    up: 'ALTER TABLE person DROP COLUMN legacy_email',
    reversibility: { reversible: false, repair_procedure: 'restore legacy_email from the last verified backup' },
    forwardSafety: { classification: 'breaking', depends_on_deprecation_sequence: afterDeprecating.record.sequence },
  });
  assert.equal('breaks' in breaking.forward_safety, false);
  assert.throws(
    () => assignTrivially(afterDeprecating.registry, breaking),
    (e) => e.code === 'missing_deprecation_dependency',
  );
});

test('QA-03 (Round 3): forward_safety.breaks must be a non-empty string when present', () => {
  for (const breaks of ['', 42, null]) {
    assert.throws(
      () => additive({ forwardSafety: { classification: 'breaking', depends_on_deprecation_sequence: 1, breaks } }),
      (e) => e.code === 'invalid_forward_safety',
      `${JSON.stringify(breaks)} must be refused`,
    );
  }
});

test('QA-03 (Round 3): the ADR documents the target-match fix and states its own scope boundary', () => {
  assert.match(ADR, /A deprecation dependency must name the SAME target the referenced window opened/);
  assert.match(ADR, /A\s*\nwindow opened for one target is not standing permission to break a different one/);
  assert.match(ADR, /this closes the gap where the target was never compared \*at\s*\nall\*/);
});

test('AC03: reversibility is exactly one of down or reversible:false+repair_procedure, never neither, never both', () => {
  for (const reversibility of [
    {},
    { down: '' },
    { reversible: false },
    { repair_procedure: 'x' },
    { down: 'x', reversible: false, repair_procedure: 'y' },
    { down: 'x', reversible: true, repair_procedure: 'y' },
  ]) {
    assert.throws(() => additive({ reversibility }), (e) => e.code === 'invalid_reversibility',
      `${JSON.stringify(reversibility)} must be refused`);
  }
  assert.equal(additive({ reversibility: { down: 'DROP x' } }).reversibility.down, 'DROP x');
  const forwardOnly = additive({ reversibility: { reversible: false, repair_procedure: 'manual fix' } });
  assert.equal(forwardOnly.reversibility.reversible, false);
});

test('AC03: an assigned record is immutable — a correction is a new record, never an edit', () => {
  const proposal = additive();
  const { record, registry } = assignTrivially(freshRegistry(), proposal);
  assert.equal(assertNotAnEdit(registry, record), true, 'the unchanged record is not an edit');
  const forged = { ...record, up: 'MALICIOUSLY REWRITTEN HISTORY' };
  assert.throws(() => assertNotAnEdit(registry, forged), (e) => e.code === 'immutable_record');
  const differentSequence = { ...record, sequence: record.sequence + 1 };
  assert.equal(assertNotAnEdit(registry, differentSequence), true, 'a genuinely new sequence position is not an edit of an existing one');
});

test('AC03: the ADR states forward-safety reuses ADR-0004\'s additive/breaking split precisely', () => {
  assert.match(ADR, /reuses ADR-0004's additive-vs-breaking split, one layer down/);
  assert.match(ADR, /identical additive-versus-breaking\s*\ndistinction ADR-0004 §1\.3/);
  assert.match(ADR, /`Deprecation`\/`Sunset`\s+pattern/);
});

test('AC03: the ADR states the append-only posture for assigned records, reusing ADR-0001/0003', () => {
  assert.match(ADR, /An assigned record is immutable/);
  assert.match(ADR, /identical\s*\nappend-only posture ADR-0001 §3\.3 and ADR-0003 already apply/);
});

// ---------------------------------------------------------------------------
// BIO-ARC-005-AC04 — fresh-create and upgrade-from-previous tests are mandatory.
// ---------------------------------------------------------------------------

test('AC04: proposeMigration refuses to build a proposal missing either proof', () => {
  for (const proofs of [
    {}, { fresh_create_state_hash: 'h' }, { upgrade_from_previous_state_hash: 'h' },
    { fresh_create_state_hash: '', upgrade_from_previous_state_hash: 'h' },
  ]) {
    assert.throws(() => additive({ proofs }), (e) => e.code === 'missing_proofs', `${JSON.stringify(proofs)} must be refused`);
  }
});

test('AC04: verifyFreshCreateMatchesUpgrade requires priorState, and refuses the tautological default', () => {
  const registry = assignTrivially(freshRegistry(), additive()).registry;
  assert.throws(
    () => verifyFreshCreateMatchesUpgrade(registry.records, { applyUp: (s) => s, hashState: hashSchemaState }),
    (e) => e.code === 'missing_prior_state',
  );
});

test('AC04: verifyFreshCreateMatchesUpgrade converges when the real prior state matches what fresh-create would have reached', () => {
  const proposals = [
    { description: 'create person', up: 'CREATE person' },
    { description: 'add middle_name', up: 'ADD middle_name' },
    { description: 'add suffix', up: 'ADD suffix' },
  ];
  let registry = freshRegistry();
  for (const over of proposals) registry = assignTrivially(registry, additive(over)).registry;

  const applyUp = (state, record) => {
    const columns = state.columns ?? ['id'];
    if (record.description === 'add middle_name') return { columns: [...columns, 'middle_name'] };
    if (record.description === 'add suffix') return { columns: [...columns, 'suffix'] };
    return { columns };
  };
  const priorState = records => records.reduce(applyUp, {});
  const result = verifyFreshCreateMatchesUpgrade(registry.records, {
    applyUp, hashState: hashSchemaState, priorState: priorState(registry.records.slice(0, -1)),
  });
  assert.equal(result.matches, true);
  assert.equal(result.fresh_create_state_hash, result.upgrade_from_previous_state_hash);
});

test('AC04/hostile/QA-02: verifyFreshCreateMatchesUpgrade itself refuses a migration reading accumulated data, called directly', () => {
  // Decision 4.4's own guarantee, independent of assignSequence: the standalone verifier
  // fails closed on a genuine mismatch — an `up` script whose computed default reads
  // existing rows, so it behaves differently against a real, long-lived tenant than
  // against the synthetic empty history fresh-create starts from.
  const proposals = [
    { description: 'create widget', up: 'CREATE widget' },
    { description: 'add computed_count', up: 'ALTER widget ADD computed_count DEFAULT (SELECT COUNT(*) FROM widget)' },
  ];
  let registry = freshRegistry();
  for (const over of proposals) registry = assignTrivially(registry, additive(over)).registry;

  const applyUp = (state, record) => {
    if (record.description === 'create widget') return { rowCount: 0 };
    if (record.description === 'add computed_count') return { ...state, computed_count: state.rowCount };
    return state;
  };
  const realTenantPriorState = { rowCount: 42 };
  assert.throws(
    () => verifyFreshCreateMatchesUpgrade(registry.records, { applyUp, hashState: hashSchemaState, priorState: realTenantPriorState }),
    (e) => e.code === 'proof_mismatch'
      && e.detail.fresh_create_state_hash !== e.detail.upgrade_from_previous_state_hash,
    'a migration reading existing data diverges from a fresh-create assumption, and this must refuse, not report',
  );
});

test('AC04/QA-02: the ADR documents the fail-closed proof fix as two independent, complementary closures', () => {
  assert.match(ADR, /A failed proof refuses assignment; it does not merely get reported/);
  assert.match(ADR, /now \*\*throws\*\* `proof_mismatch` on a genuine hash/);
});

test('AC04/hostile: a migration with no real data dependency genuinely converges — the check does not false-flag', () => {
  const proposals = [
    { description: 'create widget', up: 'CREATE widget' },
    { description: 'add flag', up: 'ALTER widget ADD flag DEFAULT false' },
  ];
  let registry = freshRegistry();
  for (const over of proposals) registry = assignTrivially(registry, additive(over)).registry;

  const applyUp = (state, record) => {
    if (record.description === 'create widget') return { rowCount: 7 };
    if (record.description === 'add flag') return { ...state, flag: false };
    return state;
  };
  const realTenantPriorState = applyUp({}, registry.records[0]);
  assert.deepEqual(realTenantPriorState, { rowCount: 7 });

  const result = verifyFreshCreateMatchesUpgrade(registry.records, { applyUp, hashState: hashSchemaState, priorState: realTenantPriorState });
  assert.equal(result.matches, true, 'a migration that never reads accumulated data converges regardless of how much data exists');
});

test('AC04: verifyFreshCreateMatchesUpgrade requires a gapless 1..N sequence', () => {
  const withGap = [
    { ...additive({ description: 'a' }), sequence: 1, assigned_by: 'Kemi', assigned_at: '2026-09-21T00:00:00.000Z' },
    { ...additive({ description: 'b' }), sequence: 3, assigned_by: 'Kemi', assigned_at: '2026-09-21T00:00:01.000Z' },
  ];
  assert.throws(
    () => verifyFreshCreateMatchesUpgrade(withGap, { applyUp: (s) => s, hashState: hashSchemaState }),
    (e) => e.code === 'non_gapless_sequence',
  );
});

test('AC04: verifyFreshCreateMatchesUpgrade refuses an empty record list', () => {
  assert.throws(() => verifyFreshCreateMatchesUpgrade([], { applyUp: (s) => s, hashState: hashSchemaState }),
    (e) => e.code === 'invalid_records');
});

test('AC04: hashSchemaState ignores key order, matching every other canonical hash in this contract series', () => {
  assert.equal(hashSchemaState({ a: 1, b: 2 }), hashSchemaState({ b: 2, a: 1 }));
  assert.notEqual(hashSchemaState({ a: 1 }), hashSchemaState({ a: 2 }));
});

test('AC04: the ADR states both proofs are mandatory before assignment, not optional afterthoughts', () => {
  assert.match(ADR, /Two ways to reach the same state, both proved equal/);
  assert.match(ADR, /both mandatory before assignment, not optional/);
  assert.match(ADR, /verifyFreshCreateMatchesUpgrade`\s+is the shared check both paths run through/);
});

// ---------------------------------------------------------------------------
// BIO-ARC-005-QA-02 — the module runs the fresh-create/upgrade-from-previous check itself,
// at assignment time, over the real candidate record. Nothing a caller attaches to a
// proposal is trusted as proof, because Round 3 proved any shape checked for is a shape a
// caller can construct by hand.
// ---------------------------------------------------------------------------

test('QA-02: assignSequence refuses to assign without applyUp/hashState — there is no path that skips running the real check', () => {
  assert.throws(
    () => assignSequence(freshRegistry(), additive(), { lockHeldBy: 'Kemi', priorState: {} }),
    (e) => e.code === 'verification_required',
  );
  assert.throws(
    () => assignSequence(freshRegistry(), additive(), { lockHeldBy: 'Kemi', applyUp: trivialApplyUp, priorState: {} }),
    (e) => e.code === 'verification_required',
    'hashState alone missing must also be refused',
  );
});

test('QA-02 (Round 4): a caller-forged, perfectly verifier-shaped proof is irrelevant — the exact gap Round 3 left open', () => {
  // codex/independent-review.mjs's round-3 re-review probe: "caller-forged verifier-shaped
  // hashes are accepted without verifier provenance." Round 3's assertVerifiedProofs only
  // checked SHAPE (matches:true, hex format) — trivially reconstructible by reading this
  // file. This proposal's declared proofs are exactly that: hand-typed, hex-shaped,
  // claiming matches:true, never run through any real check.
  const forged = additive({
    proofs: { fresh_create_state_hash: 'a'.repeat(64), upgrade_from_previous_state_hash: 'a'.repeat(64) },
  });
  // Even supplying a REAL applyUp/hashState/priorState, assignment computes its OWN result
  // (TRIVIAL_HASH here, from the trivial migration) and finds it disagrees with what was
  // forged — the forged declaration cannot make assignment trust it, because assignment
  // never reads it as the safety gate.
  assert.throws(
    () => assignTrivially(freshRegistry(), forged),
    (e) => e.code === 'proof_declaration_mismatch',
    'a caller-forged, shape-valid proof was accepted without ever being computed',
  );
});

test('QA-02 (Round 4): assignSequence computes the proof itself; a genuinely converging migration assigns cleanly with no caller-attached claim trusted', () => {
  const proposal = additive({
    proofs: { fresh_create_state_hash: TRIVIAL_HASH, upgrade_from_previous_state_hash: TRIVIAL_HASH },
  });
  const { record } = assignTrivially(freshRegistry(), proposal);
  assert.equal(record.sequence, 1);
});

test('QA-02 (Round 4): a migration that genuinely does not converge is refused, even if its declared proofs happen to agree with each other', () => {
  const proposals = [
    { description: 'create widget', up: 'CREATE widget' },
    { description: 'add computed_count', up: 'ALTER widget ADD computed_count DEFAULT (SELECT COUNT(*) FROM widget)' },
  ];
  let registry = freshRegistry();
  for (const over of proposals.slice(0, 1)) {
    registry = assignTrivially(registry, additive(over)).registry;
  }
  const applyUp = (state, record) => {
    if (record.description === 'create widget') return { rowCount: 0 };
    if (record.description === 'add computed_count') return { ...state, computed_count: state.rowCount };
    return state;
  };
  const proposal = additive({ ...proposals[1] });
  assert.throws(
    () => assignSequence(registry, proposal, { lockHeldBy: 'Kemi', applyUp, hashState: hashSchemaState, priorState: { rowCount: 42 } }),
    (e) => e.code === 'proof_mismatch' && e.detail.fresh_create_state_hash !== e.detail.upgrade_from_previous_state_hash,
    'a migration reading existing data diverges from a fresh-create assumption, and this must refuse assignment',
  );
});

test('QA-02 (Round 4): the proposal-declared proofs are cross-checked as an honesty signal, distinct from a real convergence failure', () => {
  const registry = freshRegistry();
  const proposal = additive({
    description: 'create widget', up: 'CREATE widget',
    proofs: { fresh_create_state_hash: 'lied-about-this'.padEnd(64, '0'), upgrade_from_previous_state_hash: 'lied-about-this'.padEnd(64, '0') },
  });
  const applyUp = (state, record) => (record.up === 'CREATE widget' ? { rowCount: 0 } : state);
  assert.throws(
    () => assignSequence(registry, proposal, { lockHeldBy: 'Kemi', applyUp, hashState: hashSchemaState, priorState: { rowCount: 0 } }),
    (e) => e.code === 'proof_declaration_mismatch' && 'declared' in e.detail && 'computed' in e.detail,
    'the migration genuinely converges, so this must be reported as a declaration mismatch, not proof_mismatch',
  );
});

test('QA-02 (Round 4): the ADR documents why shape-checking was still insufficient and states the fused-verification design precisely', () => {
  assert.match(ADR, /The module runs the check itself; it does not accept a claim to have run it/);
  assert.match(ADR, /A shape is data\. Data that this/);
  assert.match(ADR, /the fix has to change WHO\s*\ncomputes the proof, not WHAT the proof looks like/);
  assert.match(ADR, /architecturally different from, not just stricter than, Round 3/);
});

// ---------------------------------------------------------------------------
// BIO-ARC-005-QA-02 (Round 5) — a caller-supplied applyUp/hashState that ignores the
// candidate record entirely (e.g. `(state) => state`) still converges trivially and still
// assigns. This is NOT patched further: it is a genuine scope boundary (verifying an
// interpreter's fidelity to opaque SQL/DDL requires actually executing it, infrastructure
// this story does not build), recorded explicitly in the ADR rather than left silent.
// ---------------------------------------------------------------------------

test('QA-02 (Round 5): a no-op applyUp still converges and still assigns — this is the accepted, documented scope boundary, not an oversight', () => {
  // codex/independent-review.mjs's round-4 re-review probe, reproduced here so the boundary
  // is demonstrated in this suite rather than only asserted about in prose: a migration
  // whose body announces it is never executed still gets a sequence number, because this
  // contract cannot verify that a supplied applyUp is a faithful interpreter of opaque SQL
  // (ADR-0005 Decision 4.6) — only that its own fresh-create/upgrade-from-previous
  // computation, whatever it computes, converges.
  const proposal = additive({ up: 'THIS MIGRATION BODY IS NEVER EXECUTED' });
  const { record } = assignTrivially(freshRegistry(), proposal);
  assert.equal(record.sequence, 1, 'a faithless runner is not rejected by this contract — see Decision 4.6 for why');
  assert.equal(record.assigned_by, 'Kemi', 'the same record that could not be verified faithful DOES record who chose the runner');
});

test('QA-02 (Round 5): the ADR states the honest, narrower guarantee and records the scope decision explicitly, not silently', () => {
  assert.match(ADR, /What this module can and cannot prove about a supplied runner/);
  assert.match(ADR, /it is a genuinely\s*\ndifferent kind of gap, and it is worth being precise about why/);
  assert.match(ADR, /Proving an\s*\ninterpreter faithfully executes arbitrary opaque SQL\/DDL requires actually running it/);
  assert.match(ADR, /This is recorded as an accepted, explicit scope boundary, not a defect carried forward\s*\nsilently/);
  assert.match(ADR, /Building it here, to close one review finding, would smuggle a database dependency and a/);
});

// ---------------------------------------------------------------------------
// Contract hygiene.
// ---------------------------------------------------------------------------

test('every published schema is self-describing JSON Schema 2020-12', () => {
  for (const name of ['migration-proposal.schema.json', 'migration-record.schema.json', 'migration-registry.schema.json']) {
    const s = schema(name);
    assert.equal(s.$schema, 'https://json-schema.org/draft/2020-12/schema', name);
    assert.ok(s.$id && s.title && s.description, `${name} is self-describing`);
  }
});

test('a full record built through the contract satisfies migration-record.schema.json shape', () => {
  const proposal = additive();
  const { record } = assignTrivially(freshRegistry(), proposal);
  const recordSchema = schema('migration-record.schema.json');
  for (const field of recordSchema.required) assert.ok(record[field] !== undefined, `${field} present`);
  assert.equal(record.assigned_by, 'Kemi');
});

test('ADR-0005 records the decision, the reuses, and the freeze policy', () => {
  assert.match(ADR, /# ADR-0005/);
  assert.match(ADR, /\*\*Status:\*\* Accepted \(BIO-ARC-005/);
  assert.match(ADR, /Freezes at W01 exit/);
  for (const ac of ['BIO-ARC-005-AC01', 'BIO-ARC-005-AC02', 'BIO-ARC-005-AC03', 'BIO-ARC-005-AC04']) {
    assert.ok(ADR.includes(ac), `${ac} is traceable to a decision section`);
  }
  assert.match(ADR, /ADR-0001/);
  assert.match(ADR, /ADR-0003/);
  assert.match(ADR, /ADR-0004/);
});

test('FORWARD_SAFETY_CLASSES matches the schema enum exactly', () => {
  const enumValues = schema('migration-proposal.schema.json').$defs.forwardSafety.properties.classification.enum;
  assert.deepEqual([...FORWARD_SAFETY_CLASSES].sort(), [...enumValues].sort());
});
