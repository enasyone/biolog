import { createMockAdapter } from '../../lib/module-port';
import type {
  CreateServiceAccountPort,
  CreateServiceAccountResult,
  ListServiceAccountsPort,
  RevokeServiceAccountPort,
  RotateServiceAccountSecretPort,
  ServiceAccountSummary,
} from './ports';

/**
 * Deterministic in-memory stand-in for the real `/api/v1/integrations/service-accounts`
 * backend (BIO-INT-003-AC01/AC04) - reproducible for local testing and demos, same shape as
 * every other mock adapter in this epic.
 */
let nextAccountNumber = 1;
const accountsByTenant = new Map<string, Map<string, ServiceAccountSummary>>();

function tenantAccounts(tenantId: string): Map<string, ServiceAccountSummary> {
  let accounts = accountsByTenant.get(tenantId);
  if (!accounts) {
    accounts = new Map();
    accountsByTenant.set(tenantId, accounts);
  }
  return accounts;
}

function mockSecret(): string {
  return `mock-secret-${Math.random().toString(36).slice(2)}${Date.now().toString(36)}`;
}

const listBase = createMockAdapter('IntegrationService');
export const listServiceAccountsMockPort: ListServiceAccountsPort = {
  ...listBase,
  async primaryAction(input) {
    if (!input) return { outcome: 'failure', message: 'No tenant was provided.' };
    return { outcome: 'success', message: 'Service accounts loaded (SIMULATION).', accounts: [...tenantAccounts(input.tenantId).values()] };
  },
};

const createBase = createMockAdapter('IntegrationService');
export const createServiceAccountMockPort: CreateServiceAccountPort = {
  ...createBase,
  async primaryAction(input): Promise<CreateServiceAccountResult> {
    if (!input) return { outcome: 'failure', message: 'No service account definition was provided.' };
    if (!input.displayName.trim()) {
      return { outcome: 'failure', message: 'A display name is required (SIMULATION).' };
    }
    const id = `mock-sa-${nextAccountNumber++}`;
    const now = new Date().toISOString();
    const account: ServiceAccountSummary = {
      id,
      clientId: id,
      displayName: input.displayName,
      scopes: input.scopes,
      status: 'Active',
      version: 1,
      createdAt: now,
      secretLastRotatedAt: now,
    };
    tenantAccounts(input.tenantId).set(id, account);
    return { outcome: 'success', message: `Service account '${input.displayName}' created (SIMULATION).`, account, secret: mockSecret() };
  },
};

const rotateBase = createMockAdapter('IntegrationService');
export const rotateServiceAccountSecretMockPort: RotateServiceAccountSecretPort = {
  ...rotateBase,
  async primaryAction(input) {
    if (!input) return { outcome: 'failure', message: 'No service account was provided.' };
    const accounts = tenantAccounts(input.tenantId);
    const current = accounts.get(input.id);
    if (!current) return { outcome: 'failure', message: 'Unknown service account (SIMULATION).' };
    const updated: ServiceAccountSummary = { ...current, version: current.version + 1, secretLastRotatedAt: new Date().toISOString() };
    accounts.set(input.id, updated);
    return { outcome: 'success', message: 'Secret rotated (SIMULATION) - copy it now, it will not be shown again.', account: updated, secret: mockSecret() };
  },
};

const revokeBase = createMockAdapter('IntegrationService');
export const revokeServiceAccountMockPort: RevokeServiceAccountPort = {
  ...revokeBase,
  async primaryAction(input) {
    if (!input) return { outcome: 'failure', message: 'No service account was provided.' };
    const accounts = tenantAccounts(input.tenantId);
    const current = accounts.get(input.id);
    if (!current) return { outcome: 'failure', message: 'Unknown service account (SIMULATION).' };
    const updated: ServiceAccountSummary = { ...current, status: 'Revoked', version: current.version + 1 };
    accounts.set(input.id, updated);
    return { outcome: 'success', message: `'${current.displayName}' revoked (SIMULATION).`, account: updated };
  },
};
