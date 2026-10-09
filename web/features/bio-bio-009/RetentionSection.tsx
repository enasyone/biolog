'use client';

import { useState } from 'react';
import { biometricsPort } from '../biometrics';
import type {
  BiometricDeletionResult,
  BiometricRecordKind,
  RetentionCategory,
  RetentionPolicyResult,
} from '../biometrics/ports';
import { Badge } from '../bio-ux-008/Badge';
import type { BadgeTone } from '../bio-ux-008/formatters';
import styles from '../bio-ux-008/biometric-journeys.module.css';

const RETENTION_CATEGORIES: readonly { value: RetentionCategory; label: string }[] = [
  { value: 'fingerprint_template', label: 'Fingerprint template' },
  { value: 'face_template', label: 'Face template' },
  { value: 'raw_image', label: 'Raw image' },
];

const RECORD_KINDS: readonly { value: BiometricRecordKind; label: string }[] = [
  { value: 'fingerprint_session', label: 'Fingerprint session' },
  { value: 'face_reference', label: 'Face reference' },
];

function policyBadge(result: RetentionPolicyResult): { tone: BadgeTone; text: string } {
  return { tone: 'success', text: `${result.category}: ${result.retentionDays} day(s) applied.` };
}

function deletionBadge(result: BiometricDeletionResult): { tone: BadgeTone; text: string } {
  if (result.outcome === 'NotFound') {
    return { tone: 'warning', text: 'No matching record was found.' };
  }
  if (result.outcome === 'DeletedPendingGalleryCleanup') {
    return { tone: 'warning', text: 'Deleted — gallery cleanup is still converging.' };
  }
  return { tone: 'success', text: `Deleted — gallery propagated: ${String(result.galleryPropagated)}.` };
}

/**
 * BIO-BIO-009-AC01/AC02/AC05 (round 3, Seyi's BIO-BIO-009-F03): the first real control surface
 * for retention policy and audited deletion, reached entirely through `biometricsPort`'s own
 * `manageRetentionPolicy`/`requestBiometricDeletion` keys — independently switchable ports, the
 * same mechanism BIO-BIO-008's own `checkLiveness` established (`feature-mode.ts`'s
 * `MODULE_PORT_KEYS` comment). In SIMULATION (the module's whole-module default, untouched by
 * this story) both actions return a deterministic synthetic result (`mock.ts`); switching
 * `NEXT_PUBLIC_FEATURE_MODE_BIOMETRICSERVICE_MANAGERETENTIONPOLICY`/`_DELETEBIOMETRICRECORD` to
 * LIVE reaches the real `Biolog.Api` endpoints this story's backend ships
 * (`/api/v1/biometrics/retention/policy`, `/api/v1/biometrics/retention/delete`) without this
 * component changing — `BiometricService` genuinely reaches `HYBRID` (BIO-UX-005-AC02) through
 * this control alone, the other three still-unconnected actions unaffected.
 *
 * <b>Round 4: disclosed HYBRID boundary on the LIVE path (not this story's bug to fix).</b> Both
 * reviewers independently found that `live.ts`'s calls send NO actor identity at all - no
 * `X-Actor-Id`, let alone an `X-Actor-Provenance` proof - so the real backend's
 * `ActorResolver.Resolve` rejects every real LIVE call with 400 `actor_required` before
 * `ActorProvenanceGate` (ADR-0006 Decision 3, closed in round 2) is ever reached. This is the
 * exact same cross-cutting gap Tunde hit on BIO-TEN-003 (that story's own README "Round-2
 * disclosed HYBRID boundary" has the full account) and god escalated as
 * TD-WEB-PROVENANCE-BRIDGE: no browser anywhere in this codebase can satisfy an identity-gated
 * endpoint yet, because asserting a real actor identity AND computing a valid provenance proof
 * client-side would mean shipping a shared secret in the JS bundle, defeating its purpose. That
 * bridge is foundational, cross-cutting infrastructure (Kemi/Emeka/Damilola), not something this
 * story invents alone. The honest claim this component and its LIVE-mode Playwright spec make:
 * the backend is genuinely connected and correctly rejects an unauthenticated browser call (400
 * `actor_required`, surfaced via the `role="alert"` paths below), never a silently-faked
 * successful write.
 */
export function RetentionSection() {
  const [category, setCategory] = useState<RetentionCategory>('fingerprint_template');
  const [retentionDays, setRetentionDays] = useState(90);
  const [policyResult, setPolicyResult] = useState<RetentionPolicyResult | null>(null);
  const [policyError, setPolicyError] = useState<string | null>(null);
  const [policyLoading, setPolicyLoading] = useState(false);

  const [kind, setKind] = useState<BiometricRecordKind>('fingerprint_session');
  const [recordId, setRecordId] = useState('');
  const [reason, setReason] = useState('');
  const [deletionResult, setDeletionResult] = useState<BiometricDeletionResult | null>(null);
  const [deletionError, setDeletionError] = useState<string | null>(null);
  const [deletionLoading, setDeletionLoading] = useState(false);

  // Round 4 (god's dispatch, Zainab/Seyi): the live adapter can genuinely fail - e.g. the real
  // backend correctly 401ing an unprovenanced browser call (see this file's own header's
  // "disclosed HYBRID boundary" note) - and this must surface as a visible failure, never a
  // stuck spinner or a silently-missing result, the same `role="alert"` discipline
  // BIO-TEN-003's own feature-configuration-console.tsx established for the identical gap.
  async function updatePolicy() {
    setPolicyLoading(true);
    setPolicyResult(null);
    setPolicyError(null);
    try {
      const result = await biometricsPort.manageRetentionPolicy(category, retentionDays);
      setPolicyResult(result);
    } catch {
      setPolicyError('Could not update the retention policy - the live service may be unavailable or the request was rejected.');
    } finally {
      setPolicyLoading(false);
    }
  }

  async function requestDeletion() {
    if (!recordId.trim()) {
      return;
    }
    setDeletionLoading(true);
    setDeletionResult(null);
    setDeletionError(null);
    try {
      const result = await biometricsPort.requestBiometricDeletion(kind, recordId.trim(), reason.trim() || 'operator-requested deletion');
      setDeletionResult(result);
    } catch {
      setDeletionError('Could not request deletion - the live service may be unavailable or the request was rejected.');
    } finally {
      setDeletionLoading(false);
    }
  }

  return (
    <section className={styles.section} aria-labelledby="retention-heading">
      <h2 id="retention-heading">Privacy &amp; retention</h2>
      <p role="note">
        BIO-BIO-009: retention policy and deletion requests are sent through the real BiometricService port, audited
        server-side — this page never stores biometric data.
      </p>
      <div className={styles.controlsRow}>
        <div className={styles.field}>
          <label htmlFor="retention-category">Category</label>
          <select
            id="retention-category"
            value={category}
            onChange={(event) => setCategory(event.target.value as RetentionCategory)}
          >
            {RETENTION_CATEGORIES.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </div>
        <div className={styles.field}>
          <label htmlFor="retention-days">Retention days</label>
          <input
            id="retention-days"
            type="number"
            min={1}
            value={retentionDays}
            onChange={(event) => setRetentionDays(Number(event.target.value))}
          />
        </div>
        <button type="button" className="biolog-button-primary" onClick={updatePolicy} disabled={policyLoading}>
          {policyLoading ? 'Updating…' : 'Update retention policy'}
        </button>
      </div>
      {policyResult && (
        <div data-testid="policy-result">
          <Badge {...policyBadge(policyResult)} />
        </div>
      )}
      {policyError && <p role="alert">{policyError}</p>}

      <div className={styles.controlsRow}>
        <div className={styles.field}>
          <label htmlFor="deletion-kind">Record kind</label>
          <select id="deletion-kind" value={kind} onChange={(event) => setKind(event.target.value as BiometricRecordKind)}>
            {RECORD_KINDS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </div>
        <div className={styles.field}>
          <label htmlFor="deletion-record-id">Record id</label>
          <input id="deletion-record-id" value={recordId} onChange={(event) => setRecordId(event.target.value)} />
        </div>
        <div className={styles.field}>
          <label htmlFor="deletion-reason">Reason</label>
          <input id="deletion-reason" value={reason} onChange={(event) => setReason(event.target.value)} />
        </div>
        <button type="button" className="biolog-button-primary" onClick={requestDeletion} disabled={deletionLoading}>
          {deletionLoading ? 'Requesting…' : 'Request deletion'}
        </button>
      </div>
      {deletionResult && (
        <div data-testid="deletion-result">
          <Badge {...deletionBadge(deletionResult)} />
        </div>
      )}
      {deletionError && <p role="alert">{deletionError}</p>}
    </section>
  );
}
