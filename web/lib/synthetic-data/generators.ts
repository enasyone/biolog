/**
 * Deterministic generators for the `normal` baseline dataset (BIO-UX-004-AC01).
 *
 * Every generator is a pure function of an already-seeded `Rng` plus the
 * tenant it is generating for — no generator reads global/module state or the
 * current time, so the same seed always reproduces the same dataset
 * byte-for-byte (verified by `bio-ux-004-generators.test.ts`). Scenario
 * transforms (`scenarios.ts`) are applied afterwards, on top of this one
 * `normal` dataset, rather than each generator knowing about scenarios.
 */
import { createSeededRng, rngBool, rngInt, rngPick, type Rng } from './rng';
import {
  DEVICE_NAME_TEMPLATES,
  DOCUMENT_TYPES,
  FIRST_NAMES,
  LAST_NAMES,
  SYNTHETIC_TENANTS,
  TRANSPORT_ROUTE_NAMES,
  withMarker,
} from './names';
import type {
  DocumentType,
  PersonCategory,
  ScenarioFlags,
  SyntheticAccessEvent,
  SyntheticAttendanceRecord,
  SyntheticCredential,
  SyntheticDataset,
  SyntheticDevice,
  SyntheticDocument,
  SyntheticId,
  SyntheticPerson,
  SyntheticPhoto,
  SyntheticTenant,
  SyntheticTransportEvent,
  SyntheticVisitor,
} from './types';

const PEOPLE_PER_TENANT = 8;
const DEVICES_PER_TENANT = 3;
const VISITORS_PER_TENANT = 2;
const TRANSPORT_EVENTS_PER_TENANT = 2;
const DOCUMENTS_PER_TENANT = 2;

const PERSON_CATEGORIES: readonly PersonCategory[] = ['student', 'staff', 'alumni', 'contractor'];

function pad(n: number, width = 3): string {
  return String(n).padStart(width, '0');
}

export function generateTenants(): readonly SyntheticTenant[] {
  return SYNTHETIC_TENANTS.map((t) => Object.freeze({ id: t.id, name: t.name, isSynthetic: true as const }));
}

export function generatePeople(rng: Rng, tenantId: SyntheticId): SyntheticPerson[] {
  const people: SyntheticPerson[] = [];
  for (let i = 0; i < PEOPLE_PER_TENANT; i++) {
    // The first three indices deliberately reuse FIRST_NAMES/LAST_NAMES[0..2]
    // so tenant `alpha`'s people include the exact demo identities already
    // shipped in session-context.tsx (Amara Adeleke, Kelechi Okafor, Ovie
    // Eferakeya) — see bio-ux-004-tenant-parity.test.ts.
    const firstName = i < 3 ? FIRST_NAMES[i]! : rngPick(rng, FIRST_NAMES);
    const lastName = i < 3 ? LAST_NAMES[i]! : rngPick(rng, LAST_NAMES);
    const category = PERSON_CATEGORIES[i % PERSON_CATEGORIES.length]!;
    const year = rngInt(rng, 1985, 2010);
    const month = rngInt(rng, 1, 12);
    const day = rngInt(rng, 1, 28);
    people.push(
      Object.freeze({
        id: `${tenantId}-person-${pad(i)}`,
        tenantId,
        category,
        firstName,
        lastName,
        displayName: withMarker(`${firstName} ${lastName}`),
        dateOfBirth: `${year}-${pad(month, 2)}-${pad(day, 2)}`,
        lifecycleStatus: 'active' as const,
        isSynthetic: true as const,
      }),
    );
  }
  return people;
}

/**
 * The last person per tenant deliberately starts with no credential at all
 * (BIO-UX-007-AC01): a fresh 'normal' dataset where every single person
 * already holds an active card leaves the Issue Card screen with nothing to
 * ever demonstrate, which is not a realistic card-centre baseline anyway (a
 * new hire/student always exists). `rngInt` is still called for every
 * person, including the skipped one, so this does not shift how many times
 * `rng` is drawn from and therefore does not change any other generator's
 * own deterministic output for this or any later tenant.
 */
export function generateCredentials(
  rng: Rng,
  tenantId: SyntheticId,
  people: readonly SyntheticPerson[],
): SyntheticCredential[] {
  const credentials: SyntheticCredential[] = [];
  people.forEach((person, i) => {
    const cardNumber = `BIO-${tenantId.toUpperCase()}-${pad(rngInt(rng, 0, 9999), 4)}`;
    if (i === people.length - 1) return;
    credentials.push(
      Object.freeze({
        id: `${tenantId}-credential-${pad(i)}`,
        tenantId,
        personId: person.id,
        cardNumber,
        status: 'active' as const,
        issuedAt: '2026-01-15T09:00:00.000Z',
        isSynthetic: true as const,
      }),
    );
  });
  return credentials;
}

export function generatePhotos(
  rng: Rng,
  tenantId: SyntheticId,
  people: readonly SyntheticPerson[],
): SyntheticPhoto[] {
  return people.map((person, i) => {
    const quality = rngInt(rng, 80, 99);
    const hue = rngInt(rng, 0, 359);
    return Object.freeze({
      id: `${tenantId}-photo-${pad(i)}`,
      tenantId,
      personId: person.id,
      qualityScore: quality,
      belowQualityThreshold: false,
      placeholderColor: `hsl(${hue}, 45%, 55%)`,
      isSynthetic: true as const,
    });
  });
}

export function generateDevices(rng: Rng, tenantId: SyntheticId): SyntheticDevice[] {
  const devices: SyntheticDevice[] = [];
  for (let i = 0; i < DEVICES_PER_TENANT; i++) {
    devices.push(
      Object.freeze({
        id: `${tenantId}-device-${pad(i)}`,
        tenantId,
        name: withMarker(DEVICE_NAME_TEMPLATES[i % DEVICE_NAME_TEMPLATES.length]!),
        connectivity: 'online' as const,
        isSynthetic: true as const,
      }),
    );
  }
  void rng; // reserved for future device-attribute variation; kept deterministic today
  return devices;
}

export function generateAccessEvents(
  rng: Rng,
  tenantId: SyntheticId,
  people: readonly SyntheticPerson[],
  devices: readonly SyntheticDevice[],
): SyntheticAccessEvent[] {
  const events: SyntheticAccessEvent[] = [];
  let i = 0;
  for (const device of devices) {
    for (let j = 0; j < 2; j++) {
      const person = rngPick(rng, people);
      events.push(
        Object.freeze({
          id: `${tenantId}-access-${pad(i)}`,
          tenantId,
          personId: person.id,
          deviceId: device.id,
          decision: rngBool(rng, 0.85) ? ('granted' as const) : ('denied' as const),
          occurredAt: `2026-09-2${(i % 3) + 1}T08:${pad(rngInt(rng, 0, 59), 2)}:00.000Z`,
          isSynthetic: true as const,
        }),
      );
      i += 1;
    }
  }
  return events;
}

export function generateAttendanceRecords(
  rng: Rng,
  tenantId: SyntheticId,
  people: readonly SyntheticPerson[],
): SyntheticAttendanceRecord[] {
  return people
    .filter((p) => p.category === 'student' || p.category === 'staff')
    .map((person, i) =>
      Object.freeze({
        id: `${tenantId}-attendance-${pad(i)}`,
        tenantId,
        personId: person.id,
        date: '2026-09-23',
        status: rngBool(rng, 0.8) ? ('present' as const) : rngBool(rng, 0.5) ? ('late' as const) : ('absent' as const),
        isSynthetic: true as const,
      }),
    );
}

export function generateVisitors(
  rng: Rng,
  tenantId: SyntheticId,
  people: readonly SyntheticPerson[],
): SyntheticVisitor[] {
  const staff = people.filter((p) => p.category === 'staff');
  const visitors: SyntheticVisitor[] = [];
  for (let i = 0; i < VISITORS_PER_TENANT; i++) {
    const firstName = rngPick(rng, FIRST_NAMES);
    const lastName = rngPick(rng, LAST_NAMES);
    const host = staff.length > 0 ? staff[i % staff.length]! : people[0]!;
    visitors.push(
      Object.freeze({
        id: `${tenantId}-visitor-${pad(i)}`,
        tenantId,
        fullName: withMarker(`${firstName} ${lastName}`),
        hostPersonId: host.id,
        status: i === 0 ? ('checked-in' as const) : ('preregistered' as const),
        isSynthetic: true as const,
      }),
    );
  }
  return visitors;
}

export function generateTransportEvents(rng: Rng, tenantId: SyntheticId, people: readonly SyntheticPerson[]): SyntheticTransportEvent[] {
  const events: SyntheticTransportEvent[] = [];
  for (let i = 0; i < TRANSPORT_EVENTS_PER_TENANT; i++) {
    const person = rngPick(rng, people);
    events.push(
      Object.freeze({
        id: `${tenantId}-transport-${pad(i)}`,
        tenantId,
        personId: person.id,
        routeName: TRANSPORT_ROUTE_NAMES[i % TRANSPORT_ROUTE_NAMES.length]!,
        eventType: i % 2 === 0 ? ('boarding' as const) : ('alighting' as const),
        occurredAt: `2026-09-23T07:${pad(rngInt(rng, 0, 59), 2)}:00.000Z`,
        isSynthetic: true as const,
      }),
    );
  }
  return events;
}

export function generateDocuments(rng: Rng, tenantId: SyntheticId, people: readonly SyntheticPerson[]): SyntheticDocument[] {
  const students = people.filter((p) => p.category === 'student');
  const pool = students.length > 0 ? students : people;
  const documents: SyntheticDocument[] = [];
  for (let i = 0; i < DOCUMENTS_PER_TENANT; i++) {
    const person = pool[i % pool.length]!;
    documents.push(
      Object.freeze({
        id: `${tenantId}-document-${pad(i)}`,
        tenantId,
        personId: person.id,
        documentType: DOCUMENT_TYPES[i % DOCUMENT_TYPES.length] as DocumentType,
        matchResult: 'match' as const,
        isSynthetic: true as const,
      }),
    );
  }
  void rng; // reserved for future document-attribute variation; kept deterministic today
  return documents;
}

export const DEFAULT_FLAGS: ScenarioFlags = Object.freeze({
  simulateError: false,
  simulateOffline: false,
  simulateNotification: false,
});

/** Builds the full, unscenario'd `normal` dataset for the given seed. */
export function buildBaseDataset(seed: number): SyntheticDataset {
  const rng = createSeededRng(seed);
  const tenants = generateTenants();

  const people: SyntheticPerson[] = [];
  const credentials: SyntheticCredential[] = [];
  const photos: SyntheticPhoto[] = [];
  const devices: SyntheticDevice[] = [];
  const accessEvents: SyntheticAccessEvent[] = [];
  const attendanceRecords: SyntheticAttendanceRecord[] = [];
  const visitors: SyntheticVisitor[] = [];
  const transportEvents: SyntheticTransportEvent[] = [];
  const documents: SyntheticDocument[] = [];

  for (const tenant of tenants) {
    const tenantPeople = generatePeople(rng, tenant.id);
    const tenantDevices = generateDevices(rng, tenant.id);
    people.push(...tenantPeople);
    credentials.push(...generateCredentials(rng, tenant.id, tenantPeople));
    photos.push(...generatePhotos(rng, tenant.id, tenantPeople));
    devices.push(...tenantDevices);
    accessEvents.push(...generateAccessEvents(rng, tenant.id, tenantPeople, tenantDevices));
    attendanceRecords.push(...generateAttendanceRecords(rng, tenant.id, tenantPeople));
    visitors.push(...generateVisitors(rng, tenant.id, tenantPeople));
    transportEvents.push(...generateTransportEvents(rng, tenant.id, tenantPeople));
    documents.push(...generateDocuments(rng, tenant.id, tenantPeople));
  }

  return {
    seed,
    scenario: 'normal',
    flags: { ...DEFAULT_FLAGS },
    tenants,
    people: Object.freeze(people),
    credentials: Object.freeze(credentials),
    photos: Object.freeze(photos),
    devices: Object.freeze(devices),
    accessEvents: Object.freeze(accessEvents),
    attendanceRecords: Object.freeze(attendanceRecords),
    visitors: Object.freeze(visitors),
    transportEvents: Object.freeze(transportEvents),
    documents: Object.freeze(documents),
  };
}
