import { resolvePort } from '../../lib/feature-mode';
import {
  cardsMockPort,
  cardsIssueMockPort,
  cardsReplaceMockPort,
  cardsMarkLostMockPort,
  cardsRecoverMockPort,
  cardsRevokeMockPort,
  cardsEncodeTagMockPort,
  cardsVerifyReadMockPort,
  cardsRevokeMobileMockPort,
  cardsSaveDesignMockPort,
} from './mock';
import {
  cardsLivePort,
  cardsIssueLivePort,
  cardsReplaceLivePort,
  cardsMarkLostLivePort,
  cardsRecoverLivePort,
  cardsRevokeLivePort,
  cardsEncodeTagLivePort,
  cardsVerifyReadLivePort,
  cardsRevokeMobileLivePort,
  cardsSaveDesignLivePort,
} from './live';
import type {
  CardsPort,
  EncodeTagPort,
  IssueCardPort,
  ReplaceCardPort,
  RevokeMobileCredentialPort,
  CardTransitionPort,
  SaveDesignPort,
  VerifyReadPort,
} from './ports';

export type {
  CardsPort,
  CardActionResult,
  IssueCardInput,
  IssueCardPort,
  ReplaceCardInput,
  ReplaceCardPort,
  ReplaceCardReason,
  CardIdInput,
  CardTransitionPort,
  EncodeTagInput,
  EncodeTagPort,
  TagType,
  VerifyReadPort,
  PersonIdInput,
  RevokeMobileCredentialPort,
  SaveDesignInput,
  SaveDesignPort,
} from './ports';

/**
 * The Cards module's resolved ports (BIO-UX-007-AC01-AC06, following the
 * multi-port pattern `features/reporting/index.ts` already established).
 * Pages/components import these, never `./mock` or `./live` directly, and
 * each resolves independently: switching
 * `NEXT_PUBLIC_FEATURE_MODE_CREDENTIALSERVICE` moves every one of them from
 * SIMULATION to LIVE together (the whole-module default), and a per-port
 * override (see `feature-mode.ts`'s `PER_PORT_ENV_DEFAULTS`) can move just
 * one of them independently if a later story needs to.
 */
export const cardsPort: CardsPort = resolvePort(cardsMockPort, cardsLivePort);
export const cardsIssuePort: IssueCardPort = resolvePort(cardsIssueMockPort, cardsIssueLivePort, 'issueCard');
export const cardsReplacePort: ReplaceCardPort = resolvePort(cardsReplaceMockPort, cardsReplaceLivePort, 'replaceCard');
export const cardsMarkLostPort: CardTransitionPort = resolvePort(cardsMarkLostMockPort, cardsMarkLostLivePort, 'markLost');
export const cardsRecoverPort: CardTransitionPort = resolvePort(cardsRecoverMockPort, cardsRecoverLivePort, 'recoverCard');
export const cardsRevokePort: CardTransitionPort = resolvePort(cardsRevokeMockPort, cardsRevokeLivePort, 'revokeCard');
export const cardsEncodeTagPort: EncodeTagPort = resolvePort(cardsEncodeTagMockPort, cardsEncodeTagLivePort, 'encodeTag');
export const cardsVerifyReadPort: VerifyReadPort = resolvePort(cardsVerifyReadMockPort, cardsVerifyReadLivePort, 'verifyRead');
export const cardsRevokeMobilePort: RevokeMobileCredentialPort = resolvePort(
  cardsRevokeMobileMockPort,
  cardsRevokeMobileLivePort,
  'revokeMobileCredential',
);
export const cardsSaveDesignPort: SaveDesignPort = resolvePort(cardsSaveDesignMockPort, cardsSaveDesignLivePort, 'saveDesign');
