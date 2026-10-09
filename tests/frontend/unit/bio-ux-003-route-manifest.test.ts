// Developer tests for BIO-UX-003-AC01, AC02, AC03, AC05.
// SYNTHETIC: no real tenant/user data.
import { existsSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import {
  ROUTE_MANIFEST,
  ROUTE_MANIFEST_HREFS,
  STANDARD_PAGE_STATES,
  type PageState,
} from '../../../web/lib/route-manifest';
import { PRIMARY_NAV_ITEMS } from '../../../web/lib/nav-items';

const here = dirname(fileURLToPath(import.meta.url));
const webAppDir = resolve(here, '../../../web/app');

// The exact family list named in BIO-UX-003-AC01.
const AC01_PAGE_FAMILIES: readonly string[] = [
  'Dashboard',
  'People',
  'Students',
  'Staff',
  'Visitors',
  'Alumni',
  'Contractors',
  'Enrolment',
  'Photos',
  'Fingerprints',
  'Face',
  'Duplicate Review',
  'Cards',
  'Issue/Replace Card',
  'NFC/UHF',
  'Mobile Credentials',
  'Document Verify',
  'Access',
  'Attendance',
  'Transport',
  'Devices',
  'Reports',
  'Audit',
  'Administration',
  'Settings',
];

/** `/cards/issue` -> `web/app/cards/issue/page.tsx`. */
function pageFileFor(href: string): string {
  const segment = href === '/' ? '' : href;
  return resolve(webAppDir, `.${segment}`, 'page.tsx');
}

describe('BIO-UX-003-AC01 route manifest covers every named page family', () => {
  it('has at least one manifest entry per AC01-named family', () => {
    const familiesInManifest = new Set(ROUTE_MANIFEST.map((entry) => entry.pageFamily));
    for (const family of AC01_PAGE_FAMILIES) {
      expect(familiesInManifest.has(family), `missing page family: ${family}`).toBe(true);
    }
  });
});

describe('BIO-UX-003-AC02 every route identifies actions, visibility and target port', () => {
  it('every entry has at least one primary action', () => {
    for (const entry of ROUTE_MANIFEST) {
      expect(entry.primaryActions.length, `${entry.id} has no primary action`).toBeGreaterThan(0);
    }
  });

  it('every entry declares a target service port, including explicit Unassigned', () => {
    for (const entry of ROUTE_MANIFEST) {
      expect(entry.servicePort, `${entry.id} has no servicePort`).toBeTruthy();
    }
  });

  it('an Unassigned port always carries an explanatory note (not a silent gap)', () => {
    for (const entry of ROUTE_MANIFEST) {
      if (entry.servicePort === 'Unassigned') {
        expect(entry.note, `${entry.id} is Unassigned but has no note`).toBeTruthy();
      }
    }
  });

  it('every entry except Dashboard declares a required permission (matches nav-items.ts)', () => {
    for (const entry of ROUTE_MANIFEST) {
      if (entry.id === 'dashboard') continue;
      expect(entry.requiredPermission, `${entry.id} has no requiredPermission`).toBeTruthy();
    }
  });
});

describe('BIO-UX-003-AC03 empty/loading/permission-denied/error/offline/success states', () => {
  it('STANDARD_PAGE_STATES names exactly the PRODUCT-EXPERIENCE.md contract (default is implicit)', () => {
    const expected: readonly PageState[] = [
      'loading',
      'empty',
      'success',
      'warning',
      'error',
      'permission-denied',
      'offline',
    ];
    expect(STANDARD_PAGE_STATES).toEqual(expected);
  });

  it('every entry resolves to a non-empty subset of the standard state contract', () => {
    const standardSet = new Set(STANDARD_PAGE_STATES);
    for (const entry of ROUTE_MANIFEST) {
      const states = entry.states ?? STANDARD_PAGE_STATES;
      expect(states.length, `${entry.id} has no states`).toBeGreaterThan(0);
      for (const state of states) {
        expect(standardSet.has(state), `${entry.id} has an unknown state: ${state}`).toBe(true);
      }
    }
  });

  it('a narrowed states list always carries an explanatory note', () => {
    for (const entry of ROUTE_MANIFEST) {
      if (entry.states) {
        expect(entry.note, `${entry.id} narrows states but has no note explaining why`).toBeTruthy();
      }
    }
  });
});

describe('BIO-UX-003-AC05 no primary nav item points to a non-functional page', () => {
  it('every PRIMARY_NAV_ITEMS href has a real page.tsx file on disk', () => {
    for (const item of PRIMARY_NAV_ITEMS) {
      expect(existsSync(pageFileFor(item.href)), `${item.href} has no page.tsx`).toBe(true);
    }
  });

  it('every PRIMARY_NAV_ITEMS href is covered by the route manifest', () => {
    for (const item of PRIMARY_NAV_ITEMS) {
      expect(ROUTE_MANIFEST_HREFS.has(item.href), `${item.href} missing from ROUTE_MANIFEST`).toBe(
        true,
      );
    }
  });

  it('every manifest entry also has a real page.tsx file on disk', () => {
    for (const entry of ROUTE_MANIFEST) {
      expect(existsSync(pageFileFor(entry.href)), `${entry.href} has no page.tsx`).toBe(true);
    }
  });
});
