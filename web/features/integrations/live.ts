import { createLiveAdapter } from '../../lib/module-port';
import type { IntegrationsPort } from './ports';
import type { FieldMappingEntry, ImportBatchResult, ImportRowResult, MappingSummary, RowOutcome } from './types';

// BIO-INT-001-AC05 already wired this module's generic primaryAction() (route-manifest.ts's
// "Test connection" secondary action) to its own real demo-seed endpoint - do not repoint it.
// This story's own new methods below are additive (`...base`), never touching primaryAction()'s
// own endpoint.
const base = createLiveAdapter('IntegrationService', '/api/v1/integrations/primary-action');

/**
 * Integrations live adapter (BIO-UX-005-AC04, BIO-INT-002-AC06). Calls this story's own real
 * `/api/v1/integrations/*` backend (`ImportEndpoints.cs`) - genuinely connected, not a stub:
 * BIO-INT-002 is the story that builds that backend.
 */
async function postJson<T>(path: string, body: unknown): Promise<T> {
  const response = await fetch(path, {
    method: 'POST',
    headers: { 'content-type': 'application/json', 'X-Actor-Id': 'web-ui', 'X-Actor-Type': 'user' },
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    throw new Error(`Live request to ${path} failed: HTTP ${response.status}`);
  }
  return (await response.json()) as T;
}

interface MappingWireResponse {
  id: string;
  source_system: string;
  sync_direction: string;
  display_name: string;
  field_mappings: readonly { source_field: string; person_field: string }[];
  active: boolean;
  version: number;
}

function fromWireMapping(wire: MappingWireResponse): MappingSummary {
  return {
    id: wire.id,
    sourceSystem: wire.source_system as MappingSummary['sourceSystem'],
    syncDirection: wire.sync_direction as MappingSummary['syncDirection'],
    displayName: wire.display_name,
    fieldMappings: wire.field_mappings.map((fm) => ({ sourceField: fm.source_field, personField: fm.person_field })),
    active: wire.active,
    version: wire.version,
  };
}

function toWireFieldMappings(fieldMappings: readonly FieldMappingEntry[]) {
  return fieldMappings.map((fm) => ({ source_field: fm.sourceField, person_field: fm.personField }));
}

interface BatchWireResponse {
  id: string;
  mode: string;
  row_outcomes: readonly {
    external_key: string;
    outcome: string;
    person_id: string | null;
    conflicts: readonly { person_field: string; existing_value: string | null; incoming_value: string | null }[];
  }[];
  replayed: boolean;
}

function fromWireBatch(wire: BatchWireResponse): ImportBatchResult {
  const rowOutcomes: ImportRowResult[] = wire.row_outcomes.map((r) => ({
    externalKey: r.external_key,
    outcome: r.outcome as RowOutcome,
    personId: r.person_id,
    conflicts: r.conflicts.map((c) => ({
      personField: c.person_field,
      existingValue: c.existing_value,
      incomingValue: c.incoming_value,
    })),
  }));
  return { id: wire.id, mode: wire.mode as ImportBatchResult['mode'], rowOutcomes, replayed: wire.replayed };
}

export const integrationsLivePort: IntegrationsPort = {
  ...base,

  async listMappings() {
    try {
      const response = await fetch('/api/v1/integrations/mappings', {
        headers: { 'X-Actor-Id': 'web-ui', 'X-Actor-Type': 'user' },
      });
      if (!response.ok) {
        return { mappings: [] };
      }
      const body = (await response.json()) as { data: MappingWireResponse[] };
      return { mappings: body.data.map(fromWireMapping) };
    } catch {
      return { mappings: [] };
    }
  },

  async createMapping(input) {
    const wire = await postJson<MappingWireResponse>('/api/v1/integrations/mappings', {
      source_system: input.sourceSystem,
      sync_direction: input.syncDirection,
      display_name: input.displayName,
      field_mappings: toWireFieldMappings(input.fieldMappings),
    });
    return fromWireMapping(wire);
  },

  async previewImport(input) {
    const wire = await postJson<BatchWireResponse>(`/api/v1/integrations/mappings/${input.mappingId}/preview`, {
      rows: input.rows.map((r) => ({ external_key: r.externalKey, fields: r.fields })),
    });
    return fromWireBatch(wire);
  },

  async applyImport(input) {
    const response = await fetch(`/api/v1/integrations/mappings/${input.mappingId}/apply`, {
      method: 'POST',
      headers: {
        'content-type': 'application/json',
        'X-Actor-Id': 'web-ui',
        'X-Actor-Type': 'user',
        'Idempotency-Key': input.idempotencyKey ?? '',
      },
      body: JSON.stringify({ rows: input.rows.map((r) => ({ external_key: r.externalKey, fields: r.fields })) }),
    });
    if (!response.ok) {
      throw new Error(`Live request to apply failed: HTTP ${response.status}`);
    }
    return fromWireBatch((await response.json()) as BatchWireResponse);
  },
};
