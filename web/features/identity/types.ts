/**
 * Identity domain wire types (BIO-UX-006-AC06 "stable integration seam").
 *
 * These shapes are what `IdentityPort`'s real methods accept/return — the
 * same shapes `mock.ts` (today) and `live.ts` (once a real backend exists)
 * both implement, so a page component never changes when LIVE mode is
 * switched on.
 */
import type { PersonCategory, PersonLifecycleStatus } from '../../lib/synthetic-data/types';

export type { PersonCategory, PersonLifecycleStatus };

export interface PersonSummary {
  id: string;
  tenantId: string;
  displayName: string;
  category: PersonCategory;
  lifecycleStatus: PersonLifecycleStatus;
  /** True when this person is linked into an unresolved duplicate group (AC03). */
  hasDuplicateFlag: boolean;
}

export interface PersonDetail extends PersonSummary {
  firstName: string;
  lastName: string;
  dateOfBirth: string;
  credential?: { cardNumber: string; status: string };
  photo?: { qualityScore: number; belowQualityThreshold: boolean; placeholderColor: string };
}

export interface ListPeopleInput {
  tenantId: string;
  /** Omitted or `'all'` returns every category (the People page). */
  category?: PersonCategory | 'all';
}

export interface ListPeopleResult {
  people: readonly PersonSummary[];
}

export interface GetPersonInput {
  tenantId: string;
  personId: string;
}

export interface GetPersonResult {
  person: PersonDetail | null;
}

export interface LifecycleActionInput {
  tenantId: string;
  personId: string;
}

export interface EnrolmentDraft {
  tenantId: string;
  firstName: string;
  lastName: string;
  dateOfBirth: string;
  category: PersonCategory;
  photoCaptured: boolean;
  biometricCaptured: boolean;
  credentialAssigned: boolean;
}

export interface EnrolmentResult {
  outcome: 'success' | 'failure' | 'offline';
  message: string;
  personId?: string;
}

export interface DuplicateCandidate {
  personId: string;
  displayName: string;
  category: PersonCategory;
}

export interface DuplicateGroup {
  groupId: string;
  tenantId: string;
  candidates: readonly DuplicateCandidate[];
}

export interface ListDuplicatesInput {
  tenantId: string;
}

export interface ListDuplicatesResult {
  groups: readonly DuplicateGroup[];
}

export interface ResolveDuplicateInput {
  tenantId: string;
  groupId: string;
  /** Which candidate in the group survives; every other candidate is removed. */
  keepPersonId: string;
}
