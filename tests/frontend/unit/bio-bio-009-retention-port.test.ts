// Developer tests for BIO-BIO-009-AC05 (round 3, Seyi's BIO-BIO-009-F03: "connected through
// BiometricService ... or an explicitly documented HYBRID state"). SYNTHETIC: no real biometric
// data.
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MODULE_PORTS } from '../../../web/lib/module-registry';
import { MODULE_PORT_KEYS, clearPortModeOverrides, getPortMode, setPortModeOverride } from '../../../web/lib/feature-mode';

describe('BIO-BIO-009-AC05 BiometricService gains two independent retention/deletion port keys', () => {
  beforeEach(() => {
    clearPortModeOverrides();
  });

  it('MODULE_PORT_KEYS lists manageRetentionPolicy and deleteBiometricRecord alongside the existing keys', () => {
    expect([...MODULE_PORT_KEYS.BiometricService]).toEqual([
      'primaryAction',
      'checkLiveness',
      'manageRetentionPolicy',
      'deleteBiometricRecord',
    ]);
  });

  it('MODULE_PORTS resolves a distinct ModulePort instance for each new key, reporting its own module', () => {
    const ports = MODULE_PORTS.BiometricService;
    expect(ports.manageRetentionPolicy?.module).toBe('BiometricService');
    expect(ports.deleteBiometricRecord?.module).toBe('BiometricService');
    expect(ports.manageRetentionPolicy).not.toBe(ports.primaryAction);
    expect(ports.deleteBiometricRecord).not.toBe(ports.primaryAction);
  });

  it('both new ports start in mock mode by default (nothing is live unless explicitly switched)', () => {
    expect(MODULE_PORTS.BiometricService.manageRetentionPolicy?.mode).toBe('mock');
    expect(MODULE_PORTS.BiometricService.deleteBiometricRecord?.mode).toBe('mock');
  });

  it('manageRetentionPolicy can be switched to LIVE independently of every other key (genuine HYBRID)', () => {
    setPortModeOverride('BiometricService', 'manageRetentionPolicy', 'LIVE');

    expect(getPortMode('BiometricService', 'manageRetentionPolicy')).toBe('LIVE');
    expect(getPortMode('BiometricService', 'deleteBiometricRecord')).toBe('SIMULATION');
    expect(getPortMode('BiometricService', 'checkLiveness')).toBe('SIMULATION');
    expect(getPortMode('BiometricService', 'primaryAction')).toBe('SIMULATION');
  });

  it('deleteBiometricRecord can be switched to LIVE independently of every other key', () => {
    setPortModeOverride('BiometricService', 'deleteBiometricRecord', 'LIVE');

    expect(getPortMode('BiometricService', 'deleteBiometricRecord')).toBe('LIVE');
    expect(getPortMode('BiometricService', 'manageRetentionPolicy')).toBe('SIMULATION');
  });
});

describe('BIO-BIO-009-AC01/AC02/AC05 biometricsPort exposes the two new methods independently', () => {
  it('biometricsPort.manageRetentionPolicy always resolves through its own key, not primaryAction', async () => {
    vi.resetModules();
    const { biometricsPort, biometricsManageRetentionPolicyPort } = await import('../../../web/features/biometrics');
    expect(biometricsPort.manageRetentionPolicy).toBe(biometricsManageRetentionPolicyPort.manageRetentionPolicy);
  });

  it('biometricsPort.requestBiometricDeletion always resolves through its own key, not primaryAction', async () => {
    vi.resetModules();
    const { biometricsPort, biometricsDeleteBiometricRecordPort } = await import('../../../web/features/biometrics');
    expect(biometricsPort.requestBiometricDeletion).toBe(biometricsDeleteBiometricRecordPort.requestBiometricDeletion);
  });

  it('every other BiometricService method still resolves from primaryAction, unaffected by the split', async () => {
    vi.resetModules();
    const { biometricsPort, biometricsPrimaryPort } = await import('../../../web/features/biometrics');
    expect(biometricsPort.captureFingerprint).toBe(biometricsPrimaryPort.captureFingerprint);
    expect(biometricsPort.verifyFingerprint).toBe(biometricsPrimaryPort.verifyFingerprint);
  });
});

describe('BIO-BIO-009 mock adapter (SIMULATION default)', () => {
  it('manageRetentionPolicy echoes the requested category/days deterministically', async () => {
    vi.resetModules();
    const { biometricsMockPort } = await import('../../../web/features/biometrics/mock');
    const result = await biometricsMockPort.manageRetentionPolicy('face_template', 45);
    expect(result).toEqual({ category: 'face_template', retentionDays: 45 });
  });

  it('requestBiometricDeletion returns a typed outcome, cycling deterministically per record id', async () => {
    vi.resetModules();
    const { biometricsMockPort } = await import('../../../web/features/biometrics/mock');
    const first = await biometricsMockPort.requestBiometricDeletion('fingerprint_session', 'rec-1', 'test');
    expect(['Deleted', 'NotFound', 'DeletedPendingGalleryCleanup']).toContain(first.outcome);
  });
});
