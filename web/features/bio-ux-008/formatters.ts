/**
 * Pure display-formatting helpers (no React, no DOM) so quality/match/
 * liveness badge text and tone are unit-testable in Node (see
 * `scenarioOptions.ts`'s own header comment for why this is split out of
 * the `.tsx` components).
 */
import type { LivenessResult, MatchOutcome, QualityOutcome, VerificationResult } from '../biometrics/ports';

export type BadgeTone = 'success' | 'warning' | 'error';

export interface Badge {
  tone: BadgeTone;
  text: string;
}

export function qualityBadge(outcome: QualityOutcome, score: number): Badge {
  return outcome === 'good'
    ? { tone: 'success', text: `Good quality — ${score}/100 (SIMULATED)` }
    : { tone: 'warning', text: `Low quality — ${score}/100 (SIMULATED)` };
}

export function exceptionBadge(reason: string): Badge {
  return { tone: 'error', text: `Exception (SIMULATED): ${reason}` };
}

export function matchBadge(outcome: MatchOutcome, score: number): Badge {
  return outcome === 'match'
    ? { tone: 'success', text: `Match — ${score}/100 (SIMULATED)` }
    : { tone: 'error', text: `No match — ${score}/100 (SIMULATED)` };
}

export function verificationBadge(result: VerificationResult): Badge {
  return matchBadge(result.outcome, result.score);
}

export function livenessBadge(result: LivenessResult): Badge {
  return result.outcome === 'live'
    ? { tone: 'success', text: 'Live (SIMULATED placeholder)' }
    : { tone: 'warning', text: 'Spoof suspected (SIMULATED placeholder)' };
}
