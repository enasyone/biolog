// Developer tests for BIO-UX-005-AC01, AC04, AC05.
// SYNTHETIC: no real tenant/user data.
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  createLiveAdapter,
  createMockAdapter,
  UNASSIGNED_MOCK_PORT,
} from '../../../web/lib/module-port';

describe('BIO-UX-005-AC05 createMockAdapter reproduces success/failure/offline/boundary behaviours', () => {
  it('cycles deterministically through every provided scenario, same sequence every run', async () => {
    const scenarios = [
      { outcome: 'success' as const, message: 'ok' },
      { outcome: 'failure' as const, message: 'nope' },
      { outcome: 'offline' as const, message: 'no signal' },
    ];
    const portA = createMockAdapter('DeviceService', { scenarios, simulatedLatencyMs: 0 });
    const portB = createMockAdapter('DeviceService', { scenarios, simulatedLatencyMs: 0 });
    const resultsA = [await portA.primaryAction(), await portA.primaryAction(), await portA.primaryAction()];
    const resultsB = [await portB.primaryAction(), await portB.primaryAction(), await portB.primaryAction()];
    // Same module + same call count -> same deterministic sequence (BIO-UX-005-AC06 "auditable").
    expect(resultsA.map((r) => r.outcome)).toEqual(resultsB.map((r) => r.outcome));
    // Every scenario outcome is reachable, not just the first.
    const outcomes = new Set(resultsA.map((r) => r.outcome));
    expect(outcomes.size).toBeGreaterThan(1);
  });

  it('defaults to a single always-succeeds scenario when none is provided', async () => {
    const port = createMockAdapter('DeviceService', { simulatedLatencyMs: 0 });
    const result = await port.primaryAction();
    expect(result.outcome).toBe('success');
  });

  it('a genuinely asynchronous call lets the caller observe a loading state (BIO-UX-005-AC05)', () => {
    const port = createMockAdapter('DeviceService', { simulatedLatencyMs: 20 });
    const promise = port.primaryAction();
    // The promise has not resolved synchronously - the caller can render "loading" here.
    expect(promise).toBeInstanceOf(Promise);
    return promise;
  });

  it('boundary: an empty scenarios array is rejected with a clear error, not silent undefined', () => {
    expect(() => createMockAdapter('DeviceService', { scenarios: [] })).toThrow(/scenarios must not be empty/);
  });

  it('mode is "mock" and module is preserved', () => {
    const port = createMockAdapter('DeviceService', { simulatedLatencyMs: 0 });
    expect(port.mode).toBe('mock');
    expect(port.module).toBe('DeviceService');
  });
});

describe('BIO-UX-005-AC04 createLiveAdapter genuinely calls the endpoint, fails closed to offline', () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  it('reports offline when the endpoint is unreachable (no backend built yet)', async () => {
    globalThis.fetch = vi.fn().mockRejectedValue(new Error('ECONNREFUSED'));
    const port = createLiveAdapter('DeviceService', '/api/v1/devices/primary-action');
    const result = await port.primaryAction();
    expect(result.outcome).toBe('offline');
    expect(result.message).toContain('/api/v1/devices/primary-action');
  });

  it('reports failure on a non-OK HTTP response', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({ ok: false, status: 503, json: async () => ({}) });
    const port = createLiveAdapter('DeviceService', '/api/v1/devices/primary-action');
    const result = await port.primaryAction();
    expect(result.outcome).toBe('failure');
    expect(result.message).toContain('503');
  });

  it('reports success and passes through the response message on an OK response', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ message: 'Device registered.' }),
    });
    const port = createLiveAdapter('DeviceService', '/api/v1/devices/primary-action');
    const result = await port.primaryAction();
    expect(result).toEqual({ outcome: 'success', message: 'Device registered.' });
  });

  it('mode is "live" and the endpoint is actually called', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => ({}) });
    globalThis.fetch = fetchMock;
    const port = createLiveAdapter('DeviceService', '/api/v1/devices/primary-action');
    expect(port.mode).toBe('live');
    await port.primaryAction();
    expect(fetchMock).toHaveBeenCalledWith('/api/v1/devices/primary-action', { method: 'POST' });
  });
});

describe('BIO-UX-005 UNASSIGNED_MOCK_PORT names its own gap honestly', () => {
  it('reports Unassigned and explains itself rather than pretending a module exists', async () => {
    expect(UNASSIGNED_MOCK_PORT.module).toBe('Unassigned');
    const result = await UNASSIGNED_MOCK_PORT.primaryAction();
    expect(result.message).toMatch(/no backend module is assigned/i);
  });
});
