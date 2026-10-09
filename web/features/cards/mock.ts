import { createMockAdapter } from '../../lib/module-port';
import * as store from './data-store';
import type {
  CardActionResult,
  CardIdInput,
  CardsPort,
  EncodeTagInput,
  EncodeTagPort,
  IssueCardInput,
  IssueCardPort,
  PersonIdInput,
  ReplaceCardInput,
  ReplaceCardPort,
  RevokeMobileCredentialPort,
  CardTransitionPort,
  SaveDesignInput,
  SaveDesignPort,
  VerifyReadPort,
} from './ports';

/**
 * Cards mock adapters (BIO-UX-007-AC01-AC04, following BIO-UX-005-AC05's own
 * "reproduce approved success, failure, loading, offline and boundary
 * behaviours" bar).
 *
 * Unlike the generic placeholder ports (`createMockAdapter`), a Cards action
 * has a real, checkable precondition (a card already active/lost/replaced,
 * a person who already holds a card, etc.) — so `failure` here always means
 * the same reproducible thing a real backend would reject for, not a
 * scripted outcome unrelated to the input (`data-store.ts`'s own mutation
 * functions are the single source of truth for what is/isn't valid).
 * `offline` is the one outcome still on a deterministic schedule (every 5th
 * call to a given action), simulating a dropped connection *before* the
 * request would have reached the "server" — nothing is mutated when it
 * fires, exactly like a real request that never arrived.
 *
 * Every `primaryAction` below takes its real input as an *optional*
 * parameter (`input?: X`), not because it is ever meaningfully optional in
 * practice (every real page call site always passes it), but because
 * `ModulePort.primaryAction` itself takes zero arguments and TypeScript
 * checks parameter count/required-ness strictly even for method-shorthand
 * members — an interface whose method *requires* an argument cannot extend
 * one whose method takes none (see `ports.ts`'s own header note). A missing
 * `input` is therefore a genuine programmer error, not a real runtime case,
 * and is handled as an explicit `failure` rather than a thrown exception so
 * a caller that forgets to pass it gets a visible, testable result instead
 * of an unhandled rejection.
 */
const cardsMockPort: CardsPort = createMockAdapter('CredentialService', {
  scenarios: [
    { outcome: 'success', message: 'Cards: simulated action completed.' },
    { outcome: 'failure', message: 'Cards: simulated action failed — try again.' },
    { outcome: 'offline', message: 'Cards: device or network appears offline (simulated).' },
  ],
});
export { cardsMockPort };

function evidenceRef(action: string, n: number): string {
  return `SIM-EVD-${action}-${String(n).padStart(6, '0')}`;
}

const MISSING_INPUT: CardActionResult = { outcome: 'failure', message: 'No input was provided to this action.' };

/** Every 5th call to a given action simulates a dropped connection — see this file's own header note. */
function makeOfflineGate(): () => boolean {
  let n = 0;
  return () => {
    n += 1;
    return n % 5 === 0;
  };
}

export const cardsIssueMockPort: IssueCardPort = (() => {
  const isOffline = makeOfflineGate();
  let n = 0;
  return {
    module: 'CredentialService',
    mode: 'mock',
    async primaryAction(input?: IssueCardInput): Promise<CardActionResult> {
      if (!input) return MISSING_INPUT;
      if (isOffline()) {
        return { outcome: 'offline', message: 'Cards: device or network appears offline (simulated).' };
      }
      n += 1;
      const result = store.issueCard(input.tenantId, input.personId);
      if (!result.ok) {
        return { outcome: 'failure', message: `Issue card failed: ${result.reason}` };
      }
      return {
        outcome: 'success',
        message: `Issued card ${result.credential!.cardNumber} for ${input.personId}.`,
        evidenceRef: evidenceRef('issue', n),
      };
    },
  };
})();

export const cardsReplaceMockPort: ReplaceCardPort = (() => {
  const isOffline = makeOfflineGate();
  let n = 0;
  return {
    module: 'CredentialService',
    mode: 'mock',
    async primaryAction(input?: ReplaceCardInput): Promise<CardActionResult> {
      if (!input) return MISSING_INPUT;
      if (isOffline()) {
        return { outcome: 'offline', message: 'Cards: device or network appears offline (simulated).' };
      }
      n += 1;
      const result = store.replaceCard(input.tenantId, input.cardId, input.reason);
      if (!result.ok) {
        return { outcome: 'failure', message: `Replace card failed: ${result.reason}` };
      }
      return {
        outcome: 'success',
        message: `Card replaced — new card ${result.credential!.cardNumber} issued.`,
        evidenceRef: evidenceRef('replace', n),
      };
    },
  };
})();

function transitionPort(
  action: 'mark-lost' | 'recover' | 'revoke',
  run: (tenantId: string, cardId: string, reason?: string) => store.MutationOutcome,
): CardTransitionPort {
  const isOffline = makeOfflineGate();
  let n = 0;
  return {
    module: 'CredentialService',
    mode: 'mock',
    async primaryAction(input?: CardIdInput & { reason?: string }): Promise<CardActionResult> {
      if (!input) return MISSING_INPUT;
      if (isOffline()) {
        return { outcome: 'offline', message: 'Cards: device or network appears offline (simulated).' };
      }
      n += 1;
      const result = run(input.tenantId, input.cardId, input.reason);
      if (!result.ok) {
        return { outcome: 'failure', message: `${action}: ${result.reason}` };
      }
      return {
        outcome: 'success',
        message: `Card ${result.credential!.cardNumber} is now ${result.credential!.status}.`,
        evidenceRef: evidenceRef(action, n),
      };
    },
  };
}

export const cardsMarkLostMockPort: CardTransitionPort = transitionPort('mark-lost', (t, c, r) => store.markCardLost(t, c, r));
export const cardsRecoverMockPort: CardTransitionPort = transitionPort('recover', (t, c) => store.recoverCard(t, c));
export const cardsRevokeMockPort: CardTransitionPort = transitionPort('revoke', (t, c, r) => store.revokeCard(t, c, r ?? 'Revoked'));

export const cardsEncodeTagMockPort: EncodeTagPort = (() => {
  const isOffline = makeOfflineGate();
  let n = 0;
  return {
    module: 'CredentialService',
    mode: 'mock',
    async primaryAction(input?: EncodeTagInput): Promise<CardActionResult> {
      if (!input) return MISSING_INPUT;
      if (isOffline()) {
        return { outcome: 'offline', message: 'NFC/UHF: encoder appears offline (simulated).' };
      }
      const credential = store.getCredential(input.tenantId, input.cardId);
      if (!credential) {
        return { outcome: 'failure', message: `Encode tag failed: no credential ${input.cardId}.` };
      }
      if (credential.status !== 'active' && credential.status !== 'recovered') {
        return { outcome: 'failure', message: `Encode tag failed: card is ${credential.status}, not active.` };
      }
      n += 1;
      return {
        outcome: 'success',
        message: `${input.tagType} tag encoded for card ${credential.cardNumber}.`,
        evidenceRef: evidenceRef('encode', n),
      };
    },
  };
})();

export const cardsVerifyReadMockPort: VerifyReadPort = (() => {
  const isOffline = makeOfflineGate();
  let n = 0;
  return {
    module: 'CredentialService',
    mode: 'mock',
    async primaryAction(input?: CardIdInput): Promise<CardActionResult> {
      if (!input) return MISSING_INPUT;
      if (isOffline()) {
        return { outcome: 'offline', message: 'NFC/UHF: reader appears offline (simulated).' };
      }
      const credential = store.getCredential(input.tenantId, input.cardId);
      if (!credential) {
        return { outcome: 'failure', message: `Verify read failed: no credential ${input.cardId}.` };
      }
      n += 1;
      return {
        outcome: 'success',
        message: `Read verified for card ${credential.cardNumber} (${credential.status}).`,
        evidenceRef: evidenceRef('verify-read', n),
      };
    },
  };
})();

export const cardsRevokeMobileMockPort: RevokeMobileCredentialPort = (() => {
  const isOffline = makeOfflineGate();
  let n = 0;
  return {
    module: 'CredentialService',
    mode: 'mock',
    async primaryAction(input?: PersonIdInput): Promise<CardActionResult> {
      if (!input) return MISSING_INPUT;
      if (isOffline()) {
        return { outcome: 'offline', message: 'Mobile Credentials: device or network appears offline (simulated).' };
      }
      const credential = store
        .listCredentials(input.tenantId)
        .find((c) => c.personId === input.personId && (c.status === 'active' || c.status === 'recovered'));
      if (!credential) {
        return { outcome: 'failure', message: `Revoke mobile credential failed: no active credential for ${input.personId}.` };
      }
      const result = store.revokeCard(input.tenantId, credential.id, 'Mobile credential revoked');
      if (!result.ok) {
        return { outcome: 'failure', message: `Revoke mobile credential failed: ${result.reason}` };
      }
      n += 1;
      return {
        outcome: 'success',
        message: `Mobile credential revoked for ${input.personId}.`,
        evidenceRef: evidenceRef('revoke-mobile', n),
      };
    },
  };
})();

export const cardsSaveDesignMockPort: SaveDesignPort = (() => {
  const isOffline = makeOfflineGate();
  let n = 0;
  return {
    module: 'CredentialService',
    mode: 'mock',
    async primaryAction(input?: SaveDesignInput): Promise<CardActionResult> {
      if (!input) return MISSING_INPUT;
      if (isOffline()) {
        return { outcome: 'offline', message: 'Card Designer: device or network appears offline (simulated).' };
      }
      if (!input.name.trim()) {
        return { outcome: 'failure', message: 'Save design failed: a design name is required.' };
      }
      n += 1;
      return {
        outcome: 'success',
        message: `Design "${input.name}" saved (simulated).`,
        evidenceRef: evidenceRef('save-design', n),
      };
    },
  };
})();
