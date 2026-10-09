'use client';

import type { ReactNode } from 'react';

/**
 * Small shared page-state fragments for BIO-UX-006's own screens, matching
 * `route-manifest.ts`'s `STANDARD_PAGE_STATES` naming
 * (loading/empty/error/permission-denied/offline) — feature-local, not a
 * shared-shell component (`PlaceholderPage` itself has no per-permission
 * gate, so this is additive, not a replacement for shell behaviour).
 */
export function LoadingState({ label = 'Loading…' }: { label?: string }) {
  return (
    <p role="status" className="biolog-identity-status biolog-identity-status--loading">
      {label}
    </p>
  );
}

export function EmptyState({ children }: { children: ReactNode }) {
  return <p className="biolog-identity-status biolog-identity-status--empty">{children}</p>;
}

export function ErrorState({ children }: { children: ReactNode }) {
  return (
    <p role="alert" className="biolog-identity-status biolog-identity-status--error">
      {children}
    </p>
  );
}

export function OfflineState({ children }: { children: ReactNode }) {
  return (
    <p role="alert" className="biolog-identity-status biolog-identity-status--offline">
      {children}
    </p>
  );
}

export function PermissionDeniedState({ children }: { children: ReactNode }) {
  return (
    <section className="biolog-card">
      <h2>Permission required</h2>
      <p role="alert" className="biolog-identity-status biolog-identity-status--error">
        {children}
      </p>
    </section>
  );
}
