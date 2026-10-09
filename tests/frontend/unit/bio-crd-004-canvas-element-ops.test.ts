// Developer tests for BIO-CRD-004. SYNTHETIC fixtures only.
import { describe, expect, it } from 'vitest';
import { bringToFront, moveElement, rotateElement, sendToBack, setLocked } from '../../../web/features/bio-crd-004/canvasElementOps';
import { createBarcodeElement } from '../../../web/features/bio-crd-004/barcodeObjects';
import { createSignatureElement } from '../../../web/features/bio-crd-004/signatureObjects';
import { createLogoElement } from '../../../web/features/bio-crd-004/logoObjects';

describe('shared generic element operations work identically across all three of this story\'s element kinds', () => {
  it('move/rotate/lock apply the same way to a barcode, a signature and a logo', () => {
    const barcode = createBarcodeElement('b1', { xPx: 0, yPx: 0, widthPx: 10, heightPx: 10 }, 0);
    const signature = createSignatureElement('s1', 'lineage-1', 1, { xPx: 0, yPx: 0, widthPx: 10, heightPx: 10 }, 1);
    const logo = createLogoElement('l1', 'ref', { xPx: 0, yPx: 0, widthPx: 10, heightPx: 10 }, 2);

    for (const element of [barcode, signature, logo]) {
      const moved = moveElement(element, 5, 5);
      expect(moved.xPx).toBe(5);
      expect(moved.yPx).toBe(5);

      const rotated = rotateElement(element, 45);
      expect(rotated.rotationDeg).toBe(45);

      const locked = setLocked(element, true);
      const refusedMove = moveElement(locked, 5, 5);
      expect(refusedMove.xPx).toBe(element.xPx); // locked refuses move
    }
  });

  it('layer reordering works across a mixed array of all three element kinds', () => {
    const barcode = createBarcodeElement('b1', { xPx: 0, yPx: 0, widthPx: 10, heightPx: 10 }, 0);
    const signature = createSignatureElement('s1', 'lineage-1', 1, { xPx: 0, yPx: 0, widthPx: 10, heightPx: 10 }, 1);
    const logo = createLogoElement('l1', 'ref', { xPx: 0, yPx: 0, widthPx: 10, heightPx: 10 }, 2);

    const reordered = bringToFront([barcode, signature, logo], 'b1');
    expect(reordered[reordered.length - 1]!.id).toBe('b1');

    const sentBack = sendToBack(reordered, 'l1');
    expect(sentBack[0]!.id).toBe('l1');
  });
});
