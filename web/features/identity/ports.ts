import type { ModulePort } from '../../lib/module-port';
import type {
  EnrolmentDraft,
  EnrolmentResult,
  GetPersonInput,
  GetPersonResult,
  LifecycleActionInput,
  ListDuplicatesInput,
  ListDuplicatesResult,
  ListPeopleInput,
  ListPeopleResult,
  ResolveDuplicateInput,
} from './types';
import type { PrimaryActionResult } from '../../lib/module-port';

/**
 * Identity module port (BIO-UX-005-AC01, extended by BIO-UX-006-AC06).
 *
 * `mock.ts`/`live.ts` are the only two files that implement this interface;
 * every page in `web/app/{people,students,staff,alumni,contractors,
 * enrolment,duplicate-review}` calls the resolved `identityPort` from
 * `./index`, never `./mock` or `./live` directly, so switching
 * `NEXT_PUBLIC_FEATURE_MODE_IDENTITYSERVICE` to `LIVE` is the only change
 * needed to point every one of these screens at a real backend, without
 * touching any page.
 */
export interface IdentityPort extends ModulePort {
  listPeople(input: ListPeopleInput): Promise<ListPeopleResult>;
  getPerson(input: GetPersonInput): Promise<GetPersonResult>;
  suspendPerson(input: LifecycleActionInput): Promise<PrimaryActionResult>;
  reinstatePerson(input: LifecycleActionInput): Promise<PrimaryActionResult>;
  submitEnrolment(draft: EnrolmentDraft): Promise<EnrolmentResult>;
  listDuplicates(input: ListDuplicatesInput): Promise<ListDuplicatesResult>;
  resolveDuplicate(input: ResolveDuplicateInput): Promise<PrimaryActionResult>;
}
