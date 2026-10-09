// Developer tests for BIO-UX-011-AC04 (feature flags visibly alter navigation).
// SYNTHETIC: no real tenant/user data.
import { describe, expect, it } from 'vitest';
import { filterNavByFeatureFlags, type NavItem } from '../../../web/lib/permissions';
import { PRIMARY_NAV_ITEMS } from '../../../web/lib/nav-items';
import { DEFAULT_FEATURE_FLAGS } from '../../../web/context/session-context';

const ITEMS: NavItem[] = [
  { id: 'a', label: 'A', href: '/a' },
  { id: 'b', label: 'B', href: '/b', requiredFeatureFlag: 'mobileCredentials' },
  { id: 'c', label: 'C', href: '/c', requiredFeatureFlag: 'cardDesigner' },
];

describe('BIO-UX-011-AC04 filterNavByFeatureFlags', () => {
  it('always shows an item with no requiredFeatureFlag', () => {
    const visible = filterNavByFeatureFlags(ITEMS, {});
    expect(visible.map((i) => i.id)).toEqual(['a']);
  });

  it('shows a gated item only when its flag is explicitly true', () => {
    const visible = filterNavByFeatureFlags(ITEMS, { mobileCredentials: true, cardDesigner: false });
    expect(visible.map((i) => i.id)).toEqual(['a', 'b']);
  });

  it('hides a gated item when its flag is false or absent', () => {
    const visible = filterNavByFeatureFlags(ITEMS, { mobileCredentials: false });
    expect(visible.map((i) => i.id)).toEqual(['a']);
  });

  it('preserves order and handles an empty flag map (fails closed, not open)', () => {
    expect(filterNavByFeatureFlags(ITEMS, {}).map((i) => i.id)).toEqual(['a']);
  });
});

describe('BIO-UX-011-AC04 real nav items gated by a feature flag', () => {
  it('Card Designer, Document Verify and Mobile Credentials each declare a requiredFeatureFlag matching a real FeatureFlagKey', () => {
    const gated = PRIMARY_NAV_ITEMS.filter((item) => item.requiredFeatureFlag);
    const ids = gated.map((item) => item.id).sort();
    expect(ids).toEqual(['card-designer', 'document-verify', 'mobile-credentials']);
    for (const item of gated) {
      expect(Object.keys(DEFAULT_FEATURE_FLAGS)).toContain(item.requiredFeatureFlag);
    }
  });

  it('with DEFAULT_FEATURE_FLAGS, Mobile Credentials is hidden and Card Designer/Document Verify are visible', () => {
    const visible = filterNavByFeatureFlags(PRIMARY_NAV_ITEMS, DEFAULT_FEATURE_FLAGS);
    const visibleIds = new Set(visible.map((item) => item.id));
    expect(visibleIds.has('mobile-credentials')).toBe(false);
    expect(visibleIds.has('card-designer')).toBe(true);
    expect(visibleIds.has('document-verify')).toBe(true);
  });
});
