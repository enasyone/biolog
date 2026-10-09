/**
 * Record shapes for the BIOLOG synthetic data/scenario system (BIO-UX-004-AC01).
 *
 * Every record type here is deliberately shallow and structural, never a
 * real biometric template or document image (BIO-UX-004-AC04): a photo is a
 * quality score plus a deterministic placeholder colour, not image bytes; a
 * fingerprint/face capture is represented only through that same photo
 * record's quality fields, never a template blob. A future story that adds
 * real capture storage must not reuse these fields as if they already held
 * real biometric data — see `docs/features/bio-ux-004/README.md`'s "Scope
 * boundaries" section.
 */

export type SyntheticId = string;

export interface Synthetic {
  /** Always `true` — machine-checkable companion to the `(SYNTHETIC)` label marker (AC04, AC05). */
  isSynthetic: true;
}

export interface SyntheticTenant extends Synthetic {
  id: SyntheticId;
  /** Includes the `(SYNTHETIC)` marker. */
  name: string;
}

export type PersonCategory = 'student' | 'staff' | 'alumni' | 'contractor';

/**
 * Identity lifecycle state (BIO-UX-006-AC03). Added by BIO-UX-006, the first
 * domain story to give a person a lifecycle beyond bare existence — every
 * generated person starts `active`; `suspended` is reached only through an
 * in-session mutation (the "Suspend (emergency)" primary action), never part
 * of the deterministic baseline, so `generateDataset` stays byte-for-byte
 * reproducible per BIO-UX-004-AC01/AC03.
 */
export type PersonLifecycleStatus = 'active' | 'suspended';

export interface SyntheticPerson extends Synthetic {
  id: SyntheticId;
  tenantId: SyntheticId;
  category: PersonCategory;
  firstName: string;
  lastName: string;
  /** `"First Last (SYNTHETIC)"`. */
  displayName: string;
  dateOfBirth: string;
  /** Present only when the `duplicate-identity` scenario links this person to another (AC02). */
  duplicateGroupId?: string;
  /** BIO-UX-006-AC03. Always `'active'` in every generated dataset — see this field's own type doc. */
  lifecycleStatus: PersonLifecycleStatus;
}

/**
 * `recovered` (BIO-UX-007-AC03) is deliberately its own status, not a silent
 * reset back to `active`: a card that was reported lost and later found
 * keeps that history visible in the registry, mirroring the already-shipped
 * backend's own `CardLockState.Recovered` (BIO-CRD-006) rather than losing
 * the fact it was ever lost. A recovered card is usable again in every way
 * `active` is.
 */
export type CredentialStatus = 'active' | 'lost' | 'recovered' | 'revoked' | 'replaced';

export interface SyntheticCredential extends Synthetic {
  id: SyntheticId;
  tenantId: SyntheticId;
  personId: SyntheticId;
  cardNumber: string;
  status: CredentialStatus;
  issuedAt: string;
  /** Reason recorded for the current `lost`/`revoked` status, if any (BIO-UX-007-AC04). */
  statusReason?: string;
  /** When `status === 'replaced'`, the id of the credential issued in its place (BIO-UX-007-AC03/AC04). */
  replacedByCardId?: SyntheticId;
  /** The id of the credential this one replaced, if it was itself issued as a replacement. */
  replacesCardId?: SyntheticId;
}

export interface SyntheticPhoto extends Synthetic {
  id: SyntheticId;
  tenantId: SyntheticId;
  personId: SyntheticId;
  /** 0-100. Never a real image or biometric template — see this file's header note. */
  qualityScore: number;
  belowQualityThreshold: boolean;
  /** Deterministic placeholder swatch colour, standing in for a captured photo. */
  placeholderColor: string;
}

/** `'degraded'` (BIO-UX-010-AC02): device is reachable but reporting a poor-quality/intermittent link, distinct from fully `'offline'`. */
export type DeviceConnectivity = 'online' | 'offline' | 'degraded';

export interface SyntheticDevice extends Synthetic {
  id: SyntheticId;
  tenantId: SyntheticId;
  name: string;
  connectivity: DeviceConnectivity;
}

/**
 * The 4 vendors BIO-DEV-003's adapter layer actually certifies against
 * (`api/features/bio-dev-003/Biolog.Features.DeviceAdapters/Adapters/*.cs`,
 * matched case-insensitively on this exact spelling, including `OrbitID`'s
 * capital D) — used as the device-registration form's Vendor options
 * (BIO-UX-010-DEVICE-FORM-FOLLOWUP) so a registered device's vendor lines up
 * with real certification/adapter behaviour instead of an invented name.
 */
export type DeviceVendor = 'Acme' | 'NovaVision' | 'OrbitID' | 'PrintWorks';

export const DEVICE_VENDORS: readonly DeviceVendor[] = Object.freeze([
  'Acme',
  'NovaVision',
  'OrbitID',
  'PrintWorks',
]);

/** Synthetic site options for device registration (BIO-UX-010-DEVICE-FORM-FOLLOWUP) — no real site/location concept exists elsewhere in the app yet. */
export type DeviceSite = 'HQ' | 'Warehouse A' | 'Remote Office' | 'Distribution Center';

export const DEVICE_SITES: readonly DeviceSite[] = Object.freeze([
  'HQ',
  'Warehouse A',
  'Remote Office',
  'Distribution Center',
]);

export type AccessDecision = 'granted' | 'denied';

/** Reason codes a denied/policy-governed access decision can carry (BIO-UX-010-AC03). */
export type AccessReasonCode =
  | 'after-hours'
  | 'restricted-zone'
  | 'assurance-level-insufficient'
  | 'visitor-escort-required'
  | 'device-degraded';

export const ACCESS_REASON_CODES: readonly AccessReasonCode[] = Object.freeze([
  'after-hours',
  'restricted-zone',
  'assurance-level-insufficient',
  'visitor-escort-required',
  'device-degraded',
]);

/** Assurance levels a policy-builder control can require (BIO-UX-010-AC03). */
export type AssuranceLevel = 'card' | 'card-pin' | 'biometric' | 'card-biometric';

export const ASSURANCE_LEVELS: readonly AssuranceLevel[] = Object.freeze([
  'card',
  'card-pin',
  'biometric',
  'card-biometric',
]);

export interface SyntheticAccessEvent extends Synthetic {
  id: SyntheticId;
  tenantId: SyntheticId;
  personId: SyntheticId;
  deviceId: SyntheticId;
  decision: AccessDecision;
  occurredAt: string;
  /** Present only on a `denied` decision (BIO-UX-010-AC03) — why the policy refused entry. */
  reasonCode?: AccessReasonCode;
}

export type AttendanceStatus = 'present' | 'absent' | 'late';

export interface SyntheticAttendanceRecord extends Synthetic {
  id: SyntheticId;
  tenantId: SyntheticId;
  personId: SyntheticId;
  date: string;
  status: AttendanceStatus;
}

export type VisitorStatus = 'preregistered' | 'checked-in' | 'checked-out';

export interface SyntheticVisitor extends Synthetic {
  id: SyntheticId;
  tenantId: SyntheticId;
  /** Includes the `(SYNTHETIC)` marker. */
  fullName: string;
  hostPersonId: SyntheticId;
  status: VisitorStatus;
}

export type TransportEventType = 'boarding' | 'alighting';

export interface SyntheticTransportEvent extends Synthetic {
  id: SyntheticId;
  tenantId: SyntheticId;
  personId: SyntheticId;
  routeName: string;
  eventType: TransportEventType;
  occurredAt: string;
}

export type DocumentType = 'birth-certificate' | 'national-id' | 'admission-letter';
export type DocumentMatchResult = 'match' | 'mismatch' | 'pending';

export interface SyntheticDocument extends Synthetic {
  id: SyntheticId;
  tenantId: SyntheticId;
  personId: SyntheticId;
  documentType: DocumentType;
  matchResult: DocumentMatchResult;
}

/**
 * Scenario presets named verbatim in BIO-UX-004-AC02, plus three more
 * (BIO-UX-010-AC02) added for the operational journeys' own scenario
 * selector: `'degraded-device'`, `'denied-access'`, `'notification'`. Kept in
 * this one shared union (rather than a parallel per-story type) so every
 * scenario — old or new — flows through the same `generateDataset`/
 * `SyntheticDataStore` machinery BIO-UX-004 already built and BIO-UX-005's
 * mock adapters already assume.
 */
export type ScenarioId =
  | 'normal'
  | 'empty'
  | 'error'
  | 'offline'
  | 'low-quality-biometric'
  | 'duplicate-identity'
  | 'lost-card'
  | 'document-mismatch'
  | 'degraded-device'
  | 'denied-access'
  | 'notification';

export const SCENARIO_IDS: readonly ScenarioId[] = Object.freeze([
  'normal',
  'empty',
  'error',
  'offline',
  'low-quality-biometric',
  'duplicate-identity',
  'lost-card',
  'document-mismatch',
  'degraded-device',
  'denied-access',
  'notification',
]);

/**
 * The five scenarios BIO-UX-010-AC02 names for the operational journeys
 * ("online, offline, degraded-device, denied-access and notification") —
 * `'normal'` stands in for "online". A narrower, ordered subset of
 * `SCENARIO_IDS` for that story's own scenario-picker control, so its five
 * pages don't also surface BIO-UX-004 scenarios (`low-quality-biometric`,
 * `duplicate-identity`, ...) that describe identity/credential domains, not
 * access/attendance/visitors/transport/devices.
 */
export const OPERATIONAL_SCENARIO_IDS: readonly ScenarioId[] = Object.freeze([
  'normal',
  'offline',
  'degraded-device',
  'denied-access',
  'notification',
]);

/**
 * Non-record-shaped scenario effects a future mock adapter (BIO-UX-005) can
 * read to decide whether to simulate a failed or degraded call, rather than
 * needing its own scenario-interpretation logic.
 */
export interface ScenarioFlags {
  simulateError: boolean;
  simulateOffline: boolean;
  /** Set by the `notification` scenario (BIO-UX-010-AC02) — see `notifications.ts`. */
  simulateNotification: boolean;
}

export type NotificationSeverity = 'info' | 'warning' | 'critical';

/** A derived, display-ready alert (BIO-UX-010-AC02) — never part of the generated dataset itself, see `notifications.ts`. */
export interface SyntheticNotification {
  id: string;
  severity: NotificationSeverity;
  message: string;
}

export interface SyntheticDataset {
  seed: number;
  scenario: ScenarioId;
  flags: ScenarioFlags;
  tenants: readonly SyntheticTenant[];
  people: readonly SyntheticPerson[];
  credentials: readonly SyntheticCredential[];
  photos: readonly SyntheticPhoto[];
  devices: readonly SyntheticDevice[];
  accessEvents: readonly SyntheticAccessEvent[];
  attendanceRecords: readonly SyntheticAttendanceRecord[];
  visitors: readonly SyntheticVisitor[];
  transportEvents: readonly SyntheticTransportEvent[];
  documents: readonly SyntheticDocument[];
}
