// Developer tests for BIO-UX-010-AC01, AC05.
// SYNTHETIC: no real tenant/user data.
import { describe, expect, it } from 'vitest';
import { MODULE_PORTS } from '../../../web/lib/module-registry';
import { MODULE_PORT_KEYS } from '../../../web/lib/feature-mode';

const NEW_MULTI_PORT_MODULES = [
  { module: 'AccessService' as const, keys: ['primaryAction', 'viewAccessEvents'] },
  { module: 'AttendanceService' as const, keys: ['primaryAction', 'flagException', 'workforceAttendance'] },
  { module: 'VisitorService' as const, keys: ['primaryAction', 'checkOutVisitor', 'viewVisitHistory'] },
  {
    module: 'TransportService' as const,
    keys: [
      'primaryAction', 'assignRoute',
      'listTransportNotificationRules', 'createTransportNotificationRule', 'setTransportNotificationRuleEnabled', 'listTransportNotificationDeliveries',
    ],
  },
  {
    module: 'DeviceService' as const,
    keys: ['primaryAction', 'viewConnectivityStatus', 'listDevices', 'pollDeviceHealth', 'getRemoteJobCapabilities', 'runRemoteJob'],
  },
];

describe('BIO-UX-010-AC01, AC05: every operational module has its route-manifest-named ports', () => {
  it('MODULE_PORT_KEYS lists exactly the expected keys, in order, for each module', () => {
    for (const { module, keys } of NEW_MULTI_PORT_MODULES) {
      expect([...MODULE_PORT_KEYS[module]]).toEqual(keys);
    }
  });

  it('MODULE_PORTS resolves a distinct ModulePort instance for every key, each reporting its own module', () => {
    for (const { module, keys } of NEW_MULTI_PORT_MODULES) {
      const ports = MODULE_PORTS[module];
      expect(Object.keys(ports).sort()).toEqual([...keys].sort());
      for (const key of keys) {
        expect(ports[key]?.module).toBe(module);
        expect(ports[key]?.mode).toBe('mock');
      }
      // Each port key is its own adapter instance (independent scenario cycle/mode), not the
      // same object reused under two names - same guarantee bio-ux-005-module-registry.test.ts
      // already checks for ReportingService's two ports.
      const instances = new Set(keys.map((key) => ports[key]));
      expect(instances.size).toBe(keys.length);
    }
  });

  it("every new port's first mock call resolves without throwing and returns a valid outcome", async () => {
    for (const { module, keys } of NEW_MULTI_PORT_MODULES) {
      for (const key of keys) {
        const result = await MODULE_PORTS[module][key]!.primaryAction();
        expect(['success', 'failure', 'offline']).toContain(result.outcome);
        expect(typeof result.message).toBe('string');
        expect(result.message.length).toBeGreaterThan(0);
      }
    }
  });
});
