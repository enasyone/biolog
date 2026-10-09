/**
 * Identity feature's own in-session data access (BIO-UX-006-AC02, AC03, AC05).
 *
 * Two independent `SyntheticDataStore` instances (BIO-UX-004's own store
 * class, not wired anywhere until now — see its header comment):
 *
 * - `identityStore`: the `normal` scenario, module-singleton so a suspend or
 *   a new enrolment persists while a human clicks between the People list
 *   and a detail page in the same browser session (an ES module is
 *   evaluated once per session, not re-created on every route navigation).
 * - `duplicateReviewStore`: the `duplicate-identity` scenario specifically,
 *   so the Duplicate Review screen always has a candidate to resolve on
 *   first load (AC03) without requiring a scenario-switcher UI, which does
 *   not exist yet (BIO-UX-012, Human Test Console, not built). Kept
 *   deliberately separate from `identityStore` — resolving a duplicate here
 *   does not remove anyone from the main People list's own `normal` dataset,
 *   which never had that duplicate row to begin with.
 *
 * Every function here is a plain, synchronous, pure-ish helper over one of
 * the two stores; `mock.ts` is what adds the simulated async latency and
 * `PrimaryActionResult` shape a UI component actually calls.
 */
import { SyntheticDataStore } from '../../lib/synthetic-data/store';
import { DEFAULT_SEED } from '../../lib/synthetic-data/dataset';
import type { PersonCategory, SyntheticPerson } from '../../lib/synthetic-data/types';
import type {
  DuplicateGroup,
  EnrolmentDraft,
  PersonDetail,
  PersonSummary,
} from './types';

export const identityStore = new SyntheticDataStore(DEFAULT_SEED, 'normal');
export const duplicateReviewStore = new SyntheticDataStore(DEFAULT_SEED, 'duplicate-identity');

let enrolmentCounter = 0;

function toSummary(person: SyntheticPerson): PersonSummary {
  return {
    id: person.id,
    tenantId: person.tenantId,
    displayName: person.displayName,
    category: person.category,
    lifecycleStatus: person.lifecycleStatus,
    hasDuplicateFlag: person.duplicateGroupId !== undefined,
  };
}

export function listPeopleInStore(
  tenantId: string,
  category: PersonCategory | 'all' | undefined,
): readonly PersonSummary[] {
  const { people } = identityStore.getDataset();
  return people
    .filter((p) => p.tenantId === tenantId)
    .filter((p) => !category || category === 'all' || p.category === category)
    .map(toSummary);
}

export function getPersonInStore(tenantId: string, personId: string): PersonDetail | null {
  const dataset = identityStore.getDataset();
  const person = dataset.people.find((p) => p.tenantId === tenantId && p.id === personId);
  if (!person) return null;
  const credential = dataset.credentials.find((c) => c.personId === person.id);
  const photo = dataset.photos.find((p) => p.personId === person.id);
  return {
    ...toSummary(person),
    firstName: person.firstName,
    lastName: person.lastName,
    dateOfBirth: person.dateOfBirth,
    credential: credential ? { cardNumber: credential.cardNumber, status: credential.status } : undefined,
    photo: photo
      ? {
          qualityScore: photo.qualityScore,
          belowQualityThreshold: photo.belowQualityThreshold,
          placeholderColor: photo.placeholderColor,
        }
      : undefined,
  };
}

/** Returns the resulting lifecycle status, or `null` if no such person exists in this tenant. */
export function setLifecycleStatusInStore(
  tenantId: string,
  personId: string,
  status: SyntheticPerson['lifecycleStatus'],
): SyntheticPerson['lifecycleStatus'] | null {
  let result: SyntheticPerson['lifecycleStatus'] | null = null;
  identityStore.mutate((dataset) => {
    const people = dataset.people.map((p) => {
      if (p.tenantId === tenantId && p.id === personId) {
        result = status;
        return { ...p, lifecycleStatus: status };
      }
      return p;
    });
    return { ...dataset, people };
  });
  return result;
}

/** Appends a brand-new synthetic person (+credential+photo where captured) to `identityStore`. */
export function enrolPersonInStore(draft: EnrolmentDraft): { personId: string } {
  enrolmentCounter += 1;
  const personId = `${draft.tenantId}-person-enrolled-${enrolmentCounter}`;
  identityStore.mutate((dataset) => {
    const person: SyntheticPerson = {
      id: personId,
      tenantId: draft.tenantId,
      category: draft.category,
      firstName: draft.firstName,
      lastName: draft.lastName,
      displayName: `${draft.firstName} ${draft.lastName} (SYNTHETIC)`,
      dateOfBirth: draft.dateOfBirth,
      lifecycleStatus: 'active',
      isSynthetic: true,
    };
    const credentials = draft.credentialAssigned
      ? [
          ...dataset.credentials,
          {
            id: `${personId}-credential`,
            tenantId: draft.tenantId,
            personId,
            cardNumber: `BIO-${draft.tenantId.toUpperCase()}-${String(enrolmentCounter).padStart(4, '0')}`,
            status: 'active' as const,
            issuedAt: new Date(0).toISOString(),
            isSynthetic: true as const,
          },
        ]
      : dataset.credentials;
    const photos = draft.photoCaptured
      ? [
          ...dataset.photos,
          {
            id: `${personId}-photo`,
            tenantId: draft.tenantId,
            personId,
            qualityScore: 90,
            belowQualityThreshold: false,
            placeholderColor: 'hsl(210, 45%, 55%)',
            isSynthetic: true as const,
          },
        ]
      : dataset.photos;
    return { ...dataset, people: [...dataset.people, person], credentials, photos };
  });
  return { personId };
}

export function listDuplicateGroupsInStore(tenantId: string): readonly DuplicateGroup[] {
  const { people } = duplicateReviewStore.getDataset();
  const byGroup = new Map<string, SyntheticPerson[]>();
  for (const person of people) {
    if (person.tenantId !== tenantId || !person.duplicateGroupId) continue;
    const existing = byGroup.get(person.duplicateGroupId) ?? [];
    existing.push(person);
    byGroup.set(person.duplicateGroupId, existing);
  }
  return [...byGroup.entries()].map(([groupId, candidates]) => ({
    groupId,
    tenantId,
    candidates: candidates.map((c) => ({ personId: c.id, displayName: c.displayName, category: c.category })),
  }));
}

/** Removes every candidate in `groupId` except `keepPersonId`. Returns `false` if the group doesn't exist. */
export function resolveDuplicateInStore(tenantId: string, groupId: string, keepPersonId: string): boolean {
  let found = false;
  duplicateReviewStore.mutate((dataset) => {
    const inGroup = dataset.people.filter((p) => p.tenantId === tenantId && p.duplicateGroupId === groupId);
    if (inGroup.length === 0) return dataset;
    found = true;
    const people = dataset.people
      .filter((p) => !(p.tenantId === tenantId && p.duplicateGroupId === groupId && p.id !== keepPersonId))
      .map((p) => (p.id === keepPersonId ? { ...p, duplicateGroupId: undefined } : p));
    return { ...dataset, people };
  });
  return found;
}
