import type { ModulePort } from '../../lib/module-port';
import type {
  CreateMappingInput,
  ImportBatchResult,
  ListMappingsInput,
  ListMappingsResult,
  MappingSummary,
  RunImportInput,
} from './types';

/**
 * Integrations module port (BIO-UX-005-AC01, extended by BIO-INT-002-AC06).
 *
 * Real methods for the SIS/HR/ERP import framework this story builds: list/create a versioned
 * source mapping (AC01/AC02), preview a dry-run (AC03), and apply an idempotent import (AC04/
 * AC05). `mock.ts`/`live.ts` are the only two files that implement this interface; the
 * `/administration/integrations` page calls the resolved `integrationsPort` from `./index`,
 * never `./mock` or `./live` directly.
 */
export interface IntegrationsPort extends ModulePort {
  listMappings(input: ListMappingsInput): Promise<ListMappingsResult>;
  createMapping(input: CreateMappingInput): Promise<MappingSummary>;
  previewImport(input: RunImportInput): Promise<ImportBatchResult>;
  applyImport(input: RunImportInput): Promise<ImportBatchResult>;
}
