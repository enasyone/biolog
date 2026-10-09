import { resolvePort } from '../../lib/feature-mode';
import {
  createServiceAccountMockPort,
  listServiceAccountsMockPort,
  revokeServiceAccountMockPort,
  rotateServiceAccountSecretMockPort,
} from './mock';
import {
  createServiceAccountLivePort,
  listServiceAccountsLivePort,
  revokeServiceAccountLivePort,
  rotateServiceAccountSecretLivePort,
} from './live';

export type {
  CreateServiceAccountInput,
  CreateServiceAccountResult,
  ListServiceAccountsInput,
  ListServiceAccountsResult,
  RevokeServiceAccountResult,
  RotateServiceAccountSecretResult,
  ServiceAccountIdInput,
  ServiceAccountLifecycleStatus,
  ServiceAccountSummary,
} from './ports';
export { KNOWN_SCOPES } from './ports';

/**
 * BIO-INT-003's resolved ports. Pages/components import these, never `./mock` or `./live`
 * directly (same discipline as every other module in this epic). Each is independently
 * mode-switchable via its own `NEXT_PUBLIC_FEATURE_MODE_INTEGRATIONSERVICE_<KEY>` override
 * (`web/lib/feature-mode.ts`'s own `PER_PORT_ENV_DEFAULTS`), falling back to the module-wide
 * `NEXT_PUBLIC_FEATURE_MODE_INTEGRATIONSERVICE` default.
 */
export const listServiceAccountsPort = resolvePort(listServiceAccountsMockPort, listServiceAccountsLivePort, 'listServiceAccounts');
export const createServiceAccountPort = resolvePort(createServiceAccountMockPort, createServiceAccountLivePort, 'createServiceAccount');
export const rotateServiceAccountSecretPort = resolvePort(
  rotateServiceAccountSecretMockPort,
  rotateServiceAccountSecretLivePort,
  'rotateServiceAccountSecret',
);
export const revokeServiceAccountPort = resolvePort(revokeServiceAccountMockPort, revokeServiceAccountLivePort, 'revokeServiceAccount');
