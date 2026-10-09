// Developer tests for BIO-UX-002-AC01, AC02 (grouped nav) and AC04 (full route inventory).
// SYNTHETIC: no real tenant/user data.
import { describe, expect, it } from 'vitest';
import { groupNavItems, type NavItem } from '../../../web/lib/permissions';
import { PRIMARY_NAV_ITEMS } from '../../../web/lib/nav-items';

describe('BIO-UX-002-AC01 groupNavItems partitions items into their sections in order', () => {
  const ITEMS: NavItem[] = [
    { id: 'a', label: 'A', href: '/a' },
    { id: 'b', label: 'B', href: '/b', group: 'One' },
    { id: 'c', label: 'C', href: '/c', group: 'One' },
    { id: 'd', label: 'D', href: '/d', group: 'Two' },
  ];

  it('creates one group per distinct group name, first-seen order, ungrouped first', () => {
    const groups = groupNavItems(ITEMS);
    expect(groups.map((g) => g.group)).toEqual([undefined, 'One', 'Two']);
    expect(groups[0]!.items.map((i) => i.id)).toEqual(['a']);
    expect(groups[1]!.items.map((i) => i.id)).toEqual(['b', 'c']);
    expect(groups[2]!.items.map((i) => i.id)).toEqual(['d']);
  });

  it('an empty list produces no groups', () => {
    expect(groupNavItems([])).toEqual([]);
  });
});

describe('BIO-UX-002-AC02 primary nav covers the approved product areas', () => {
  it('every item has a non-empty label and an href starting with /', () => {
    for (const item of PRIMARY_NAV_ITEMS) {
      expect(item.label.length).toBeGreaterThan(0);
      expect(item.href.startsWith('/')).toBe(true);
    }
  });

  it('groups the catalog into the sections shown in the approved reference screenshot, in order', () => {
    const groups = groupNavItems(PRIMARY_NAV_ITEMS);
    expect(groups.map((g) => g.group)).toEqual([
      undefined,
      'Identity',
      'Enrolment',
      'Credentials',
      'Documents',
      'Access & Operations',
      'Reports',
      'Administration',
    ]);
  });

  it('every nav id is unique', () => {
    const ids = PRIMARY_NAV_ITEMS.map((item) => item.id);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it('every nav href is unique (no two sidebar entries point at the same route)', () => {
    const hrefs = PRIMARY_NAV_ITEMS.map((item) => item.href);
    expect(new Set(hrefs).size).toBe(hrefs.length);
  });
});
