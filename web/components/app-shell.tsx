'use client';

import Link from 'next/link';
import { useState, type ReactNode } from 'react';
import { BrandMark } from './brand-mark';
import { SkipLink } from './skip-link';
import { ThemeToggle } from './theme-toggle';
import { SearchBar } from './search-bar';
import { UserMenu } from './user-menu';
import { filterNavByFeatureFlags, filterNavByPermission, groupNavItems } from '../lib/permissions';
import { PRIMARY_NAV_ITEMS } from '../lib/nav-items';
import { useSession } from '../context/session-context';

/**
 * BIOLOG application shell: header (brand, search, tenant/role selectors,
 * theme, user menu), permission-filtered grouped primary navigation and a
 * main content region (BIO-UX-002-AC01, AC02, AC05 — extends BIO-FND-007).
 *
 * Contains no feature logic of its own (BIO-UX-002-AC05): it renders
 * `children` for page content and reads nav structure/session shape from
 * `lib/` and `context/`, both owned by this story but replaceable by later
 * stories without changing this component.
 */
export function AppShell({ children }: { children: ReactNode }) {
  const session = useSession();
  const [navOpen, setNavOpen] = useState(false);
  const visibleItems = filterNavByFeatureFlags(
    filterNavByPermission(PRIMARY_NAV_ITEMS, session.permissions),
    session.featureFlags,
  );
  const groups = groupNavItems(visibleItems);

  return (
    <div className="biolog-shell" data-nav-open={navOpen}>
      <SkipLink />
      {/* TD-SHELL-NAVY-INVERSION: nav now precedes header in DOM order,
          matching the sidebar's new full-height column position (left of,
          not below, the header — see .biolog-shell's grid-template-areas).
          Tab order therefore goes sidebar (brand, nav links) -> header
          (search, session selectors, theme, user menu) -> main, matching the
          new left-to-right visual layout. This only changes desktop tab
          order in practice: on mobile the drawer is visibility:hidden (and
          so excluded from the tab order entirely) until opened, exactly as
          before (BIO-FND-007-QA-01, unaffected by this reorder). */}
      <nav
        id="biolog-primary-nav"
        className="biolog-primary-nav"
        aria-label="Primary"
        data-open={navOpen}
      >
        {/* The sidebar's own brand block, shown whenever the nav itself is
            visible (permanently at >= 768px, inside the open drawer on
            mobile) — the header's brand link (needed so mobile users always
            see it, since the drawer is off-canvas by default there) is
            hidden at >= 768px instead, so desktop never shows it twice. */}
        <div className="biolog-nav-brand">
          <Link href="/" className="biolog-brand-link" aria-label="BIOLOG home">
            <BrandMark />
          </Link>
        </div>
        {groups.map(({ group, items }) => (
          <div className="biolog-nav-group" key={group ?? '__ungrouped'}>
            {group && <p className="biolog-nav-group-label">{group}</p>}
            <ul>
              {items.map((item) => (
                <li key={item.id}>
                  <Link href={item.href} onClick={() => setNavOpen(false)}>
                    {item.label}
                  </Link>
                </li>
              ))}
            </ul>
          </div>
        ))}
      </nav>

      <header className="biolog-header">
        <button
          type="button"
          className="biolog-nav-toggle"
          aria-expanded={navOpen}
          aria-controls="biolog-primary-nav"
          onClick={() => setNavOpen((open) => !open)}
        >
          <span aria-hidden="true">&#9776;</span>
          <span className="biolog-visually-hidden">
            {navOpen ? 'Close navigation' : 'Open navigation'}
          </span>
        </button>
        <Link href="/" className="biolog-brand-link" aria-label="BIOLOG home">
          <BrandMark />
        </Link>

        <SearchBar />

        <div className="biolog-session-context">
          <label className="biolog-context-select">
            <span className="biolog-visually-hidden">Tenant</span>
            <span aria-hidden="true" className="biolog-context-select-label">
              Tenant
            </span>
            <select
              value={session.tenant.id}
              onChange={(event) => session.setTenantId(event.target.value)}
            >
              {session.tenants.map((tenant) => (
                <option key={tenant.id} value={tenant.id}>
                  {tenant.name}
                </option>
              ))}
            </select>
          </label>
          <label className="biolog-context-select">
            <span className="biolog-visually-hidden">Role</span>
            <span aria-hidden="true" className="biolog-context-select-label">
              Role
            </span>
            <select value={session.role.id} onChange={(event) => session.setRoleId(event.target.value)}>
              {session.roles.map((role) => (
                <option key={role.id} value={role.id}>
                  {role.name}
                </option>
              ))}
            </select>
          </label>
        </div>

        <ThemeToggle />
        <UserMenu />
      </header>

      <main id="biolog-main" className="biolog-main" tabIndex={-1}>
        {children}
      </main>
    </div>
  );
}
