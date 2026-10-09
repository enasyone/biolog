// SUPERSEDED by evidence/BIO-ARC-005/builder/adapted-review-round3.mjs — kept as the
// historical record of what Round 2's fix actually closed at the time. Seyi's Round 2
// re-review found the Round 2 design (an identity-keyed ledger, a bare hash-equality check)
// itself insufficient, and this file's own probes now fail against the Round 3
// implementation for the SAME reason Seyi's script does: `assignSequence` no longer accepts
// a plain `{ records: [] }` snapshot at all (QA-01, Decision 2.4), and this file's filler
// proofs ('agree'/'agree') are no longer shaped like a real verifier result (QA-02,
// Decision 4.5). Left unmodified, not deleted, as the evidence trail; do not treat it as a
// current-state reproduction.
//
// Adapted transcription of evidence/BIO-ARC-005/codex/independent-review.mjs's AC02 and
// AC03 probes, for Round 2 (QA-01/QA-02/QA-03 closure). Seyi's own script is UNCHANGED and
// re-run as-is — see git-state.txt / SUMMARY.md Round 2 for its output, where both AC04
// probes now pass unmodified.
//
// Why this file exists: Seyi's shared `proposal()` fixture attaches deliberately
// fabricated, unequal proof hashes ('fabricated-fresh' / 'fabricated-upgrade') as the
// baseline for EVERY probe in that script, including AC02 (registry-snapshot race) and
// AC03 (deprecation-dependency relevance), neither of which is testing proofs. Before this
// round, proof content was inert, so that fixture choice didn't matter. The QA-02 fix
// makes assignSequence enforce proof-hash equality (this is what Seyi's own AC04 probe,
// "refuses assignment when attached fresh and upgrade proof hashes disagree", explicitly
// requires) — so as of this round, calling `proposal()` unmodified now correctly throws
// proof_mismatch on the very first assignSequence call in AC02 and AC03, before either
// probe's own logic is reached. That is not a QA-01/QA-03 regression; it is QA-02 firing
// correctly on a fixture that happens to carry invalid proofs.
//
// This file transcribes AC02 and AC03 exactly, swapping only the fixture's proofs for
// valid (equal) ones, to demonstrate the race-prevention and dependency-relevance fixes
// independently of the fixture interaction above. All identifiers and bodies are SYNTHETIC.
import assert from 'node:assert/strict';
import { proposeMigration, assignSequence } from '../../../contracts/js/migrations.mjs';

const proposal = (overrides = {}) => proposeMigration({
  storyId: 'BIO-IDN-001',
  domainConflictGroup: 'identity',
  description: 'synthetic migration',
  up: 'synthetic up',
  reversibility: { down: 'synthetic down' },
  forwardSafety: { classification: 'additive' },
  proofs: {
    fresh_create_state_hash: 'agree',
    upgrade_from_previous_state_hash: 'agree',
  },
  ...overrides,
});

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

probe('AC02 (adapted, valid proofs) rejects two assignments made from the same registry snapshot', () => {
  const snapshot = { records: [] };
  const first = assignSequence(snapshot, proposal(), { lockHeldBy: 'Kemi' });
  const second = assignSequence(snapshot, proposal({ storyId: 'BIO-IDN-002' }), { lockHeldBy: 'Kemi' });
  assert.notEqual(first.record.sequence, second.record.sequence,
    'two distinct records were both assigned sequence 1');
});

probe('AC03 (adapted, valid proofs) breaking dependency must reference a declared deprecation record', () => {
  const unrelated = assignSequence({ records: [] }, proposal(), { lockHeldBy: 'Kemi' }).registry;
  const breaking = proposal({
    storyId: 'BIO-IDN-003',
    description: 'drop unrelated field',
    forwardSafety: { classification: 'breaking', depends_on_deprecation_sequence: 1 },
    reversibility: { reversible: false, repair_procedure: 'synthetic forward repair' },
  });
  assert.throws(
    () => assignSequence(unrelated, breaking, { lockHeldBy: 'Kemi' }),
    (error) => error?.code === 'missing_deprecation_dependency',
    'an unrelated additive record was accepted as the required deprecation window',
  );
});

if (failures) process.exitCode = 1;
