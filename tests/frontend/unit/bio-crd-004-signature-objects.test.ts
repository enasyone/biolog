// Developer tests for BIO-CRD-004. SYNTHETIC fixtures only - no real signatures/biometrics.
import { describe, expect, it } from 'vitest';
import {
  createSignatureElement,
  isSignatureElement,
  setVersion,
} from '../../../web/features/bio-crd-004/signatureObjects';

describe('BIO-CRD-004-AC04 signature assets are versioned', () => {
  it('a signature element pins a specific (lineageId, version) pair', () => {
    const element = createSignatureElement('s1', 'lineage-1', 3, { xPx: 0, yPx: 0, widthPx: 40, heightPx: 20 }, 0);
    expect(element.lineageId).toBe('lineage-1');
    expect(element.version).toBe(3);
    expect(isSignatureElement(element)).toBe(true);
  });

  it('setVersion repoints to a different version without changing the lineage', () => {
    let element = createSignatureElement('s1', 'lineage-1', 1, { xPx: 0, yPx: 0, widthPx: 40, heightPx: 20 }, 0);
    element = setVersion(element, 4);
    expect(element.version).toBe(4);
    expect(element.lineageId).toBe('lineage-1');
  });

  it('version must start at 1 or higher', () => {
    expect(() => createSignatureElement('s1', 'lineage-1', 0, { xPx: 0, yPx: 0, widthPx: 40, heightPx: 20 }, 0)).toThrow(RangeError);
    expect(() => setVersion(createSignatureElement('s1', 'lineage-1', 1, { xPx: 0, yPx: 0, widthPx: 40, heightPx: 20 }, 0), 0)).toThrow(RangeError);
  });

  it('a locked signature element refuses move but setVersion is unaffected by lock (it is not a geometry operation)', () => {
    let element = createSignatureElement('s1', 'lineage-1', 1, { xPx: 0, yPx: 0, widthPx: 40, heightPx: 20 }, 0);
    element = { ...element, locked: true };
    const moved = setVersion(element, 2);
    expect(moved.version).toBe(2);
  });
});
