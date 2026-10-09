'use client';

import { useState } from 'react';
import { StatusBadge } from '../../components/status-badge';
import { photosPort } from '.';
import type { PhotoQualityCapture, QualityVerdict } from './ports';

/**
 * BIO-PHO-003-AC01-AC05. Enrolment staff load a person's photo-quality capture history, submit
 * a new candidate photo, and see print-quality and biometric-suitability as two independent,
 * always-visible verdicts with their own reasons (AC01/AC02) before deciding to approve
 * (promotes a real BIO-PHO-001 PhotoMaster) or reject (requires a reason) - both decisions
 * audited server-side (AC03). No image filter of any kind exists anywhere in this path (AC04) -
 * this component only ever uploads the file the operator picked, unmodified.
 */
function VerdictLine({ label, verdict }: { label: string; verdict: QualityVerdict }) {
  return (
    <div className="biolog-op-field">
      <span>
        {label} <StatusBadge tone={verdict.passed ? 'success' : 'warning'}>{verdict.passed ? 'Pass' : 'Fail'}</StatusBadge>
      </span>
      {verdict.reasons.length > 0 && (
        <ul className="biolog-empty-state biolog-health-reason">
          {verdict.reasons.map((reason) => (
            <li key={reason}>{reason}</li>
          ))}
        </ul>
      )}
    </div>
  );
}

function CaptureCard({
  capture,
  onDecided,
}: {
  capture: PhotoQualityCapture;
  onDecided: (message: string) => void;
}) {
  const [working, setWorking] = useState(false);
  const [rejectReason, setRejectReason] = useState('');
  const [error, setError] = useState<string | null>(null);

  async function approve() {
    setWorking(true);
    setError(null);
    const result = await photosPort.approveCapture(capture.personId, capture.sequenceNumber, capture.version);
    setWorking(false);
    if (result.outcome === 'success') {
      onDecided(result.message);
    } else {
      setError(result.message);
    }
  }

  async function reject() {
    setWorking(true);
    setError(null);
    const result = await photosPort.rejectCapture(capture.personId, capture.sequenceNumber, rejectReason, capture.version);
    setWorking(false);
    if (result.outcome === 'success') {
      onDecided(result.message);
    } else {
      setError(result.message);
    }
  }

  return (
    <div className="biolog-card" data-testid={`photo-quality-capture-${capture.sequenceNumber}`}>
      <h3>
        Capture #{capture.sequenceNumber} <StatusBadge tone={capture.status === 'Approved' ? 'success' : capture.status === 'Rejected' ? 'error' : 'info'}>{capture.status}</StatusBadge>
      </h3>
      <p className="biolog-placeholder-description">
        {capture.format.toUpperCase()}, {capture.widthPx}x{capture.heightPx}px{capture.nativeDpi ? `, ${Math.round(capture.nativeDpi)} DPI` : ''} — submitted {capture.submittedAt}
      </p>
      <div className="biolog-op-form">
        <VerdictLine label="Print quality" verdict={capture.printQuality} />
        <VerdictLine label="Biometric suitability" verdict={capture.biometricSuitability} />
      </div>

      {capture.status === 'Approved' && (
        <p role="status" className="biolog-placeholder-status biolog-placeholder-status--success">
          Promoted to photo master version {capture.promotedMasterVersion}.
        </p>
      )}
      {capture.status === 'Rejected' && (
        <p role="status" className="biolog-placeholder-status biolog-placeholder-status--failure">
          Rejected: {capture.decisionReason}
        </p>
      )}

      {capture.status === 'Pending' && (
        <>
          <div className="biolog-identity-toolbar">
            <button type="button" className="biolog-button-primary" disabled={working} onClick={approve} data-testid={`approve-${capture.sequenceNumber}`}>
              {working ? 'Working…' : 'Approve'}
            </button>
          </div>
          <label className="biolog-op-field">
            <span>Rejection reason</span>
            <input value={rejectReason} onChange={(e) => setRejectReason(e.target.value)} data-testid={`reject-reason-${capture.sequenceNumber}`} />
          </label>
          <div className="biolog-identity-toolbar">
            <button
              type="button"
              className="biolog-button-secondary"
              disabled={working || rejectReason.trim().length === 0}
              onClick={reject}
              data-testid={`reject-${capture.sequenceNumber}`}
            >
              {working ? 'Working…' : 'Reject'}
            </button>
          </div>
        </>
      )}

      {error && (
        <p role="status" className="biolog-placeholder-status biolog-placeholder-status--failure">
          {error}
        </p>
      )}
    </div>
  );
}

export function PhotoQualityReview() {
  const [personId, setPersonId] = useState('');
  const [captures, setCaptures] = useState<readonly PhotoQualityCapture[] | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [file, setFile] = useState<File | null>(null);

  async function load(targetPersonId: string) {
    if (!targetPersonId.trim()) return;
    setLoading(true);
    const result = await photosPort.listCaptures(targetPersonId.trim());
    setLoading(false);
    setStatus(result.message);
    if (result.outcome === 'success') {
      setCaptures(result.captures ?? []);
    }
  }

  async function submit() {
    if (!personId.trim() || !file) return;
    setLoading(true);
    const result = await photosPort.submitCapture(personId.trim(), file);
    setLoading(false);
    setStatus(result.message);
    if (result.outcome === 'success') {
      setFile(null);
      await load(personId);
    }
  }

  return (
    <div className="biolog-placeholder-page">
      <h1>Photographs</h1>
      <p className="biolog-placeholder-description biolog-health-description">
        Print-quality and biometric-suitability review for a person&rsquo;s candidate photos — independent verdicts, operator decision audited.
      </p>

      <section className="biolog-card" data-testid="photo-quality-review">
        <div className="biolog-op-form">
          <label className="biolog-op-field">
            <span>Person ID</span>
            <input value={personId} onChange={(e) => setPersonId(e.target.value)} data-testid="photo-quality-person-id" />
          </label>
        </div>
        <div className="biolog-identity-toolbar">
          <button type="button" className="biolog-button-secondary" disabled={loading || !personId.trim()} onClick={() => load(personId)} data-testid="photo-quality-load">
            {loading ? 'Working…' : 'Load captures'}
          </button>
        </div>

        <div className="biolog-op-form">
          <label className="biolog-op-field">
            <span>New candidate photo</span>
            <input type="file" accept="image/png,image/jpeg" onChange={(e) => setFile(e.target.files?.[0] ?? null)} data-testid="photo-quality-file" />
          </label>
        </div>
        <div className="biolog-identity-toolbar">
          <button type="button" className="biolog-button-primary" disabled={loading || !personId.trim() || !file} onClick={submit} data-testid="photo-quality-submit">
            {loading ? 'Working…' : 'Submit for review'}
          </button>
        </div>

        {status && (
          <p role="status" className="biolog-placeholder-status">
            {status}
          </p>
        )}
      </section>

      {captures && captures.length === 0 && <p className="biolog-empty-state">No captures submitted yet for this person.</p>}

      {captures && captures.length > 0 && (
        <div className="biolog-metrics-grid" data-testid="photo-quality-captures">
          {captures.map((capture) => (
            <CaptureCard key={capture.sequenceNumber} capture={capture} onDecided={(message) => { setStatus(message); void load(personId); }} />
          ))}
        </div>
      )}
    </div>
  );
}
