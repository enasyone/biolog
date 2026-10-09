/**
 * BIO-TEN-003: this module's own domain types, mirroring the backend's
 * `/api/v1/tenants/{tenantId}/feature-configuration` wire shapes (snake_case on the wire,
 * camelCase here - translated at the `live.ts` boundary, never leaked into a component).
 */

/** AC01: the closed set of six top-level feature modules. */
export type TenantModuleKey =
  | 'biometrics'
  | 'card_production'
  | 'visitor'
  | 'attendance'
  | 'transport'
  | 'document_verify';

/** AC01: the closed set of eight Document Verify subfeature toggles. */
export type DocumentVerifySubfeatureKey =
  | 'capture'
  | 'mrz'
  | 'barcode'
  | 'ocr'
  | 'authenticity'
  | 'document_identification'
  | 'nfc'
  | 'selfie_comparison';

export const DOCUMENT_VERIFY_SUBFEATURES: readonly DocumentVerifySubfeatureKey[] = [
  'capture',
  'mrz',
  'barcode',
  'ocr',
  'authenticity',
  'document_identification',
  'nfc',
  'selfie_comparison',
];

/** AC02: Biometrics' own nested config - `enabled` and `face1NIdentificationEnabled` are two
 * independent, separately-settable fields (never implicitly derived from one another). */
export interface BiometricsConfig {
  enabled: boolean;
  face1NIdentificationEnabled: boolean;
}

export interface DocumentVerifyConfig {
  enabled: boolean;
  captureEnabled: boolean;
  mrzEnabled: boolean;
  barcodeEnabled: boolean;
  ocrEnabled: boolean;
  authenticityEnabled: boolean;
  documentIdentificationEnabled: boolean;
  nfcEnabled: boolean;
  selfieComparisonEnabled: boolean;
}

export interface TenantFeatureConfiguration {
  tenantId: string;
  biometrics: BiometricsConfig;
  cardProductionEnabled: boolean;
  visitorEnabled: boolean;
  attendanceEnabled: boolean;
  transportEnabled: boolean;
  documentVerify: DocumentVerifyConfig;
  version: number;
  createdAt: string;
  updatedAt: string;
}

/** Every field but `tenantId`/`version` is optional - omitted means "leave unchanged",
 * mirroring the backend's own `UpdateTenantFeatureConfigurationRequest`. */
export interface UpdateTenantFeatureConfigurationInput {
  tenantId: string;
  version: number;
  biometrics?: Partial<BiometricsConfig>;
  cardProductionEnabled?: boolean;
  visitorEnabled?: boolean;
  attendanceEnabled?: boolean;
  transportEnabled?: boolean;
  documentVerify?: Partial<DocumentVerifyConfig>;
}

export function documentVerifySubfeatureLabel(key: DocumentVerifySubfeatureKey): string {
  switch (key) {
    case 'capture': return 'Capture';
    case 'mrz': return 'MRZ';
    case 'barcode': return 'Barcode';
    case 'ocr': return 'OCR';
    case 'authenticity': return 'Authenticity';
    case 'document_identification': return 'Document identification';
    case 'nfc': return 'NFC';
    case 'selfie_comparison': return 'Selfie comparison';
  }
}
