// Developer tests for BIO-CRD-004. SYNTHETIC fixtures only.
import { describe, expect, it } from 'vitest';
import {
  ALWAYS_VISIBLE,
  CARD_CLASSES,
  isVisible,
  withCardClasses,
  withProfiles,
  type VisibilityRule,
} from '../../../web/features/bio-crd-004/visibility';

describe('BIO-CRD-004-AC05 conditional visibility supports profile/card class rules', () => {
  it('a rule with no restrictions is always visible, for every card class', () => {
    for (const cardClass of CARD_CLASSES) {
      expect(isVisible(ALWAYS_VISIBLE, { cardClass })).toBe(true);
    }
  });

  it('a card-class allowlist hides the object for classes not on the list', () => {
    const rule: VisibilityRule = withCardClasses(ALWAYS_VISIBLE, ['staff']);
    expect(isVisible(rule, { cardClass: 'staff' })).toBe(true);
    expect(isVisible(rule, { cardClass: 'student' })).toBe(false);
    expect(isVisible(rule, { cardClass: 'visitor' })).toBe(false);
  });

  it('a profile allowlist hides the object when no profile is supplied in context', () => {
    const rule: VisibilityRule = withProfiles(ALWAYS_VISIBLE, ['qr']);
    expect(isVisible(rule, { cardClass: 'staff' })).toBe(false);
    expect(isVisible(rule, { cardClass: 'staff', profile: 'qr' })).toBe(true);
    expect(isVisible(rule, { cardClass: 'staff', profile: 'alphanumeric' })).toBe(false);
  });

  it('card-class and profile restrictions combine with AND semantics, not OR', () => {
    let rule = withCardClasses(ALWAYS_VISIBLE, ['visitor']);
    rule = withProfiles(rule, ['qr']);

    expect(isVisible(rule, { cardClass: 'visitor', profile: 'qr' })).toBe(true);
    expect(isVisible(rule, { cardClass: 'staff', profile: 'qr' })).toBe(false); // wrong class
    expect(isVisible(rule, { cardClass: 'visitor', profile: 'alphanumeric' })).toBe(false); // wrong profile
  });

  it('withCardClasses/withProfiles never mutate the rule passed in', () => {
    const original = ALWAYS_VISIBLE;
    withCardClasses(original, ['staff']);
    expect(original.cardClasses).toBeNull();
  });
});
