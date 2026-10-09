/**
 * Reads a Human Test Console preset off the URL (BIO-UX-012-AC01/AC03/AC06)
 * as an already-existing page's own initial state, the same way
 * `session-context.tsx` already reads `?tenant=`/`?as=` — additive only,
 * every consumer's default behaviour is unchanged when the param is absent.
 *
 * `process.env.NODE_ENV === 'production'` is inlined to a literal at
 * Next.js build time, so in a production build this always returns the
 * fallback before the search-param read happens, and that branch is
 * dead-code-eliminated entirely — a stray `?scenario=`/`?hint=` typed into
 * a production URL has no effect (BIO-UX-012-AC02 "without exposing
 * production-only controls", AC04). Same proven technique as
 * `feature-mode-panel.tsx`'s own production gate.
 *
 * Deliberately React-free (unlike `use-test-scenario-preset.ts`, which
 * wraps this): every other file `tests/frontend/unit/` exercises directly
 * is plain TypeScript with no `react` import, since that package has no
 * `react` dependency of its own — see `bio-ux-012-test-scenario-params.test.ts`.
 */
export function readTestScenarioParam<T extends string>(key: string, validValues: readonly T[], fallback: T): T {
  if (process.env.NODE_ENV === 'production' || typeof window === 'undefined') {
    return fallback;
  }
  const raw = new URLSearchParams(window.location.search).get(key);
  return raw !== null && (validValues as readonly string[]).includes(raw) ? (raw as T) : fallback;
}
