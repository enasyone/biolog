import { resolvePort } from '../../lib/feature-mode';
import { integrationsMockPort } from './mock';
import { integrationsLivePort } from './live';
import type { IntegrationsPort } from './ports';

export type { IntegrationsPort } from './ports';

/**
 * The Integrations module's resolved port (BIO-UX-005-AC01, AC04). Pages/
 * components import this, never `./mock` or `./live` directly — switching
 * `NEXT_PUBLIC_FEATURE_MODE_INTEGRATIONSERVICE` from SIMULATION to LIVE changes what
 * this resolves to without any importer changing.
 */
export const integrationsPort: IntegrationsPort = resolvePort(integrationsMockPort, integrationsLivePort);
