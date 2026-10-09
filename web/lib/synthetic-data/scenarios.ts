/**
 * Scenario transforms (BIO-UX-004-AC02).
 *
 * Each transform is a pure function from the `normal` baseline dataset to a
 * new dataset — no transform re-runs generation or touches the RNG, so a
 * scenario is exactly as deterministic as the baseline it's built from.
 * `applyScenario('normal', base) === base` in effect (a same-shape copy),
 * every other scenario mutates a small, documented, testable slice.
 */
import { withMarker } from './names';
import type { ScenarioId, SyntheticDataset } from './types';

function emptyOf(base: SyntheticDataset): SyntheticDataset {
  return {
    ...base,
    people: [],
    credentials: [],
    photos: [],
    devices: [],
    accessEvents: [],
    attendanceRecords: [],
    visitors: [],
    transportEvents: [],
    documents: [],
  };
}

function errorOf(base: SyntheticDataset): SyntheticDataset {
  return { ...base, flags: { ...base.flags, simulateError: true } };
}

function offlineOf(base: SyntheticDataset): SyntheticDataset {
  return {
    ...base,
    flags: { ...base.flags, simulateOffline: true },
    devices: base.devices.map((d) => ({ ...d, connectivity: 'offline' as const })),
  };
}

function lowQualityBiometricOf(base: SyntheticDataset): SyntheticDataset {
  return {
    ...base,
    photos: base.photos.map((p, i) =>
      i % 3 === 0 ? { ...p, qualityScore: 22, belowQualityThreshold: true } : p,
    ),
  };
}

function duplicateIdentityOf(base: SyntheticDataset): SyntheticDataset {
  const original = base.people[0];
  if (!original) return base;
  const duplicateGroupId = `dup-${original.tenantId}-000`;
  const clone = {
    ...original,
    id: `${original.id}-dup`,
    displayName: withMarker(`${original.firstName} ${original.lastName}`),
    duplicateGroupId,
  };
  const people = base.people.map((p) => (p.id === original.id ? { ...p, duplicateGroupId } : p));
  people.push(clone);
  return { ...base, people };
}

function lostCardOf(base: SyntheticDataset): SyntheticDataset {
  if (base.credentials.length === 0) return base;
  return {
    ...base,
    credentials: base.credentials.map((c, i) => (i === 0 ? { ...c, status: 'lost' as const } : c)),
  };
}

function documentMismatchOf(base: SyntheticDataset): SyntheticDataset {
  if (base.documents.length === 0) return base;
  return {
    ...base,
    documents: base.documents.map((d, i) => (i === 0 ? { ...d, matchResult: 'mismatch' as const } : d)),
  };
}

/**
 * BIO-UX-010-AC02 "degraded-device": the first device is reachable but
 * reporting a poor-quality/intermittent link — distinct from `offlineOf`,
 * which takes every device fully offline.
 */
function degradedDeviceOf(base: SyntheticDataset): SyntheticDataset {
  if (base.devices.length === 0) return base;
  return {
    ...base,
    devices: base.devices.map((d, i) => (i === 0 ? { ...d, connectivity: 'degraded' as const } : d)),
  };
}

/**
 * BIO-UX-010-AC02 "denied-access": the most recent access event is forced to
 * `denied` with an explicit reason code, so a human tester or Playwright run
 * always finds at least one denied event to inspect, deterministically.
 */
function deniedAccessOf(base: SyntheticDataset): SyntheticDataset {
  if (base.accessEvents.length === 0) return base;
  const lastIndex = base.accessEvents.length - 1;
  return {
    ...base,
    accessEvents: base.accessEvents.map((event, i) =>
      i === lastIndex
        ? { ...event, decision: 'denied' as const, reasonCode: 'assurance-level-insufficient' as const }
        : event,
    ),
  };
}

/**
 * BIO-UX-010-AC02 "notification": sets `flags.simulateNotification` and
 * composes the `degraded-device`/`denied-access` transforms on top of the
 * baseline so the pages' notification banner (`notifications.ts`) always has
 * at least one real, deterministic device and access alert to render — not
 * just a bare flag with nothing to show for it.
 */
function notificationOf(base: SyntheticDataset): SyntheticDataset {
  const withDegradedDevice = degradedDeviceOf(base);
  const withDeniedAccess = deniedAccessOf(withDegradedDevice);
  return { ...withDeniedAccess, flags: { ...withDeniedAccess.flags, simulateNotification: true } };
}

/** Applies `scenario` to an already-generated `normal` baseline dataset. */
export function applyScenario(base: SyntheticDataset, scenario: ScenarioId): SyntheticDataset {
  const transformed = ((): SyntheticDataset => {
    switch (scenario) {
      case 'normal':
        return { ...base };
      case 'empty':
        return emptyOf(base);
      case 'error':
        return errorOf(base);
      case 'offline':
        return offlineOf(base);
      case 'low-quality-biometric':
        return lowQualityBiometricOf(base);
      case 'duplicate-identity':
        return duplicateIdentityOf(base);
      case 'lost-card':
        return lostCardOf(base);
      case 'document-mismatch':
        return documentMismatchOf(base);
      case 'degraded-device':
        return degradedDeviceOf(base);
      case 'denied-access':
        return deniedAccessOf(base);
      case 'notification':
        return notificationOf(base);
    }
  })();
  return { ...transformed, scenario };
}
