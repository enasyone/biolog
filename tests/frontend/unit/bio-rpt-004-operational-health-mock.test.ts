// Developer tests for BIO-RPT-004 (AC01/AC03/AC05). SYNTHETIC fixtures only.
import { describe, expect, it } from 'vitest';
import { MODULE_PORTS } from '../../../web/lib/module-registry';
import { MODULE_PORT_KEYS, clearPortModeOverrides, getPortMode, setPortModeOverride } from '../../../web/lib/feature-mode';
import { operationalHealthMockPort } from '../../../web/features/bio-rpt-004/mock';

describe('BIO-RPT-004-AC05 ReportingService gains an operationalHealth port key', () => {
  it('MODULE_PORT_KEYS lists operationalHealth alongside every existing key', () => {
    expect([...MODULE_PORT_KEYS.ReportingService]).toContain('operationalHealth');
  });

  it('MODULE_PORTS resolves a distinct ModulePort instance, reporting ReportingService', () => {
    const ports = MODULE_PORTS.ReportingService;
    expect(ports.operationalHealth?.module).toBe('ReportingService');
    expect(ports.operationalHealth).not.toBe(ports.searchAuditEvents);
    expect(ports.operationalHealth).not.toBe(ports.generateReport);
  });

  it('starts in mock mode by default', () => {
    expect(MODULE_PORTS.ReportingService.operationalHealth?.mode).toBe('mock');
  });

  it('operationalHealth can be switched to LIVE independently of every other ReportingService port (genuine HYBRID)', () => {
    clearPortModeOverrides();
    setPortModeOverride('ReportingService', 'operationalHealth', 'LIVE');

    expect(getPortMode('ReportingService', 'operationalHealth')).toBe('LIVE');
    expect(getPortMode('ReportingService', 'viewAuditTrail')).toBe('SIMULATION');
    expect(getPortMode('ReportingService', 'searchAuditEvents')).toBe('SIMULATION');
    clearPortModeOverrides();
  });
});

describe('BIO-RPT-004-AC01/AC03 operationalHealthMockPort', () => {
  it('returns a snapshot covering all six domains', async () => {
    const result = await operationalHealthMockPort.primaryAction();
    expect(result.outcome).toBe('success');
    const snapshot = result.snapshot!;
    expect(snapshot.identity.total).toBeGreaterThan(0);
    expect(snapshot.access.total).toBeGreaterThan(0);
    expect(snapshot.production.total).toBeGreaterThan(0);
    expect(snapshot.card.reason.length).toBeGreaterThan(0);
    expect(snapshot.device.reason.length).toBeGreaterThan(0);
    expect(snapshot.sync.reason.length).toBeGreaterThan(0);
  });

  it('AC03/F02: identity/access/production mirror the LIVE happy path (live), card/device/sync are unavailable - never stale', async () => {
    const result = await operationalHealthMockPort.primaryAction();
    const snapshot = result.snapshot!;
    expect(snapshot.identity.freshness).toBe('live');
    expect(snapshot.access.freshness).toBe('live');
    expect(snapshot.production.freshness).toBe('live');
    expect(snapshot.card.freshness).toBe('unavailable');
    expect(snapshot.device.freshness).toBe('unavailable');
    expect(snapshot.sync.freshness).toBe('unavailable');
  });

  it('AC03: every panel carries its own observedAt timestamp', async () => {
    const result = await operationalHealthMockPort.primaryAction();
    const snapshot = result.snapshot!;
    expect(snapshot.identity.observedAt.length).toBeGreaterThan(0);
    expect(snapshot.access.observedAt.length).toBeGreaterThan(0);
    expect(snapshot.production.observedAt.length).toBeGreaterThan(0);
    expect(snapshot.card.observedAt.length).toBeGreaterThan(0);
    expect(snapshot.device.observedAt.length).toBeGreaterThan(0);
    expect(snapshot.sync.observedAt.length).toBeGreaterThan(0);
  });
});
