import type { ModulePort } from '../../lib/module-port';
import type { TenantFeatureConfiguration, UpdateTenantFeatureConfigurationInput } from './types';

/**
 * Tenants module port (BIO-UX-005-AC01, extended by BIO-TEN-003-AC05). Real methods for this
 * story's own tenant feature-module configuration surface: read a tenant's effective
 * configuration (AC04: always resolves, never a 404, for a tenant with nothing stored yet)
 * and update it under optimistic concurrency (AC03). `mock.ts`/`live.ts` are the only two
 * files that implement this interface; `/administration/tenants` calls the resolved
 * `tenantsPort` from `./index`, never `./mock` or `./live` directly.
 */
export interface TenantsPort extends ModulePort {
  getFeatureConfiguration(tenantId: string): Promise<TenantFeatureConfiguration>;
  updateFeatureConfiguration(input: UpdateTenantFeatureConfigurationInput): Promise<TenantFeatureConfiguration>;
}
