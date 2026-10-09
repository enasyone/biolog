// Developer tests for BIO-UX-006. SYNTHETIC: demo/synthetic fixture data only.
import { beforeEach, describe, expect, it } from 'vitest';
import {
  duplicateReviewStore,
  enrolPersonInStore,
  getPersonInStore,
  identityStore,
  listDuplicateGroupsInStore,
  listPeopleInStore,
  resolveDuplicateInStore,
  setLifecycleStatusInStore,
} from '../../../web/features/identity/data-store';
import { DEFAULT_SEED } from '../../../web/lib/synthetic-data/dataset';
import { SyntheticDataStore } from '../../../web/lib/synthetic-data/store';

// Every store here is a module-level singleton (deliberately, so a suspend/enrolment persists
// across a session — see data-store.ts's own header comment); reset it to a known state before
// each test so tests don't leak mutations into one another.
beforeEach(() => {
  identityStore.resetToSeed();
  duplicateReviewStore.resetToSeed();
});

describe('BIO-UX-006-AC01 listPeopleInStore/getPersonInStore', () => {
  it('lists only the requested tenant\'s people', () => {
    const alpha = listPeopleInStore('alpha', 'all');
    const beta = listPeopleInStore('beta', 'all');
    expect(alpha.length).toBeGreaterThan(0);
    expect(beta.length).toBeGreaterThan(0);
    expect(alpha.every((p) => p.tenantId === 'alpha')).toBe(true);
    expect(beta.every((p) => p.tenantId === 'beta')).toBe(true);
  });

  it('filters by category', () => {
    const students = listPeopleInStore('alpha', 'student');
    expect(students.length).toBeGreaterThan(0);
    expect(students.every((p) => p.category === 'student')).toBe(true);
  });

  it('getPersonInStore returns null for an unknown id, and null across a tenant boundary', () => {
    const [first] = listPeopleInStore('alpha', 'all');
    expect(first).toBeDefined();
    expect(getPersonInStore('alpha', 'no-such-person')).toBeNull();
    expect(getPersonInStore('beta', first!.id)).toBeNull();
  });

  it('getPersonInStore returns the full detail shape including credential/photo when present', () => {
    const [first] = listPeopleInStore('alpha', 'all');
    const detail = getPersonInStore('alpha', first!.id);
    expect(detail).not.toBeNull();
    expect(detail!.credential).toBeDefined();
    expect(detail!.photo).toBeDefined();
  });
});

describe('BIO-UX-006-AC03 identity lifecycle / emergency suspension', () => {
  it('every generated person starts active', () => {
    const people = listPeopleInStore('alpha', 'all');
    expect(people.every((p) => p.lifecycleStatus === 'active')).toBe(true);
  });

  it('suspend then reinstate round-trips the lifecycle status', () => {
    const [first] = listPeopleInStore('alpha', 'all');
    expect(setLifecycleStatusInStore('alpha', first!.id, 'suspended')).toBe('suspended');
    expect(getPersonInStore('alpha', first!.id)!.lifecycleStatus).toBe('suspended');
    expect(setLifecycleStatusInStore('alpha', first!.id, 'active')).toBe('active');
    expect(getPersonInStore('alpha', first!.id)!.lifecycleStatus).toBe('active');
  });

  it('cannot suspend a person in a different tenant (tenant isolation)', () => {
    const [first] = listPeopleInStore('alpha', 'all');
    expect(setLifecycleStatusInStore('beta', first!.id, 'suspended')).toBeNull();
  });

  it('suspending one person does not affect another', () => {
    const [first, second] = listPeopleInStore('alpha', 'all');
    setLifecycleStatusInStore('alpha', first!.id, 'suspended');
    expect(getPersonInStore('alpha', second!.id)!.lifecycleStatus).toBe('active');
  });
});

describe('BIO-UX-006-AC03 duplicate review', () => {
  it('the duplicate-identity scenario store has at least one group with two candidates', () => {
    const groups = listDuplicateGroupsInStore('alpha');
    expect(groups.length).toBeGreaterThan(0);
    expect(groups[0]!.candidates.length).toBe(2);
  });

  it('resolving a group removes every candidate except the kept one', () => {
    const [group] = listDuplicateGroupsInStore('alpha');
    const keep = group!.candidates[0]!.personId;
    expect(resolveDuplicateInStore('alpha', group!.groupId, keep)).toBe(true);
    expect(listDuplicateGroupsInStore('alpha')).toHaveLength(0);
    // The kept record survives, with its duplicate flag cleared.
    const survivor = duplicateReviewStore.getDataset().people.find((p) => p.id === keep);
    expect(survivor).toBeDefined();
    expect(survivor!.duplicateGroupId).toBeUndefined();
  });

  it('resolving an unknown group id is a no-op that reports failure', () => {
    expect(resolveDuplicateInStore('alpha', 'no-such-group', 'irrelevant')).toBe(false);
  });

  it('duplicate-identity scenario data does not leak into the main identityStore (separate stores)', () => {
    const mainPeopleIds = new Set(identityStore.getDataset().people.map((p) => p.id));
    const dupOnlyPerson = duplicateReviewStore
      .getDataset()
      .people.find((p) => p.id.endsWith('-dup'));
    expect(dupOnlyPerson).toBeDefined();
    expect(mainPeopleIds.has(dupOnlyPerson!.id)).toBe(false);
  });
});

describe('BIO-UX-006-AC02 enrolment', () => {
  it('enrolPersonInStore adds a new active person, with credential/photo only when captured', () => {
    const before = listPeopleInStore('alpha', 'all').length;
    const { personId } = enrolPersonInStore({
      tenantId: 'alpha',
      firstName: 'Test',
      lastName: 'Person',
      dateOfBirth: '2005-01-01',
      category: 'student',
      photoCaptured: true,
      biometricCaptured: true,
      credentialAssigned: false,
    });
    const after = listPeopleInStore('alpha', 'all');
    expect(after.length).toBe(before + 1);
    const detail = getPersonInStore('alpha', personId);
    expect(detail).not.toBeNull();
    expect(detail!.lifecycleStatus).toBe('active');
    expect(detail!.photo).toBeDefined();
    expect(detail!.credential).toBeUndefined();
  });

  it('two enrolments in the same session get distinct ids', () => {
    const a = enrolPersonInStore({
      tenantId: 'alpha',
      firstName: 'A',
      lastName: 'One',
      dateOfBirth: '2000-01-01',
      category: 'staff',
      photoCaptured: false,
      biometricCaptured: false,
      credentialAssigned: false,
    });
    const b = enrolPersonInStore({
      tenantId: 'alpha',
      firstName: 'B',
      lastName: 'Two',
      dateOfBirth: '2000-01-01',
      category: 'staff',
      photoCaptured: false,
      biometricCaptured: false,
      credentialAssigned: false,
    });
    expect(a.personId).not.toBe(b.personId);
  });
});

describe('BIO-UX-004-AC01 determinism is preserved by the BIO-UX-006 lifecycleStatus field', () => {
  it('the identity store\'s underlying dataset is still deterministic for a fresh seed', () => {
    const other = new SyntheticDataStore(DEFAULT_SEED, 'normal');
    expect(other.getDataset().people).toEqual(identityStore.getDataset().people);
  });
});
