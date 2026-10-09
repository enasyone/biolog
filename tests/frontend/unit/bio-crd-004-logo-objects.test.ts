// Developer tests for BIO-CRD-004. SYNTHETIC fixtures only.
import { describe, expect, it } from 'vitest';
import { createLogoElement, isLogoElement, setContent } from '../../../web/features/bio-crd-004/logoObjects';

describe('logo card object', () => {
  it('content is an opaque reference, not validated here', () => {
    const element = createLogoElement('l1', 'asset-ref-123', { xPx: 0, yPx: 0, widthPx: 30, heightPx: 30 }, 0);
    expect(element.content).toBe('asset-ref-123');
    expect(isLogoElement(element)).toBe(true);
  });

  it('setContent replaces the reference without touching geometry', () => {
    let element = createLogoElement('l1', 'asset-ref-123', { xPx: 5, yPx: 5, widthPx: 30, heightPx: 30 }, 0);
    element = setContent(element, 'asset-ref-456');
    expect(element.content).toBe('asset-ref-456');
    expect(element.xPx).toBe(5);
  });
});
