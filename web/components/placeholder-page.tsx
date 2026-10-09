'use client';

import Link from 'next/link';
import { useState } from 'react';
import type { PrimaryActionResult } from '../lib/module-port';
import { MODULE_PORTS } from '../lib/module-registry';
import type { ServicePort } from '../lib/route-manifest';

/**
 * Deliberate page shell for a route whose domain story hasn't shipped a real
 * screen yet (BIO-UX-002-AC04: "every route renders a deliberate page shell
 * rather than a blank/dead route"). Matches
 * `reference/ui/INITIAL_PROTOTYPE_REFERENCE.png`'s System Settings
 * placeholder: a title, a description, a primary-action card and a
 * supporting info card.
 *
 * Generic on purpose (BIO-UX-002-AC05: no feature-specific business logic in
 * shell-owned code) — every prop beyond `module` is a plain string/string[].
 * `module` (BIO-UX-005-AC01) is the one piece of real wiring: the primary
 * action resolves that module's port from `MODULE_PORTS` and calls
 * `port.primaryAction()` rather than flipping local component state, so
 * every one of the 31 routes using this component is a real, working
 * demonstration of the mock/live adapter framework, not just a static
 * mockup. The owning domain story replaces the *page* that renders this
 * with real content when it ships — it does not need to touch this
 * component to do so (BIO-UX-005-AC04).
 *
 * `module` is a plain string, not the resolved `ModulePort` object, because
 * a `page.tsx` is a Server Component and `ModulePort.primaryAction` is a
 * function — React Server Components cannot serialise a function as a prop
 * crossing into this Client Component. Resolving from `MODULE_PORTS` happens
 * here, entirely client-side.
 */
export interface PlaceholderRelatedLink {
  label: string;
  href: string;
}

export interface PlaceholderPageProps {
  title: string;
  description: string;
  /** Which module's port this page's primary action calls (BIO-UX-005-AC01). */
  module: ServicePort;
  /** Which of that module's ports, for the rare module with more than one (default: `'primaryAction'`). */
  portKey?: string;
  primaryActionLabel?: string;
  notes?: readonly string[];
  /** Sub-routes reachable from this page but not in the primary sidebar (e.g. Cards → Issue Card). */
  relatedLinks?: readonly PlaceholderRelatedLink[];
}

type ActionState = { status: 'idle' } | { status: 'loading' } | ({ status: 'done' } & PrimaryActionResult);

export function PlaceholderPage({
  title,
  description,
  module,
  portKey = 'primaryAction',
  primaryActionLabel = 'Simulate primary action',
  relatedLinks = [],
  notes = [
    'Simple default view with progressive disclosure',
    'Tenant-scoped data and permissions',
    'Security-sensitive actions audited',
    'Offline-safe behaviour where relevant',
    'Clear status and actionable errors',
  ],
}: PlaceholderPageProps) {
  const [action, setAction] = useState<ActionState>({ status: 'idle' });

  async function runPrimaryAction() {
    setAction({ status: 'loading' });
    const port = MODULE_PORTS[module][portKey];
    if (!port) {
      throw new Error(`No port "${portKey}" registered for module "${module}".`);
    }
    const result = await port.primaryAction();
    setAction({ status: 'done', ...result });
  }

  return (
    <div className="biolog-placeholder-page">
      <h1>{title}</h1>
      <p className="biolog-placeholder-description">{description}</p>
      <div className="biolog-placeholder-cards">
        <section className="biolog-card biolog-placeholder-primary">
          <h2>{title}</h2>
          <p>
            This page is a deliberate shell (SIMULATION). The domain story that owns this area
            connects it to a live or mock service port without changing this route.
          </p>
          <button
            type="button"
            className="biolog-button-primary"
            onClick={runPrimaryAction}
            disabled={action.status === 'loading'}
          >
            {action.status === 'loading' ? 'Working…' : primaryActionLabel}
          </button>
          {action.status === 'loading' && (
            <p role="status" className="biolog-placeholder-status biolog-placeholder-status--loading">
              Loading…
            </p>
          )}
          {action.status === 'done' && (
            <p
              role="status"
              className={`biolog-placeholder-status biolog-placeholder-status--${action.outcome}`}
            >
              {action.message}
            </p>
          )}
          {relatedLinks.length > 0 && (
            <nav aria-label={`${title} related pages`} className="biolog-placeholder-related">
              <ul>
                {relatedLinks.map((link) => (
                  <li key={link.href}>
                    <Link href={link.href}>{link.label}</Link>
                  </li>
                ))}
              </ul>
            </nav>
          )}
        </section>
        <aside className="biolog-card biolog-placeholder-notes">
          <h2>Design principles</h2>
          <ul>
            {notes.map((note) => (
              <li key={note}>{note}</li>
            ))}
          </ul>
        </aside>
      </div>
    </div>
  );
}
