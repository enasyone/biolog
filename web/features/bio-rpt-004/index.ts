import { resolvePort } from '../../lib/feature-mode';
import { operationalHealthMockPort } from './mock';
import { operationalHealthLivePort } from './live';

export type {
  OperationalHealthPort,
  PanelFreshness,
  IdentityHealthPanel,
  AccessHealthPanel,
  ProductionHealthPanel,
  UnavailableHealthPanel,
  OperationalHealthSnapshot,
  OperationalHealthResult,
} from './ports';

/**
 * BIO-RPT-004's resolved port. Pages/components import this, never `./mock` or `./live`
 * directly (same discipline as every other module in this epic).
 */
export const operationalHealthPort = resolvePort(operationalHealthMockPort, operationalHealthLivePort, 'operationalHealth');
