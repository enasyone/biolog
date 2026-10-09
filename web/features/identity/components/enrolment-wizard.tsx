'use client';

import Link from 'next/link';
import { useState } from 'react';
import { useSession } from '../../../context/session-context';
import { biometricsPort } from '../../biometrics';
import { cardsPort } from '../../cards';
import { photosPort } from '../../photos';
import { identityPort } from '../index';
import type { EnrolmentResult, PersonCategory } from '../types';
import { PermissionDeniedState } from './page-states';

type StepId = 'identity' | 'profile' | 'photo' | 'biometric' | 'credential' | 'review';

const STEPS: readonly { id: StepId; label: string }[] = [
  { id: 'identity', label: 'Identity' },
  { id: 'profile', label: 'Profile' },
  { id: 'photo', label: 'Photo' },
  { id: 'biometric', label: 'Biometric' },
  { id: 'credential', label: 'Credential' },
  { id: 'review', label: 'Review' },
];

interface Draft {
  firstName: string;
  lastName: string;
  dateOfBirth: string;
  category: PersonCategory;
  photoCaptured: boolean;
  photoMessage: string | null;
  biometricCaptured: boolean;
  biometricMessage: string | null;
  credentialAssigned: boolean;
  credentialMessage: string | null;
}

const EMPTY_DRAFT: Draft = {
  firstName: '',
  lastName: '',
  dateOfBirth: '',
  category: 'student',
  photoCaptured: false,
  photoMessage: null,
  biometricCaptured: false,
  biometricMessage: null,
  credentialAssigned: false,
  credentialMessage: null,
};

/**
 * New Enrolment wizard (BIO-UX-006-AC02). Photo/biometric/credential stages
 * call the already-scaffolded `PhotoService`/`BiometricService`/
 * `CredentialService` mock ports' generic `primaryAction()` (BIO-UX-005) —
 * this story does not build those domains' own real screens, only proves
 * an enrolment can drive their existing simulated primary action, exactly
 * as those modules are designed to be called (see `module-port.ts`).
 */
export function EnrolmentWizard() {
  const session = useSession();
  const [stepIndex, setStepIndex] = useState(0);
  const [draft, setDraft] = useState<Draft>(EMPTY_DRAFT);
  const [working, setWorking] = useState(false);
  const [submitResult, setSubmitResult] = useState<EnrolmentResult | null>(null);

  const canEnrol = session.permissions.has('nav:enrolment');
  const step = STEPS[stepIndex]!;

  function goNext() {
    setStepIndex((i) => Math.min(i + 1, STEPS.length - 1));
  }
  function goBack() {
    setStepIndex((i) => Math.max(i - 1, 0));
  }

  async function runPhotoStage() {
    setWorking(true);
    const result = await photosPort.primaryAction();
    setDraft((d) => ({ ...d, photoCaptured: result.outcome === 'success', photoMessage: result.message }));
    setWorking(false);
  }

  async function runBiometricStage() {
    setWorking(true);
    const result = await biometricsPort.primaryAction();
    setDraft((d) => ({ ...d, biometricCaptured: result.outcome === 'success', biometricMessage: result.message }));
    setWorking(false);
  }

  async function runCredentialStage() {
    setWorking(true);
    const result = await cardsPort.primaryAction();
    setDraft((d) => ({ ...d, credentialAssigned: result.outcome === 'success', credentialMessage: result.message }));
    setWorking(false);
  }

  async function submitEnrolment() {
    setWorking(true);
    const result = await identityPort.submitEnrolment({
      tenantId: session.tenant.id,
      firstName: draft.firstName,
      lastName: draft.lastName,
      dateOfBirth: draft.dateOfBirth,
      category: draft.category,
      photoCaptured: draft.photoCaptured,
      biometricCaptured: draft.biometricCaptured,
      credentialAssigned: draft.credentialAssigned,
    });
    setSubmitResult(result);
    setWorking(false);
  }

  if (!canEnrol) {
    return (
      <div className="biolog-placeholder-page">
        <h1>New Enrolment</h1>
        <PermissionDeniedState>
          Your current role ({session.role.name}) does not include the &ldquo;nav:enrolment&rdquo;
          permission required to start an enrolment.
        </PermissionDeniedState>
      </div>
    );
  }

  if (submitResult) {
    return (
      <div className="biolog-placeholder-page">
        <h1>New Enrolment</h1>
        <section className="biolog-card">
          <h2>{submitResult.outcome === 'success' ? 'Enrolment submitted' : 'Enrolment not submitted'}</h2>
          <p
            role="status"
            className={`biolog-identity-status biolog-identity-status--${submitResult.outcome === 'success' ? 'ready' : 'error'}`}
          >
            {submitResult.message}
          </p>
          <div className="biolog-identity-toolbar">
            {submitResult.personId && (
              <Link href={`/people/${submitResult.personId}`} className="biolog-button-primary">
                View new profile
              </Link>
            )}
            <button
              type="button"
              className="biolog-button-secondary"
              onClick={() => {
                setDraft(EMPTY_DRAFT);
                setSubmitResult(null);
                setStepIndex(0);
              }}
            >
              Start another enrolment
            </button>
          </div>
        </section>
      </div>
    );
  }

  return (
    <div className="biolog-placeholder-page">
      <h1>New Enrolment</h1>
      <p className="biolog-placeholder-description">
        Step-by-step simulated enrolment for {session.tenant.name}. Every stage calls its module's mock
        adapter — no real capture hardware or backend is involved (SIMULATION).
      </p>

      <nav aria-label="Enrolment steps" className="biolog-identity-wizard-steps">
        <ol>
          {STEPS.map((s, i) => (
            <li key={s.id} aria-current={i === stepIndex ? 'step' : undefined} className={i === stepIndex ? 'biolog-identity-wizard-step--current' : undefined}>
              {s.label}
            </li>
          ))}
        </ol>
      </nav>

      <section className="biolog-card">
        {step.id === 'identity' && (
          <>
            <h2>Identity</h2>
            {/* TD-UX-FIDELITY-FIXES-BATCH1: matches Access Control's own form fields
                (features/access/access-page.tsx) exactly — .biolog-op-field (not
                .biolog-field-row, whose input styling is scoped to
                input[type='text']/select and would miss this step's type="date"
                field) stacks a label span above its control with real spacing. */}
            <div className="biolog-op-form">
              <label className="biolog-op-field">
                <span>First name</span>
                <input
                  value={draft.firstName}
                  onChange={(e) => setDraft((d) => ({ ...d, firstName: e.target.value }))}
                />
              </label>
              <label className="biolog-op-field">
                <span>Last name</span>
                <input
                  value={draft.lastName}
                  onChange={(e) => setDraft((d) => ({ ...d, lastName: e.target.value }))}
                />
              </label>
              <label className="biolog-op-field">
                <span>Date of birth</span>
                <input
                  type="date"
                  value={draft.dateOfBirth}
                  onChange={(e) => setDraft((d) => ({ ...d, dateOfBirth: e.target.value }))}
                />
              </label>
            </div>
          </>
        )}

        {step.id === 'profile' && (
          <>
            <h2>Profile</h2>
            <div className="biolog-op-form">
              <label className="biolog-op-field">
                <span>Category</span>
                <select
                  value={draft.category}
                  onChange={(e) => setDraft((d) => ({ ...d, category: e.target.value as PersonCategory }))}
                >
                  <option value="student">Student</option>
                  <option value="staff">Staff</option>
                  <option value="alumni">Alumni</option>
                  <option value="contractor">Contractor</option>
                </select>
              </label>
            </div>
          </>
        )}

        {step.id === 'photo' && (
          <>
            <h2>Photo</h2>
            <button type="button" className="biolog-button-primary" disabled={working} onClick={runPhotoStage}>
              {working ? 'Working…' : 'Capture photo (simulated)'}
            </button>
            {draft.photoMessage && (
              <p role="status" className="biolog-identity-status biolog-identity-status--inline">
                {draft.photoMessage}
              </p>
            )}
          </>
        )}

        {step.id === 'biometric' && (
          <>
            <h2>Biometric</h2>
            <button type="button" className="biolog-button-primary" disabled={working} onClick={runBiometricStage}>
              {working ? 'Working…' : 'Capture biometric (simulated)'}
            </button>
            {draft.biometricMessage && (
              <p role="status" className="biolog-identity-status biolog-identity-status--inline">
                {draft.biometricMessage}
              </p>
            )}
          </>
        )}

        {step.id === 'credential' && (
          <>
            <h2>Credential</h2>
            <button type="button" className="biolog-button-primary" disabled={working} onClick={runCredentialStage}>
              {working ? 'Working…' : 'Assign credential (simulated)'}
            </button>
            {draft.credentialMessage && (
              <p role="status" className="biolog-identity-status biolog-identity-status--inline">
                {draft.credentialMessage}
              </p>
            )}
          </>
        )}

        {step.id === 'review' && (
          <>
            <h2>Review</h2>
            <dl className="biolog-identity-dl">
              <dt>Name</dt>
              <dd>
                {draft.firstName} {draft.lastName}
              </dd>
              <dt>Date of birth</dt>
              <dd>{draft.dateOfBirth || '—'}</dd>
              <dt>Category</dt>
              <dd>{draft.category}</dd>
              <dt>Photo</dt>
              <dd>{draft.photoCaptured ? 'Captured' : 'Not captured'}</dd>
              <dt>Biometric</dt>
              <dd>{draft.biometricCaptured ? 'Captured' : 'Not captured'}</dd>
              <dt>Credential</dt>
              <dd>{draft.credentialAssigned ? 'Assigned' : 'Not assigned'}</dd>
            </dl>
            <button type="button" className="biolog-button-primary" disabled={working} onClick={submitEnrolment}>
              {working ? 'Submitting…' : 'Submit enrolment (simulated)'}
            </button>
          </>
        )}

        <div className="biolog-identity-toolbar">
          <button type="button" className="biolog-button-secondary" disabled={stepIndex === 0} onClick={goBack}>
            Back
          </button>
          {step.id !== 'review' && (
            <button type="button" className="biolog-button-secondary" onClick={goNext}>
              Next
            </button>
          )}
        </div>
      </section>
    </div>
  );
}
