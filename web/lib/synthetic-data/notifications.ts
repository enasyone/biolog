/**
 * Notification derivation (BIO-UX-010-AC02).
 *
 * A pure function of an already-generated dataset — no notification is its
 * own stored record, so switching scenario/seed and re-deriving always
 * produces the same alerts for the same dataset, same as every other
 * transform in this directory. Deliberately gated on `flags.simulateNotification`
 * (set only by the `notification` scenario — see `scenarios.ts`) rather than
 * always surfacing every degraded/denied condition it finds: a human tester
 * choosing `online`/`offline`/`degraded-device`/`denied-access` should see
 * that scenario's own list/table content change without an unrelated
 * notification banner appearing on every scenario that happens to contain a
 * denied event or a degraded device.
 */
import type { SyntheticDataset, SyntheticNotification } from './types';

export function deriveNotifications(dataset: SyntheticDataset): readonly SyntheticNotification[] {
  if (!dataset.flags.simulateNotification) return [];

  const notifications: SyntheticNotification[] = [];

  for (const device of dataset.devices) {
    if (device.connectivity === 'offline') {
      notifications.push({
        id: `notification-device-offline-${device.id}`,
        severity: 'critical',
        message: `${device.name} is offline (SYNTHETIC).`,
      });
    } else if (device.connectivity === 'degraded') {
      notifications.push({
        id: `notification-device-degraded-${device.id}`,
        severity: 'warning',
        message: `${device.name} connectivity is degraded (SYNTHETIC).`,
      });
    }
  }

  for (const event of dataset.accessEvents) {
    if (event.decision === 'denied') {
      const reason = event.reasonCode ? ` — ${event.reasonCode}` : '';
      notifications.push({
        id: `notification-access-denied-${event.id}`,
        severity: 'critical',
        message: `Access denied at event ${event.id}${reason} (SYNTHETIC).`,
      });
    }
  }

  return notifications;
}
