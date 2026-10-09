// SUPERSEDED by evidence/BIO-ARC-005/builder/adapted-review-round4.mjs — kept as the
// historical record of what Round 3's fix actually closed at the time. Seyi's round-3
// re-review found Round 3's design itself insufficient (QA-01: a branded handle is still
// just an identity a caller's OWN factory call can duplicate; QA-02: a shape check is still
// just data a caller can reconstruct by reading the source), and this file's own probes now
// fail against the Round 4 implementation for the same reasons Seyi's script does:
// createMigrationRegistry() requires a registryId argument this file never supplies (QA-01,
// Decision 2.4), and assignSequence requires applyUp/hashState/priorState this file never
// supplies (QA-02, Decision 4.5). Left unmodified, not deleted, as the evidence trail; do
// not treat it as a current-state reproduction.
//
// Adapted transcription of evidence/BIO-ARC-005/codex/independent-review.mjs's Round 2
// probes, for Round 3 (QA-01/QA-02/QA-03 closure, second pass). Seyi's own script is
// UNCHANGED and re-run as-is (see git-state.txt / SUMMARY.md Round 3 for its output).
//
// Why every probe here needed adapting, not just one this time: the Round 3 fixes are all
// API-surface changes, not just stricter validation of the existing shape —
//   - QA-01: assignSequence now REQUIRES a handle from createMigrationRegistry(), not a
//     plain { records } snapshot. This is deliberate: no fix expressible as "trust this
//     snapshot a little more carefully" can distinguish a real reload of persisted state
//     from a fabricated or merely-stale one (see ADR-0005 Decision 2.4) — the ONLY sound
//     fix removes plain snapshots from having assignment authority at all.
//   - QA-02: assignSequence now REQUIRES proofs shaped like verifyFreshCreateMatchesUpgrade's
//     real successful output (matches:true + three SHA-256 hex hashes), not just two equal
//     strings (see ADR-0005 Decision 4.5).
// Seyi's independent-review.mjs passes a raw `{ records: [] }` object directly in all four
// of its probes, and its shared `proposal()` fixture's proofs never carry `matches`/hex
// hashes — so ALL FOUR of Seyi's probes now hit the QA-01 registry-handle gate (or, for the
// one probe testing this directly, correctly gets exactly the intended `unverified_proofs`
// rejection) before reaching what each was individually testing. Re-running Seyi's script
// unmodified after this fix therefore shows FAIL on three probes for the QA-01 reason and
// PASS on the fourth for the intended QA-02 reason — disclosed in full in SUMMARY.md.
//
// This file transcribes all four probes, swapping the raw snapshot for a real
// createMigrationRegistry() handle and the fixture's fake proofs for genuinely-shaped ones,
// to confirm all three fixes independently of that interaction. All identifiers, targets and
// bodies are SYNTHETIC.
import assert from 'node:assert/strict';
import {
  proposeMigration, assignSequence, createMigrationRegistry,
  verifyFreshCreateMatchesUpgrade, hashSchemaState,
} from '../../../contracts/js/migrations.mjs';

const realProof = (seed) => {
  const h = hashSchemaState({ seed });
  return {
    matches: true,
    fresh_create_state_hash: h,
    upgrade_from_previous_state_hash: h,
    prior_state_hash: hashSchemaState({ seed: `${seed}-prior` }),
  };
};

const proposal = (overrides = {}) => proposeMigration({
  storyId: 'BIO-IDN-001',
  domainConflictGroup: 'identity',
  description: 'synthetic migration',
  up: 'synthetic up',
  reversibility: { down: 'synthetic down' },
  forwardSafety: { classification: 'additive' },
  proofs: realProof('a'),
  ...overrides,
});

let failures = 0;
function probe(name, fn) {
  try { fn(); console.log(`PASS ${name}`); }
  catch (error) { failures += 1; console.error(`FAIL ${name}: ${error.message}`); }
}

// QA-01: two INDEPENDENT createMigrationRegistry() calls are legitimately unrelated stores
// (nothing establishes they represent "the same" persisted state) -- the real proof is that
// a plain/reconstructed snapshot (what a JSON round-trip produces) is REFUSED outright, so
// there is no way to construct two unsynchronized authoritative views of one real registry.
probe('QA-01 (adapted) a reconstructed/deserialized snapshot cannot stand in for a real registry handle', () => {
  const real = createMigrationRegistry([]);
  assignSequence(real, proposal(), { lockHeldBy: 'Kemi' });
  const persisted = JSON.parse(JSON.stringify(real));
  assert.throws(
    () => assignSequence(persisted, proposal({ storyId: 'BIO-IDN-002' }), { lockHeldBy: 'Kemi' }),
    (e) => e.code === 'invalid_registry_handle',
    'a JSON round-trip of a real handle was silently accepted as authoritative',
  );
});

probe('QA-01 (adapted) reusing the SAME real handle serializes correctly across two assignments', () => {
  const real = createMigrationRegistry([]);
  const first = assignSequence(real, proposal(), { lockHeldBy: 'Kemi' });
  const second = assignSequence(real, proposal({ storyId: 'BIO-IDN-002' }), { lockHeldBy: 'Kemi' });
  assert.notEqual(first.record.sequence, second.record.sequence);
  assert.equal(second.record.sequence, 2);
});

// QA-02
probe('QA-02 (adapted) refuses assignment when equal proof strings lack verifier provenance', () => {
  const registry = createMigrationRegistry([]);
  assert.throws(
    () => assignSequence(registry, proposal({ proofs: { fresh_create_state_hash: 'same-fixture-hash', upgrade_from_previous_state_hash: 'same-fixture-hash' } }), { lockHeldBy: 'Kemi' }),
    (e) => e.code === 'unverified_proofs',
    'assignment accepted two caller-fabricated equal strings as proof',
  );
});

probe('QA-02 (adapted) accepts the real output of verifyFreshCreateMatchesUpgrade', () => {
  const registry = createMigrationRegistry([]);
  const seed = assignSequence(registry, proposal({
    proofs: { matches: true, fresh_create_state_hash: 'a'.repeat(64), upgrade_from_previous_state_hash: 'a'.repeat(64), prior_state_hash: 'b'.repeat(64) },
  }), { lockHeldBy: 'Kemi' }).record;
  const result = verifyFreshCreateMatchesUpgrade([seed], {
    emptyState: { rows: 0 }, priorState: { rows: 0 },
    applyUp: (state) => ({ ...state, marker: true }), hashState: hashSchemaState,
  });
  const registry2 = createMigrationRegistry([]);
  const { record } = assignSequence(registry2, proposal({ proofs: result }), { lockHeldBy: 'Kemi' });
  assert.equal(record.sequence, 1);
});

probe('QA-02 (adapted) verifier fails closed rather than returning an assignable mismatch', () => {
  const registry = createMigrationRegistry([]);
  const seed = assignSequence(registry, proposal({
    proofs: { matches: true, fresh_create_state_hash: 'a'.repeat(64), upgrade_from_previous_state_hash: 'a'.repeat(64), prior_state_hash: 'b'.repeat(64) },
  }), { lockHeldBy: 'Kemi' }).record;
  assert.throws(
    () => verifyFreshCreateMatchesUpgrade([seed], {
      emptyState: { rows: 0 }, priorState: { rows: 7 },
      applyUp: (state) => ({ ...state, marker: state.rows }), hashState: hashSchemaState,
    }),
    (e) => e.code === 'proof_mismatch',
  );
});

// QA-03
probe('QA-03 (adapted) breaking dependency must match the declared deprecation target', () => {
  const registry = createMigrationRegistry([]);
  assignSequence(registry, proposal({
    forwardSafety: { classification: 'additive', deprecation_window: { target: 'person.legacy_email' } },
  }), { lockHeldBy: 'Kemi' });
  const breaking = proposal({
    storyId: 'BIO-IDN-003', description: 'drop credential.secret', up: 'DROP COLUMN credential.secret',
    forwardSafety: { classification: 'breaking', depends_on_deprecation_sequence: 1, breaks: 'credential.secret' },
    reversibility: { reversible: false, repair_procedure: 'synthetic forward repair' },
  });
  assert.throws(
    () => assignSequence(registry, breaking, { lockHeldBy: 'Kemi' }),
    (e) => e.code === 'missing_deprecation_dependency',
    'a deprecation window for person.legacy_email authorized dropping credential.secret',
  );
});

probe('QA-03 (adapted) breaking dependency assigns cleanly once target genuinely matches', () => {
  const registry = createMigrationRegistry([]);
  assignSequence(registry, proposal({
    forwardSafety: { classification: 'additive', deprecation_window: { target: 'person.legacy_email' } },
  }), { lockHeldBy: 'Kemi' });
  const breaking = proposal({
    storyId: 'BIO-IDN-005', description: 'drop person.legacy_email', up: 'DROP COLUMN person.legacy_email',
    forwardSafety: { classification: 'breaking', depends_on_deprecation_sequence: 1, breaks: 'person.legacy_email' },
    reversibility: { reversible: false, repair_procedure: 'restore from backup' },
  });
  const { record } = assignSequence(registry, breaking, { lockHeldBy: 'Kemi' });
  assert.equal(record.sequence, 2);
});

if (failures) process.exitCode = 1;
