import type { NavItem } from './permissions';

/**
 * Primary navigation sitemap (BIO-UX-002-AC02).
 *
 * Labels, target paths and grouping only — no business rules. `group` names
 * the section header a link renders under in the sidebar (BIO-UX-002-AC01),
 * matching `reference/ui/INITIAL_PROTOTYPE_REFERENCE.png`'s grouped layout
 * (IDENTITY / ENROLMENT / CREDENTIALS, visible in that screenshot; the
 * later groups below follow the same section-per-domain pattern and
 * `04-FEATURE-SPECS/PRODUCT-EXPERIENCE.md`'s "Required page families" list,
 * in the same order). An item with no `group` (Dashboard) renders above every
 * group, ungrouped. Every `href` below resolves to a real route
 * (BIO-UX-002-AC04) — see `web/app/<route>/page.tsx` for each.
 *
 * Routes are taken from `governance/ui-integration-map.json` wherever it
 * defines one for the owning backend port; `docs/features/bio-ux-002/README.md`
 * "Routes with no ui-integration-map entry" lists and justifies the few that
 * aren't (Alumni, Contractors, NFC/UHF, Mobile Credentials, Administration
 * landing) — this file does not invent a route silently.
 *
 * `requiredPermission` names are still placeholders (BIO-FND-007's own
 * disclaimer continues to apply): chosen to read sensibly per demo role
 * until a real identity/RBAC story defines the actual permission catalog.
 *
 * `requiredFeatureFlag` (BIO-UX-011-AC04) additionally gates the three
 * items whose flag Settings' Feature Flags section already named — the
 * same key must exist in `session-context.tsx`'s `FeatureFlagKey`. A nav
 * item needs both its permission AND its feature flag (if any) to render;
 * see `permissions.ts`'s `filterNavByFeatureFlags`.
 */
export const PRIMARY_NAV_ITEMS: readonly NavItem[] = Object.freeze([
  { id: 'dashboard', label: 'Dashboard', href: '/' },

  { id: 'people', label: 'People', href: '/people', requiredPermission: 'nav:people', group: 'Identity' },
  { id: 'students', label: 'Students', href: '/students', requiredPermission: 'nav:students', group: 'Identity' },
  { id: 'staff', label: 'Staff', href: '/staff', requiredPermission: 'nav:staff', group: 'Identity' },
  { id: 'visitors', label: 'Visitors', href: '/visitors', requiredPermission: 'nav:visitors', group: 'Identity' },
  { id: 'alumni', label: 'Alumni', href: '/alumni', requiredPermission: 'nav:alumni', group: 'Identity' },
  { id: 'contractors', label: 'Contractors', href: '/contractors', requiredPermission: 'nav:contractors', group: 'Identity' },

  { id: 'new-enrolment', label: 'New Enrolment', href: '/enrolment', requiredPermission: 'nav:enrolment', group: 'Enrolment' },
  { id: 'photographs', label: 'Photographs', href: '/photographs', requiredPermission: 'nav:photographs', group: 'Enrolment' },
  { id: 'fingerprints', label: 'Fingerprints', href: '/fingerprints', requiredPermission: 'nav:fingerprints', group: 'Enrolment' },
  { id: 'facial-recognition', label: 'Facial Recognition', href: '/facial-recognition', requiredPermission: 'nav:facial-recognition', group: 'Enrolment' },
  { id: 'duplicate-review', label: 'Duplicate Review', href: '/duplicate-review', requiredPermission: 'nav:duplicate-review', group: 'Enrolment' },

  { id: 'cards', label: 'Cards', href: '/cards', requiredPermission: 'nav:cards', group: 'Credentials' },
  { id: 'credentials', label: 'Credentials', href: '/credentials', requiredPermission: 'nav:credentials', group: 'Credentials' },
  { id: 'issue-card', label: 'Issue Card', href: '/cards/issue', requiredPermission: 'nav:cards', group: 'Credentials' },
  { id: 'replace-card', label: 'Replace Card', href: '/cards/replace', requiredPermission: 'nav:cards', group: 'Credentials' },
  { id: 'card-designer', label: 'Card Designer', href: '/card-designer', requiredPermission: 'nav:card-designer', requiredFeatureFlag: 'cardDesigner', group: 'Credentials' },
  { id: 'nfc-uhf', label: 'NFC / UHF', href: '/nfc-uhf', requiredPermission: 'nav:cards', group: 'Credentials' },
  { id: 'mobile-credentials', label: 'Mobile Credentials', href: '/mobile-credentials', requiredPermission: 'nav:mobile-credentials', requiredFeatureFlag: 'mobileCredentials', group: 'Credentials' },

  { id: 'document-verify', label: 'Document Verify', href: '/document-verify', requiredPermission: 'nav:document-verify', requiredFeatureFlag: 'documentVerify', group: 'Documents' },

  { id: 'access-control', label: 'Access Control', href: '/access-control', requiredPermission: 'nav:access-control', group: 'Access & Operations' },
  { id: 'attendance', label: 'Attendance', href: '/attendance', requiredPermission: 'nav:attendance', group: 'Access & Operations' },
  { id: 'transport', label: 'Transport', href: '/transport', requiredPermission: 'nav:transport', group: 'Access & Operations' },
  { id: 'devices', label: 'Devices', href: '/devices', requiredPermission: 'nav:devices', group: 'Access & Operations' },

  { id: 'reports', label: 'Reports', href: '/reports', requiredPermission: 'nav:reports', group: 'Reports' },
  { id: 'audit', label: 'Audit', href: '/audit', requiredPermission: 'nav:audit', group: 'Reports' },

  { id: 'administration', label: 'Administration', href: '/administration', requiredPermission: 'nav:administration', group: 'Administration' },
  { id: 'settings', label: 'Settings', href: '/settings', requiredPermission: 'nav:settings', group: 'Administration' },
]);
