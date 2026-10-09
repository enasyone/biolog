// BIOLOG migration ownership and sequencing policy — ADR-0005.
// Owner: Kemi (Chief Architect & Integrator). A proposal joins the history only when
// assignSequence is called while the migration-core lock is held; nothing here computes
// "the next number" from a proposer's own view of the registry.

import { createHash } from 'node:crypto';
import { newId } from './identifiers.mjs';
import { nowUtc, isUtcInstant } from './clock.mjs';

/** Reserved for the sequencing process itself; a schema change never uses this as its own domain group. */
export const SEQUENCING_CONFLICT_GROUP = 'migration-core';
export const FORWARD_SAFETY_CLASSES = Object.freeze(['additive', 'breaking']);
export const SOLE_ASSIGNER = 'Kemi';

export class MigrationError extends Error {
  constructor(message, code, status = 500, detail = {}) {
    super(message);
    this.name = 'MigrationError';
    this.code = code;
    this.status = status;
    this.detail = detail;
  }
}

const STORY_ID_PATTERN = /^BIO-[A-Z]+-[0-9]+$/;

/**
 * Validate `{ down }` or `{ reversible: false, repair_procedure }` — exactly one, never
 * neither (ADR-0005 Decision 3.2). Absence of a stated answer is never the safe answer.
 */
function assertReversibility(reversibility) {
  if (!reversibility || typeof reversibility !== 'object') {
    throw new MigrationError('reversibility is required', 'invalid_reversibility', 500);
  }
  const hasDown = typeof reversibility.down === 'string' && reversibility.down.length > 0;
  const hasRepair = typeof reversibility.repair_procedure === 'string' && reversibility.repair_procedure.length > 0;
  const isForwardOnly = reversibility.reversible === false && hasRepair;
  // Both signals present at once is refused too, not only neither: a caller who supplies
  // BOTH a down script and a repair_procedure has left it ambiguous which one actually
  // governs recovery, and this contract does not decide that silently on their behalf.
  const bothSignalsPresent = hasDown && hasRepair;
  if ((hasDown === isForwardOnly) || bothSignalsPresent) {
    throw new MigrationError(
      'reversibility must be exactly one of: a down script, or reversible:false with a repair_procedure — never both, never neither',
      'invalid_reversibility', 500,
    );
  }
}

/**
 * Validate forward-safety classification (ADR-0005 Decision 3.1, reusing ADR-0004 §1.3's
 * additive-vs-breaking split one layer down). A breaking change must name the earlier
 * deprecating record its window depends on — refused, not merely warned about, otherwise.
 */
function assertForwardSafety(forwardSafety) {
  if (!forwardSafety || !FORWARD_SAFETY_CLASSES.includes(forwardSafety.classification)) {
    throw new MigrationError(
      `forward_safety.classification must be one of ${FORWARD_SAFETY_CLASSES.join(', ')}`,
      'invalid_forward_safety', 500,
    );
  }
  if (forwardSafety.classification === 'breaking') {
    const dep = forwardSafety.depends_on_deprecation_sequence;
    if (!Number.isInteger(dep) || dep < 1) {
      throw new MigrationError(
        'a breaking migration must name the earlier deprecating record its window depends on',
        'missing_deprecation_dependency', 500,
      );
    }
  }
  if ('deprecation_window' in forwardSafety) {
    const target = forwardSafety.deprecation_window?.target;
    if (typeof target !== 'string' || target.length === 0) {
      throw new MigrationError(
        'deprecation_window.target is required and must name what is being deprecated',
        'invalid_forward_safety', 500,
      );
    }
  }
  // `breaks` names what a breaking migration removes (ADR-0005 Decision 3.5). It is NOT
  // required here, deliberately: a proposal is provisional, and an omitted `breaks` is
  // refused at ASSIGNMENT (never matches a real declared window there — see assignSequence)
  // rather than at construction. When present, though, it must actually be a name.
  if ('breaks' in forwardSafety && (typeof forwardSafety.breaks !== 'string' || forwardSafety.breaks.length === 0)) {
    throw new MigrationError(
      'forward_safety.breaks must be a non-empty string naming what this migration removes',
      'invalid_forward_safety', 500,
    );
  }
}

/**
 * The declared target of a breaking migration's dependency must be the SAME target the
 * referenced record's deprecation_window names (ADR-0005 Decision 3.5). A window opened for
 * one target does not authorise dropping something else: "a record exists there and
 * self-declares as SOME deprecation window" (Decision 3.4) is still not "the window THIS
 * migration is relying on." Absence on either side can never match, by construction —
 * `undefined !== 'x'` for any real target name, so an unnamed `breaks` is refused exactly
 * as a wrongly-named one would be.
 */
function dependsOnTheDeclaredWindow(depRecord, proposal) {
  const windowTarget = depRecord?.forward_safety?.deprecation_window?.target;
  const claimedTarget = proposal.forward_safety.breaks;
  return typeof windowTarget === 'string' && windowTarget.length > 0 && windowTarget === claimedTarget;
}

/** Both proofs are mandatory before assignment (ADR-0005 Decision 4.2), never optional. */
function assertProofs(proofs) {
  if (!proofs || typeof proofs.fresh_create_state_hash !== 'string' || proofs.fresh_create_state_hash.length === 0
    || typeof proofs.upgrade_from_previous_state_hash !== 'string' || proofs.upgrade_from_previous_state_hash.length === 0) {
    throw new MigrationError(
      'both fresh_create_state_hash and upgrade_from_previous_state_hash are required before assignment',
      'missing_proofs', 500,
    );
  }
}

/**
 * Build a proposal. Carries no sequence number — a request to join the history, not part
 * of it (ADR-0005 Decision 1.1).
 */
export function proposeMigration({
  storyId, domainConflictGroup, description, up, reversibility, forwardSafety, proofs,
}) {
  if (typeof storyId !== 'string' || !STORY_ID_PATTERN.test(storyId)) {
    throw new MigrationError(`story_id must match BIO-XXX-000, got ${JSON.stringify(storyId)}`, 'invalid_story_id', 500);
  }
  if (typeof domainConflictGroup !== 'string' || domainConflictGroup.length === 0) {
    throw new MigrationError('domain_conflict_group is required', 'invalid_conflict_group', 500);
  }
  if (domainConflictGroup === SEQUENCING_CONFLICT_GROUP) {
    // The hostile question: could a proposer name their own change "migration-core" to
    // imply it IS the sequencing process and route around ordinary domain review? Refused.
    throw new MigrationError(
      `domain_conflict_group must not be "${SEQUENCING_CONFLICT_GROUP}"; that name is reserved for the sequencing process itself`,
      'reserved_conflict_group', 500,
    );
  }
  if (typeof description !== 'string' || description.length === 0) {
    throw new MigrationError('description is required', 'invalid_description', 500);
  }
  if (typeof up !== 'string' || up.length === 0) {
    throw new MigrationError('up is required', 'invalid_up', 500);
  }
  assertReversibility(reversibility);
  assertForwardSafety(forwardSafety);
  assertProofs(proofs);

  return Object.freeze({
    proposal_id: newId('event'),
    story_id: storyId,
    domain_conflict_group: domainConflictGroup,
    description,
    up,
    reversibility: Object.freeze({ ...reversibility }),
    forward_safety: Object.freeze({ ...forwardSafety }),
    proofs: Object.freeze({ ...proofs }),
  });
}

/**
 * ADR-0005 Decision 2.4 (Round 2): object identity was the wrong thing to key authoritative
 * state on. Two INDEPENDENT deserializations of the identical persisted registry state were
 * two DIFFERENT object identities, each treated as a fresh, untracked lineage.
 *
 * ADR-0005 Decision 2.4 (Round 4): Round 3's fix — branding the object minted by
 * `createMigrationRegistry()` — closed reconstruction (a JSON round-trip, a hand-built
 * lookalike) but not the deeper version of the SAME gap: `createMigrationRegistry()` was a
 * public factory, and nothing stopped it being called TWICE for what a caller believes is
 * "the same" real registry, minting two independent, equally-branded, equally-legitimate
 * handles — two authorities, not one.
 *
 * ADR-0005 Decision 2.4 (Round 5): Round 4's fix — keying live handles by a `registryId` in
 * a `Map` — still let a caller pick the id: `openMigrationRegistry('migration-core')` and
 * `openMigrationRegistry('migration-core-alias')` are two DIFFERENT keys for what a caller
 * believes is the SAME real registry, and the `Map` has no way to know they were meant to
 * collide. A caller-namespaced `Map` is not a fix for "there must be exactly one authority"
 * if the caller also gets to choose which namespace entry is authoritative. There is no
 * longer a caller-supplied id on the production path at all: `openMigrationRegistry()`
 * takes none, and always resolves to the ONE hardcoded id this module owns,
 * `SEQUENCING_CONFLICT_GROUP` — reflecting the premise this ADR opens with, that there is
 * exactly one migration-core history, as an invariant the caller cannot parameterise their
 * way around. `registryId` survives only on `createMigrationRegistryForTests`, a
 * deliberately, unmistakably separately-named export that exists so independent test
 * scenarios in one process do not collide with each other — never imported by, or
 * documented for, production callers (`contracts/README.md` rule 9 names only
 * `openMigrationRegistry`).
 */
const REGISTRY_BRAND = Symbol('migrations.registryBrand');
const registryState = new WeakMap();

function mintHandle(initialRecords) {
  if (!Array.isArray(initialRecords)) {
    throw new MigrationError('initialRecords must be an array of already-assigned records', 'invalid_registry', 500);
  }
  const handle = Object.freeze({
    [REGISTRY_BRAND]: true,
    get records() { return registryState.get(handle); },
  });
  registryState.set(handle, Object.freeze([...initialRecords]));
  return handle;
}

// Two SEPARATE namespaces, deliberately not one `Map` keyed by a string both paths share —
// a test id that happens to spell `SEQUENCING_CONFLICT_GROUP` must never be able to collide
// with the real production entry, and two different data structures cannot collide no
// matter what string either one is given.
let productionRegistry;
const testRegistries = new Map();

/**
 * Open (or, if already open in this process, re-open) the ONE authoritative migration-core
 * registry handle. Takes no id — there is exactly one, and this is the only production-
 * facing way to reach it. `initialRecords` seeds a FIRST open only — for a fresh history,
 * `[]`; for a real session, whatever was actually persisted, read exactly once at the start
 * of the session. A later call in the same process returns the SAME live handle regardless
 * of what `initialRecords` it is given.
 */
export function openMigrationRegistry(initialRecords = []) {
  if (!productionRegistry) {
    productionRegistry = mintHandle(initialRecords);
  }
  return productionRegistry;
}

/**
 * TEST-ONLY. Never call this from production code — accepting a caller-chosen `registryId`
 * is exactly the mistake Decision 2.4 (Round 5) closes for the real migration-core registry.
 * Exists so independent test scenarios in this one process can each have their own isolated
 * authority. Backed by its own namespace, entirely separate from `openMigrationRegistry`'s —
 * a test id, however it is spelled, can never resolve to the real production handle.
 */
export function createMigrationRegistryForTests(registryId, initialRecords = []) {
  if (typeof registryId !== 'string' || registryId.length === 0) {
    throw new MigrationError('registryId is required: it names WHICH test-scoped registry this handle represents', 'invalid_registry', 500);
  }
  if (!testRegistries.has(registryId)) {
    testRegistries.set(registryId, mintHandle(initialRecords));
  }
  return testRegistries.get(registryId);
}

function assertRegistryHandle(registry) {
  if (!registry || registry[REGISTRY_BRAND] !== true || !registryState.has(registry)) {
    throw new MigrationError(
      'registry must be a handle from openMigrationRegistry() — a plain object, a JSON round-trip, or any other reconstructed snapshot cannot authoritatively arbitrate sequence assignment',
      'invalid_registry_handle', 500,
    );
  }
}

/**
 * Assign the next sequence number to a proposal, appending it to the registry.
 *
 * The caller MUST already hold the migration-core lock (ADR-0005 Decision 2.1) — that is a
 * process-level fact this function cannot itself observe from inside a single Node
 * process, so it is asserted explicitly via `lockHeldBy`, refusing to proceed without
 * confirmation rather than silently trusting the caller remembered. The next number is
 * never `max(existing) + 1` computed from a snapshot the caller might not still be looking
 * at — this function is the ONE place that number is decided, and it is decided from the
 * registry's own authoritative live state (Decision 2.4), never from a caller-supplied copy
 * of it.
 *
 * `applyUp`/`hashState`/`priorState` (and optional `emptyState`) are ADR-0005 Decision 4.5:
 * assignment RUNS the fresh-create/upgrade-from-previous check itself, over the real
 * candidate record about to be added — it does not trust `proposal.proofs` as the safety
 * gate, because Round 3 proved any shape that gate checks for is a shape a caller can
 * construct by hand. `proposal.proofs` is still cross-checked against what assignment
 * actually computes (below) as an audit signal — a proposer whose self-reported proof
 * doesn't match what really happens is worth surfacing.
 *
 * **The precise guarantee this gives, stated honestly (ADR-0005 Decision 4.6):** IF the
 * supplied `applyUp`/`hashState` faithfully interpret the candidate's opaque `up`/`down`
 * text, fresh-create and upgrade-from-previous are proven to converge before assignment.
 * This module cannot, and does not claim to, verify that they ARE faithful — `up` is
 * opaque to this contract by design (Decision 1.1: "a real SQL/DDL body ... opaque to this
 * contract"), and confirming an interpreter's fidelity to arbitrary SQL/DDL would require
 * actually executing it, which needs real database infrastructure no story in this series
 * has built. Selecting a trustworthy `applyUp`/`hashState` at assignment time is Kemi's own
 * responsibility — recorded, not merely assumed, since the same call that supplies them
 * also stamps the resulting record `assigned_by: 'Kemi'`.
 */
export function assignSequence(registry, proposal, {
  lockHeldBy, assignedAt, applyUp, hashState, emptyState = {}, priorState,
} = {}) {
  if (lockHeldBy !== SOLE_ASSIGNER) {
    throw new MigrationError(
      `sequence assignment requires the ${SEQUENCING_CONFLICT_GROUP} lock held by ${SOLE_ASSIGNER}, got ${JSON.stringify(lockHeldBy)}`,
      'lock_not_held', 500,
    );
  }
  assertRegistryHandle(registry);
  if (!proposal || 'sequence' in proposal) {
    throw new MigrationError(
      'a proposal already carrying a sequence number cannot be (re)assigned one — records are immutable once assigned',
      'already_assigned', 409,
    );
  }
  if (typeof applyUp !== 'function' || typeof hashState !== 'function') {
    // ADR-0005 Decision 4.5: assignment cannot skip running the real check — there is no
    // caller-attached proof shape it will accept instead. Omitting the means to run it is
    // refused exactly as omitting the lock declaration is: a missing input is never treated
    // as permission to proceed without it.
    throw new MigrationError(
      'assignSequence requires applyUp and hashState to run the fresh-create/upgrade-from-previous check itself — it no longer accepts a caller-attached proof as a substitute',
      'verification_required', 500,
    );
  }

  const authoritative = registryState.get(registry);

  if (authoritative.some((r) => r.proposal_id === proposal.proposal_id)) {
    throw new MigrationError(
      `proposal ${proposal.proposal_id} has already been assigned a sequence number`,
      'already_assigned', 409, { proposal_id: proposal.proposal_id },
    );
  }

  const breaking = proposal.forward_safety.classification === 'breaking';
  const dep = proposal.forward_safety.depends_on_deprecation_sequence;
  if (breaking) {
    const depRecord = authoritative.find((r) => r.sequence === dep);
    // ADR-0005 Decisions 3.3/3.5: existing at that sequence position is not enough (3.3),
    // and self-declaring AS a deprecation window is not enough either (3.5) unless it is
    // declared for the SAME target this breaking migration names as what it removes.
    if (!depRecord || !dependsOnTheDeclaredWindow(depRecord, proposal)) {
      throw new MigrationError(
        `breaking migration depends on deprecation sequence ${dep}, which is not a declared deprecation window for "${proposal.forward_safety.breaks ?? '(unnamed)'}" in the registry`,
        'missing_deprecation_dependency', 409, { depends_on: dep },
      );
    }
  }

  const nextSequence = authoritative.length === 0
    ? 1
    : Math.max(...authoritative.map((r) => r.sequence)) + 1;

  const candidate = Object.freeze({
    sequence: nextSequence,
    proposal_id: proposal.proposal_id,
    story_id: proposal.story_id,
    domain_conflict_group: proposal.domain_conflict_group,
    description: proposal.description,
    up: proposal.up,
    reversibility: proposal.reversibility,
    forward_safety: proposal.forward_safety,
    proofs: proposal.proofs,
    assigned_by: SOLE_ASSIGNER,
    assigned_at: isUtcInstant(assignedAt) ? assignedAt : nowUtc(),
  });

  // The real check, run here, now, by this module — not trusted from a claim attached to
  // the proposal. Throws missing_prior_state / non_gapless_sequence / proof_mismatch
  // exactly as verifyFreshCreateMatchesUpgrade does; nothing is assigned if it throws.
  const computed = computeFreshCreateAndUpgrade([...authoritative, candidate], { applyUp, hashState, emptyState, priorState });

  if (proposal.proofs.fresh_create_state_hash !== computed.fresh_create_state_hash
    || proposal.proofs.upgrade_from_previous_state_hash !== computed.upgrade_from_previous_state_hash) {
    // Not a safety failure (the migration itself DID just verify as upgrade-safe, above) —
    // an honesty failure: what the proposal claimed does not match what assignment actually
    // computed. Surfaced distinctly from proof_mismatch, which means the migration itself
    // does not converge; this means the proposer's own record of it disagrees with reality.
    throw new MigrationError(
      'the proposal\'s declared proofs do not match what assignment actually computed — the proposal record is not trustworthy even though the migration itself verifies',
      'proof_declaration_mismatch', 409,
      { declared: proposal.proofs, computed },
    );
  }

  // Advance the SAME handle's authoritative state in place — there is no "new registry" to
  // return; the one real handle a caller holds for the session IS the registry, before and
  // after this call, which is exactly what makes reusing it for the next assignment safe.
  registryState.set(registry, Object.freeze([...authoritative, candidate]));

  return { record: candidate, registry };
}

/** True only if sequence numbers are 1..N with no gaps and no duplicates, in array order. */
export function isGaplessSequence(records) {
  return records.every((r, i) => r.sequence === i + 1);
}

/**
 * A record, once assigned, is immutable (ADR-0005 Decision 3.4) — the identical append-only
 * posture ADR-0001 §3.3 and ADR-0003 already apply to other records. There is no "edit"
 * function; a correction is a new, later record. This function only proves a candidate
 * "corrected" record is NOT the same position edited in place.
 */
export function assertNotAnEdit(registry, candidateRecord) {
  const existing = registry.records.find((r) => r.sequence === candidateRecord.sequence);
  if (existing && JSON.stringify(existing) !== JSON.stringify(candidateRecord)) {
    throw new MigrationError(
      `sequence ${candidateRecord.sequence} is already assigned and immutable; a correction is a new, later record, never an edit`,
      'immutable_record', 409, { sequence: candidateRecord.sequence },
    );
  }
  return true;
}

/**
 * The one place fresh-create and upgrade-from-previous are actually computed (ADR-0005
 * Decision 4.2/4.6) — shared by `verifyFreshCreateMatchesUpgrade` (a proposer's own
 * pre-assignment check) and `assignSequence` itself (Decision 4.5: assignment RUNS this,
 * it does not trust a claim that someone else ran it), so the two can never validate two
 * different things by drifting apart. Throws exactly as documented on
 * `verifyFreshCreateMatchesUpgrade` below; returns the three real hashes on success.
 */
function computeFreshCreateAndUpgrade(records, { applyUp, hashState, emptyState = {}, priorState }) {
  if (records.length === 0) {
    throw new MigrationError('verifyFreshCreateMatchesUpgrade requires at least one record', 'invalid_records', 500);
  }
  if (!isGaplessSequence(records)) {
    throw new MigrationError('records must be sequence 1..N with no gaps', 'non_gapless_sequence', 500);
  }
  if (priorState === undefined) {
    // `priorState` is required, not merely accepted, and this is deliberate. Folding
    // records 1..N-1 over the SAME `emptyState` fresh-create uses would give the upgrade
    // path exactly the intermediate value fresh-create already passes through on its way
    // to N — for a deterministic `applyUp` that makes the two paths structurally unable to
    // disagree, regardless of whether the migration is actually upgrade-safe. A check that
    // cannot fail proves nothing; ADR-0005 §4.1 exists because a fresh-create-only proof is
    // not evidence about the situation every real tenant is actually in. `priorState` must
    // be a state reflecting a REAL, already-migrated-through-N-1 tenant — including
    // whatever a synthetic replay from empty cannot represent, such as accumulated row
    // data a computed default or backfill could behave differently against.
    throw new MigrationError(
      'priorState is required: replaying records from emptyState would make this check a tautology that can never fail',
      'missing_prior_state', 500,
    );
  }

  let freshState = emptyState;
  for (const record of records) freshState = applyUp(freshState, record);
  const freshHash = hashState(freshState);

  const priorHash = hashState(priorState);
  const upgradeState = applyUp(priorState, records[records.length - 1]);
  const upgradeHash = hashState(upgradeState);

  if (freshHash !== upgradeHash) {
    // ADR-0005 Decision 4.4: fails closed. Returning `matches: false` and trusting every
    // caller to check it before proceeding is exactly the gap that let an unverified proof
    // reach assignment — a failed proof refuses here, at the one place this check is run,
    // rather than becoming an assignable value a caller could ignore.
    throw new MigrationError(
      'fresh-create and upgrade-from-previous produced different final states: this migration is not upgrade-safe',
      'proof_mismatch', 409,
      { fresh_create_state_hash: freshHash, upgrade_from_previous_state_hash: upgradeHash, prior_state_hash: priorHash },
    );
  }

  return { fresh_create_state_hash: freshHash, upgrade_from_previous_state_hash: upgradeHash, prior_state_hash: priorHash };
}

/**
 * A proposer's own pre-assignment check: does this migration converge? `applyUp(state,
 * record)` is a caller-supplied pure function applying one record's `up` to a schema-state
 * value; `hashState` collapses a state to a comparable string. Runs fresh-create (apply
 * every record from empty) and upgrade-from-previous (apply 1..N-1, snapshot, apply N
 * alone) and asserts the two converge on the same final state.
 *
 * This is informational for the proposer — ADR-0005 Decision 4.5 means `assignSequence`
 * does NOT trust this function's output when it is handed back on a proposal; it runs the
 * identical check itself, at assignment time, over the real registry.
 */
export function verifyFreshCreateMatchesUpgrade(records, options) {
  const result = computeFreshCreateAndUpgrade(records, options);
  return Object.freeze({ matches: true, ...result });
}

/** A stable, deterministic hash for a plain-object schema state, for use with the function above. */
export function hashSchemaState(state) {
  return createHash('sha256').update(canonicalJson(state)).digest('hex');
}

function canonicalJson(value) {
  if (value === null || typeof value !== 'object') return JSON.stringify(value) ?? 'null';
  if (Array.isArray(value)) return `[${value.map(canonicalJson).join(',')}]`;
  return `{${Object.keys(value).sort().map((k) => `${JSON.stringify(k)}:${canonicalJson(value[k])}`).join(',')}}`;
}

