import { createMockAdapter } from '../../lib/module-port';
import type { IdentityPort } from './ports';
import {
  enrolPersonInStore,
  getPersonInStore,
  listDuplicateGroupsInStore,
  listPeopleInStore,
  resolveDuplicateInStore,
  setLifecycleStatusInStore,
} from './data-store';

const SIMULATED_LATENCY_MS = 200;

function delay(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, SIMULATED_LATENCY_MS));
}

const base = createMockAdapter('IdentityService', {
  scenarios: [
    { outcome: 'success', message: 'Identity: simulated action completed.' },
    { outcome: 'failure', message: 'Identity: simulated action failed — try again.' },
    { outcome: 'offline', message: 'Identity: device or network appears offline (simulated).' },
  ],
});

/**
 * Identity mock adapter (BIO-UX-005-AC05, extended BIO-UX-006-AC02/AC03/AC05).
 * Every real method reads/writes `data-store.ts`'s two `SyntheticDataStore`
 * instances and returns a genuinely simulated async result (real latency,
 * real success/failure branching on bad input) rather than resolving
 * instantly — so a caller genuinely observes a loading state, matching
 * `module-port.ts`'s own established convention.
 */
export const identityMockPort: IdentityPort = {
  ...base,

  async listPeople(input) {
    await delay();
    return { people: listPeopleInStore(input.tenantId, input.category) };
  },

  async getPerson(input) {
    await delay();
    return { person: getPersonInStore(input.tenantId, input.personId) };
  },

  async suspendPerson(input) {
    await delay();
    const result = setLifecycleStatusInStore(input.tenantId, input.personId, 'suspended');
    if (!result) {
      return { outcome: 'failure', message: 'Suspend failed — person not found in this tenant.' };
    }
    return { outcome: 'success', message: 'Person suspended (emergency, simulated).' };
  },

  async reinstatePerson(input) {
    await delay();
    const result = setLifecycleStatusInStore(input.tenantId, input.personId, 'active');
    if (!result) {
      return { outcome: 'failure', message: 'Reinstate failed — person not found in this tenant.' };
    }
    return { outcome: 'success', message: 'Person reinstated to active (simulated).' };
  },

  async submitEnrolment(draft) {
    await delay();
    const missing: string[] = [];
    if (!draft.firstName.trim()) missing.push('first name');
    if (!draft.lastName.trim()) missing.push('last name');
    if (!draft.dateOfBirth.trim()) missing.push('date of birth');
    if (missing.length > 0) {
      const fields =
        missing.length === 1
          ? missing[0]
          : `${missing.slice(0, -1).join(', ')} and ${missing[missing.length - 1]}`;
      const verb = missing.length === 1 ? 'is' : 'are';
      return {
        outcome: 'failure',
        message: `Enrolment failed — ${fields} ${verb} required. Return to the Identity step and complete the missing field${missing.length === 1 ? '' : 's'}.`,
      };
    }
    const { personId } = enrolPersonInStore(draft);
    return { outcome: 'success', message: 'Enrolment submitted (simulated).', personId };
  },

  async listDuplicates(input) {
    await delay();
    return { groups: listDuplicateGroupsInStore(input.tenantId) };
  },

  async resolveDuplicate(input) {
    await delay();
    const found = resolveDuplicateInStore(input.tenantId, input.groupId, input.keepPersonId);
    if (!found) {
      return { outcome: 'failure', message: 'Resolve failed — duplicate group not found.' };
    }
    return { outcome: 'success', message: 'Duplicate resolved (simulated).' };
  },
};
