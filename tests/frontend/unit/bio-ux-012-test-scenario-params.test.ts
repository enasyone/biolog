// Developer tests for BIO-UX-012-AC01/AC02/AC04. SYNTHETIC: no real data.
import { afterEach, describe, expect, it } from 'vitest';
import { readTestScenarioParam } from '../../web/dev-tools/test-scenario-params';

function withWindowSearch(search: string, run: () => void) {
  // @ts-expect-error - minimal stub, only `.location.search` is read by the function under test.
  globalThis.window = { location: { search } };
  try {
    run();
  } finally {
    // @ts-expect-error - restore the node test environment's lack of a `window` global.
    delete globalThis.window;
  }
}

describe('BIO-UX-012-AC01/AC03 readTestScenarioParam', () => {
  afterEach(() => {
    // @ts-expect-error - see bio-ux-005-feature-mode.test.ts for the same pattern.
    delete globalThis.window;
  });

  it('returns the fallback when no window exists (this test environment, and SSR)', () => {
    expect(readTestScenarioParam('scenario', ['normal', 'offline'], 'normal')).toBe('normal');
  });

  it('returns a valid value present in the URL', () => {
    withWindowSearch('?scenario=offline', () => {
      expect(readTestScenarioParam('scenario', ['normal', 'offline'], 'normal')).toBe('offline');
    });
  });

  it('returns the fallback when the URL value is not in the valid set (typo/forged/stale link)', () => {
    withWindowSearch('?scenario=not-a-real-scenario', () => {
      expect(readTestScenarioParam('scenario', ['normal', 'offline'], 'normal')).toBe('normal');
    });
  });

  it('returns the fallback when the param is absent entirely', () => {
    withWindowSearch('?other=1', () => {
      expect(readTestScenarioParam('scenario', ['normal', 'offline'], 'normal')).toBe('normal');
    });
  });

  it('BIO-UX-012-AC04: returns the fallback in production even with a valid window+param present', () => {
    const original = process.env.NODE_ENV;
    // @ts-expect-error - NODE_ENV is typed readonly in this TS config; test needs to set it.
    process.env.NODE_ENV = 'production';
    try {
      withWindowSearch('?scenario=offline', () => {
        expect(readTestScenarioParam('scenario', ['normal', 'offline'], 'normal')).toBe('normal');
      });
    } finally {
      process.env.NODE_ENV = original;
    }
  });
});
