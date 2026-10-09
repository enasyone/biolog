// Developer tests for BIO-FND-007-AC03 (permission-based nav visibility) and
// BIO-FND-007-AC04 (shell carries no feature-specific business logic).
// SYNTHETIC: no real tenant/user data.
import { describe, expect, it } from 'vitest';
import { filterNavByPermission, isNavItemVisible, type NavItem } from '../../../web/lib/permissions';
import { PRIMARY_NAV_ITEMS } from '../../../web/lib/nav-items';

const ITEMS: NavItem[] = [
  { id: 'a', label: 'Always visible', href: '/a' },
  { id: 'b', label: 'Needs perm b', href: '/b', requiredPermission: 'nav:b' },
  { id: 'c', label: 'Needs perm c', href: '/c', requiredPermission: 'nav:c' },
];

describe('BIO-FND-007-AC03 permission-based nav visibility', () => {
  it('shows an item with no requirement to anyone', () => {
    expect(isNavItemVisible(ITEMS[0]!, new Set())).toBe(true);
  });

  it('hides an item whose required permission is not granted', () => {
    expect(isNavItemVisible(ITEMS[1]!, new Set())).toBe(false);
  });

  it('shows an item whose required permission is granted', () => {
    expect(isNavItemVisible(ITEMS[1]!, new Set(['nav:b']))).toBe(true);
  });

  it('filters a list down to only the visible items, preserving order', () => {
    const visible = filterNavByPermission(ITEMS, new Set(['nav:c']));
    expect(visible.map((item) => item.id)).toEqual(['a', 'c']);
  });

  it('accepts a plain array of granted permissions, not only a Set', () => {
    const visible = filterNavByPermission(ITEMS, ['nav:b']);
    expect(visible.map((item) => item.id)).toEqual(['a', 'b']);
  });

  it('an empty grant set still shows every unrestricted item', () => {
    const visible = filterNavByPermission(ITEMS, []);
    expect(visible.map((item) => item.id)).toEqual(['a']);
  });
});

describe('BIO-FND-007-AC04 shell nav carries no business logic', () => {
  it('every placeholder nav item is a plain label/href/permission tuple', () => {
    for (const item of PRIMARY_NAV_ITEMS) {
      expect(typeof item.id).toBe('string');
      expect(typeof item.label).toBe('string');
      expect(item.href.startsWith('/')).toBe(true);
      if (item.requiredPermission !== undefined) {
        expect(typeof item.requiredPermission).toBe('string');
      }
      // No item carries computed values, functions, or nested feature payloads.
      for (const value of Object.values(item)) {
        expect(typeof value === 'string' || value === undefined).toBe(true);
      }
    }
  });
});
