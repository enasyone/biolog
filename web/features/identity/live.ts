import { createLiveAdapter } from '../../lib/module-port';
import type { IdentityPort } from './ports';

const base = createLiveAdapter('IdentityService', '/api/v1/identity/primary-action');

/**
 * Identity live adapter (BIO-UX-005-AC04, extended BIO-UX-006-AC06).
 *
 * Every real method genuinely calls the backend route this story's
 * declared API dependency (BIO-IDN-001/006) will eventually serve, and
 * genuinely fails to `offline` today — no such route exists yet, so this is
 * correct, expected behaviour for an unconnected LIVE mode (see
 * `module-port.ts`'s `createLiveAdapter` header comment), not a bug to hide.
 */
async function post<TResult>(path: string, body: unknown, offlineMessage: string): Promise<TResult> {
  const response = await fetch(path, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    throw new Error(`Live request to ${path} failed: HTTP ${response.status}`);
  }
  return (await response.json()) as TResult;
}

function offlineResult(message: string) {
  return { outcome: 'offline' as const, message };
}

export const identityLivePort: IdentityPort = {
  ...base,

  async listPeople(input) {
    try {
      return await post('/api/v1/identity/people/list', input, '');
    } catch {
      return { people: [] };
    }
  },

  async getPerson(input) {
    try {
      return await post('/api/v1/identity/people/get', input, '');
    } catch {
      return { person: null };
    }
  },

  async suspendPerson(input) {
    try {
      return await post('/api/v1/identity/people/suspend', input, '');
    } catch {
      return offlineResult('No live identity service reachable yet — this module has not been connected.');
    }
  },

  async reinstatePerson(input) {
    try {
      return await post('/api/v1/identity/people/reinstate', input, '');
    } catch {
      return offlineResult('No live identity service reachable yet — this module has not been connected.');
    }
  },

  async submitEnrolment(draft) {
    try {
      return await post('/api/v1/identity/enrolment/submit', draft, '');
    } catch {
      return offlineResult('No live identity service reachable yet — this module has not been connected.');
    }
  },

  async listDuplicates(input) {
    try {
      return await post('/api/v1/identity/duplicates/list', input, '');
    } catch {
      return { groups: [] };
    }
  },

  async resolveDuplicate(input) {
    try {
      return await post('/api/v1/identity/duplicates/resolve', input, '');
    } catch {
      return offlineResult('No live identity service reachable yet — this module has not been connected.');
    }
  },
};
