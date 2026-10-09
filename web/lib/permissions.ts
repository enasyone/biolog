/**
 * Generic permission-based visibility for the shell (BIO-FND-007-AC03).
 *
 * This module knows nothing about BIOLOG features — no card, credential,
 * biometric or tenant rules. It filters an arbitrary list of navigation
 * entries against an arbitrary set of granted permission strings. Owning
 * stories (identity/RBAC) supply the real permission set and real nav
 * entries later; the shell only provides the mechanism (BIO-FND-007-AC04).
 */

export type Permission = string;

export interface NavItem {
  id: string;
  label: string;
  href: string;
  /** Omitted or empty means visible to every authenticated user. */
  requiredPermission?: Permission;
  /** Sidebar section header this item renders under (BIO-UX-002-AC01). Omitted = ungrouped. */
  group?: string;
  /**
   * Omitted means always visible regardless of feature flags. When set, the
   * item also requires this key to be enabled in the current session's
   * feature-flag map (BIO-UX-011-AC04) — see `session-context.tsx`'s
   * `FeatureFlagKey` and `filterNavByFeatureFlags` below. Still just a
   * string here: this module knows nothing about what the flag controls
   * (BIO-FND-007-AC04's "no feature knowledge" still holds).
   */
  requiredFeatureFlag?: string;
}

/** True when `granted` satisfies `item`'s visibility requirement. */
export function isNavItemVisible(item: NavItem, granted: ReadonlySet<Permission>): boolean {
  if (!item.requiredPermission) return true;
  return granted.has(item.requiredPermission);
}

/** Filter `items` down to the ones `granted` may see, preserving order. */
export function filterNavByPermission(
  items: readonly NavItem[],
  granted: ReadonlySet<Permission> | readonly Permission[],
): NavItem[] {
  const grantedSet = granted instanceof Set ? granted : new Set(granted);
  return items.filter((item) => isNavItemVisible(item, grantedSet));
}

/**
 * Filter `items` down to the ones whose `requiredFeatureFlag` (if any) is
 * enabled in `flags` (BIO-UX-011-AC04). An item with no `requiredFeatureFlag`
 * always passes. Composed with `filterNavByPermission` in `app-shell.tsx` —
 * an item needs both to render.
 */
export function filterNavByFeatureFlags(
  items: readonly NavItem[],
  flags: Readonly<Record<string, boolean>>,
): NavItem[] {
  return items.filter((item) => !item.requiredFeatureFlag || flags[item.requiredFeatureFlag] === true);
}

export interface NavItemGroup {
  /** `undefined` for the leading, ungrouped items (e.g. Dashboard). */
  group: string | undefined;
  items: NavItem[];
}

/**
 * Partition an already-permission-filtered item list into its `group`
 * sections, in first-seen order (BIO-UX-002-AC01). Purely structural — still
 * no feature knowledge (BIO-UX-002-AC05).
 */
export function groupNavItems(items: readonly NavItem[]): NavItemGroup[] {
  const groups: NavItemGroup[] = [];
  const indexByGroup = new Map<string | undefined, number>();
  for (const item of items) {
    let index = indexByGroup.get(item.group);
    if (index === undefined) {
      index = groups.length;
      indexByGroup.set(item.group, index);
      groups.push({ group: item.group, items: [] });
    }
    groups[index]!.items.push(item);
  }
  return groups;
}
