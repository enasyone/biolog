/**
 * Generic module port shape and mock/live adapter factories (BIO-UX-005-AC01, AC05).
 *
 * `02-ARCHITECTURE/UI-ADAPTER-ARCHITECTURE.md`'s port rule: "page/components call
 * feature ports. They do not call a mock store or raw backend endpoint directly."
 * Every one of BIOLOG's 31 existing routes (BIO-UX-002/003) today calls neither —
 * their "Simulate primary action" button just flips local component state
 * (`PlaceholderPage`'s own `useState`). This module gives every one of them a
 * real port to call instead, closing that gap end to end rather than leaving
 * the framework unproven.
 *
 * Every domain currently needs exactly one operation shape (a primary action
 * that can succeed, fail, or find the device/network offline) because no
 * route has shipped real feature-specific behaviour yet — that is each
 * domain story's job (BIO-UX-006 onward), not this one's. `ModulePort` is
 * therefore deliberately generic and reused everywhere; a domain story
 * extends its own `web/features/<domain>/ports.ts` interface with real
 * methods without touching this file or breaking any other module.
 */
import type { ServicePort } from './route-manifest';

export type PrimaryActionOutcome = 'success' | 'failure' | 'offline';

export interface PrimaryActionResult {
  outcome: PrimaryActionOutcome;
  /** Human-readable, already suitable to render directly (BIO-UX-005-AC05). */
  message: string;
}

/** Every module's stable port shape (BIO-UX-005-AC01). */
export interface ModulePort {
  readonly module: ServicePort;
  readonly mode: 'mock' | 'live';
  primaryAction(): Promise<PrimaryActionResult>;
}

/**
 * A deterministic scenario script (BIO-UX-005-AC05: "mock adapters reproduce
 * approved success, failure, loading, offline and boundary behaviours").
 * `loading` is not a scenario outcome — every mock call is asynchronous
 * (see `simulatedLatencyMs`), so the caller observes a real loading state
 * regardless of which scenario runs. Boundary: an empty `scenarios` array is
 * itself a boundary case, handled explicitly (`createMockAdapter` throws a
 * clear configuration error rather than looping forever or returning
 * `undefined`).
 */
export interface MockScenario {
  outcome: PrimaryActionOutcome;
  message: string;
}

const DEFAULT_SCENARIOS: readonly MockScenario[] = Object.freeze([
  Object.freeze({ outcome: 'success', message: 'Simulated — no live service is connected yet.' }),
]);

/** Small, deterministic pseudo-random cycle — no `Math.random()`, so repeated runs/tests are stable. */
function deterministicIndex(seed: string, length: number): number {
  let hash = 0;
  for (let i = 0; i < seed.length; i += 1) {
    hash = (hash * 31 + seed.charCodeAt(i)) >>> 0;
  }
  return hash % length;
}

export interface CreateMockAdapterOptions {
  scenarios?: readonly MockScenario[];
  /** Simulated network latency so callers genuinely observe a loading state. */
  simulatedLatencyMs?: number;
}

/**
 * A mock adapter that cycles deterministically through `scenarios` (default:
 * always succeed) — the same scenario sequence every run, so tests and
 * human demos are reproducible, not flaky (BIO-UX-005-AC05, AC06 "auditable").
 *
 * The call counter is scoped to this one adapter instance, not shared
 * module-wide: every module gets its own `web/features/<domain>/mock.ts`
 * singleton (one `createMockAdapter` call each), so each module's *own*
 * first call is always scenario 0, regardless of how many other modules'
 * ports were called first in the same browser session — a shared counter
 * would make a module's scenario depend on unrelated page-navigation order,
 * which is not "reproducible," it just moves the non-determinism elsewhere.
 */
export function createMockAdapter(
  module: ServicePort,
  { scenarios = DEFAULT_SCENARIOS, simulatedLatencyMs = 250 }: CreateMockAdapterOptions = {},
): ModulePort {
  if (scenarios.length === 0) {
    throw new Error(`createMockAdapter(${module}): scenarios must not be empty`);
  }
  let callCount = 0;
  return {
    module,
    mode: 'mock',
    async primaryAction() {
      callCount += 1;
      const index = deterministicIndex(`${module}:${callCount}`, scenarios.length);
      const scenario = scenarios[index]!;
      if (simulatedLatencyMs > 0) {
        await new Promise((resolve) => setTimeout(resolve, simulatedLatencyMs));
      }
      return { outcome: scenario.outcome, message: scenario.message };
    },
  };
}

/**
 * A live adapter stub (BIO-UX-005-AC04): the real shape a domain story fills
 * in once its backend endpoint exists, without any UI-side change — today it
 * genuinely calls `endpointPath` and genuinely fails, since no such backend
 * route exists yet. That failure is correct, expected behaviour for an
 * unconnected LIVE mode, not a bug to hide.
 */
/**
 * The shared fallback for a route whose page family has no backend port
 * mapping at all yet (`/alumni`, `/contractors`, `/nfc-uhf`, `/mobile-credentials`
 * — see `route-manifest.ts`'s `'Unassigned'` entries and their notes). Says so
 * plainly rather than pretending a module exists.
 */
export const UNASSIGNED_MOCK_PORT: ModulePort = createMockAdapter('Unassigned', {
  scenarios: [
    {
      outcome: 'success',
      message: 'No backend module is assigned to this page yet (SIMULATION) — see route-manifest.ts.',
    },
  ],
});

export function createLiveAdapter(module: ServicePort, endpointPath: string): ModulePort {
  return {
    module,
    mode: 'live',
    async primaryAction() {
      try {
        const response = await fetch(endpointPath, { method: 'POST' });
        if (!response.ok) {
          return { outcome: 'failure', message: `Live request failed: HTTP ${response.status}` };
        }
        const body = (await response.json()) as { message?: string };
        return { outcome: 'success', message: body.message ?? 'Live action completed.' };
      } catch {
        return {
          outcome: 'offline',
          message: `No live service reachable at ${endpointPath} yet — this module has not been connected.`,
        };
      }
    },
  };
}
