// Developer tests for BIO-UX-010-AC02.
// SYNTHETIC: no real tenant/user data.
import { describe, expect, it } from 'vitest';
import { DEFAULT_SEED, generateDataset } from '../../../web/lib/synthetic-data/dataset';
import { deriveNotifications } from '../../../web/lib/synthetic-data/notifications';
import { OPERATIONAL_SCENARIO_IDS, SCENARIO_IDS } from '../../../web/lib/synthetic-data/types';

describe('BIO-UX-010-AC02 the three new scenarios are named and generate without throwing', () => {
  it('SCENARIO_IDS is extended with degraded-device, denied-access and notification', () => {
    expect(SCENARIO_IDS).toContain('degraded-device');
    expect(SCENARIO_IDS).toContain('denied-access');
    expect(SCENARIO_IDS).toContain('notification');
  });

  it('OPERATIONAL_SCENARIO_IDS names exactly the 5 scenarios AC02 lists, "online" as normal', () => {
    expect(OPERATIONAL_SCENARIO_IDS).toEqual(['normal', 'offline', 'degraded-device', 'denied-access', 'notification']);
  });

  it('every operational scenario id generates without throwing', () => {
    for (const scenario of OPERATIONAL_SCENARIO_IDS) {
      expect(() => generateDataset(DEFAULT_SEED, scenario)).not.toThrow();
    }
  });
});

describe('BIO-UX-010-AC02 each new scenario produces its documented, testable effect', () => {
  it('degraded-device: at least one device reports degraded, the rest are unaffected', () => {
    const normal = generateDataset(DEFAULT_SEED, 'normal');
    const dataset = generateDataset(DEFAULT_SEED, 'degraded-device');
    expect(dataset.devices.some((d) => d.connectivity === 'degraded')).toBe(true);
    expect(dataset.devices.length).toBe(normal.devices.length);
  });

  it('denied-access: the most recent access event is denied with a reason code', () => {
    const dataset = generateDataset(DEFAULT_SEED, 'denied-access');
    const last = dataset.accessEvents[dataset.accessEvents.length - 1];
    expect(last?.decision).toBe('denied');
    expect(last?.reasonCode).toBeDefined();
  });

  it('notification: simulateNotification is set, and composes a degraded device + a denied event', () => {
    const dataset = generateDataset(DEFAULT_SEED, 'notification');
    expect(dataset.flags.simulateNotification).toBe(true);
    expect(dataset.devices.some((d) => d.connectivity === 'degraded')).toBe(true);
    expect(dataset.accessEvents.some((e) => e.decision === 'denied')).toBe(true);
  });

  it('scenario application stays deterministic for the 3 new scenarios', () => {
    for (const scenario of ['degraded-device', 'denied-access', 'notification'] as const) {
      const a = generateDataset(DEFAULT_SEED, scenario);
      const b = generateDataset(DEFAULT_SEED, scenario);
      expect(a).toEqual(b);
    }
  });
});

describe('BIO-UX-010-AC02 deriveNotifications', () => {
  it('is empty when simulateNotification is not set, even if a device is offline/degraded', () => {
    const offline = generateDataset(DEFAULT_SEED, 'offline');
    expect(deriveNotifications(offline)).toEqual([]);
    const degraded = generateDataset(DEFAULT_SEED, 'degraded-device');
    expect(deriveNotifications(degraded)).toEqual([]);
  });

  it('surfaces a warning for a degraded device and a critical for a denied access event under the notification scenario', () => {
    const dataset = generateDataset(DEFAULT_SEED, 'notification');
    const notifications = deriveNotifications(dataset);
    expect(notifications.some((n) => n.severity === 'warning' && /degraded/i.test(n.message))).toBe(true);
    expect(notifications.some((n) => n.severity === 'critical' && /denied/i.test(n.message))).toBe(true);
  });

  it('surfaces a critical notification per offline device when both flags are true', () => {
    const base = generateDataset(DEFAULT_SEED, 'notification');
    const withOffline = { ...base, devices: base.devices.map((d) => ({ ...d, connectivity: 'offline' as const })) };
    const notifications = deriveNotifications(withOffline);
    expect(notifications.filter((n) => n.severity === 'critical' && /offline/i.test(n.message))).toHaveLength(
      withOffline.devices.length,
    );
  });
});
