import { resolvePort } from '../../lib/feature-mode';
import { tenantsMockPort } from './mock';
import { tenantsLivePort } from './live';
import type { TenantsPort } from './ports';

export type { TenantsPort } from './ports';

/**
 * The Tenants module's resolved port (BIO-UX-005-AC01, AC04). Pages/
 * components import this, never `./mock` or `./live` directly — switching
 * `NEXT_PUBLIC_FEATURE_MODE_TENANTSERVICE` from SIMULATION to LIVE changes what
 * this resolves to without any importer changing.
 */
export const tenantsPort: TenantsPort = resolvePort(tenantsMockPort, tenantsLivePort);
