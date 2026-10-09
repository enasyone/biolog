// Developer tests for BIO-UX-006-AC06 ("stable integration seam for later live identity APIs").
// SYNTHETIC: no real network call ever succeeds here — every assertion is about the fail-closed
// shape when no backend exists yet, exactly like module-port.ts's own createLiveAdapter contract.
import { describe, expect, it } from 'vitest';
import { identityLivePort } from '../../../web/features/identity/live';

describe('BIO-UX-006-AC06 identityLivePort implements the full IdentityPort seam', () => {
  it('implements every real method the mock port implements, plus the generic primaryAction', () => {
    for (const method of [
      'primaryAction',
      'listPeople',
      'getPerson',
      'suspendPerson',
      'reinstatePerson',
      'submitEnrolment',
      'listDuplicates',
      'resolveDuplicate',
    ] as const) {
      expect(typeof identityLivePort[method]).toBe('function');
    }
  });

  it('mutating methods fail closed to "offline" when no backend is reachable, not a thrown error', async () => {
    await expect(
      identityLivePort.suspendPerson({ tenantId: 'alpha', personId: 'alpha-person-000' }),
    ).resolves.toMatchObject({ outcome: 'offline' });
    await expect(
      identityLivePort.reinstatePerson({ tenantId: 'alpha', personId: 'alpha-person-000' }),
    ).resolves.toMatchObject({ outcome: 'offline' });
    await expect(
      identityLivePort.resolveDuplicate({ tenantId: 'alpha', groupId: 'g', keepPersonId: 'p' }),
    ).resolves.toMatchObject({ outcome: 'offline' });
  });

  it('read methods fail closed to an empty result, not a thrown error', async () => {
    await expect(identityLivePort.listPeople({ tenantId: 'alpha' })).resolves.toEqual({ people: [] });
    await expect(identityLivePort.getPerson({ tenantId: 'alpha', personId: 'x' })).resolves.toEqual({
      person: null,
    });
    await expect(identityLivePort.listDuplicates({ tenantId: 'alpha' })).resolves.toEqual({ groups: [] });
  });

  it('module identity is IdentityService in both mock and live mode', () => {
    expect(identityLivePort.module).toBe('IdentityService');
    expect(identityLivePort.mode).toBe('live');
  });
});
