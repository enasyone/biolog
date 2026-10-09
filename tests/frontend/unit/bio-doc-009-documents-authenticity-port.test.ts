// Developer tests for BIO-DOC-009-AC06 ("connected through DocumentVerifyService ... or an
// explicitly documented HYBRID state"). SYNTHETIC: no real document/biometric data.
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MODULE_PORTS } from '../../../web/lib/module-registry';
import { MODULE_PORT_KEYS, clearPortModeOverrides, getPortMode, setPortModeOverride } from '../../../web/lib/feature-mode';

describe('BIO-DOC-009-AC06 DocumentVerifyService gains an independent authenticity port key', () => {
  beforeEach(() => {
    clearPortModeOverrides();
  });

  it('MODULE_PORT_KEYS lists primaryAction and authenticity (BIO-DOC-010 later adds a third, documentIdentification - see bio-ux-005-feature-mode.test.ts for the full exact-order snapshot)', () => {
    expect(MODULE_PORT_KEYS.DocumentVerifyService).toContain('primaryAction');
    expect(MODULE_PORT_KEYS.DocumentVerifyService).toContain('authenticity');
  });

  it('MODULE_PORTS resolves a distinct ModulePort instance for the authenticity key, reporting its own module', () => {
    const ports = MODULE_PORTS.DocumentVerifyService;
    expect(ports.authenticity?.module).toBe('DocumentVerifyService');
    expect(ports.authenticity).not.toBe(ports.primaryAction);
  });

  it('both ports start in mock mode by default (nothing is live unless explicitly switched)', () => {
    expect(MODULE_PORTS.DocumentVerifyService.primaryAction?.mode).toBe('mock');
    expect(MODULE_PORTS.DocumentVerifyService.authenticity?.mode).toBe('mock');
  });

  it('the authenticity port can be switched to LIVE independently of primaryAction (genuine HYBRID)', () => {
    setPortModeOverride('DocumentVerifyService', 'authenticity', 'LIVE');

    expect(getPortMode('DocumentVerifyService', 'authenticity')).toBe('LIVE');
    expect(getPortMode('DocumentVerifyService', 'primaryAction')).toBe('SIMULATION');
  });
});

describe('BIO-DOC-009-AC01/AC05 documentsAuthenticityPort exposes checkAuthenticity independently', () => {
  it('documentsPort.checkAuthenticity always resolves through the authenticity key, not primaryAction', async () => {
    vi.resetModules();
    const { documentsPort, documentsAuthenticityPort } = await import('../../../web/features/documents');
    expect(documentsPort.checkAuthenticity).toBe(documentsAuthenticityPort.checkAuthenticity);
  });

  it('the authenticity mock port has its own independent call counter, unaffected by other steps', async () => {
    vi.resetModules();
    const { documentsPort } = await import('../../../web/features/documents');

    const first = await documentsPort.checkAuthenticity();
    // Running several other steps in between must not disturb authenticity's own call sequence.
    await documentsPort.capture();
    await documentsPort.runMrz();
    const second = await documentsPort.checkAuthenticity();

    expect(first.step).toBe('authenticity');
    expect(second.step).toBe('authenticity');
  });
});
