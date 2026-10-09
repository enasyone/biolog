import type { ModulePort, PrimaryActionResult } from '../../lib/module-port';

/**
 * BIO-INT-005's own dedicated connection: external import conflict review (AC01-AC04), backing
 * a new "Conflict Review" panel on `/administration/integrations` mounted ALONGSIDE BIO-INT-002's
 * `ImportConsole`, BIO-INT-003's `ServiceAccountsPanel` and BIO-INT-004's
 * `WebhookSubscriptionsPanel` - same precedent `web/features/bio-int-004/ports.ts` already
 * documents for its own new `IntegrationService` ports next to BIO-INT-002/003's existing ones.
 *
 * `/settings/authentication` is deliberately out of scope - the same disclosed boundary every
 * sibling story adding `IntegrationService` ports already states (BIO-INT-004-QA-01). A conflict
 * review has no relationship to authentication settings, so there is nothing for this story to
 * wire there.
 */
export type ImportConflictReviewStatusWire = 'PENDING_REVIEW' | 'KEEP_EXISTING' | 'USE_SOURCE' | 'ESCALATED' | 'WITHDRAWN';

/** AC02: the three outcomes an operator can choose - see `ImportConflictReviewStatus`'s own backend header for why `USE_SOURCE` records a decision but never applies anything (AC04 - no PERSON field-mutator exists anywhere in this codebase). */
export type ImportConflictDecision = 'KEEP_EXISTING' | 'USE_SOURCE' | 'ESCALATED';

export interface ImportFieldConflictSummary {
  personField: string;
  existingValue: string | null;
  incomingValue: string | null;
}

export interface ImportConflictReviewSummary {
  mappingId: string;
  externalKey: string;
  personId: string | null;
  conflicts: readonly ImportFieldConflictSummary[];
  status: ImportConflictReviewStatusWire;
  sourceBatchId: string;
  raisedAt: string;
  lastSeenAt: string;
  decidedAt: string | null;
  decidedByActorId: string | null;
  decisionReasonCode: string | null;
  decisionNotes: string | null;
  version: number;
}

export interface MappingScopedInput {
  tenantId: string;
  mappingId: string;
}

export interface ScanConflictsResult extends PrimaryActionResult {
  reviews?: readonly ImportConflictReviewSummary[];
}

export interface ListConflictsResult extends PrimaryActionResult {
  reviews?: readonly ImportConflictReviewSummary[];
}

export interface DecideConflictInput extends MappingScopedInput {
  externalKey: string;
  decision: ImportConflictDecision;
  reasonCode: string;
  notes?: string;
  expectedVersion: number;
}

export interface DecideConflictResult extends PrimaryActionResult {
  review?: ImportConflictReviewSummary;
}

export interface ScanConflictsPort extends ModulePort {
  primaryAction(input?: MappingScopedInput): Promise<ScanConflictsResult>;
}

export interface ListConflictsPort extends ModulePort {
  primaryAction(input?: MappingScopedInput): Promise<ListConflictsResult>;
}

export interface DecideConflictPort extends ModulePort {
  primaryAction(input?: DecideConflictInput): Promise<DecideConflictResult>;
}
