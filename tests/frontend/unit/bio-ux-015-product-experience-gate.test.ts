import { existsSync, readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { ROUTE_MANIFEST } from '../../../web/lib/route-manifest';
import { PRIMARY_NAV_ITEMS } from '../../../web/lib/nav-items';
import { PRODUCT_EXPERIENCE_GATE } from '../playwright/fixtures/product-experience-gate';
import { MAJOR_PAGES } from '../playwright/fixtures/approved-viewports';

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(here, '../../..');

function findPackRoot(startDir: string): string {
  let dir = startDir;
  for (let i = 0; i < 6; i += 1) {
    const candidate = resolve(dir, 'BIOLOG_Orchestration_Pack_v2.3');
    if (existsSync(candidate)) return candidate;
    dir = resolve(dir, '..');
  }
  throw new Error(`could not locate BIOLOG_Orchestration_Pack_v2.3 above ${startDir}`);
}

describe('BIO-UX-015-AC01/AC03 complete route and interaction traceability', () => {
  it('maps every approved manifest route exactly once to an executable canonical action', () => {
    const manifestIds = ROUTE_MANIFEST.map((entry) => entry.id).sort();
    const gateIds = PRODUCT_EXPERIENCE_GATE.map((entry) => entry.id).sort();
    expect(gateIds).toEqual(manifestIds);
    expect(new Set(gateIds).size).toBe(gateIds.length);
  });

  it('keeps every primary-navigation route in the approved route manifest and gate', () => {
    const manifestPaths = new Set(ROUTE_MANIFEST.map((entry) => entry.href));
    const gatePaths = new Set(PRODUCT_EXPERIENCE_GATE.map((entry) => entry.href.split('?')[0]));
    for (const item of PRIMARY_NAV_ITEMS) {
      expect(manifestPaths.has(item.href), `${item.href} missing from route manifest`).toBe(true);
      expect(gatePaths.has(item.href), `${item.href} missing from product gate`).toBe(true);
    }
  });

  it('binds every route to a checked-in focused browser evidence file', () => {
    for (const entry of PRODUCT_EXPERIENCE_GATE) {
      expect(
        existsSync(resolve(repoRoot, 'tests/frontend/playwright', entry.evidenceSpec)),
        `${entry.id} evidence ${entry.evidenceSpec} is missing`,
      ).toBe(true);
    }
  });

  it('retains BIO-UX-014 visual evidence for every major navigation group', () => {
    const snapshotDir = resolve(
      repoRoot,
      'tests/frontend/playwright/bio-ux-014-visual-regression.spec.ts-snapshots',
    );
    const gateRoutes = new Set(PRODUCT_EXPERIENCE_GATE.map((entry) => entry.href.split('?')[0]));
    for (const page of MAJOR_PAGES) expect(gateRoutes.has(page.route.split('?')[0])).toBe(true);
    expect(MAJOR_PAGES).toHaveLength(9);
    expect(existsSync(resolve(snapshotDir, 'dashboard-desktop-light-darwin.png'))).toBe(true);
    expect(existsSync(resolve(snapshotDir, 'dashboard-desktop-dark-darwin.png'))).toBe(true);
  });

  it('keeps the composed Card Designer page to one level-one heading', () => {
    const typography = readFileSync(
      resolve(repoRoot, 'web/features/bio-crd-003/CardTypographyApp.tsx'),
      'utf8',
    );
    const objects = readFileSync(
      resolve(repoRoot, 'web/features/bio-crd-004/CardObjectsApp.tsx'),
      'utf8',
    );
    expect(typography).toContain('<h2>Card Typography, Name Fields &amp; Watermarks</h2>');
    expect(typography).not.toContain('<h1>');
    expect(objects).toContain('<h2>Card Objects: Barcode, Signature &amp; Logo</h2>');
    expect(objects).not.toContain('<h1>');
  });
});

describe('BIO-UX-015-AC05/AC06 governance gate remains fail closed', () => {
  const packRoot = findPackRoot(repoRoot);

  it('documents a genuine, well-formed human approval record for BIO-UX-015', () => {
    const approval = readFileSync(
      resolve(packRoot, 'governance/ui-approval.yaml'),
      'utf8',
    );
    expect(approval).toMatch(/ui_product_experience_approved:\s*true/);
    expect(approval).toMatch(/approved_by:\s*human-owner/);
    expect(approval).toMatch(/approved_story:\s*BIO-UX-015/);
    expect(approval).toMatch(/approval_phrase:\s*I-APPROVE-BIOLOG-W01UX-PRODUCT-EXPERIENCE/);
  });

  it('keeps GitHub publication disabled', () => {
    const releaseState = readFileSync(resolve(packRoot, 'governance/release-state.yaml'), 'utf8');
    expect(releaseState).toMatch(/github_push_enabled:\s*false/);
  });

  it('the acceptance engine rejects BIO-UX-015 PASS until human approval exists', () => {
    const engine = readFileSync(resolve(packRoot, 'automation/biolog.py'), 'utf8');
    expect(engine).toContain("if args.story=='BIO-UX-015' and verdict=='PASS' and not ui_human_approved():");
    expect(engine).toContain('BIO-UX-015 requires explicit human UI approval');
  });
});
