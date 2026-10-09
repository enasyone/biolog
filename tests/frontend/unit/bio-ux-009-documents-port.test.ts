// Developer tests for BIO-UX-009-AC01, AC04, AC05. SYNTHETIC: no real document/biometric data.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { DocumentsPort } from '../../../web/features/documents/ports';

/**
 * Every test imports a FRESH instance of `mock.ts`/`live.ts` after
 * `vi.resetModules()` — each step method closes over its own private call
 * counter (module-scope `let callCount = 0`), so importing a shared,
 * already-called instance across tests would make a test's "first call"
 * assertion depend on unrelated tests' execution order. A fresh module
 * per test keeps every test's counter genuinely starting at zero.
 */
async function freshMockPort(): Promise<DocumentsPort> {
  const mod = await import('../../../web/features/documents/mock');
  return mod.documentsMockPort;
}

async function freshLivePort(): Promise<DocumentsPort> {
  const mod = await import('../../../web/features/documents/live');
  return mod.documentsLivePort;
}

beforeEach(() => {
  vi.resetModules();
});

describe('BIO-UX-009-AC01/AC05 documentsMockPort exposes one named method per interactive area', () => {
  it('every step method exists and returns a step-tagged result', async () => {
    const port = await freshMockPort();
    expect((await port.capture()).step).toBe('capture');
    expect((await port.runMrz()).step).toBe('mrz');
    expect((await port.runBarcode()).step).toBe('barcode');
    expect((await port.runOcr()).step).toBe('ocr');
    expect((await port.identifyDocument()).step).toBe('document-identification');
    expect((await port.runNfc()).step).toBe('nfc');
    expect((await port.checkAuthenticity()).step).toBe('authenticity');
    expect((await port.compareSelfie()).step).toBe('selfie-comparison');
  });

  it('each step method has its own independent, deterministic call counter', async () => {
    const port = await freshMockPort();
    // Calling MRZ three times first must not disturb OCR's own first-call scenario.
    await port.runMrz();
    await port.runMrz();
    await port.runMrz();
    const ocrFirstCall = await port.runOcr();

    const { runEvidenceScenario } = await import('../../../web/features/documents/evidence');
    expect(ocrFirstCall).toMatchObject(runEvidenceScenario('ocr', 1));
  });

  it('mode is "mock" and module is DocumentVerifyService', async () => {
    const port = await freshMockPort();
    expect(port.mode).toBe('mock');
    expect(port.module).toBe('DocumentVerifyService');
  });
});

describe('BIO-UX-009-AC05 documentsLivePort genuinely calls its own endpoint per step, fails closed to offline', () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  it('capture() reports offline when its endpoint is unreachable (no backend built yet)', async () => {
    globalThis.fetch = vi.fn().mockRejectedValue(new Error('network error')) as unknown as typeof fetch;
    const port = await freshLivePort();
    const result = await port.capture();
    expect(result.outcome).toBe('offline');
    expect(result.state).toBe('not-checked');
  });

  it('runOcr() and runMrz() call distinct endpoint paths', async () => {
    const calls: string[] = [];
    globalThis.fetch = vi.fn(async (input: RequestInfo | URL) => {
      calls.push(String(input));
      throw new Error('network error');
    }) as unknown as typeof fetch;
    const port = await freshLivePort();
    await port.runOcr();
    await port.runMrz();
    expect(calls).toEqual(['/api/v1/documents/ocr', '/api/v1/documents/mrz']);
  });

  it('a successful live response is trusted for state only when it is a real EvidenceCheckState', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ message: 'ok', state: 'not-a-real-state' }),
    }) as unknown as typeof fetch;
    const port = await freshLivePort();
    const result = await port.runBarcode();
    expect(result.outcome).toBe('success');
    expect(result.state).toBe('read'); // falls back rather than trusting an unrecognised value
  });
});
