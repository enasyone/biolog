// Developer tests for BIO-RPT-003 (AC01/AC03/AC05). SYNTHETIC fixtures only.
import { describe, expect, it } from 'vitest';
import { MODULE_PORTS } from '../../../web/lib/module-registry';
import { MODULE_PORT_KEYS, clearPortModeOverrides, getPortMode, setPortModeOverride } from '../../../web/lib/feature-mode';
import { auditInvestigationSearchMockPort, auditInvestigationExportMockPort } from '../../../web/features/bio-rpt-003/mock';

describe('BIO-RPT-003-AC05 ReportingService gains two independent audit-investigation port keys', () => {
  it('MODULE_PORT_KEYS lists searchAuditEvents and exportAuditEvents alongside every existing key', () => {
    expect([...MODULE_PORT_KEYS.ReportingService]).toContain('searchAuditEvents');
    expect([...MODULE_PORT_KEYS.ReportingService]).toContain('exportAuditEvents');
  });

  it('MODULE_PORTS resolves a distinct ModulePort instance for each new key, reporting ReportingService', () => {
    const ports = MODULE_PORTS.ReportingService;
    expect(ports.searchAuditEvents?.module).toBe('ReportingService');
    expect(ports.exportAuditEvents?.module).toBe('ReportingService');
    expect(ports.searchAuditEvents).not.toBe(ports.viewAuditTrail);
    expect(ports.exportAuditEvents).not.toBe(ports.searchAuditEvents);
  });

  it('both new ports start in mock mode by default', () => {
    expect(MODULE_PORTS.ReportingService.searchAuditEvents?.mode).toBe('mock');
    expect(MODULE_PORTS.ReportingService.exportAuditEvents?.mode).toBe('mock');
  });

  it('searchAuditEvents can be switched to LIVE independently of viewAuditTrail (genuine HYBRID)', () => {
    clearPortModeOverrides();
    setPortModeOverride('ReportingService', 'searchAuditEvents', 'LIVE');

    expect(getPortMode('ReportingService', 'searchAuditEvents')).toBe('LIVE');
    expect(getPortMode('ReportingService', 'viewAuditTrail')).toBe('SIMULATION');
    clearPortModeOverrides();
  });
});

describe('BIO-RPT-003-AC01 auditInvestigationSearchMockPort filters', () => {
  it('an empty filter returns every synthetic record', async () => {
    const result = await auditInvestigationSearchMockPort.primaryAction({});
    expect(result.outcome).toBe('success');
    expect(result.records?.length).toBeGreaterThan(1);
    expect(result.totalCount).toBe(result.records?.length);
  });

  it('filters by actor id', async () => {
    const result = await auditInvestigationSearchMockPort.primaryAction({ actorId: 'operator-2' });
    expect(result.records?.every((record) => record.actor.id === 'operator-2')).toBe(true);
    expect(result.records?.length).toBe(1);
  });

  it('filters by person id', async () => {
    const result = await auditInvestigationSearchMockPort.primaryAction({ personId: 'per_00000000-0000-7000-8000-000000000201' });
    expect(result.records?.length).toBe(1);
    expect(result.records?.[0]?.action).toBe('rbac.role_assign');
  });

  it('filters by action', async () => {
    const result = await auditInvestigationSearchMockPort.primaryAction({ action: 'card_template.publish' });
    expect(result.records?.length).toBe(1);
  });

  it('filters by device id - the one record with a real (non-null) device value in this fixture', async () => {
    const result = await auditInvestigationSearchMockPort.primaryAction({ deviceId: 'dev_printer_1' });
    expect(result.records?.length).toBe(1);
    expect(result.records?.[0]?.device?.deviceId).toBe('dev_printer_1');
  });

  it('filters by reason code', async () => {
    const result = await auditInvestigationSearchMockPort.primaryAction({ reasonCode: 'card_template_published' });
    expect(result.records?.length).toBe(1);
  });

  it('filters by an inclusive occurred_at time range', async () => {
    const result = await auditInvestigationSearchMockPort.primaryAction({ occurredFrom: '2026-09-21T00:00:00.000Z', occurredTo: '2026-09-21T23:59:59.999Z' });
    expect(result.records?.length).toBe(1);
    expect(result.records?.[0]?.sourceFeature).toBe('card_production');
  });

  it('a filter matching nothing returns an empty, not a failed, result', async () => {
    const result = await auditInvestigationSearchMockPort.primaryAction({ reasonCode: 'no_such_reason' });
    expect(result.outcome).toBe('success');
    expect(result.records).toEqual([]);
    expect(result.totalCount).toBe(0);
  });
});

describe('BIO-RPT-003-AC03 auditInvestigationExportMockPort', () => {
  it('exports the same filtered set as search, as CRLF-terminated CSV', async () => {
    const result = await auditInvestigationExportMockPort.primaryAction({ action: 'rbac.role_assign' });
    expect(result.outcome).toBe('success');
    expect(result.csv).toContain('\r\n');
    expect(result.csv).toContain('rbac.role_assign');
    expect(result.csv).not.toContain('card_template.publish');
  });
});
