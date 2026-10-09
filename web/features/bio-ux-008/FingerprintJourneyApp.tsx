'use client';

import { useMemo, useState } from 'react';
import { biometricsPort } from '../biometrics';
import {
  FINGER_LABELS,
  TENPRINT_FINGER_POSITIONS,
  type CaptureScenarioHint,
  type FingerPosition,
  type FingerprintCaptureResult,
  type VerificationResult,
  type VerificationScenarioHint,
} from '../biometrics/ports';
import { AUTO_OPTION_VALUE, FINGERPRINT_CAPTURE_OPTIONS, VERIFICATION_OPTIONS } from './scenarioOptions';
import { exceptionBadge, qualityBadge, verificationBadge } from './formatters';
import { Badge } from './Badge';
import { DeviceConnectionPanel } from './DeviceConnectionPanel';
import { countByStatus, createInitialTenprintSlots, isTenprintComplete, recordFingerCapture, type TenprintSlots } from './tenprintState';
import { useTestScenarioPreset } from '../../dev-tools/use-test-scenario-preset';
import { RetentionSection } from '../bio-bio-009/RetentionSection';
import styles from './biometric-journeys.module.css';

const BIOMETRIC_RESULT_VALUES: readonly CaptureScenarioHint[] = ['good', 'low', 'exception'];

/**
 * BIO-UX-008-AC01: fingerprint tenprint, single-finger, quality, exception
 * and verification screens, all interactive against `biometricsPort`
 * (BIO-UX-008-AC05: a stable port `/api/v1/biometrics/*` a later domain
 * story connects LIVE without this component changing). Every outcome is
 * clearly labelled SIMULATED (AC04) — see `mock.ts`'s own message text.
 */
export function FingerprintJourneyApp() {
  return (
    <div className={styles.journey}>
      <p className={styles.syntheticBanner} role="note">
        SIMULATED — no real fingerprint sensor is attached and no biometric data is stored (BIO-UX-008-AC04).
      </p>
      <section className={styles.section} aria-labelledby="fingerprint-device-heading">
        <h2 id="fingerprint-device-heading">Device connection</h2>
        <DeviceConnectionPanel port={biometricsPort} kind="fingerprint_scanner" label="Fingerprint scanner" />
      </section>
      <TenprintSection />
      <SingleFingerSection />
      <VerificationSection />
      <RetentionSection />
    </div>
  );
}

function TenprintSection() {
  const [slots, setSlots] = useState<TenprintSlots>(() => createInitialTenprintSlots());
  // Console's "biometric result" dimension pre-seeds only this, the primary/first
  // capture control — the single-finger/verification sections below stay manual
  // (BIO-UX-012-AC01/AC03: a documented, bounded scope, not every sub-section).
  const [hint, setHint] = useTestScenarioPreset<CaptureScenarioHint | ''>('hint', BIOMETRIC_RESULT_VALUES, '');
  const [capturingFinger, setCapturingFinger] = useState<FingerPosition | null>(null);

  const capturedOnly = countByStatus(slots, 'captured');
  const exceptions = countByStatus(slots, 'exception');
  // "Attempted" counts a resolved exception as progress too (tenprintState.ts's own
  // isTenprintComplete does the same) - a bandaged-finger exception is a completed
  // attempt for that slot, not a stalled one blocking the rest of the grid.
  const attempted = capturedOnly + exceptions;
  const complete = isTenprintComplete(slots);

  async function capture(finger: FingerPosition) {
    setCapturingFinger(finger);
    const result: FingerprintCaptureResult = await biometricsPort.captureFingerprint(finger, hint === '' ? undefined : hint);
    setSlots((current) => recordFingerCapture(current, result));
    setCapturingFinger(null);
  }

  return (
    <section className={styles.section} aria-labelledby="tenprint-heading">
      <div className={styles.sectionHeader}>
        <h2 id="tenprint-heading">Tenprint capture</h2>
        <span className={styles.progress} role="status">
          {attempted}/10 captured{exceptions > 0 ? `, ${exceptions} exception(s)` : ''}
          {complete ? ' — complete' : ''}
        </span>
      </div>
      <div className={styles.controlsRow}>
        <div className={styles.field}>
          <label htmlFor="tenprint-hint">Simulate next capture outcome</label>
          <select id="tenprint-hint" value={hint} onChange={(event) => setHint(event.target.value as CaptureScenarioHint | '')}>
            <option value={AUTO_OPTION_VALUE}>Auto (cycles)</option>
            {FINGERPRINT_CAPTURE_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </div>
      </div>
      <div className={styles.tenprintGrid}>
        {TENPRINT_FINGER_POSITIONS.map((finger) => {
          const slot = slots[finger];
          return (
            <div key={finger} className={styles.fingerSlot} data-status={slot.status}>
              <span className={styles.fingerLabel}>{FINGER_LABELS[finger]}</span>
              <button
                type="button"
                className="biolog-button-primary"
                onClick={() => capture(finger)}
                disabled={capturingFinger === finger}
              >
                {capturingFinger === finger ? 'Capturing…' : slot.status === 'pending' ? 'Capture' : 'Recapture'}
              </button>
              {slot.result && !slot.result.exceptionReason && <Badge {...qualityBadge(slot.result.outcome, slot.result.score)} />}
              {slot.result?.exceptionReason && <Badge {...exceptionBadge(slot.result.exceptionReason)} />}
            </div>
          );
        })}
      </div>
    </section>
  );
}

function SingleFingerSection() {
  const [finger, setFinger] = useState<FingerPosition>('right_index');
  const [hint, setHint] = useState<CaptureScenarioHint | ''>('');
  const [result, setResult] = useState<FingerprintCaptureResult | null>(null);
  const [loading, setLoading] = useState(false);

  async function capture() {
    setLoading(true);
    setResult(null);
    const outcome = await biometricsPort.captureFingerprint(finger, hint === '' ? undefined : hint);
    setResult(outcome);
    setLoading(false);
  }

  return (
    <section className={styles.section} aria-labelledby="single-finger-heading">
      <h2 id="single-finger-heading">Single-finger capture</h2>
      <div className={styles.controlsRow}>
        <div className={styles.field}>
          <label htmlFor="single-finger-select">Finger</label>
          <select id="single-finger-select" value={finger} onChange={(event) => setFinger(event.target.value as FingerPosition)}>
            {TENPRINT_FINGER_POSITIONS.map((position) => (
              <option key={position} value={position}>
                {FINGER_LABELS[position]}
              </option>
            ))}
          </select>
        </div>
        <div className={styles.field}>
          <label htmlFor="single-finger-hint">Simulate outcome</label>
          <select id="single-finger-hint" value={hint} onChange={(event) => setHint(event.target.value as CaptureScenarioHint | '')}>
            <option value={AUTO_OPTION_VALUE}>Auto (cycles)</option>
            {FINGERPRINT_CAPTURE_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </div>
        <button type="button" className="biolog-button-primary" onClick={capture} disabled={loading}>
          {loading ? 'Capturing…' : 'Capture'}
        </button>
      </div>
      {result && (result.exceptionReason ? <Badge {...exceptionBadge(result.exceptionReason)} /> : <Badge {...qualityBadge(result.outcome, result.score)} />)}
    </section>
  );
}

function VerificationSection() {
  const [finger, setFinger] = useState<FingerPosition>('right_index');
  const [hint, setHint] = useState<VerificationScenarioHint | ''>('');
  const [result, setResult] = useState<VerificationResult | null>(null);
  const [loading, setLoading] = useState(false);

  const badge = useMemo(() => (result ? verificationBadge(result) : null), [result]);

  async function verify() {
    setLoading(true);
    setResult(null);
    const outcome = await biometricsPort.verifyFingerprint(finger, hint === '' ? undefined : hint);
    setResult(outcome);
    setLoading(false);
  }

  return (
    <section className={styles.section} aria-labelledby="fingerprint-verify-heading">
      <h2 id="fingerprint-verify-heading">1:1 verification</h2>
      <div className={styles.controlsRow}>
        <div className={styles.field}>
          <label htmlFor="verify-finger-select">Finger</label>
          <select id="verify-finger-select" value={finger} onChange={(event) => setFinger(event.target.value as FingerPosition)}>
            {TENPRINT_FINGER_POSITIONS.map((position) => (
              <option key={position} value={position}>
                {FINGER_LABELS[position]}
              </option>
            ))}
          </select>
        </div>
        <div className={styles.field}>
          <label htmlFor="verify-hint">Simulate outcome</label>
          <select id="verify-hint" value={hint} onChange={(event) => setHint(event.target.value as VerificationScenarioHint | '')}>
            <option value={AUTO_OPTION_VALUE}>Auto (cycles)</option>
            {VERIFICATION_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </div>
        <button type="button" className="biolog-button-primary" onClick={verify} disabled={loading}>
          {loading ? 'Verifying…' : 'Run verification'}
        </button>
      </div>
      {badge && <Badge {...badge} />}
    </section>
  );
}
