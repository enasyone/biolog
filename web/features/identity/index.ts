import { resolvePort } from '../../lib/feature-mode';
import { identityMockPort } from './mock';
import { identityLivePort } from './live';
import type { IdentityPort } from './ports';

export type { IdentityPort } from './ports';

/**
 * The Identity module's resolved port (BIO-UX-005-AC01, AC04). Pages/
 * components import this, never `./mock` or `./live` directly — switching
 * `NEXT_PUBLIC_FEATURE_MODE_IDENTITYSERVICE` from SIMULATION to LIVE changes what
 * this resolves to without any importer changing.
 */
export const identityPort: IdentityPort = resolvePort(identityMockPort, identityLivePort);
