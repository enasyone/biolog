'use client';

import { useState } from 'react';
import Link from 'next/link';
import { auditFeatureModes } from '../lib/feature-mode';
import { OPERATIONAL_SCENARIO_OPTIONS } from '../components/scenario-picker';
import { DEFAULT_ROLE_ID, DEFAULT_TENANT_ID, useSession } from '../context/session-context';
import type { CredentialStatus, PersonCategory } from '../lib/synthetic-data';
import type { CaptureScenarioHint, DeviceScenarioHint } from '../features/biometrics/ports';
import type { EvidenceCheckState } from '../features/documents/evidence';

/**
 * Local Human Test Console (BIO-UX-012). Lets a human tester set tenant,
 * role and six domain scenario dimensions from one place, then jump
 * straight to the affected workflow (AC01, AC06) — without editing code or
 * hunting through six different pages' own "Simulate outcome" controls.
 *
 * Architecture: tenant/role/feature flags are already global session state
 * (BIO-UX-002/011) and apply immediately, exactly like the shell's own
 * top-bar selectors — this console is a second, more complete place to set
 * them, not a new mechanism. The other six dimensions are new: this console
 * is their only source of truth, held as local component state (reset to
 * fixed literal defaults — AC03's "can be reset" — is therefore trivial and
 * exact, no server/store round-trip involved). Where a target page already
 * has a matching "Simulate outcome" control, the value travels as a query
 * param that page reads once, on mount, as its own initial state via
 * `readTestScenarioParam` (additive: every page's default behaviour for a
 * human arriving without that param is completely unchanged) — the same
 * pattern `session-context.tsx` already uses for `?tenant=`/`?as=`.
 *
 * "Identity type" is still a pure navigation shortcut — it doesn't
 * correspond to a single settable value on one page, it selects *which* of
 * six separate routes to view. Credential state and document result DO
 * change something observable on arrival too (Round 2, CHANGES_REQUIRED):
 * `?credentialState=` on `/cards` deterministically ensures a credential in
 * that exact status exists for the tenant's first person (via the same
 * mutation functions the page's own Mark lost/Recover/Revoke buttons call
 * — CardsRegistryApp.tsx's own `useEffect`); `?documentResult=` on
 * `/document-verify` deterministically sets the Authenticity step's result
 * (DocumentVerifyApp.tsx's own `useEffect`, using the same pure, already-
 * exported `runEvidenceScenario` its Run button calls, not a new mechanism).
 * Full reasoning in `docs/features/bio-ux-012/README.md`.
 *
 * `process.env.NODE_ENV === 'production'` is inlined to a literal at
 * Next.js build time, so in a production build this always returns `null`
 * before any hook runs and the branch below is dead-code-eliminated
 * entirely — the same proven technique `feature-mode-panel.tsx` uses
 * (BIO-UX-012-AC04, "impossible to expose accidentally in production").
 */
export function HumanTestConsole() {
  if (process.env.NODE_ENV === 'production') {
    return null;
  }
  return <HumanTestConsoleContent />;
}

const IDENTITY_TYPES: ReadonlyArray<{ value: PersonCategory | 'all'; label: string; href: string }> = [
  { value: 'all', label: 'All people', href: '/people' },
  { value: 'student', label: 'Student', href: '/students' },
  { value: 'staff', label: 'Staff', href: '/staff' },
  { value: 'alumni', label: 'Alumni', href: '/alumni' },
  { value: 'contractor', label: 'Contractor', href: '/contractors' },
];

const CREDENTIAL_STATES: ReadonlyArray<{ value: CredentialStatus; label: string }> = [
  { value: 'active', label: 'Active' },
  { value: 'lost', label: 'Lost' },
  { value: 'recovered', label: 'Recovered' },
  { value: 'revoked', label: 'Revoked' },
  { value: 'replaced', label: 'Replaced' },
];

const BIOMETRIC_RESULTS: ReadonlyArray<{ value: CaptureScenarioHint; label: string }> = [
  { value: 'good', label: 'Good quality' },
  { value: 'low', label: 'Low quality' },
  { value: 'exception', label: 'Capture exception' },
];

// Exactly the 'authenticity' step's own 3 non-default outcomes (evidence.ts's SCENARIOS table) -
// the step this preset seeds (see DocumentVerifyApp.tsx's own useEffect). Not every EvidenceCheckState
// applies to every step (e.g. 'authentic' comes from 'nfc'/'selfie-comparison', not 'authenticity'),
// so the option list is deliberately scoped to what this one seeded step can actually produce.
const DOCUMENT_RESULTS: ReadonlyArray<{ value: EvidenceCheckState; label: string }> = [
  { value: 'consistent', label: 'Consistent' },
  { value: 'inconclusive', label: 'Inconclusive' },
  { value: 'failed', label: 'Failed' },
];

const DEVICE_STATES: ReadonlyArray<{ value: DeviceScenarioHint; label: string }> = [
  { value: 'connected', label: 'Connected' },
  { value: 'offline', label: 'Offline' },
];

const OPERATIONAL_ROUTES: ReadonlyArray<{ id: string; label: string; href: string }> = [
  { id: 'access-control', label: 'Access Control', href: '/access-control' },
  { id: 'attendance', label: 'Attendance', href: '/attendance' },
  { id: 'devices', label: 'Devices', href: '/devices' },
  { id: 'transport', label: 'Transport', href: '/transport' },
  { id: 'visitors', label: 'Visitors', href: '/visitors' },
];

function HumanTestConsoleContent() {
  const [open, setOpen] = useState(false);
  const session = useSession();
  const [connectivity, setConnectivity] = useState<string>(OPERATIONAL_SCENARIO_OPTIONS[0]!.id);
  const [identityType, setIdentityType] = useState<PersonCategory | 'all'>('all');
  const [credentialState, setCredentialState] = useState<CredentialStatus>('active');
  const [biometricResult, setBiometricResult] = useState<CaptureScenarioHint>('good');
  const [documentResult, setDocumentResult] = useState<EvidenceCheckState>('consistent');
  const [deviceState, setDeviceState] = useState<DeviceScenarioHint>('connected');

  const modeSummary = auditFeatureModes();
  const liveCount = modeSummary.filter((entry) => entry.status === 'LIVE').length;
  const hybridCount = modeSummary.filter((entry) => entry.status === 'HYBRID').length;
  const simulationCount = modeSummary.filter((entry) => entry.status === 'SIMULATION').length;

  function resetScenario() {
    session.setTenantId(DEFAULT_TENANT_ID);
    session.setRoleId(DEFAULT_ROLE_ID);
    setConnectivity(OPERATIONAL_SCENARIO_OPTIONS[0]!.id);
    setIdentityType('all');
    setCredentialState('active');
    setBiometricResult('good');
    setDocumentResult('consistent');
    setDeviceState('connected');
  }

  const identityTarget = IDENTITY_TYPES.find((option) => option.value === identityType)!;

  return (
    <div className="biolog-test-console" data-testid="biolog-human-test-console">
      <button
        type="button"
        className="biolog-test-console-toggle"
        onClick={() => setOpen((value) => !value)}
        aria-expanded={open}
        aria-controls="biolog-test-console-panel"
      >
        {/* AC05: a visible development indicator naming LOCAL and current feature modes,
            always visible on the toggle itself — not only after expanding the panel. */}
        LOCAL test console — {simulationCount} SIMULATION{hybridCount ? `, ${hybridCount} HYBRID` : ''}
        {liveCount ? `, ${liveCount} LIVE` : ''}
      </button>
      {open && (
        <div id="biolog-test-console-panel" className="biolog-test-console-panel" role="region" aria-label="Human Test Console">
          <div className="biolog-test-console-row">
            <label htmlFor="console-tenant">Tenant</label>
            {/* Explicit aria-label, not the label association alone: the shell's own top-bar
                selector is already accessibly named "Tenant" (app-shell.tsx), and two controls
                sharing one accessible name on the same page is ambiguous for assistive tech and
                for tests alike — same reasoning `scenario-picker.tsx` documents for itself. */}
            <select
              id="console-tenant"
              aria-label="Test console tenant"
              value={session.tenant.id}
              onChange={(event) => session.setTenantId(event.target.value)}
            >
              {session.tenants.map((tenant) => (
                <option key={tenant.id} value={tenant.id}>
                  {tenant.name}
                </option>
              ))}
            </select>
          </div>
          <div className="biolog-test-console-row">
            <label htmlFor="console-role">Role</label>
            <select
              id="console-role"
              aria-label="Test console role"
              value={session.role.id}
              onChange={(event) => session.setRoleId(event.target.value)}
            >
              {session.roles.map((role) => (
                <option key={role.id} value={role.id}>
                  {role.name}
                </option>
              ))}
            </select>
          </div>
          <div className="biolog-test-console-row">
            <label htmlFor="console-connectivity">Connectivity</label>
            <select
              id="console-connectivity"
              aria-label="Test console connectivity"
              value={connectivity}
              onChange={(event) => setConnectivity(event.target.value)}
            >
              {OPERATIONAL_SCENARIO_OPTIONS.map((option) => (
                <option key={option.id} value={option.id}>
                  {option.label}
                </option>
              ))}
            </select>
            <span className="biolog-test-console-links">
              {OPERATIONAL_ROUTES.map((route) => (
                <Link key={route.id} href={`${route.href}?scenario=${connectivity}`}>
                  {route.label}
                </Link>
              ))}
            </span>
          </div>
          <div className="biolog-test-console-row">
            <label htmlFor="console-identity-type">Identity type</label>
            <select
              id="console-identity-type"
              aria-label="Test console identity type"
              value={identityType}
              onChange={(event) => setIdentityType(event.target.value as PersonCategory | 'all')}
            >
              {IDENTITY_TYPES.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
            <span className="biolog-test-console-links">
              <Link href={identityTarget.href}>Go to {identityTarget.label}</Link>
            </span>
          </div>
          <div className="biolog-test-console-row">
            <label htmlFor="console-credential-state">Credential state</label>
            <select
              id="console-credential-state"
              aria-label="Test console credential state"
              value={credentialState}
              onChange={(event) => setCredentialState(event.target.value as CredentialStatus)}
            >
              {CREDENTIAL_STATES.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
            <span className="biolog-test-console-links">
              <Link href={`/cards?credentialState=${credentialState}`}>Go to Cards</Link>
            </span>
          </div>
          <div className="biolog-test-console-row">
            <label htmlFor="console-biometric-result">Biometric result</label>
            <select
              id="console-biometric-result"
              aria-label="Test console biometric result"
              value={biometricResult}
              onChange={(event) => setBiometricResult(event.target.value as CaptureScenarioHint)}
            >
              {BIOMETRIC_RESULTS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
            <span className="biolog-test-console-links">
              <Link href={`/fingerprints?hint=${biometricResult}`}>Go to Fingerprints (biometric result)</Link>
              <Link href={`/facial-recognition?hint=${biometricResult}`}>Go to Facial Recognition (biometric result)</Link>
            </span>
          </div>
          <div className="biolog-test-console-row">
            <label htmlFor="console-document-result">Document result</label>
            <select
              id="console-document-result"
              aria-label="Test console document result"
              value={documentResult}
              onChange={(event) => setDocumentResult(event.target.value as EvidenceCheckState)}
            >
              {DOCUMENT_RESULTS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
            <span className="biolog-test-console-links">
              <Link href={`/document-verify?documentResult=${documentResult}`}>Go to Document Verify</Link>
            </span>
          </div>
          <div className="biolog-test-console-row">
            <label htmlFor="console-device-state">Device state</label>
            <select
              id="console-device-state"
              aria-label="Test console device state"
              value={deviceState}
              onChange={(event) => setDeviceState(event.target.value as DeviceScenarioHint)}
            >
              {DEVICE_STATES.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
            <span className="biolog-test-console-links">
              <Link href={`/fingerprints?deviceHint=${deviceState}`}>Go to Fingerprints (device state)</Link>
              <Link href={`/facial-recognition?deviceHint=${deviceState}`}>Go to Facial Recognition (device state)</Link>
            </span>
          </div>
          <button type="button" className="biolog-button-secondary" onClick={resetScenario}>
            Reset scenario
          </button>
        </div>
      )}
    </div>
  );
}
