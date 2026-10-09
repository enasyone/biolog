import { createLiveAdapter } from '../../lib/module-port';
import type { TenantsPort } from './ports';
import type { BiometricsConfig, DocumentVerifyConfig, TenantFeatureConfiguration, UpdateTenantFeatureConfigurationInput } from './types';

// BIO-UX-005-AC04 already wired this module's generic primaryAction() to its own endpoint -
// do not repoint it. This story's own new methods below are additive (`...base`), never
// touching primaryAction()'s own endpoint - the same discipline BIO-INT-002's own live.ts
// documents for the identical reason.
const base = createLiveAdapter('TenantService', '/api/v1/tenants/primary-action');

interface BiometricsWireResponse {
  enabled: boolean;
  face_1n_identification_enabled: boolean;
}

interface DocumentVerifyWireResponse {
  enabled: boolean;
  capture_enabled: boolean;
  mrz_enabled: boolean;
  barcode_enabled: boolean;
  ocr_enabled: boolean;
  authenticity_enabled: boolean;
  document_identification_enabled: boolean;
  nfc_enabled: boolean;
  selfie_comparison_enabled: boolean;
}

interface TenantFeatureConfigurationWireResponse {
  tenant_id: string;
  biometrics: BiometricsWireResponse;
  card_production_enabled: boolean;
  visitor_enabled: boolean;
  attendance_enabled: boolean;
  transport_enabled: boolean;
  document_verify: DocumentVerifyWireResponse;
  version: number;
  created_at: string;
  updated_at: string;
}

function fromWire(wire: TenantFeatureConfigurationWireResponse): TenantFeatureConfiguration {
  return {
    tenantId: wire.tenant_id,
    biometrics: {
      enabled: wire.biometrics.enabled,
      face1NIdentificationEnabled: wire.biometrics.face_1n_identification_enabled,
    },
    cardProductionEnabled: wire.card_production_enabled,
    visitorEnabled: wire.visitor_enabled,
    attendanceEnabled: wire.attendance_enabled,
    transportEnabled: wire.transport_enabled,
    documentVerify: {
      enabled: wire.document_verify.enabled,
      captureEnabled: wire.document_verify.capture_enabled,
      mrzEnabled: wire.document_verify.mrz_enabled,
      barcodeEnabled: wire.document_verify.barcode_enabled,
      ocrEnabled: wire.document_verify.ocr_enabled,
      authenticityEnabled: wire.document_verify.authenticity_enabled,
      documentIdentificationEnabled: wire.document_verify.document_identification_enabled,
      nfcEnabled: wire.document_verify.nfc_enabled,
      selfieComparisonEnabled: wire.document_verify.selfie_comparison_enabled,
    },
    version: wire.version,
    createdAt: wire.created_at,
    updatedAt: wire.updated_at,
  };
}

function toWireBiometrics(partial: Partial<BiometricsConfig> | undefined) {
  if (!partial) return undefined;
  return {
    enabled: partial.enabled,
    face_1n_identification_enabled: partial.face1NIdentificationEnabled,
  };
}

function toWireDocumentVerify(partial: Partial<DocumentVerifyConfig> | undefined) {
  if (!partial) return undefined;
  return {
    enabled: partial.enabled,
    capture_enabled: partial.captureEnabled,
    mrz_enabled: partial.mrzEnabled,
    barcode_enabled: partial.barcodeEnabled,
    ocr_enabled: partial.ocrEnabled,
    authenticity_enabled: partial.authenticityEnabled,
    document_identification_enabled: partial.documentIdentificationEnabled,
    nfc_enabled: partial.nfcEnabled,
    selfie_comparison_enabled: partial.selfieComparisonEnabled,
  };
}

/**
 * Tenants live adapter (BIO-UX-005-AC04, BIO-TEN-003-AC05). Calls this story's own real
 * `/api/v1/tenants/{tenantId}/feature-configuration` backend
 * (`TenantFeatureConfigurationEndpoints.cs`) - genuinely connected, not a stub.
 */
export const tenantsLivePort: TenantsPort = {
  ...base,

  async getFeatureConfiguration(tenantId) {
    const response = await fetch(`/api/v1/tenants/${tenantId}/feature-configuration`, {
      headers: { 'X-Actor-Id': 'web-ui', 'X-Actor-Type': 'user' },
    });
    if (!response.ok) {
      throw new Error(`Live request to read feature configuration failed: HTTP ${response.status}`);
    }
    return fromWire((await response.json()) as TenantFeatureConfigurationWireResponse);
  },

  async updateFeatureConfiguration(input: UpdateTenantFeatureConfigurationInput) {
    const response = await fetch(`/api/v1/tenants/${input.tenantId}/feature-configuration`, {
      method: 'PATCH',
      headers: { 'content-type': 'application/json', 'X-Actor-Id': 'web-ui', 'X-Actor-Type': 'user' },
      body: JSON.stringify({
        version: input.version,
        biometrics: toWireBiometrics(input.biometrics),
        card_production_enabled: input.cardProductionEnabled,
        visitor_enabled: input.visitorEnabled,
        attendance_enabled: input.attendanceEnabled,
        transport_enabled: input.transportEnabled,
        document_verify: toWireDocumentVerify(input.documentVerify),
      }),
    });
    if (!response.ok) {
      throw new Error(`Live request to update feature configuration failed: HTTP ${response.status}`);
    }
    return fromWire((await response.json()) as TenantFeatureConfigurationWireResponse);
  },
};
