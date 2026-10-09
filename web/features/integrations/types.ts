/**
 * BIO-INT-002: the Integrations module's own domain types, mirroring the backend's
 * `/api/v1/integrations/*` wire shapes (snake_case on the wire, camelCase here — translated at
 * the `live.ts` boundary, never leaked into a component).
 */

export type SourceSystem = 'sis' | 'hr' | 'payroll' | 'erp' | 'csv_excel';

/** AC02: which side is the system of record for this one integration. */
export type SyncDirection = 'source_is_master' | 'biolog_is_master';

export interface FieldMappingEntry {
  sourceField: string;
  personField: string;
}

export interface MappingSummary {
  id: string;
  sourceSystem: SourceSystem;
  syncDirection: SyncDirection;
  displayName: string;
  fieldMappings: readonly FieldMappingEntry[];
  active: boolean;
  version: number;
}

export interface ListMappingsInput {
  tenantId: string;
}

export interface ListMappingsResult {
  mappings: readonly MappingSummary[];
}

export interface CreateMappingInput {
  tenantId: string;
  sourceSystem: SourceSystem;
  syncDirection: SyncDirection;
  displayName: string;
  fieldMappings: readonly FieldMappingEntry[];
}

export interface ImportRowInput {
  externalKey: string;
  fields: Readonly<Record<string, string>>;
}

export type RowOutcome = 'created' | 'unchanged' | 'conflict' | 'rejected';

export interface FieldConflict {
  personField: string;
  existingValue: string | null;
  incomingValue: string | null;
}

export interface ImportRowResult {
  externalKey: string;
  outcome: RowOutcome;
  personId: string | null;
  conflicts: readonly FieldConflict[];
}

export interface RunImportInput {
  tenantId: string;
  mappingId: string;
  rows: readonly ImportRowInput[];
  /** Only meaningful for apply - AC05's idempotency key. Ignored by preview. */
  idempotencyKey?: string;
}

export interface ImportBatchResult {
  id: string;
  mode: 'dry_run' | 'applied';
  rowOutcomes: readonly ImportRowResult[];
  replayed: boolean;
}
