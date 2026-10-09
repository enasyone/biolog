// Developer tests for TD-WEB-PROVENANCE-BRIDGE.
// SYNTHETIC: no real tenant/user data.
import { describe, expect, it } from 'vitest';
import { isCanonicalId, parseId } from '../../../contracts/js/identifiers.mjs';
import { DEMO_ROLE_IDS as SESSION_DEMO_ROLE_IDS } from '../../../web/context/session-context';
import {
  DEFAULT_DEMO_ROLE_ID,
  DEMO_ROLE_IDS,
  deriveDemoActorId,
  isDemoRoleId,
} from '../../../web/lib/demo-identity';

describe('demo-identity role ids stay in sync with session-context.tsx', () => {
  it('lib/demo-identity.ts\'s DEMO_ROLE_IDS has exactly the same ids as session-context.tsx\'s own list', () => {
    expect([...DEMO_ROLE_IDS].sort()).toEqual([...SESSION_DEMO_ROLE_IDS].sort());
  });
});

describe('isDemoRoleId', () => {
  it('accepts every known role id', () => {
    for (const id of DEMO_ROLE_IDS) {
      expect(isDemoRoleId(id)).toBe(true);
    }
  });

  it('rejects unknown, empty, null and undefined values', () => {
    expect(isDemoRoleId('superadmin')).toBe(false);
    expect(isDemoRoleId('')).toBe(false);
    expect(isDemoRoleId(null)).toBe(false);
    expect(isDemoRoleId(undefined)).toBe(false);
  });
});

describe('deriveDemoActorId', () => {
  it('derives a stable, role-only (not tenant-scoped) machine id per known role', () => {
    for (const id of DEMO_ROLE_IDS) {
      expect(deriveDemoActorId(id)).toMatch(/^per_/);
    }
  });

  it('never produces the same id for two different roles', () => {
    const ids = DEMO_ROLE_IDS.map((id) => deriveDemoActorId(id));
    expect(new Set(ids).size).toBe(ids.length);
  });

  // TD-WEB-PROVENANCE-BRIDGE round 6 (Kemi's ruling): the earlier `demo-actor-{roleId}` value
  // was not a well-formed canonical BIOLOG identifier (ADR-0001 Decision 1) -
  // CanonicalId.IsPersonId rejected it on format before any RBAC grant lookup ever ran, so no
  // story could ever get a real "granted" result through this bridge. This is the actual
  // root-cause proof: every derived actor id must pass the SAME canonical-identifier check the
  // real backend applies (contracts/js/identifiers.mjs, mirrored by
  // api/features/bio-idn-001/Identifiers/CanonicalId.cs's IsPersonId), as a well-formed
  // `per_<UUIDv7>` person id - not merely "looks like a string starting with per_".
  it('is a well-formed canonical per_ identifier for every known role, per the real contract validator', () => {
    for (const id of DEMO_ROLE_IDS) {
      const actorId = deriveDemoActorId(id);
      expect(isCanonicalId(actorId)).toBe(true);
      expect(parseId(actorId, 'person').type).toBe('person');
    }
  });

  it('is stable (hardcoded, not freshly minted) across repeated calls for the same role', () => {
    for (const id of DEMO_ROLE_IDS) {
      expect(deriveDemoActorId(id)).toBe(deriveDemoActorId(id));
    }
  });
});

describe('DEFAULT_DEMO_ROLE_ID', () => {
  it('matches session-context.tsx\'s own DEFAULT_ROLE_ID and is a known role', () => {
    expect(DEFAULT_DEMO_ROLE_ID).toBe('staff');
    expect(isDemoRoleId(DEFAULT_DEMO_ROLE_ID)).toBe(true);
  });
});
