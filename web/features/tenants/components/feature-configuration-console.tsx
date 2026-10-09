'use client';

import { useCallback, useEffect, useState } from 'react';
import { useSession } from '../../../context/session-context';
import { tenantsPort } from '../index';
import {
  DOCUMENT_VERIFY_SUBFEATURES,
  documentVerifySubfeatureLabel,
  type DocumentVerifyConfig,
  type DocumentVerifySubfeatureKey,
  type TenantFeatureConfiguration,
} from '../types';

type LoadState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; config: TenantFeatureConfiguration };

const DOCUMENT_VERIFY_FIELD_BY_SUBFEATURE: Record<DocumentVerifySubfeatureKey, keyof DocumentVerifyConfig> = {
  capture: 'captureEnabled',
  mrz: 'mrzEnabled',
  barcode: 'barcodeEnabled',
  ocr: 'ocrEnabled',
  authenticity: 'authenticityEnabled',
  document_identification: 'documentIdentificationEnabled',
  nfc: 'nfcEnabled',
  selfie_comparison: 'selfieComparisonEnabled',
};

/**
 * BIO-TEN-003-AC05: the real /administration/tenants console - module toggles for the six
 * AC01 modules (Biometrics' own Face-1N identification flag kept deliberately separate from
 * its parent module switch, AC02), the eight Document Verify subfeature toggles, all under
 * optimistic concurrency (AC03). SYNTHETIC data only, per this story's own constraints.
 *
 * TD-WEB-PROVENANCE-BRIDGE-REVERIFY (2026-10-07): `session.tenant.id` is one of the shell's two
 * fixed demo tenants (`alpha`/`beta`, `context/session-context.tsx`'s own `TENANTS`) - neither
 * is, or can be, a real `ten_<UUIDv7>` row in BIO-TEN-001's TenantRegistry, so this console could
 * never target the one real synthetic tenant the web-session bridge resolves to
 * (`WebSessionBridge:SyntheticTenantId`), which this story's own `ITenantExistenceGateway` check
 * requires. An optional `?targetTenant=` override - read the same deliberately client-only way
 * `session-context.tsx` reads `?tenant=`/`?as=` (avoids opting this subtree out of server
 * rendering) - lets this ONE page's own admin-console design (explicit tenant id in the URL,
 * reading/writing several tenants from one surface, unlike an ambient-tenant-scoped page) target
 * a real tenant id instead, without touching the shared tenant/role selector at all: absent, this
 * resolves to exactly `session.tenant.id`, the original behaviour, unchanged.
 */
export function FeatureConfigurationConsole() {
  const session = useSession();
  const [targetTenantOverride, setTargetTenantOverride] = useState<string | null>(null);
  const tenantId = targetTenantOverride ?? session.tenant.id;
  const [state, setState] = useState<LoadState>({ status: 'loading' });
  const [working, setWorking] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    setTargetTenantOverride(params.get('targetTenant'));
  }, []);

  const load = useCallback(() => {
    setState({ status: 'loading' });
    tenantsPort
      .getFeatureConfiguration(tenantId)
      .then((config) => setState({ status: 'ready', config }))
      .catch(() => setState({ status: 'error', message: "Could not load this tenant's feature configuration - try again." }));
  }, [tenantId]);

  useEffect(() => {
    load();
  }, [load]);

  async function applyUpdate(version: number, patch: Parameters<typeof tenantsPort.updateFeatureConfiguration>[0]) {
    setWorking(true);
    setNotice(null);
    try {
      const updated = await tenantsPort.updateFeatureConfiguration({ ...patch, tenantId, version });
      setState({ status: 'ready', config: updated });
    } catch {
      setNotice('Update failed - the live service may be offline (SIMULATION mode is always reachable).');
    } finally {
      setWorking(false);
    }
  }

  function toggleModule(
    field: 'cardProductionEnabled' | 'visitorEnabled' | 'attendanceEnabled' | 'transportEnabled',
    config: TenantFeatureConfiguration,
  ) {
    return applyUpdate(config.version, { tenantId, version: config.version, [field]: !config[field] });
  }

  function toggleBiometrics(field: 'enabled' | 'face1NIdentificationEnabled', config: TenantFeatureConfiguration) {
    return applyUpdate(config.version, {
      tenantId,
      version: config.version,
      biometrics: { [field]: !config.biometrics[field] },
    });
  }

  function toggleDocumentVerifyModule(config: TenantFeatureConfiguration) {
    return applyUpdate(config.version, {
      tenantId,
      version: config.version,
      documentVerify: { enabled: !config.documentVerify.enabled },
    });
  }

  function toggleSubfeature(subfeature: DocumentVerifySubfeatureKey, config: TenantFeatureConfiguration) {
    const field = DOCUMENT_VERIFY_FIELD_BY_SUBFEATURE[subfeature];
    return applyUpdate(config.version, {
      tenantId,
      version: config.version,
      documentVerify: { [field]: !config.documentVerify[field] },
    });
  }

  return (
    <div className="biolog-placeholder-page">
      <h1>Tenant Administration</h1>
      <p className="biolog-placeholder-description">
        {session.tenant.name} - feature module configuration (BIO-TEN-003). Disabled modules fail
        closed at both the API and UI boundary. SYNTHETIC data only.
      </p>

      {state.status === 'loading' && <p role="status">Loading...</p>}
      {state.status === 'error' && <p role="alert">{state.message}</p>}

      {state.status === 'ready' && (
        <>
          <section className="biolog-card">
            <h2>Modules</h2>
            <table className="biolog-table" aria-label="Feature modules">
              <thead>
                <tr>
                  <th>Module</th>
                  <th>Enabled</th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td>Biometrics</td>
                  <td>
                    <button
                      type="button"
                      className="biolog-button-secondary"
                      disabled={working}
                      onClick={() => toggleBiometrics('enabled', state.config)}
                    >
                      {state.config.biometrics.enabled ? 'Enabled - disable' : 'Disabled - enable'}
                    </button>
                  </td>
                </tr>
                <tr>
                  <td>
                    Biometrics - 1:N face identification
                    <div className="biolog-placeholder-description">
                      AC02: independent of the Biometrics module switch above - enabling the
                      module never implicitly enables this.
                    </div>
                  </td>
                  <td>
                    <button
                      type="button"
                      className="biolog-button-secondary"
                      disabled={working}
                      onClick={() => toggleBiometrics('face1NIdentificationEnabled', state.config)}
                    >
                      {state.config.biometrics.face1NIdentificationEnabled ? 'Enabled - disable' : 'Disabled - enable'}
                    </button>
                  </td>
                </tr>
                {(
                  [
                    ['cardProductionEnabled', 'Card production'],
                    ['visitorEnabled', 'Visitor'],
                    ['attendanceEnabled', 'Attendance'],
                    ['transportEnabled', 'Transport'],
                  ] as const
                ).map(([field, label]) => (
                  <tr key={field}>
                    <td>{label}</td>
                    <td>
                      <button
                        type="button"
                        className="biolog-button-secondary"
                        disabled={working}
                        onClick={() => toggleModule(field, state.config)}
                      >
                        {state.config[field] ? 'Enabled - disable' : 'Disabled - enable'}
                      </button>
                    </td>
                  </tr>
                ))}
                <tr>
                  <td>Document Verify</td>
                  <td>
                    <button
                      type="button"
                      className="biolog-button-secondary"
                      disabled={working}
                      onClick={() => toggleDocumentVerifyModule(state.config)}
                    >
                      {state.config.documentVerify.enabled ? 'Enabled - disable' : 'Disabled - enable'}
                    </button>
                  </td>
                </tr>
              </tbody>
            </table>
          </section>

          <section className="biolog-card">
            <h2>Document Verify subfeatures</h2>
            <p className="biolog-placeholder-description">
              AC01's eight closed subfeature toggles, each independent of the module switch above.
            </p>
            <table className="biolog-table" aria-label="Document Verify subfeatures">
              <thead>
                <tr>
                  <th>Subfeature</th>
                  <th>Enabled</th>
                </tr>
              </thead>
              <tbody>
                {DOCUMENT_VERIFY_SUBFEATURES.map((subfeature) => {
                  const field = DOCUMENT_VERIFY_FIELD_BY_SUBFEATURE[subfeature];
                  const enabled = state.config.documentVerify[field];
                  return (
                    <tr key={subfeature}>
                      <td>{documentVerifySubfeatureLabel(subfeature)}</td>
                      <td>
                        <button
                          type="button"
                          className="biolog-button-secondary"
                          disabled={working}
                          onClick={() => toggleSubfeature(subfeature, state.config)}
                        >
                          {enabled ? 'Enabled - disable' : 'Disabled - enable'}
                        </button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </section>

          <p className="biolog-placeholder-description">
            Version {state.config.version} - updated {state.config.updatedAt}
          </p>
        </>
      )}

      {notice && (
        <p role="status" className="biolog-identity-status biolog-identity-status--inline">
          {notice}
        </p>
      )}
    </div>
  );
}
