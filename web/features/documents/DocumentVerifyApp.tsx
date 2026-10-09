'use client';

import { useEffect, useMemo, useState } from 'react';
import { generateDataset } from '../../lib/synthetic-data';
import { StatusBadge, type StatusBadgeTone } from '../../components/status-badge';
import { readTestScenarioParam } from '../../dev-tools/test-scenario-params';
import {
  EVIDENCE_STEPS,
  documentsPort,
  requiresHumanReview,
  runEvidenceScenario,
  type EvidenceCheckState,
  type EvidenceStepId,
  type EvidenceStepResult,
} from './index';
import styles from './document-verify.module.css';

/**
 * The real BIO-UX-009 Document Verify journey, replacing the shell's
 * `PlaceholderPage` for `/document-verify` (BIO-UX-009-AC01: "Document
 * capture/upload, MRZ, barcode, OCR, document identification, NFC and
 * authenticity pages are interactive").
 *
 * A Client Component (not a Server Component + `PlaceholderPage`) because
 * every step here calls its own named `documentsPort` method and carries
 * its own local run/loading/result state — a genuinely interactive
 * multi-step journey, not one generic "Simulate primary action" button.
 */

type StepUIState = { status: 'idle' } | { status: 'loading' } | ({ status: 'done' } & EvidenceStepResult);

interface ReviewQueueEntry {
  id: string;
  step: EvidenceStepId | null;
  message: string;
  resolved: boolean;
}

const STATE_LABELS: Readonly<Record<EvidenceCheckState, string>> = Object.freeze({
  'not-checked': 'Not checked',
  read: 'Read',
  'structurally-valid': 'Structurally valid',
  consistent: 'Consistent',
  authentic: 'Authentic',
  failed: 'Failed',
  inconclusive: 'Inconclusive',
});

const STATE_BADGE_TONE: Readonly<Record<EvidenceCheckState, StatusBadgeTone>> = Object.freeze({
  'not-checked': 'neutral',
  read: 'info',
  'structurally-valid': 'info',
  consistent: 'success',
  authentic: 'success',
  failed: 'error',
  inconclusive: 'warning',
});

const STEP_ACTIONS: Readonly<Record<EvidenceStepId, () => Promise<EvidenceStepResult>>> = Object.freeze({
  capture: () => documentsPort.capture(),
  mrz: () => documentsPort.runMrz(),
  barcode: () => documentsPort.runBarcode(),
  ocr: () => documentsPort.runOcr(),
  'document-identification': () => documentsPort.identifyDocument(),
  nfc: () => documentsPort.runNfc(),
  authenticity: () => documentsPort.checkAuthenticity(),
  'selfie-comparison': () => documentsPort.compareSelfie(),
});

function initialStepUIState(): Record<EvidenceStepId, StepUIState> {
  const state = {} as Record<EvidenceStepId, StepUIState>;
  for (const step of EVIDENCE_STEPS) {
    state[step.id] = { status: 'idle' };
  }
  return state;
}

// The 'authenticity' step's own 3 non-default outcomes (evidence.ts's SCENARIOS table) — the
// only states BIO-UX-012's Human Test Console preset can request (see its own DOCUMENT_RESULTS).
const DOCUMENT_RESULT_PRESET_VALUES: readonly EvidenceCheckState[] = ['consistent', 'inconclusive', 'failed'];

/**
 * The first call count (1-based) at which `runEvidenceScenario('authenticity', n)` lands on
 * `target`, or `null` if none of the first 20 do (SCENARIOS has 4 entries; in practice every
 * value is reached within a handful of calls — 20 is a generous, cheap bound, not a tuned one).
 */
function findAuthenticityCallCount(target: EvidenceCheckState): number | null {
  for (let n = 1; n <= 20; n += 1) {
    if (runEvidenceScenario('authenticity', n).state === target) {
      return n;
    }
  }
  return null;
}

function StateBadge({ state }: { state: EvidenceCheckState }) {
  return (
    <span data-testid="evidence-state-badge">
      <StatusBadge tone={STATE_BADGE_TONE[state]}>{STATE_LABELS[state]}</StatusBadge>
    </span>
  );
}

function stepButtonLabel(stepId: EvidenceStepId, label: string, ui: StepUIState): string {
  if (ui.status === 'loading') return 'Working…';
  const hasRun = ui.status === 'done';
  if (stepId === 'capture') return hasRun ? 'Recapture / re-upload document' : 'Capture/upload document';
  if (stepId === 'ocr') return hasRun ? 'Retry OCR' : 'Run OCR';
  return hasRun ? `Retry ${label}` : `Run ${label}`;
}

export function DocumentVerifyApp() {
  // A fixed, deterministic seed (BIO-UX-004-AC01/AC03) — the same applicant
  // and document context every time this page loads, so a human demo or a
  // Playwright test can assert on stable names/labels.
  const dataset = useMemo(() => generateDataset(), []);
  const document = dataset.documents[0];
  const applicant = document ? dataset.people.find((p) => p.id === document.personId) : undefined;

  const [results, setResults] = useState<Record<EvidenceStepId, StepUIState>>(initialStepUIState);
  const [reviewQueue, setReviewQueue] = useState<readonly ReviewQueueEntry[]>([]);
  const [manualEscalations, setManualEscalations] = useState(0);

  // BIO-UX-012-AC03 (Human Test Console): a `?documentResult=` preset deterministically sets the
  // Authenticity step's result on arrival, so a chosen value has a real, observable effect — not
  // just navigation. Uses the same pure, already-exported `runEvidenceScenario` the Run button
  // itself calls (via `documentsPort.checkAuthenticity()`); this does not call the port or add
  // any new mock/hint mechanism, so it neither changes AC05's port contract nor this step's own
  // deterministic-by-call-count design. A post-mount `useEffect`, not a `useState` initializer —
  // see `web/dev-tools/use-test-scenario-preset.ts`'s own header comment for why an initializer
  // would silently never apply the preset (it also runs during SSR, before hydration settles).
  useEffect(() => {
    // Literal check here too, not only inside readTestScenarioParam: `process.env.NODE_ENV ===
    // 'production'` is inlined to a literal at build time, so this early return (and everything
    // after it) is dead-code-eliminated from a production bundle entirely - verified by grepping
    // .next/ output, not just asserted.
    if (process.env.NODE_ENV === 'production') return;
    const preset = readTestScenarioParam('documentResult', DOCUMENT_RESULT_PRESET_VALUES, '' as const);
    if (preset === '') return;
    const callCount = findAuthenticityCallCount(preset);
    if (callCount === null) return;
    setResults((prev) => ({ ...prev, authenticity: { status: 'done', ...runEvidenceScenario('authenticity', callCount) } }));
    // Only the initial URL matters (a one-time preset) — intentionally not re-run on prop changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const captureResult = results.capture;
  const captureSucceeded = captureResult.status === 'done' && captureResult.state !== 'not-checked' && captureResult.state !== 'failed';

  async function runStep(stepId: EvidenceStepId) {
    setResults((prev) => ({ ...prev, [stepId]: { status: 'loading' } }));
    const result = await STEP_ACTIONS[stepId]();
    setResults((prev) => ({ ...prev, [stepId]: { status: 'done', ...result } }));
    setReviewQueue((prev) => {
      const withoutThisStep = prev.filter((entry) => entry.step !== stepId);
      // A retry that no longer requires review drops any earlier queue entry for this step
      // (BIO-UX-009-AC03: the routing reflects the CURRENT result, not a stale one).
      return requiresHumanReview(result)
        ? [...withoutThisStep, { id: `step-${stepId}`, step: stepId, message: `${labelFor(stepId)}: ${result.message}`, resolved: false }]
        : withoutThisStep;
    });
  }

  function labelFor(stepId: EvidenceStepId): string {
    return EVIDENCE_STEPS.find((s) => s.id === stepId)?.label ?? stepId;
  }

  function routeToHumanReviewManually() {
    setManualEscalations((count) => count + 1);
    setReviewQueue((prev) => [
      ...prev,
      {
        id: `manual-${manualEscalations + 1}`,
        step: null,
        message: 'Manually escalated for human review.',
        resolved: false,
      },
    ]);
  }

  function resolveReviewEntry(id: string) {
    setReviewQueue((prev) => prev.map((entry) => (entry.id === id ? { ...entry, resolved: true } : entry)));
  }

  const openReviewCount = reviewQueue.filter((entry) => !entry.resolved).length;

  return (
    <div className="biolog-placeholder-page">
      <h1>Document Verify</h1>
      <p className="biolog-placeholder-description">
        Document capture, MRZ/barcode/OCR reads, document identification, NFC and authenticity checks
        (SIMULATION unless a live engine has been connected — see the feature-mode panel).
      </p>

      {document && applicant && (
        <div className={`biolog-card ${styles.context}`} data-testid="document-context">
          <span>
            <span className={styles.contextLabel}>Applicant</span>
            {applicant.displayName}
          </span>
          <span>
            <span className={styles.contextLabel}>Document type</span>
            {document.documentType.replace(/-/g, ' ')}
          </span>
        </div>
      )}

      <div className={styles.pageActions}>
        <button
          type="button"
          className={styles.buttonSecondary}
          onClick={routeToHumanReviewManually}
          disabled={!captureSucceeded}
        >
          Route to human review
        </button>
        {openReviewCount > 0 && (
          <span data-testid="review-queue-count">
            <StatusBadge tone="warning">
              {openReviewCount} item{openReviewCount === 1 ? '' : 's'} awaiting review
            </StatusBadge>
          </span>
        )}
      </div>

      <h2 className="biolog-visually-hidden">Verification steps</h2>
      <div className={styles.steps}>
        {EVIDENCE_STEPS.map((step) => {
          const ui = results[step.id];
          const gated = step.requiresCapture && !captureSucceeded;
          return (
            <section key={step.id} className={styles.stepCard} data-testid={`step-${step.id}`}>
              <h3>{step.label}</h3>
              <p className={styles.stepDescription}>{step.description}</p>
              {ui.status === 'done' ? (
                <>
                  <StateBadge state={ui.state} />
                  <p className={styles.stepMessage} role="status">
                    {ui.message}
                    {typeof ui.confidence === 'number' && ` (confidence ${Math.round(ui.confidence * 100)}%)`}
                  </p>
                </>
              ) : (
                <StateBadge state="not-checked" />
              )}
              <button
                type="button"
                className={styles.buttonPrimarySmall}
                onClick={() => runStep(step.id)}
                disabled={ui.status === 'loading' || gated}
              >
                {stepButtonLabel(step.id, step.label, ui)}
              </button>
              {gated && <p className={styles.stepGateHint}>Capture/upload the document first.</p>}
              {step.id === 'selfie-comparison' && (
                <div className={styles.selfieRow}>
                  <div className={styles.selfiePlaceholder} aria-hidden="true">
                    Live selfie (simulated)
                  </div>
                  <p className={styles.selfieNote}>
                    No camera or real biometric data is used — this comparison is entirely simulated
                    (BIO-UX-009-AC04).
                  </p>
                </div>
              )}
            </section>
          );
        })}
      </div>

      <section className={`biolog-card ${styles.section}`}>
        <h2>Evidence Comparison</h2>
        <table className="biolog-table" data-testid="evidence-comparison-table">
          <caption>Evidence Comparison — current state of every verification step</caption>
          <thead>
            <tr>
              <th scope="col">Area</th>
              <th scope="col">State</th>
              <th scope="col">Detail</th>
            </tr>
          </thead>
          <tbody>
            {EVIDENCE_STEPS.map((step) => {
              const ui = results[step.id];
              return (
                <tr key={step.id}>
                  <th scope="row">{step.label}</th>
                  <td>
                    <StateBadge state={ui.status === 'done' ? ui.state : 'not-checked'} />
                  </td>
                  <td>{ui.status === 'done' ? ui.message : '—'}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </section>

      <section className={`biolog-card ${styles.section}`} data-testid="human-review-queue">
        <h2>Human Review Queue (simulated)</h2>
        {reviewQueue.length === 0 ? (
          <p className={styles.reviewQueueEmpty}>No items require review.</p>
        ) : (
          <ul className={styles.reviewQueueList}>
            {reviewQueue.map((entry) => (
              <li
                key={entry.id}
                className={`${styles.reviewQueueItem} ${entry.resolved ? styles.reviewQueueItemResolved : ''}`}
              >
                <span className={styles.reviewQueueItemLabel}>{entry.message}</span>
                <button
                  type="button"
                  className={styles.buttonSecondary}
                  onClick={() => resolveReviewEntry(entry.id)}
                  disabled={entry.resolved}
                >
                  {entry.resolved ? 'Resolved' : 'Resolve'}
                </button>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
