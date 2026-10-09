import { createMockAdapter } from '../../lib/module-port';
import type {
  DecideConflictPort,
  ImportConflictReviewSummary,
  ListConflictsPort,
  MappingScopedInput,
  ScanConflictsPort,
} from './ports';

/**
 * Deterministic in-memory stand-in for the real `/api/v1/integrations/mappings/{mappingId}/
 * conflicts/*` backend (BIO-INT-005-AC01-AC04) - reproducible for local testing and demos, same
 * shape as every other mock adapter in this epic.
 */
const reviewsByTenantAndMapping = new Map<string, Map<string, ImportConflictReviewSummary>>();

function mappingKey(tenantId: string, mappingId: string): string {
  return `${tenantId}::${mappingId}`;
}

function mappingReviews(tenantId: string, mappingId: string): Map<string, ImportConflictReviewSummary> {
  const key = mappingKey(tenantId, mappingId);
  let reviews = reviewsByTenantAndMapping.get(key);
  if (!reviews) {
    reviews = new Map();
    reviewsByTenantAndMapping.set(key, reviews);
  }
  return reviews;
}

/** First scan for a (tenant, mapping) pair with nothing on file yet seeds two SYNTHETIC conflicts, deterministically - a mock conflict review queue needs something to review. */
function seedIfEmpty(reviews: Map<string, ImportConflictReviewSummary>): void {
  if (reviews.size > 0) {
    return;
  }

  const now = new Date().toISOString();
  reviews.set('ext-mock-001', {
    mappingId: '',
    externalKey: 'ext-mock-001',
    personId: 'per_mock-0001',
    conflicts: [{ personField: 'last_name', existingValue: 'Okafor', incomingValue: 'Okeke' }],
    status: 'PENDING_REVIEW',
    sourceBatchId: 'batch-mock-001',
    raisedAt: now,
    lastSeenAt: now,
    decidedAt: null,
    decidedByActorId: null,
    decisionReasonCode: null,
    decisionNotes: null,
    version: 1,
  });
  reviews.set('ext-mock-002', {
    mappingId: '',
    externalKey: 'ext-mock-002',
    personId: 'per_mock-0002',
    conflicts: [{ personField: 'born_on', existingValue: '2005-03-14', incomingValue: '2005-03-04' }],
    status: 'PENDING_REVIEW',
    sourceBatchId: 'batch-mock-001',
    raisedAt: now,
    lastSeenAt: now,
    decidedAt: null,
    decidedByActorId: null,
    decisionReasonCode: null,
    decisionNotes: null,
    version: 1,
  });
}

function withMappingId(mappingId: string, review: ImportConflictReviewSummary): ImportConflictReviewSummary {
  return { ...review, mappingId };
}

const scanBase = createMockAdapter('IntegrationService');
export const scanConflictsMockPort: ScanConflictsPort = {
  ...scanBase,
  async primaryAction(input?: MappingScopedInput) {
    if (!input) return { outcome: 'failure', message: 'No mapping was provided.' };
    const reviews = mappingReviews(input.tenantId, input.mappingId);
    seedIfEmpty(reviews);
    return {
      outcome: 'success',
      message: 'Conflict scan complete (SIMULATION).',
      reviews: [...reviews.values()].map((r) => withMappingId(input.mappingId, r)),
    };
  },
};

const listBase = createMockAdapter('IntegrationService');
export const listConflictsMockPort: ListConflictsPort = {
  ...listBase,
  async primaryAction(input?: MappingScopedInput) {
    if (!input) return { outcome: 'failure', message: 'No mapping was provided.' };
    const reviews = mappingReviews(input.tenantId, input.mappingId);
    return {
      outcome: 'success',
      message: 'Conflict reviews loaded (SIMULATION).',
      reviews: [...reviews.values()].map((r) => withMappingId(input.mappingId, r)),
    };
  },
};

const decideBase = createMockAdapter('IntegrationService');
export const decideConflictMockPort: DecideConflictPort = {
  ...decideBase,
  async primaryAction(input) {
    if (!input) return { outcome: 'failure', message: 'No decision was provided.' };
    const reviews = mappingReviews(input.tenantId, input.mappingId);
    const current = reviews.get(input.externalKey);
    if (!current) return { outcome: 'failure', message: 'Unknown conflict review (SIMULATION).' };
    if (current.version !== input.expectedVersion) {
      return { outcome: 'failure', message: 'This conflict review was updated since it was loaded - reload and try again (SIMULATION).' };
    }
    if (!input.reasonCode.trim()) {
      return { outcome: 'failure', message: 'A reason is required to record a decision (SIMULATION).' };
    }

    const now = new Date().toISOString();
    const updated: ImportConflictReviewSummary = {
      ...current,
      status: input.decision,
      decidedAt: now,
      decidedByActorId: 'web-ui',
      decisionReasonCode: input.reasonCode,
      decisionNotes: input.notes ?? null,
      lastSeenAt: now,
      version: current.version + 1,
    };
    reviews.set(input.externalKey, updated);
    return { outcome: 'success', message: `Decision recorded: ${input.decision.replaceAll('_', ' ').toLowerCase()} (SIMULATION).`, review: withMappingId(input.mappingId, updated) };
  },
};
