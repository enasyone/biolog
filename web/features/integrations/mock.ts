import { createMockAdapter } from '../../lib/module-port';
import type { IntegrationsPort } from './ports';
import type { MappingSummary } from './types';

/**
 * Integrations mock adapter (BIO-UX-005-AC05, extended BIO-INT-002-AC06): deterministic,
 * in-memory stand-in for the real `/api/v1/integrations/*` backend, reproducible for local
 * testing and demos.
 */
const base = createMockAdapter('IntegrationService', {
  scenarios: [
    { outcome: 'success', message: 'Integrations: simulated action completed.' },
    { outcome: 'failure', message: 'Integrations: simulated action failed — try again.' },
    { outcome: 'offline', message: 'Integrations: device or network appears offline (simulated).' },
  ],
});

let nextMappingId = 1;
let nextPersonId = 1;
const mappings = new Map<string, MappingSummary>();
const links = new Map<string, Map<string, string>>(); // mappingId -> externalKey -> personId
const people = new Map<string, Record<string, string | undefined>>(); // personId -> field values

export const integrationsMockPort: IntegrationsPort = {
  ...base,

  async listMappings() {
    return { mappings: [...mappings.values()] };
  },

  async createMapping(input) {
    const id = `mock-mapping-${nextMappingId++}`;
    const mapping: MappingSummary = {
      id,
      sourceSystem: input.sourceSystem,
      syncDirection: input.syncDirection,
      displayName: input.displayName,
      fieldMappings: input.fieldMappings,
      active: true,
      version: 1,
    };
    mappings.set(id, mapping);
    links.set(id, new Map());
    return mapping;
  },

  async previewImport(input) {
    const mapping = mappings.get(input.mappingId);
    if (!mapping) throw new Error(`mock: unknown mapping ${input.mappingId}`);
    if (mapping.syncDirection === 'biolog_is_master') {
      return { id: `mock-batch-${Date.now()}`, mode: 'dry_run', rowOutcomes: [], replayed: false };
    }

    const mappingLinks = links.get(input.mappingId)!;
    const rowOutcomes = input.rows.map((row) => classify(mapping, mappingLinks, row));
    return { id: `mock-batch-${Date.now()}`, mode: 'dry_run', rowOutcomes, replayed: false };
  },

  async applyImport(input) {
    const mapping = mappings.get(input.mappingId);
    if (!mapping) throw new Error(`mock: unknown mapping ${input.mappingId}`);
    if (mapping.syncDirection === 'biolog_is_master') {
      return { id: `mock-batch-${Date.now()}`, mode: 'applied', rowOutcomes: [], replayed: false };
    }

    const mappingLinks = links.get(input.mappingId)!;
    const rowOutcomes = input.rows.map((row) => {
      const outcome = classify(mapping, mappingLinks, row);
      if (outcome.outcome === 'created') {
        const personId = `mock-person-${nextPersonId++}`;
        const values: Record<string, string | undefined> = {};
        for (const fm of mapping.fieldMappings) {
          values[fm.personField] = row.fields[fm.sourceField];
        }
        people.set(personId, values);
        mappingLinks.set(row.externalKey, personId);
        return { ...outcome, personId };
      }
      return outcome;
    });
    return { id: `mock-batch-${Date.now()}`, mode: 'applied', rowOutcomes, replayed: false };
  },
};

function classify(
  mapping: MappingSummary,
  mappingLinks: Map<string, string>,
  row: { externalKey: string; fields: Readonly<Record<string, string>> },
) {
  const personId = mappingLinks.get(row.externalKey);
  if (!personId) {
    return { externalKey: row.externalKey, outcome: 'created' as const, personId: null, conflicts: [] };
  }

  const current = people.get(personId) ?? {};
  const conflicts: { personField: string; existingValue: string | null; incomingValue: string | null }[] = [];
  for (const fm of mapping.fieldMappings) {
    const incoming = row.fields[fm.sourceField];
    const existing = current[fm.personField];
    if (!incoming) continue;
    if (!existing || existing === incoming) continue;
    conflicts.push({ personField: fm.personField, existingValue: existing ?? null, incomingValue: incoming });
  }

  const outcome: 'conflict' | 'unchanged' = conflicts.length > 0 ? 'conflict' : 'unchanged';
  return { externalKey: row.externalKey, outcome, personId, conflicts };
}
