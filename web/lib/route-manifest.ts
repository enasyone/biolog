/**
 * Route manifest and page-family interaction map (BIO-UX-003).
 *
 * A typed, testable companion to `nav-items.ts`: where that file is the
 * sidebar's own sitemap (label/href/group/permission, rendered), this file
 * is the fuller design record BIO-UX-003-AC01/AC02/AC03 ask for — primary/
 * secondary actions, target backend port and the applicable page-level
 * states — for every route, including the sub-routes reachable only via a
 * placeholder page's `relatedLinks` (BIO-UX-002), not just the primary nav.
 *
 * Source hierarchy for each entry's data: `governance/ui-integration-map.json`
 * for `servicePort` wherever it defines one; `04-FEATURE-SPECS/PRODUCT-EXPERIENCE.md`
 * and the already-approved `BIO-UX-006`..`BIO-UX-011` story ACs (which define
 * what each domain's *real* screens will do) for `primaryActions`/
 * `secondaryActions` — these describe the intended production behaviour a
 * domain story connects, not today's literal "Simulate primary action"
 * placeholder button every route currently renders (BIO-UX-002).
 */
import type { Permission } from './permissions';

/** Backend ports named in `governance/ui-integration-map.json`, plus the two shell-only cases. */
export type ServicePort =
  | 'IdentityService'
  | 'VisitorService'
  | 'CredentialService'
  | 'BiometricService'
  | 'PhotoService'
  | 'DocumentVerifyService'
  | 'AccessService'
  | 'AttendanceService'
  | 'TransportService'
  | 'DeviceService'
  | 'ReportingService'
  | 'TenantService'
  | 'IntegrationService'
  | 'FrontendRuntime'
  /** No `ui-integration-map.json` entry exists yet — see this file's own README note. */
  | 'Unassigned';

/**
 * Page-level interaction states (BIO-UX-003-AC03), matching
 * `PRODUCT-EXPERIENCE.md`'s "Interaction-state contract" naming exactly:
 * "default, loading, empty, success, warning, error, permission-denied and
 * offline/degraded". `default` is implicit (every route has one) and so is
 * not itself listed per entry below.
 */
export type PageState =
  | 'loading'
  | 'empty'
  | 'success'
  | 'warning'
  | 'error'
  | 'permission-denied'
  | 'offline';

/** The full contract every "major page family" supports unless `states` overrides it. */
export const STANDARD_PAGE_STATES: readonly PageState[] = Object.freeze([
  'loading',
  'empty',
  'success',
  'warning',
  'error',
  'permission-denied',
  'offline',
]);

export interface RouteManifestEntry {
  id: string;
  /** The AC01-named page family this route belongs to. */
  pageFamily: string;
  label: string;
  href: string;
  primaryActions: readonly string[];
  secondaryActions: readonly string[];
  /** Matches `NavItem.requiredPermission`; omitted = visible to every authenticated user. */
  requiredPermission?: Permission;
  servicePort: ServicePort;
  /** Defaults to `STANDARD_PAGE_STATES`; override only where a state genuinely doesn't apply. */
  states?: readonly PageState[];
  /** Present only for a route this manifest itself introduces beyond AC01's minimum list. */
  note?: string;
}

export const ROUTE_MANIFEST: readonly RouteManifestEntry[] = Object.freeze([
  {
    id: 'dashboard',
    pageFamily: 'Dashboard',
    label: 'Dashboard',
    href: '/',
    primaryActions: ['View metrics'],
    secondaryActions: ['Jump to module'],
    servicePort: 'ReportingService',
    note:
      "governance/ui-integration-map.json's ReportingService entry lists '/dashboard' as a route distinct from the shell's existing '/' home page. Reconciled here to '/' (the real, working route since BIO-FND-007/BIO-UX-002) rather than creating a duplicate page — flagged for governance to correct the map entry, not decided unilaterally by this frontend story.",
  },
  {
    id: 'people',
    pageFamily: 'People',
    label: 'People',
    href: '/people',
    primaryActions: ['New Enrolment'],
    secondaryActions: ['View profile', 'Suspend (emergency)', 'Filter', 'Export list'],
    requiredPermission: 'nav:people',
    servicePort: 'IdentityService',
  },
  {
    id: 'students',
    pageFamily: 'Students',
    label: 'Students',
    href: '/students',
    primaryActions: ['View profile'],
    secondaryActions: ['Filter', 'Export list'],
    requiredPermission: 'nav:students',
    servicePort: 'IdentityService',
  },
  {
    id: 'staff',
    pageFamily: 'Staff',
    label: 'Staff',
    href: '/staff',
    primaryActions: ['View profile'],
    secondaryActions: ['Filter', 'Export list'],
    requiredPermission: 'nav:staff',
    servicePort: 'IdentityService',
  },
  {
    id: 'visitors',
    pageFamily: 'Visitors',
    label: 'Visitors',
    href: '/visitors',
    primaryActions: ['Check in visitor'],
    secondaryActions: ['Check out visitor', 'View visit history'],
    requiredPermission: 'nav:visitors',
    servicePort: 'VisitorService',
    note:
      'PRODUCT-EXPERIENCE.md separately names "Visitor Management" later in its page-family list, alongside "Visitors" here. AC01 of this story names only "Visitors", and ui-integration-map.json defines exactly one VisitorService route (/visitors) — resolved to one page family, one route, covering both identity-list and check-in/out actions. See docs/features/bio-ux-003/README.md "Visitors vs. Visitor Management" for the full reasoning.',
  },
  {
    id: 'alumni',
    pageFamily: 'Alumni',
    label: 'Alumni',
    href: '/alumni',
    primaryActions: ['View profile'],
    secondaryActions: ['Filter', 'Export list'],
    requiredPermission: 'nav:alumni',
    servicePort: 'Unassigned',
    note:
      'No governance/ui-integration-map.json entry exists for this route (flagged already in BIO-UX-002 evidence). Likely IdentityService eventually; not assumed here since assigning a port is a backend/governance decision, not a frontend one.',
  },
  {
    id: 'contractors',
    pageFamily: 'Contractors',
    label: 'Contractors',
    href: '/contractors',
    primaryActions: ['View profile'],
    secondaryActions: ['Filter', 'Export list'],
    requiredPermission: 'nav:contractors',
    servicePort: 'Unassigned',
    note: 'Same governance gap as Alumni — see that entry\'s note.',
  },
  {
    id: 'new-enrolment',
    pageFamily: 'Enrolment',
    label: 'New Enrolment',
    href: '/enrolment',
    primaryActions: ['Start enrolment'],
    secondaryActions: ['Resume draft', 'Cancel'],
    requiredPermission: 'nav:enrolment',
    servicePort: 'BiometricService',
    note: 'Shared route: BiometricService and PhotoService both own steps of this one enrolment flow.',
  },
  {
    id: 'photographs',
    pageFamily: 'Photos',
    label: 'Photographs',
    href: '/photographs',
    primaryActions: ['Capture photo'],
    secondaryActions: ['Retake', 'Approve quality'],
    requiredPermission: 'nav:photographs',
    servicePort: 'PhotoService',
  },
  {
    id: 'fingerprints',
    pageFamily: 'Fingerprints',
    label: 'Fingerprints',
    href: '/fingerprints',
    primaryActions: ['Capture tenprint'],
    secondaryActions: ['Retry finger', 'Flag exception'],
    requiredPermission: 'nav:fingerprints',
    servicePort: 'BiometricService',
  },
  {
    id: 'facial-recognition',
    pageFamily: 'Face',
    label: 'Facial Recognition',
    href: '/facial-recognition',
    primaryActions: ['Capture face'],
    secondaryActions: ['Run 1:1 verification', 'Run 1:N identification'],
    requiredPermission: 'nav:facial-recognition',
    servicePort: 'BiometricService',
  },
  {
    id: 'duplicate-review',
    pageFamily: 'Duplicate Review',
    label: 'Duplicate Review',
    href: '/duplicate-review',
    primaryActions: ['Resolve match'],
    secondaryActions: ['Dismiss', 'View comparison'],
    requiredPermission: 'nav:duplicate-review',
    servicePort: 'IdentityService',
  },
  {
    id: 'cards',
    pageFamily: 'Cards',
    label: 'Cards',
    href: '/cards',
    primaryActions: ['Issue card'],
    secondaryActions: ['View card', 'Revoke card'],
    requiredPermission: 'nav:cards',
    servicePort: 'CredentialService',
  },
  {
    id: 'credentials',
    pageFamily: 'Cards',
    label: 'Credentials',
    href: '/credentials',
    primaryActions: ['View credential'],
    secondaryActions: ['Revoke credential'],
    requiredPermission: 'nav:credentials',
    servicePort: 'CredentialService',
    note: 'Not separately named in AC01 (which lists only "Cards" and "Issue/Replace Card"); kept because ui-integration-map.json maps CredentialService to /credentials as its own route, distinct from /cards.',
  },
  {
    id: 'issue-card',
    pageFamily: 'Issue/Replace Card',
    label: 'Issue Card',
    href: '/cards/issue',
    primaryActions: ['Issue card'],
    secondaryActions: ['Cancel'],
    requiredPermission: 'nav:cards',
    servicePort: 'CredentialService',
  },
  {
    id: 'replace-card',
    pageFamily: 'Issue/Replace Card',
    label: 'Replace Card',
    href: '/cards/replace',
    primaryActions: ['Replace card'],
    secondaryActions: ['Mark lost', 'Cancel'],
    requiredPermission: 'nav:cards',
    servicePort: 'CredentialService',
  },
  {
    id: 'card-designer',
    pageFamily: 'Cards',
    label: 'Card Designer',
    href: '/card-designer',
    primaryActions: ['Save design'],
    secondaryActions: ['Print preview'],
    requiredPermission: 'nav:card-designer',
    servicePort: 'CredentialService',
    note: 'Not separately named in AC01; kept per BIO-UX-002\'s own documented reasoning (a real, tested page already exists here from BIO-CRD-002/003/004).',
  },
  {
    id: 'nfc-uhf',
    pageFamily: 'NFC/UHF',
    label: 'NFC / UHF',
    href: '/nfc-uhf',
    primaryActions: ['Encode tag'],
    secondaryActions: ['Verify read'],
    requiredPermission: 'nav:cards',
    servicePort: 'CredentialService',
    note: 'Resolved by BIO-UX-007-AC01/AC06 ("stable service ports ... for later card lifecycle, production and encoding integrations"): NFC/UHF encoding is this same CredentialService domain, not a separate one. Previously "Unassigned" (BIO-UX-002, "not assumed here") — see docs/features/bio-ux-007/README.md.',
  },
  {
    id: 'mobile-credentials',
    pageFamily: 'Mobile Credentials',
    label: 'Mobile Credentials',
    href: '/mobile-credentials',
    primaryActions: ['View status'],
    secondaryActions: ['Revoke mobile credential'],
    requiredPermission: 'nav:mobile-credentials',
    servicePort: 'CredentialService',
    note: 'Resolved by BIO-UX-007-AC01: "View status" reads the same tenant-scoped credential registry directly (no port call, same as every other read-only view in this feature); "Revoke mobile credential" is a real CredentialService action. MobileService\'s own only mapped route stays "Flutter application" (provisioning) — this web page remains status/revoke only, per BIO-UX-002\'s own documented boundary, just no longer Unassigned. See docs/features/bio-ux-007/README.md.',
  },
  {
    id: 'document-verify',
    pageFamily: 'Document Verify',
    label: 'Document Verify',
    href: '/document-verify',
    primaryActions: ['Capture/upload document'],
    secondaryActions: ['Retry OCR', 'Route to human review'],
    requiredPermission: 'nav:document-verify',
    servicePort: 'DocumentVerifyService',
  },
  {
    id: 'access-control',
    pageFamily: 'Access',
    label: 'Access Control',
    href: '/access-control',
    primaryActions: ['Create policy'],
    secondaryActions: ['View access events'],
    requiredPermission: 'nav:access-control',
    servicePort: 'AccessService',
  },
  {
    id: 'attendance',
    pageFamily: 'Attendance',
    label: 'Attendance',
    href: '/attendance',
    primaryActions: ['View attendance record'],
    secondaryActions: ['Flag exception'],
    requiredPermission: 'nav:attendance',
    servicePort: 'AttendanceService',
  },
  {
    id: 'transport',
    pageFamily: 'Transport',
    label: 'Transport',
    href: '/transport',
    primaryActions: ['View boarding/alighting log'],
    secondaryActions: ['Assign route'],
    requiredPermission: 'nav:transport',
    servicePort: 'TransportService',
  },
  {
    id: 'devices',
    pageFamily: 'Devices',
    label: 'Devices',
    href: '/devices',
    primaryActions: ['Register device'],
    secondaryActions: ['View connectivity status'],
    requiredPermission: 'nav:devices',
    servicePort: 'DeviceService',
  },
  {
    id: 'reports',
    pageFamily: 'Reports',
    label: 'Reports',
    href: '/reports',
    primaryActions: ['Run report'],
    secondaryActions: ['Export', 'Schedule'],
    requiredPermission: 'nav:reports',
    servicePort: 'ReportingService',
  },
  {
    id: 'audit',
    pageFamily: 'Audit',
    label: 'Audit',
    href: '/audit',
    primaryActions: ['View audit trail'],
    secondaryActions: ['Export audit log'],
    requiredPermission: 'nav:audit',
    servicePort: 'ReportingService',
  },
  {
    id: 'administration',
    pageFamily: 'Administration',
    label: 'Administration',
    href: '/administration',
    primaryActions: ['Manage tenants'],
    secondaryActions: ['Manage integrations'],
    requiredPermission: 'nav:administration',
    servicePort: 'TenantService',
    states: ['success', 'permission-denied'],
    note: 'A landing/navigation page (links to Tenants and Integrations) — loading/empty/error/offline don\'t apply to a static links page, so states is narrowed from STANDARD_PAGE_STATES.',
  },
  {
    id: 'administration-tenants',
    pageFamily: 'Administration',
    label: 'Tenant Administration',
    href: '/administration/tenants',
    primaryActions: ['Add tenant'],
    secondaryActions: ['Edit tenant'],
    requiredPermission: 'nav:administration',
    servicePort: 'TenantService',
  },
  {
    id: 'administration-integrations',
    pageFamily: 'Administration',
    label: 'Integrations',
    href: '/administration/integrations',
    primaryActions: ['Connect integration'],
    secondaryActions: ['Test connection'],
    requiredPermission: 'nav:administration',
    servicePort: 'IntegrationService',
  },
  {
    id: 'settings',
    pageFamily: 'Settings',
    label: 'System Settings',
    href: '/settings',
    primaryActions: ['Save settings'],
    secondaryActions: ['Reset to defaults'],
    requiredPermission: 'nav:settings',
    servicePort: 'TenantService',
    states: ['success', 'permission-denied', 'error'],
    note: 'A form-driven settings page — loading/empty/offline are not meaningful states for a settings form, so states is narrowed.',
  },
  {
    id: 'settings-authentication',
    pageFamily: 'Settings',
    label: 'Authentication Settings',
    href: '/settings/authentication',
    primaryActions: ['Save authentication policy'],
    secondaryActions: ['Test sign-in'],
    requiredPermission: 'nav:settings',
    servicePort: 'IntegrationService',
    states: ['success', 'permission-denied', 'error'],
    note: 'A form-driven settings page, same reasoning as its parent Settings entry — loading/empty/offline are not meaningful here.',
  },
]);

/** Every route this manifest lists, for quick membership checks. */
export const ROUTE_MANIFEST_HREFS: ReadonlySet<string> = new Set(
  ROUTE_MANIFEST.map((entry) => entry.href),
);
