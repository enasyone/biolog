// Developer tests for BIO-DOC-010-AC05 ("connected through DocumentVerifyService ... or an
// explicitly documented HYBRID state"). SYNTHETIC: no real document/biometric data.
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MODULE_PORTS } from '../../../web/lib/module-registry';
import { MODULE_PORT_KEYS, clearPortModeOverrides, getPortMode, setPortModeOverride } from '../../../web/lib/feature-mode';

describe('BIO-DOC-010-AC05 DocumentVerifyService gains a third, independent documentIdentification port key', () => {
  beforeEach(() => {
    clearPortModeOverrides();
  });

  it('MODULE_PORT_KEYS lists exactly primaryAction, authenticity and documentIdentification, in that order', () => {
    expect([...MODULE_PORT_KEYS.DocumentVerifyService]).toEqual(['primaryAction', 'authenticity', 'documentIdentification']);
  });

  it('MODULE_PORTS resolves a distinct ModulePort instance for the documentIdentification key, reporting its own module', () => {
    const ports = MODULE_PORTS.DocumentVerifyService;
    expect(Object.keys(ports).sort()).toEqual(['authenticity', 'documentIdentification', 'primaryAction']);
    expect(ports.documentIdentification?.module).toBe('DocumentVerifyService');
    expect(ports.documentIdentification).not.toBe(ports.primaryAction);
    expect(ports.documentIdentification).not.toBe(ports.authenticity);
  });

  it('all three ports start in mock mode by default (nothing is live unless explicitly switched)', () => {
    expect(MODULE_PORTS.DocumentVerifyService.primaryAction?.mode).toBe('mock');
    expect(MODULE_PORTS.DocumentVerifyService.authenticity?.mode).toBe('mock');
    expect(MODULE_PORTS.DocumentVerifyService.documentIdentification?.mode).toBe('mock');
  });

  it('the documentIdentification port can be switched to LIVE independently of primaryAction and authenticity', () => {
    // The reason this independence matters (not just a mechanical repeat of BIO-DOC-009's own
    // test): identifyDocument is gated on capture having already succeeded. If this key shared
    // primaryAction's mode, switching it LIVE would also switch capture LIVE - and capture has no
    // backend yet, so it would always fail and permanently block identifyDocument from ever
    // running (see feature-mode.ts's own MODULE_PORT_KEYS comment).
    setPortModeOverride('DocumentVerifyService', 'documentIdentification', 'LIVE');

    expect(getPortMode('DocumentVerifyService', 'documentIdentification')).toBe('LIVE');
    expect(getPortMode('DocumentVerifyService', 'primaryAction')).toBe('SIMULATION');
    expect(getPortMode('DocumentVerifyService', 'authenticity')).toBe('SIMULATION');
  });
});

describe('BIO-DOC-010-AC01/AC05 documentsDocumentIdentificationPort exposes identifyDocument independently', () => {
  it('documentsPort.identifyDocument always resolves through the documentIdentification key, not primaryAction', async () => {
    vi.resetModules();
    const { documentsPort, documentsDocumentIdentificationPort } = await import('../../../web/features/documents');
    expect(documentsPort.identifyDocument).toBe(documentsDocumentIdentificationPort.identifyDocument);
  });

  it('the documentIdentification mock port has its own independent call counter, unaffected by other steps', async () => {
    vi.resetModules();
    const { documentsPort } = await import('../../../web/features/documents');

    const first = await documentsPort.identifyDocument();
    // Running several other steps in between must not disturb this step's own call sequence.
    await documentsPort.capture();
    await documentsPort.checkAuthenticity();
    const second = await documentsPort.identifyDocument();

    expect(first.step).toBe('document-identification');
    expect(second.step).toBe('document-identification');
  });
});
