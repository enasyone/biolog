// Developer test guarding against silent drift between this story's synthetic
// tenant/person identities and the ones session-context.tsx already shipped
// (BIO-FND-007/BIO-UX-002). SYNTHETIC: no real tenant/user data.
//
// session-context.tsx's own TENANTS/DEMO_USER_BY_ROLE consts are not
// exported (shell-internal), so this can only check the parts it does
// export plus the known literal demo names — a partial, not total,
// consistency guarantee. See web/lib/synthetic-data/names.ts's header note.
import { describe, expect, it } from 'vitest';
import { DEFAULT_TENANT_ID } from '../../../web/context/session-context';
import { SYNTHETIC_TENANTS, FIRST_NAMES, LAST_NAMES, withMarker } from '../../../web/lib/synthetic-data/names';
import { generateDataset } from '../../../web/lib/synthetic-data/dataset';

describe('BIO-UX-004 tenant/person identities stay consistent with the shipped shell demo session', () => {
  it("session-context.tsx's DEFAULT_TENANT_ID is one of this story's synthetic tenants", () => {
    expect(SYNTHETIC_TENANTS.some((t) => t.id === DEFAULT_TENANT_ID)).toBe(true);
  });

  it('the first three generated people match the shell\'s known demo identities exactly', () => {
    // session-context.tsx's DEMO_USER_BY_ROLE: Amara Adeleke, Kelechi Okafor, Ovie Eferakeya.
    const expected = [withMarker('Amara Adeleke'), withMarker('Kelechi Okafor'), withMarker('Ovie Eferakeya')];
    expect([FIRST_NAMES[0], FIRST_NAMES[1], FIRST_NAMES[2]]).toEqual(['Amara', 'Kelechi', 'Ovie']);
    expect([LAST_NAMES[0], LAST_NAMES[1], LAST_NAMES[2]]).toEqual(['Adeleke', 'Okafor', 'Eferakeya']);

    const dataset = generateDataset();
    const alphaPeople = dataset.people.filter((p) => p.tenantId === DEFAULT_TENANT_ID);
    const displayNames = alphaPeople.slice(0, 3).map((p) => p.displayName);
    expect(displayNames).toEqual(expected);
  });
});
