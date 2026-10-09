import type {
  DecideConflictPort,
  ImportConflictReviewSummary,
  ListConflictsPort,
  MappingScopedInput,
  ScanConflictsPort,
} from './ports';

/**
 * Live adapters for BIO-INT-005's real `/api/v1/integrations/mappings/{mappingId}/conflicts/*`
 * backend (`BioInt005Module.cs`) - genuinely connected, not a stub. No tenant header is sent;
 * this host resolves tenancy from the request's own Host channel (ADR-0002 Decision 1.3,
 * `TenantContextMiddleware`), the same convention every other live adapter in this epic already
 * follows (see `web/features/bio-int-004/live.ts`). These routes are nested under BIO-INT-002's
 * own already-registered `/api/v1/integrations/mappings` prefix in
 * `web/lib/actor-provenance-routes.ts`, so no separate route-table entry is needed for a demo
 * session's provenance proof to reach `decide` (see `ActorProvenanceGate.cs`'s own header).
 */
const ACTOR_HEADERS = { 'X-Actor-Id': 'web-ui', 'X-Actor-Type': 'user' } as const;

function basePath(mappingId: string): string {
  return `/api/v1/integrations/mappings/${encodeURIComponent(mappingId)}/conflicts`;
}

interface ImportFieldConflictWire {
  person_field: string;
  existing_value: string | null;
  incoming_value: string | null;
}

interface ImportConflictReviewWire {
  mapping_id: string;
  external_key: string;
  person_id: string | null;
  conflicts: readonly ImportFieldConflictWire[];
  status: ImportConflictReviewSummary['status'];
  source_batch_id: string;
  raised_at: string;
  last_seen_at: string;
  decided_at: string | null;
  decided_by_actor_id: string | null;
  decision_reason_code: string | null;
  decision_notes: string | null;
  version: number;
}

function fromWire(wire: ImportConflictReviewWire): ImportConflictReviewSummary {
  return {
    mappingId: wire.mapping_id,
    externalKey: wire.external_key,
    personId: wire.person_id,
    conflicts: wire.conflicts.map((c) => ({ personField: c.person_field, existingValue: c.existing_value, incomingValue: c.incoming_value })),
    status: wire.status,
    sourceBatchId: wire.source_batch_id,
    raisedAt: wire.raised_at,
    lastSeenAt: wire.last_seen_at,
    decidedAt: wire.decided_at,
    decidedByActorId: wire.decided_by_actor_id,
    decisionReasonCode: wire.decision_reason_code,
    decisionNotes: wire.decision_notes,
    version: wire.version,
  };
}

async function problemMessage(response: Response, fallback: string): Promise<string> {
  try {
    const body = (await response.json()) as { message?: string };
    return body.message ?? fallback;
  } catch {
    return fallback;
  }
}

export const scanConflictsLivePort: ScanConflictsPort = {
  module: 'IntegrationService',
  mode: 'live',
  async primaryAction(input?: MappingScopedInput) {
    if (!input) return { outcome: 'failure', message: 'No mapping was provided.' };
    try {
      const response = await fetch(`${basePath(input.mappingId)}/scan`, { method: 'POST', headers: ACTOR_HEADERS });
      if (!response.ok) {
        return { outcome: 'failure', message: `Could not scan for conflicts: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const body = (await response.json()) as { data: ImportConflictReviewWire[] };
      return { outcome: 'success', message: 'Conflict scan complete.', reviews: body.data.map(fromWire) };
    } catch {
      return { outcome: 'offline', message: 'Could not reach the Conflict Review service - device or network appears offline.' };
    }
  },
};

export const listConflictsLivePort: ListConflictsPort = {
  module: 'IntegrationService',
  mode: 'live',
  async primaryAction(input?: MappingScopedInput) {
    if (!input) return { outcome: 'failure', message: 'No mapping was provided.' };
    try {
      const response = await fetch(basePath(input.mappingId), { headers: ACTOR_HEADERS });
      if (!response.ok) {
        return { outcome: 'failure', message: `Could not load conflict reviews: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const body = (await response.json()) as { data: ImportConflictReviewWire[] };
      return { outcome: 'success', message: 'Conflict reviews loaded.', reviews: body.data.map(fromWire) };
    } catch {
      return { outcome: 'offline', message: 'Could not reach the Conflict Review service - device or network appears offline.' };
    }
  },
};

export const decideConflictLivePort: DecideConflictPort = {
  module: 'IntegrationService',
  mode: 'live',
  async primaryAction(input) {
    if (!input) return { outcome: 'failure', message: 'No decision was provided.' };
    try {
      const response = await fetch(`${basePath(input.mappingId)}/${encodeURIComponent(input.externalKey)}/decisions`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...ACTOR_HEADERS },
        body: JSON.stringify({ decision: input.decision, reason_code: input.reasonCode, notes: input.notes, version: input.expectedVersion }),
      });
      if (!response.ok) {
        return { outcome: 'failure', message: `Could not record decision: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const wire = (await response.json()) as ImportConflictReviewWire;
      return { outcome: 'success', message: `Decision recorded: ${input.decision.replaceAll('_', ' ').toLowerCase()}.`, review: fromWire(wire) };
    } catch {
      return { outcome: 'offline', message: 'Could not reach the Conflict Review service - device or network appears offline.' };
    }
  },
};
