/**
 * BIO-CRD-004-AC05: "Conditional visibility supports profile/card class rules." Shared by every
 * card object this story defines (barcode, signature, logo) - a visibility rule is two
 * independent, optional allowlists: which card classes an object shows for, and which barcode
 * profiles it applies to (relevant for a barcode object placed conditionally per symbology; a
 * signature/logo object simply leaves `profiles` as `null`, meaning "not profile-restricted").
 * `null` on either list means "no restriction on that dimension", not "match nothing" - an
 * object with both lists `null` is always visible, matching how BIO-CRD-002/003's own canvas
 * elements behave today (always visible) when this story is not applied.
 */

export type CardClass = 'staff' | 'student' | 'visitor';

export const CARD_CLASSES: readonly CardClass[] = Object.freeze(['staff', 'student', 'visitor']);

export interface VisibilityRule {
  readonly cardClasses: readonly CardClass[] | null;
  readonly profiles: readonly string[] | null;
}

export const ALWAYS_VISIBLE: VisibilityRule = Object.freeze({ cardClasses: null, profiles: null });

export interface VisibilityContext {
  readonly cardClass: CardClass;
  readonly profile?: string;
}

export function isVisible(rule: VisibilityRule, context: VisibilityContext): boolean {
  const classOk = rule.cardClasses === null || rule.cardClasses.includes(context.cardClass);
  const profileOk = rule.profiles === null || (context.profile !== undefined && rule.profiles.includes(context.profile));
  return classOk && profileOk;
}

export function withCardClasses(rule: VisibilityRule, cardClasses: readonly CardClass[] | null): VisibilityRule {
  return { ...rule, cardClasses };
}

export function withProfiles(rule: VisibilityRule, profiles: readonly string[] | null): VisibilityRule {
  return { ...rule, profiles };
}
