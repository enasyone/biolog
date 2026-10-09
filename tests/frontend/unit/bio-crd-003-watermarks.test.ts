// Developer tests for BIO-CRD-003-AC04/AC05. SYNTHETIC: no real card designs.
import { describe, expect, it } from 'vitest';
import { bringToFront, rotateElement, sendToBack } from '../../../web/features/bio-crd-003/canvasElementOps';
import {
  WATERMARK_PRESETS,
  createWatermarkElement,
  createWatermarkFromPreset,
  isWatermarkElement,
  setOpacity,
} from '../../../web/features/bio-crd-003/watermarks';

const POSITION = { xPx: 0, yPx: 0, widthPx: 300, heightPx: 200 };

describe('BIO-CRD-003-AC04 text and image watermarks support 0-100% opacity, rotation and layer control', () => {
  it('creates a text watermark with the given content and opacity', () => {
    const watermark = createWatermarkElement('w1', 'text', 'CONFIDENTIAL', POSITION, 0, 40);
    expect(watermark.watermarkKind).toBe('text');
    expect(watermark.content).toBe('CONFIDENTIAL');
    expect(watermark.opacityPercent).toBe(40);
  });

  it('creates an image watermark identically, distinguished only by watermarkKind', () => {
    const watermark = createWatermarkElement('w2', 'image', 'asset://logo-watermark', POSITION, 0, 25);
    expect(watermark.watermarkKind).toBe('image');
    expect(watermark.content).toBe('asset://logo-watermark');
  });

  it('accepts the full 0-100 opacity range, inclusive', () => {
    expect(createWatermarkElement('w', 'text', 'X', POSITION, 0, 0).opacityPercent).toBe(0);
    expect(createWatermarkElement('w', 'text', 'X', POSITION, 0, 100).opacityPercent).toBe(100);
  });

  it('rejects an opacity outside 0-100 at creation', () => {
    expect(() => createWatermarkElement('w', 'text', 'X', POSITION, 0, -1)).toThrow(RangeError);
    expect(() => createWatermarkElement('w', 'text', 'X', POSITION, 0, 101)).toThrow(RangeError);
  });

  it('rejects an opacity outside 0-100 when changed afterwards', () => {
    const watermark = createWatermarkElement('w', 'text', 'X', POSITION, 0);
    expect(() => setOpacity(watermark, -5)).toThrow(RangeError);
    expect(() => setOpacity(watermark, 105)).toThrow(RangeError);
  });

  it('rotation is supported via the shared element ops (inherited, not reimplemented)', () => {
    const watermark = createWatermarkElement('w', 'text', 'X', POSITION, 0);
    const rotated = rotateElement(watermark, 45);
    expect(rotated.rotationDeg).toBe(45);
    // Still a real WatermarkElement, not merely a BaseElement - the generic op preserves it.
    expect(isWatermarkElement(rotated)).toBe(true);
    expect(rotated.content).toBe('X');
  });

  it('layer control is supported via the shared element ops (inherited, not reimplemented)', () => {
    const back = createWatermarkElement('back', 'text', 'BACK', POSITION, 0);
    const front = createWatermarkElement('front', 'text', 'FRONT', POSITION, 1);
    const reordered = bringToFront([back, front], 'back');
    expect(reordered.map((w) => w.id)).toEqual(['front', 'back']);

    const sentBack = sendToBack(reordered, 'front');
    expect(sentBack.map((w) => w.id)).toEqual(['front', 'back']);
  });

  it('a locked watermark refuses rotation, like any other locked element', () => {
    const locked = { ...createWatermarkElement('w', 'text', 'X', POSITION, 0), locked: true };
    expect(rotateElement(locked, 90)).toBe(locked);
  });
});

describe('BIO-CRD-003-AC05 STAFF/STUDENT/VISITOR starter watermark behaviors are available', () => {
  it('names all three starter categories', () => {
    expect(Object.keys(WATERMARK_PRESETS).sort()).toEqual(['staff', 'student', 'visitor']);
  });

  it('mirrors the backend catalogue exact values', () => {
    expect(WATERMARK_PRESETS.staff).toEqual({ text: 'STAFF', defaultOpacityPercent: 20, defaultRotationDeg: -30 });
    expect(WATERMARK_PRESETS.student).toEqual({ text: 'STUDENT', defaultOpacityPercent: 20, defaultRotationDeg: -30 });
    expect(WATERMARK_PRESETS.visitor).toEqual({ text: 'VISITOR', defaultOpacityPercent: 35, defaultRotationDeg: -30 });
  });

  it('creates a real watermark element from a preset, applying its starter opacity and rotation', () => {
    const watermark = createWatermarkFromPreset('w1', 'student', POSITION, 0);
    expect(watermark.content).toBe('STUDENT');
    expect(watermark.opacityPercent).toBe(20);
    expect(watermark.rotationDeg).toBe(330); // -30 normalised to [0, 360)
  });

  it('a preset is a starting point, not locked - opacity can still be changed afterwards', () => {
    const watermark = createWatermarkFromPreset('w1', 'visitor', POSITION, 0);
    const restyled = setOpacity(watermark, 60);
    expect(restyled.opacityPercent).toBe(60);
  });
});
