// Developer tests for BIO-UX-008-AC05. SYNTHETIC: no real biometric data.
import { afterEach, describe, expect, it, vi } from 'vitest';
import { biometricsLivePort } from '../../../web/features/biometrics/live';

describe('BIO-UX-008-AC05 the live adapter genuinely calls a stable, named endpoint per action', () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  it('module is BiometricService and mode is live', () => {
    expect(biometricsLivePort.module).toBe('BiometricService');
    expect(biometricsLivePort.mode).toBe('live');
  });

  it('captureFingerprint throws a clear, honest error when no backend is connected yet (network failure)', async () => {
    globalThis.fetch = vi.fn().mockRejectedValue(new Error('ECONNREFUSED'));
    await expect(biometricsLivePort.captureFingerprint('right_thumb')).rejects.toThrow(
      /fingerprint\/capture.*not been connected/,
    );
  });

  it('verifyFace throws on a non-OK HTTP response rather than silently returning a fabricated match/no_match', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({ ok: false, status: 500, json: async () => ({}) });
    await expect(biometricsLivePort.verifyFace()).rejects.toThrow(/500/);
  });

  it('identifyFace calls its own distinct endpoint, not the generic primaryAction one', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ candidates: [], message: 'ok' }) });
    globalThis.fetch = fetchMock;
    await biometricsLivePort.identifyFace('candidates_found');
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/biometrics/face/identify',
      expect.objectContaining({ method: 'POST' }),
    );
  });

  it('a successful response is parsed and returned as-is', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ outcome: 'live', simulated: true, message: 'ok' }),
    });
    const result = await biometricsLivePort.checkLiveness();
    expect(result).toEqual({ outcome: 'live', simulated: true, message: 'ok' });
  });

  it('the generic primaryAction still works unchanged for /enrolment\'s PlaceholderPage', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ message: 'ok' }) });
    const result = await biometricsLivePort.primaryAction();
    expect(result.outcome).toBe('success');
  });
});
