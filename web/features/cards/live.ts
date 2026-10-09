import { createLiveAdapter } from '../../lib/module-port';
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

/**
 * Cards live adapters (BIO-UX-007-AC06, following BIO-UX-005-AC04's own
 * stub pattern). Each genuinely calls its own real backend endpoint this
 * domain's story wires up; until then every one of them genuinely fails
 * closed to `offline` (see `createLiveAdapter`'s own comment) — the correct
 * behaviour for an unconnected LIVE mode, not a bug. `createLiveAdapter`'s
 * returned object is structurally assignable to each of these richer,
 * parameterised port interfaces (its `primaryAction()` takes no arguments
 * and returns a `PrimaryActionResult`, a subset of what each interface
 * below asks for) without needing its own bespoke implementation per action.
 */
export const cardsLivePort: CardsPort = createLiveAdapter('CredentialService', '/api/v1/cards/primary-action');

export const cardsIssueLivePort: IssueCardPort = createLiveAdapter('CredentialService', '/api/v1/cards/issue');
export const cardsReplaceLivePort: ReplaceCardPort = createLiveAdapter('CredentialService', '/api/v1/cards/replace');
export const cardsMarkLostLivePort: CardTransitionPort = createLiveAdapter('CredentialService', '/api/v1/cards/mark-lost');
export const cardsRecoverLivePort: CardTransitionPort = createLiveAdapter('CredentialService', '/api/v1/cards/recover');
export const cardsRevokeLivePort: CardTransitionPort = createLiveAdapter('CredentialService', '/api/v1/cards/revoke');
export const cardsEncodeTagLivePort: EncodeTagPort = createLiveAdapter('CredentialService', '/api/v1/cards/encode-tag');
export const cardsVerifyReadLivePort: VerifyReadPort = createLiveAdapter('CredentialService', '/api/v1/cards/verify-read');
export const cardsRevokeMobileLivePort: RevokeMobileCredentialPort = createLiveAdapter(
  'CredentialService',
  '/api/v1/mobile-credentials/revoke',
);
export const cardsSaveDesignLivePort: SaveDesignPort = createLiveAdapter('CredentialService', '/api/v1/card-designer/save');
