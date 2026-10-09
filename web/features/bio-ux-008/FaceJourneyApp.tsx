'use client';

import { useState } from 'react';
import { biometricsPort } from '../biometrics';
import type {
  FaceCaptureResult,
  FaceCaptureScenarioHint,
  FaceIdentificationResult,
  IdentificationScenarioHint,
  LivenessResult,
  LivenessScenarioHint,
  VerificationResult,
  VerificationScenarioHint,
} from '../biometrics/ports';
import { AUTO_OPTION_VALUE, FACE_CAPTURE_OPTIONS, IDENTIFICATION_OPTIONS, LIVENESS_OPTIONS, VERIFICATION_OPTIONS } from './scenarioOptions';
import { livenessBadge, qualityBadge, verificationBadge } from './formatters';
import { Badge } from './Badge';
import { DeviceConnectionPanel } from './DeviceConnectionPanel';
import { useTestScenarioPreset } from '../../dev-tools/use-test-scenario-preset';
import styles from './biometric-journeys.module.css';

// Subset of the console's "biometric result" values valid for a face capture
// (no 'exception' tone here) — an invalid/inapplicable value (e.g. coming
// from a link built for the fingerprint page) safely falls back to manual.
const FACE_BIOMETRIC_RESULT_VALUES: readonly FaceCaptureScenarioHint[] = ['good', 'low'];

/**
 * BIO-UX-008-AC02: face capture, quality, 1:1 verification, 1:N
 * identification and liveness placeholder states, all interactive against
 * `biometricsPort` (BIO-UX-008-AC05). "Liveness placeholder" is rendered
 * literally as a placeholder (AC04: never implies a real liveness engine ran).
 */
export function FaceJourneyApp() {
  return (
    <div className={styles.journey}>
      <p className={styles.syntheticBanner} role="note">
        SIMULATED — no real camera is attached, no face image is captured or stored (BIO-UX-008-AC04).
      </p>
      <section className={styles.section} aria-labelledby="face-device-heading">
        <h2 id="face-device-heading">Device connection</h2>
        <DeviceConnectionPanel port={biometricsPort} kind="face_camera" label="Face camera" />
      </section>
      <CaptureSection />
      <LivenessSection />
      <VerifySection />
      <IdentifySection />
    </div>
  );
}

function CaptureSection() {
  const [hint, setHint] = useTestScenarioPreset<FaceCaptureScenarioHint | ''>('hint', FACE_BIOMETRIC_RESULT_VALUES, '');
  const [result, setResult] = useState<FaceCaptureResult | null>(null);
  const [loading, setLoading] = useState(false);

  async function capture() {
    setLoading(true);
    setResult(null);
    const outcome = await biometricsPort.captureFace(hint === '' ? undefined : hint);
    setResult(outcome);
    setLoading(false);
  }

  return (
    <section className={styles.section} aria-labelledby="face-capture-heading">
      <h2 id="face-capture-heading">Face capture</h2>
      <div className={styles.controlsRow}>
        <div className={styles.field}>
          <label htmlFor="face-capture-hint">Simulate outcome</label>
          <select id="face-capture-hint" value={hint} onChange={(event) => setHint(event.target.value as FaceCaptureScenarioHint | '')}>
            <option value={AUTO_OPTION_VALUE}>Auto (cycles)</option>
            {FACE_CAPTURE_OPTIONS.map((option) => (
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
      {loading && (
        <p role="status" className={styles.progress}>
          Capturing…
        </p>
      )}
      {result && <Badge {...qualityBadge(result.outcome, result.score)} />}
    </section>
  );
}

function LivenessSection() {
  const [hint, setHint] = useState<LivenessScenarioHint | ''>('');
  const [result, setResult] = useState<LivenessResult | null>(null);
  const [loading, setLoading] = useState(false);

  async function check() {
    setLoading(true);
    setResult(null);
    const outcome = await biometricsPort.checkLiveness(hint === '' ? undefined : hint);
    setResult(outcome);
    setLoading(false);
  }

  return (
    <section className={styles.section} aria-labelledby="face-liveness-heading">
      <h2 id="face-liveness-heading">Liveness (placeholder)</h2>
      <p className={styles.summary}>
        Placeholder state only — no real liveness detection engine is connected yet (BIO-UX-008-AC02, AC04).
      </p>
      <div className={styles.controlsRow}>
        <div className={styles.field}>
          <label htmlFor="face-liveness-hint">Simulate outcome</label>
          <select id="face-liveness-hint" value={hint} onChange={(event) => setHint(event.target.value as LivenessScenarioHint | '')}>
            <option value={AUTO_OPTION_VALUE}>Auto (cycles)</option>
            {LIVENESS_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </div>
        <button type="button" className="biolog-button-primary" onClick={check} disabled={loading}>
          {loading ? 'Checking…' : 'Run liveness check'}
        </button>
      </div>
      {result && <Badge {...livenessBadge(result)} />}
    </section>
  );
}

function VerifySection() {
  const [hint, setHint] = useState<VerificationScenarioHint | ''>('');
  const [result, setResult] = useState<VerificationResult | null>(null);
  const [loading, setLoading] = useState(false);

  async function verify() {
    setLoading(true);
    setResult(null);
    const outcome = await biometricsPort.verifyFace(hint === '' ? undefined : hint);
    setResult(outcome);
    setLoading(false);
  }

  return (
    <section className={styles.section} aria-labelledby="face-verify-heading">
      <h2 id="face-verify-heading">1:1 verification</h2>
      <div className={styles.controlsRow}>
        <div className={styles.field}>
          <label htmlFor="face-verify-hint">Simulate outcome</label>
          <select id="face-verify-hint" value={hint} onChange={(event) => setHint(event.target.value as VerificationScenarioHint | '')}>
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
      {result && <Badge {...verificationBadge(result)} />}
    </section>
  );
}

function IdentifySection() {
  const [hint, setHint] = useState<IdentificationScenarioHint | ''>('');
  const [result, setResult] = useState<FaceIdentificationResult | null>(null);
  const [loading, setLoading] = useState(false);

  async function identify() {
    setLoading(true);
    setResult(null);
    const outcome = await biometricsPort.identifyFace(hint === '' ? undefined : hint);
    setResult(outcome);
    setLoading(false);
  }

  return (
    <section className={styles.section} aria-labelledby="face-identify-heading">
      <h2 id="face-identify-heading">1:N identification</h2>
      <div className={styles.controlsRow}>
        <div className={styles.field}>
          <label htmlFor="face-identify-hint">Simulate outcome</label>
          <select id="face-identify-hint" value={hint} onChange={(event) => setHint(event.target.value as IdentificationScenarioHint | '')}>
            <option value={AUTO_OPTION_VALUE}>Auto (cycles)</option>
            {IDENTIFICATION_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </div>
        <button type="button" className="biolog-button-primary" onClick={identify} disabled={loading}>
          {loading ? 'Searching…' : 'Run 1:N identification'}
        </button>
      </div>
      {result && (
        <>
          <p className={styles.summary}>{result.message}</p>
          {result.candidates.length > 0 && (
            <ul className={styles.candidateList} aria-label="Ranked candidates">
              {result.candidates.map((candidate) => (
                <li key={candidate.candidateId} className={styles.candidateRow}>
                  <span>{candidate.displayName}</span>
                  <span>{candidate.score}/100</span>
                </li>
              ))}
            </ul>
          )}
        </>
      )}
    </section>
  );
}
