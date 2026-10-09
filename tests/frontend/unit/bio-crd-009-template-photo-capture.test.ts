// Developer tests for BIO-CRD-009-AC04: a placed photo element is correctly captured into
// BIO-CRD-008's CardTemplateSnapshot, the same way a logo already is. SYNTHETIC data only.
import { describe, expect, it } from 'vitest';
import { createInitialDesignState } from '../../../web/features/bio-crd-002/cardDesignState';
import { buildSnapshot } from '../../../web/features/bio-crd-008/TemplateLifecycleBar';
import { addPhotoOn, createInitialPhotoState } from '../../../web/features/bio-crd-009/photoDesignState';
import { createPhotoElement } from '../../../web/features/bio-crd-009/photoElement';

const DESIGN = createInitialDesignState('cr80', 'landscape');
const NO_PHOTO_SPEC = { required: false };

describe('BIO-CRD-009-AC04 a placed photo element is captured into CardTemplateSnapshot', () => {
  it('a front photo appears in frontElements with kind photo and its content/geometry', () => {
    const photoState = addPhotoOn(createInitialPhotoState(), 'front', createPhotoElement('p1', 'cardholder-photo-1', { xPx: 5, yPx: 6, widthPx: 90, heightPx: 110 }, 0));
    const snapshot = buildSnapshot(DESIGN, [], [], NO_PHOTO_SPEC, photoState);

    const captured = snapshot.frontElements.find((e) => e.kind === 'photo');
    expect(captured).toBeDefined();
    expect(captured!.text).toBe('cardholder-photo-1');
    expect(captured!.xPx).toBe(5);
    expect(captured!.yPx).toBe(6);
    expect(snapshot.backElements.some((e) => e.kind === 'photo')).toBe(false);
  });

  it('a back photo appears in backElements only, independent of the front side', () => {
    const photoState = addPhotoOn(createInitialPhotoState(), 'back', createPhotoElement('p1', 'back-photo', { xPx: 1, yPx: 1, widthPx: 90, heightPx: 110 }, 0));
    const snapshot = buildSnapshot(DESIGN, [], [], NO_PHOTO_SPEC, photoState);

    expect(snapshot.frontElements.some((e) => e.kind === 'photo')).toBe(false);
    const captured = snapshot.backElements.find((e) => e.kind === 'photo');
    expect(captured?.text).toBe('back-photo');
  });

  it('photos on both sides are both captured, each on its own side only', () => {
    let photoState = createInitialPhotoState();
    photoState = addPhotoOn(photoState, 'front', createPhotoElement('front-1', 'front-photo', { xPx: 0, yPx: 0, widthPx: 90, heightPx: 110 }, 0));
    photoState = addPhotoOn(photoState, 'back', createPhotoElement('back-1', 'back-photo', { xPx: 0, yPx: 0, widthPx: 90, heightPx: 110 }, 0));
    const snapshot = buildSnapshot(DESIGN, [], [], NO_PHOTO_SPEC, photoState);

    expect(snapshot.frontElements.filter((e) => e.kind === 'photo')).toHaveLength(1);
    expect(snapshot.backElements.filter((e) => e.kind === 'photo')).toHaveLength(1);
  });

  it('omitting photoElements entirely (every pre-existing caller) still produces a valid snapshot with no photo elements', () => {
    const snapshot = buildSnapshot(DESIGN, [], [], NO_PHOTO_SPEC);
    expect(snapshot.frontElements.some((e) => e.kind === 'photo')).toBe(false);
    expect(snapshot.backElements.some((e) => e.kind === 'photo')).toBe(false);
  });

  it('the pre-existing top-level photo spec field (required/aspectRatio) is untouched by photoElements', () => {
    const photoState = addPhotoOn(createInitialPhotoState(), 'front', createPhotoElement('p1', 'ref', { xPx: 0, yPx: 0, widthPx: 90, heightPx: 110 }, 0));
    const snapshot = buildSnapshot(DESIGN, [], [], { required: true, aspectRatio: '3:4' }, photoState);
    expect(snapshot.photo).toEqual({ required: true, aspectRatio: '3:4' });
  });
});
