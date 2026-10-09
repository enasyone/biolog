'use client';

import { useEffect, useState } from 'react';
import { readTestScenarioParam } from './test-scenario-params';

/**
 * `useState<T>(fallback)`, then applies a Human Test Console preset after
 * mount, in a `useEffect` — NOT a `useState` lazy initializer. A lazy
 * initializer runs during SSR too, where `window` is undefined, so
 * `readTestScenarioParam` would return `fallback` there; React then
 * preserves that server-computed initial state through hydration rather
 * than re-running the initializer client-side, so the preset would never
 * actually apply. This is exactly why `session-context.tsx`'s own
 * `?tenant=`/`?as=` reading uses a post-mount effect, not an initializer —
 * same fix, same reason, see its own header comment.
 */
export function useTestScenarioPreset<T extends string>(key: string, validValues: readonly T[], fallback: T): [T, (value: T) => void] {
  const [value, setValue] = useState<T>(fallback);
  useEffect(() => {
    const preset = readTestScenarioParam(key, validValues, fallback);
    if (preset !== fallback) {
      setValue(preset);
    }
    // Only the initial URL matters (a one-time preset, like tenant/role's own `?tenant=`/`?as=`
    // handling) — intentionally not re-run on prop changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  return [value, setValue];
}
