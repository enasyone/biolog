export type GateAction =
  | { kind: 'button-status'; name: string }
  | { kind: 'button-change'; name: string }
  | { kind: 'first-button-change'; name: string }
  | { kind: 'link'; name: string; destination: string }
  | { kind: 'select-detail'; label: string }
  | { kind: 'select-button-status'; label: string; button: string }
  | { kind: 'fill-button-status'; label: string; value: string; button: string };

export interface ProductExperienceGateEntry {
  id: string;
  href: string;
  heading: string;
  action: GateAction;
  /** Accepted story whose focused browser suite proves the full journey behind this gate action. */
  evidenceSpec: string;
}

/**
 * Complete BIO-UX-003 route inventory with one executable canonical primary action per route.
 * Focused story suites remain the deeper evidence for multi-step/secondary interactions; this
 * gate is the final breadth sweep that ensures no approved route or entry action went dead.
 */
export const PRODUCT_EXPERIENCE_GATE: readonly ProductExperienceGateEntry[] = Object.freeze([
  { id: 'dashboard', href: '/', heading: 'Dashboard', action: { kind: 'button-status', name: 'Refresh dashboard' }, evidenceSpec: 'bio-ux-011-admin-journeys.spec.ts' },
  { id: 'people', href: '/people?as=admin', heading: 'People', action: { kind: 'link', name: 'New Enrolment', destination: '/enrolment' }, evidenceSpec: 'bio-ux-006-identity-journeys.spec.ts' },
  { id: 'students', href: '/students?as=admin', heading: 'Students', action: { kind: 'link', name: 'View profile', destination: '/people/' }, evidenceSpec: 'bio-ux-006-identity-journeys.spec.ts' },
  { id: 'staff', href: '/staff?as=admin', heading: 'Staff', action: { kind: 'link', name: 'View profile', destination: '/people/' }, evidenceSpec: 'bio-ux-006-identity-journeys.spec.ts' },
  { id: 'visitors', href: '/visitors?as=admin', heading: 'Visitors', action: { kind: 'first-button-change', name: 'Check in visitor' }, evidenceSpec: 'bio-ux-010-operational-journeys.spec.ts' },
  { id: 'alumni', href: '/alumni?as=admin', heading: 'Alumni', action: { kind: 'link', name: 'View profile', destination: '/people/' }, evidenceSpec: 'bio-ux-006-identity-journeys.spec.ts' },
  { id: 'contractors', href: '/contractors?as=admin', heading: 'Contractors', action: { kind: 'link', name: 'View profile', destination: '/people/' }, evidenceSpec: 'bio-ux-006-identity-journeys.spec.ts' },
  { id: 'new-enrolment', href: '/enrolment?as=admin', heading: 'New Enrolment', action: { kind: 'button-change', name: 'Next' }, evidenceSpec: 'bio-ux-006-identity-journeys.spec.ts' },
  { id: 'photographs', href: '/photographs?as=admin', heading: 'Photographs', action: { kind: 'button-status', name: 'Simulate primary action' }, evidenceSpec: 'bio-ux-005-adapter-framework.spec.ts' },
  { id: 'fingerprints', href: '/fingerprints?as=admin', heading: 'Fingerprints', action: { kind: 'first-button-change', name: 'Capture' }, evidenceSpec: 'bio-ux-008-biometric-journeys.spec.ts' },
  { id: 'facial-recognition', href: '/facial-recognition?as=admin', heading: 'Facial Recognition', action: { kind: 'first-button-change', name: 'Capture' }, evidenceSpec: 'bio-ux-008-biometric-journeys.spec.ts' },
  { id: 'duplicate-review', href: '/duplicate-review?as=admin', heading: 'Duplicate Review', action: { kind: 'first-button-change', name: 'Keep this record' }, evidenceSpec: 'bio-ux-006-identity-journeys.spec.ts' },
  { id: 'cards', href: '/cards?as=admin', heading: 'Cards', action: { kind: 'link', name: 'Issue Card', destination: '/cards/issue' }, evidenceSpec: 'bio-ux-007-cards.spec.ts' },
  { id: 'credentials', href: '/credentials?as=admin', heading: 'Credentials', action: { kind: 'select-detail', label: 'View credential' }, evidenceSpec: 'bio-ux-007-cards.spec.ts' },
  { id: 'issue-card', href: '/cards/issue?as=admin', heading: 'Issue Card', action: { kind: 'select-button-status', label: 'Person', button: 'Issue card' }, evidenceSpec: 'bio-ux-007-cards.spec.ts' },
  { id: 'replace-card', href: '/cards/replace?as=admin', heading: 'Replace Card', action: { kind: 'select-button-status', label: 'Card to replace', button: 'Replace card' }, evidenceSpec: 'bio-ux-007-cards.spec.ts' },
  { id: 'card-designer', href: '/card-designer?as=admin', heading: 'Card Designer', action: { kind: 'fill-button-status', label: 'Design name', value: 'BIOLOG approval gate (SYNTHETIC)', button: 'Save design' }, evidenceSpec: 'bio-ux-007-cards.spec.ts' },
  { id: 'nfc-uhf', href: '/nfc-uhf?as=admin', heading: 'NFC / UHF', action: { kind: 'select-button-status', label: 'Card', button: 'Encode tag' }, evidenceSpec: 'bio-ux-007-cards.spec.ts' },
  { id: 'mobile-credentials', href: '/mobile-credentials?as=admin', heading: 'Mobile Credentials', action: { kind: 'select-detail', label: 'View status' }, evidenceSpec: 'bio-ux-007-cards.spec.ts' },
  { id: 'document-verify', href: '/document-verify?as=admin', heading: 'Document Verify', action: { kind: 'button-change', name: 'Capture/upload document' }, evidenceSpec: 'bio-ux-009-document-verify.spec.ts' },
  { id: 'access-control', href: '/access-control?as=admin', heading: 'Access Control', action: { kind: 'button-change', name: 'Create policy' }, evidenceSpec: 'bio-ux-010-operational-journeys.spec.ts' },
  { id: 'attendance', href: '/attendance?as=admin', heading: 'Attendance', action: { kind: 'button-change', name: 'View attendance record' }, evidenceSpec: 'bio-ux-010-operational-journeys.spec.ts' },
  { id: 'transport', href: '/transport?as=admin', heading: 'Transport', action: { kind: 'button-change', name: 'View boarding/alighting log' }, evidenceSpec: 'bio-ux-010-operational-journeys.spec.ts' },
  { id: 'devices', href: '/devices?as=admin', heading: 'Devices', action: { kind: 'button-change', name: 'Register device' }, evidenceSpec: 'bio-ux-010-operational-journeys.spec.ts' },
  { id: 'reports', href: '/reports?as=admin', heading: 'Reports', action: { kind: 'button-status', name: 'Run report' }, evidenceSpec: 'bio-ux-011-admin-journeys.spec.ts' },
  { id: 'audit', href: '/audit?as=admin', heading: 'Audit', action: { kind: 'button-status', name: 'View audit trail' }, evidenceSpec: 'bio-ux-011-admin-journeys.spec.ts' },
  { id: 'administration', href: '/administration?as=admin', heading: 'Administration', action: { kind: 'button-status', name: 'Simulate primary action' }, evidenceSpec: 'bio-ux-002-shell.spec.ts' },
  { id: 'administration-tenants', href: '/administration/tenants?as=admin', heading: 'Tenant Administration', action: { kind: 'button-status', name: 'Simulate primary action' }, evidenceSpec: 'bio-ux-002-shell.spec.ts' },
  { id: 'administration-integrations', href: '/administration/integrations?as=admin', heading: 'Integrations', action: { kind: 'button-status', name: 'Simulate primary action' }, evidenceSpec: 'bio-ux-002-shell.spec.ts' },
  { id: 'settings', href: '/settings?as=admin', heading: 'System Settings', action: { kind: 'first-button-change', name: 'Save (SIMULATION)' }, evidenceSpec: 'bio-ux-011-admin-journeys.spec.ts' },
  { id: 'settings-authentication', href: '/settings/authentication?as=admin', heading: 'Authentication Settings', action: { kind: 'button-status', name: 'Simulate primary action' }, evidenceSpec: 'bio-ux-002-shell.spec.ts' },
]);
