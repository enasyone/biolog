import { createMockAdapter } from '../../lib/module-port';
import type { TenantsPort } from './ports';
import type { TenantFeatureConfiguration } from './types';

/**
 * Tenants mock adapter (BIO-UX-005-AC05, extended BIO-TEN-003-AC05): deterministic, in-memory
 * stand-in for the real `/api/v1/tenants/{tenantId}/feature-configuration` backend,
 * reproducible for local testing and demos. Keeps the existing deterministic scenario-cycling
 * shape for `primaryAction()` (SIMULATION mode) unchanged - this story's own methods are
 * purely additive (`...base`).
 */
const base = createMockAdapter('TenantService', {
  scenarios: [
    { outcome: 'success', message: 'Tenants: simulated action completed.' },
    { outcome: 'failure', message: 'Tenants: simulated action failed — try again.' },
    { outcome: 'offline', message: 'Tenants: device or network appears offline (simulated).' },
  ],
});

function defaultConfig(tenantId: string): TenantFeatureConfiguration {
  // BIO-TEN-003-AC04: the mock mirrors the real fail-closed default exactly - every
  // module/subfeature starts disabled, version 0 ("never written").
  const now = new Date(0).toISOString();
  return {
    tenantId,
    biometrics: { enabled: false, face1NIdentificationEnabled: false },
    cardProductionEnabled: false,
    visitorEnabled: false,
    attendanceEnabled: false,
    transportEnabled: false,
    documentVerify: {
      enabled: false,
      captureEnabled: false,
      mrzEnabled: false,
      barcodeEnabled: false,
      ocrEnabled: false,
      authenticityEnabled: false,
      documentIdentificationEnabled: false,
      nfcEnabled: false,
      selfieComparisonEnabled: false,
    },
    version: 0,
    createdAt: now,
    updatedAt: now,
  };
}

const configsByTenant = new Map<string, TenantFeatureConfiguration>();

export const tenantsMockPort: TenantsPort = {
  ...base,

  async getFeatureConfiguration(tenantId) {
    return configsByTenant.get(tenantId) ?? defaultConfig(tenantId);
  },

  async updateFeatureConfiguration(input) {
    const current = configsByTenant.get(input.tenantId) ?? defaultConfig(input.tenantId);
    if (input.version !== current.version) {
      throw new Error(`mock: concurrency_conflict - expected version ${input.version}, actual ${current.version}`);
    }

    const updated: TenantFeatureConfiguration = {
      ...current,
      biometrics: { ...current.biometrics, ...input.biometrics },
      cardProductionEnabled: input.cardProductionEnabled ?? current.cardProductionEnabled,
      visitorEnabled: input.visitorEnabled ?? current.visitorEnabled,
      attendanceEnabled: input.attendanceEnabled ?? current.attendanceEnabled,
      transportEnabled: input.transportEnabled ?? current.transportEnabled,
      documentVerify: { ...current.documentVerify, ...input.documentVerify },
      version: current.version + 1,
      updatedAt: new Date().toISOString(),
    };
    configsByTenant.set(input.tenantId, updated);
    return updated;
  },
};
