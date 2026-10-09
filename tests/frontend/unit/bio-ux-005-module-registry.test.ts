// Developer tests for BIO-UX-005-AC01 (every module has a defined port).
// SYNTHETIC: no real tenant/user data.
import { describe, expect, it } from 'vitest';
import { MODULE_PORTS } from '../../../web/lib/module-registry';
import { MODULE_PORT_KEYS } from '../../../web/lib/feature-mode';
import { ROUTE_MANIFEST } from '../../../web/lib/route-manifest';

const REAL_DOMAIN_MODULES = [
  'IdentityService',
  'VisitorService',
  'CredentialService',
  'BiometricService',
  'PhotoService',
  'DocumentVerifyService',
  'AccessService',
  'AttendanceService',
  'TransportService',
  'DeviceService',
  'ReportingService',
  'TenantService',
  'IntegrationService',
] as const;

describe('BIO-UX-005-AC01 every module named in route-manifest.ts has a resolvable port', () => {
  it('every servicePort used by a real route resolves to a port map in MODULE_PORTS', () => {
    const usedPorts = new Set(ROUTE_MANIFEST.map((entry) => entry.servicePort));
    for (const servicePort of usedPorts) {
      expect(MODULE_PORTS[servicePort], `no MODULE_PORTS entry for ${servicePort}`).toBeDefined();
    }
  });

  it("every module's registered port keys match MODULE_PORT_KEYS exactly (registry and feature-mode agree)", () => {
    for (const module of REAL_DOMAIN_MODULES) {
      const registryKeys = Object.keys(MODULE_PORTS[module]).sort();
      const featureModeKeys = [...MODULE_PORT_KEYS[module]].sort();
      expect(registryKeys).toEqual(featureModeKeys);
    }
  });

  it("every real domain module's default (primaryAction) port reports its own module name", () => {
    for (const module of REAL_DOMAIN_MODULES) {
      expect(MODULE_PORTS[module].primaryAction.module).toBe(module);
    }
  });

  it("ReportingService's second port (viewAuditTrail) also reports the ReportingService module", () => {
    expect(MODULE_PORTS.ReportingService.viewAuditTrail?.module).toBe('ReportingService');
    // Independently resolvable instance, not the same object as primaryAction.
    expect(MODULE_PORTS.ReportingService.viewAuditTrail).not.toBe(MODULE_PORTS.ReportingService.primaryAction);
  });

  it('Unassigned and FrontendRuntime both resolve to the shared UNASSIGNED_MOCK_PORT', () => {
    expect(MODULE_PORTS.Unassigned.primaryAction).toBe(MODULE_PORTS.FrontendRuntime.primaryAction);
    expect(MODULE_PORTS.Unassigned.primaryAction.module).toBe('Unassigned');
  });

  it('every port starts in mock mode (nothing is live by default)', () => {
    for (const module of REAL_DOMAIN_MODULES) {
      for (const port of Object.values(MODULE_PORTS[module])) {
        expect(port.mode).toBe('mock');
      }
    }
  });
});
