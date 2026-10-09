/**
 * Approved viewport combinations (BIO-UX-014-AC02), pinned to the three named
 * breakpoints in `web/design-system/breakpoints.ts` (BIO-UX-001-AC04) rather
 * than invented independently. Originally defined inline in
 * `bio-ux-013-breakpoints.spec.ts`; extracted here so BIO-UX-013's own
 * no-overflow/primary-action sweep and BIO-UX-014's visual-regression
 * baselines test against the exact same combinations, not two lists that can
 * silently drift apart. `bio-ux-013-breakpoints.spec.ts` now imports from
 * here with no change to its own assertions.
 */
export const VIEWPORTS = {
  mobile: { width: 375, height: 812 }, // below bpTablet (768)
  tablet: { width: 768, height: 1024 }, // == bpTablet: permanent sidebar begins here
  desktop: { width: 1280, height: 800 }, // above bpDesktop (1024)
} as const;

export type ViewportName = keyof typeof VIEWPORTS;

/**
 * One representative page per distinct interaction pattern already in the
 * product (BIO-UX-013's own judgement call, reused here): Dashboard
 * (metrics/cards), People (a filterable list/table with row actions),
 * Settings (a dense multi-section form), Document Verify (a multi-step
 * journey with many cards), Cards (a table with per-row actions), Access
 * Control (an operational form + the shared scenario picker),
 * Administration, Reports — one page per `web/lib/nav-items.ts` group
 * (Identity, Credentials, Documents, Access & Operations, Reports,
 * Administration) plus the ungrouped Dashboard home.
 *
 * BIO-UX-014-AC01 requires a representative page from EVERY major module, a
 * stricter bar than BIO-UX-013's own free choice of subset - extending this
 * shared list with the one group BIO-UX-013 didn't need (Enrolment) is
 * strictly additive for BIO-UX-013's own sweep (one more page tested at all
 * three breakpoints, no existing assertion changes), so it's folded in here
 * rather than kept in a second, BIO-UX-014-only list.
 */
export const MAJOR_PAGES: readonly { route: string; primaryAction: string }[] = [
  { route: '/', primaryAction: 'Refresh dashboard' },
  { route: '/people?as=admin', primaryAction: 'New Enrolment' },
  { route: '/enrolment', primaryAction: 'Next' },
  { route: '/settings?as=admin', primaryAction: 'Save (SIMULATION)' },
  { route: '/document-verify', primaryAction: 'Capture/upload document' },
  { route: '/cards', primaryAction: 'Issue Card' },
  { route: '/access-control', primaryAction: 'Create policy' },
  { route: '/administration', primaryAction: 'Simulate primary action' },
  { route: '/reports', primaryAction: 'Run report' },
];
