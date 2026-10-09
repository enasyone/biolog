import type { ModulePort, PrimaryActionResult } from '../../lib/module-port';

/**
 * Cards module ports (BIO-UX-007-AC01, AC02, AC03, AC04, AC06).
 *
 * BIO-UX-005 shipped this file with a single, deliberately empty
 * `CardsPort = ModulePort` alias — "no Cards-specific method exists yet
 * because no Cards screen ships real behaviour yet." This story is the one
 * that changes that: every real screen (Cards registry, Issue Card, Replace
 * Card, NFC/UHF, Mobile Credentials) needs an action with real, typed
 * arguments (which card, which reason), not the zero-argument
 * `primaryAction()` every prior placeholder route shared.
 *
 * Each action below is its own independently-resolvable port instance
 * (mock/live switchable on its own `NEXT_PUBLIC_FEATURE_MODE_CREDENTIALSERVICE_*`
 * override), following the exact precedent `features/reporting/ports.ts`
 * already established for a module with more than one real action — see
 * that file's own header comment. `CardsPort` (the original, unparameterised
 * shape) is kept and still registered under the `primaryAction` key: two
 * existing tests require every real domain module to keep a bare
 * `primaryAction` port (`bio-ux-005-module-registry.test.ts`), so it is not
 * removed, just no longer what any real Cards page actually calls.
 *
 * `CardActionResult` extends the shared `PrimaryActionResult` with an
 * optional `evidenceRef` — a deterministic synthetic reference standing in
 * for a real backend's audit/evidence record (BIO-UX-007-AC04 "primary
 * issue/replace/print/encode actions return simulated evidence"). It is
 * present only on `success`; a `failure`/`offline` outcome produced nothing
 * to reference.
 */
export interface CardActionResult extends PrimaryActionResult {
  evidenceRef?: string;
}

export type CardsPort = ModulePort;

/**
 * Every input below carries its own `tenantId` explicitly. This frontend has
 * no real authentication/session backend (`session-context.tsx`'s own header
 * comment: "not authentication or RBAC"), so there is no server-side session
 * to infer tenant scope from the way a real API would; the calling page
 * already holds it (`useSession().tenant.id`) and passes it through, exactly
 * as `synthetic-data`'s own generator functions are tenant-parameterised
 * throughout. A later live integration replaces this with a real auth
 * context without changing any port's call sites.
 */
export interface IssueCardInput {
  tenantId: string;
  personId: string;
}
export interface IssueCardPort extends ModulePort {
  primaryAction(input?: IssueCardInput): Promise<CardActionResult>;
}

export type ReplaceCardReason = 'lost' | 'damaged' | 'stolen' | 'other';
export interface ReplaceCardInput {
  tenantId: string;
  cardId: string;
  reason: ReplaceCardReason;
}
export interface ReplaceCardPort extends ModulePort {
  primaryAction(input?: ReplaceCardInput): Promise<CardActionResult>;
}

export interface CardIdInput {
  tenantId: string;
  cardId: string;
}
/** Shared shape for the three single-card lifecycle transitions: mark lost, recover, revoke. */
export interface CardTransitionPort extends ModulePort {
  primaryAction(input?: CardIdInput & { reason?: string }): Promise<CardActionResult>;
}

export type TagType = 'NFC' | 'UHF';
export interface EncodeTagInput {
  tenantId: string;
  cardId: string;
  tagType: TagType;
}
export interface EncodeTagPort extends ModulePort {
  primaryAction(input?: EncodeTagInput): Promise<CardActionResult>;
}

export interface VerifyReadPort extends ModulePort {
  primaryAction(input?: CardIdInput): Promise<CardActionResult>;
}

export interface PersonIdInput {
  tenantId: string;
  personId: string;
}
export interface RevokeMobileCredentialPort extends ModulePort {
  primaryAction(input?: PersonIdInput): Promise<CardActionResult>;
}

/**
 * BIO-UX-007-AC06: "stable service ports exist for later card lifecycle,
 * production and encoding integrations" applied to Card Designer
 * specifically — per Kemi's ruling (see `docs/features/bio-ux-007/README.md`),
 * the designer canvas itself (BIO-CRD-002/003/004) stays pure client-side
 * with zero I/O; this port is the stable seam a later story connects a real
 * "save design" backend call to, without this story redesigning the canvas.
 */
export interface SaveDesignInput {
  name: string;
}
export interface SaveDesignPort extends ModulePort {
  primaryAction(input?: SaveDesignInput): Promise<CardActionResult>;
}
