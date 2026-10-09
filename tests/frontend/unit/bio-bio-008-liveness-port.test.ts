// Developer tests for BIO-BIO-008-AC05 ("connected through BiometricService ... or an explicitly
// documented HYBRID state"). SYNTHETIC: no real biometric data.
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MODULE_PORTS } from '../../../web/lib/module-registry';
import { MODULE_PORT_KEYS, clearPortModeOverrides, getPortMode, setPortModeOverride } from '../../../web/lib/feature-mode';

describe('BIO-BIO-008-AC05 BiometricService gains an independent checkLiveness port key', () => {
  beforeEach(() => {
    clearPortModeOverrides();
  });

  it('MODULE_PORT_KEYS lists primaryAction and checkLiveness among BiometricService\'s keys, in order', () => {
    // BIO-BIO-009 (round 3) added two more keys after this one - see
    // bio-bio-009-retention-port.test.ts for the full, exact list this story now pins.
    expect([...MODULE_PORT_KEYS.BiometricService].slice(0, 2)).toEqual(['primaryAction', 'checkLiveness']);
  });

  it('MODULE_PORTS resolves a distinct ModulePort instance for the checkLiveness key, reporting its own module', () => {
    const ports = MODULE_PORTS.BiometricService;
    expect(Object.keys(ports)).toContain('checkLiveness');
    expect(ports.checkLiveness?.module).toBe('BiometricService');
    expect(ports.checkLiveness).not.toBe(ports.primaryAction);
  });

  it('both ports start in mock mode by default (nothing is live unless explicitly switched)', () => {
    expect(MODULE_PORTS.BiometricService.primaryAction?.mode).toBe('mock');
    expect(MODULE_PORTS.BiometricService.checkLiveness?.mode).toBe('mock');
  });

  it('the checkLiveness port can be switched to LIVE independently of primaryAction (genuine HYBRID)', () => {
    setPortModeOverride('BiometricService', 'checkLiveness', 'LIVE');

    expect(getPortMode('BiometricService', 'checkLiveness')).toBe('LIVE');
    expect(getPortMode('BiometricService', 'primaryAction')).toBe('SIMULATION');
  });
});

describe('BIO-BIO-008-AC01/AC05 biometricsCheckLivenessPort exposes checkLiveness independently', () => {
  it('biometricsPort.checkLiveness always resolves through the checkLiveness key, not primaryAction', async () => {
    vi.resetModules();
    const { biometricsPort, biometricsCheckLivenessPort } = await import('../../../web/features/biometrics');
    expect(biometricsPort.checkLiveness).toBe(biometricsCheckLivenessPort.checkLiveness);
  });

  it('every other BiometricService method still resolves from primaryAction, unaffected by the split', async () => {
    vi.resetModules();
    const { biometricsPort, biometricsPrimaryPort } = await import('../../../web/features/biometrics');
    expect(biometricsPort.captureFingerprint).toBe(biometricsPrimaryPort.captureFingerprint);
    expect(biometricsPort.verifyFingerprint).toBe(biometricsPrimaryPort.verifyFingerprint);
    expect(biometricsPort.captureFace).toBe(biometricsPrimaryPort.captureFace);
    expect(biometricsPort.verifyFace).toBe(biometricsPrimaryPort.verifyFace);
    expect(biometricsPort.identifyFace).toBe(biometricsPrimaryPort.identifyFace);
    expect(biometricsPort.connectDevice).toBe(biometricsPrimaryPort.connectDevice);
  });
});
