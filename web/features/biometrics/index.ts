import { resolvePort } from '../../lib/feature-mode';
import { biometricsMockPort } from './mock';
import { biometricsLivePort } from './live';
import type { BiometricsPort } from './ports';

export type { BiometricsPort } from './ports';

/**
 * The Biometrics module's two independently-resolved ports (BIO-UX-005-AC01,
 * AC04, extended BIO-BIO-008-AC05). `biometricsPrimaryPort` resolves via the
 * `primaryAction` key (fingerprint/face capture, 1:1 verify, 1:N identify —
 * still SIMULATION-only, no backend exists for them yet).
 * `biometricsCheckLivenessPort` resolves independently via the
 * `checkLiveness` key: switching
 * `NEXT_PUBLIC_FEATURE_MODE_BIOMETRICSERVICE_CHECKLIVENESS` to LIVE (leaving
 * the module's own whole-module default alone) connects ONLY the liveness
 * check to BIO-BIO-008's real backend — the same independent-per-port
 * mechanism `ReportingService`/`DocumentVerifyService` already established
 * (`feature-mode.ts`'s `MODULE_PORT_KEYS` comment).
 *
 * Both resolves share the same underlying `biometricsMockPort`/
 * `biometricsLivePort` singletons (unlike Document Verify's own split, this
 * module's `checkLiveness` behaviour is stateless-per-call, not a shared
 * call-counter needing its own isolated instance) — `resolvePort` only reads
 * which whole object a given key resolves to, so the two calls below can
 * independently pick mock vs. live per key without either module needing a
 * second pair of mock/live instances.
 *
 * `biometricsPort` merges the two into the single object every page/
 * component calls through — every method resolves from
 * `biometricsPrimaryPort` except `checkLiveness`, which always resolves from
 * `biometricsCheckLivenessPort` regardless of the module's whole-module
 * default.
 */
export const biometricsPrimaryPort: BiometricsPort = resolvePort(biometricsMockPort, biometricsLivePort, 'primaryAction');
export const biometricsCheckLivenessPort: BiometricsPort = resolvePort(biometricsMockPort, biometricsLivePort, 'checkLiveness');
/** BIO-BIO-009-AC01/AC05 (round 3): independent per-port resolution, same mechanism as `checkLiveness` above. */
export const biometricsManageRetentionPolicyPort: BiometricsPort = resolvePort(biometricsMockPort, biometricsLivePort, 'manageRetentionPolicy');
/** BIO-BIO-009-AC02/AC05 (round 3): independent per-port resolution, same mechanism as `checkLiveness` above. */
export const biometricsDeleteBiometricRecordPort: BiometricsPort = resolvePort(biometricsMockPort, biometricsLivePort, 'deleteBiometricRecord');

export const biometricsPort: BiometricsPort = {
  ...biometricsPrimaryPort,
  checkLiveness: biometricsCheckLivenessPort.checkLiveness,
  manageRetentionPolicy: biometricsManageRetentionPolicyPort.manageRetentionPolicy,
  requestBiometricDeletion: biometricsDeleteBiometricRecordPort.requestBiometricDeletion,
};
