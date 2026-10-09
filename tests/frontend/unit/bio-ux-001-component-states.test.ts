// Developer tests for BIO-UX-001-AC05 (core component states).
// SYNTHETIC: no real tenant/user data.
import { describe, expect, it } from 'vitest';
import {
  COMPONENT_STATES,
  componentStateSpecs,
  type ComponentState,
} from '../../../web/design-system/component-states';
import { statusColorTokens, themeTokens } from '../../../web/design-system/tokens';

const REQUIRED_STATES: readonly ComponentState[] = [
  'default',
  'hover',
  'focus',
  'disabled',
  'loading',
  'success',
  'warning',
  'error',
  'offline',
];

describe('BIO-UX-001-AC05 core component states are specified', () => {
  it('names exactly the nine required states, in order', () => {
    expect(COMPONENT_STATES).toEqual(REQUIRED_STATES);
  });

  it('every state has a description and at least one backing token', () => {
    for (const state of COMPONENT_STATES) {
      const spec = componentStateSpecs[state];
      expect(spec.description.length).toBeGreaterThan(0);
      expect(spec.tokens.length).toBeGreaterThan(0);
    }
  });

  it('every referenced token name exists in themeTokens or statusColorTokens', () => {
    const validTokenNames = new Set([
      ...Object.keys(themeTokens.light),
      ...Object.keys(statusColorTokens.light),
    ]);
    for (const state of COMPONENT_STATES) {
      for (const tokenName of componentStateSpecs[state].tokens) {
        expect(validTokenNames.has(tokenName)).toBe(true);
      }
    }
  });

  it('disabled and loading reduce opacity; interactive/status states do not', () => {
    expect(componentStateSpecs.disabled.opacity).toBeDefined();
    expect(componentStateSpecs.loading.opacity).toBeDefined();
    expect(componentStateSpecs.default.opacity).toBeUndefined();
    expect(componentStateSpecs.success.opacity).toBeUndefined();
  });

  it('offline is distinct from error and disabled (different backing tokens)', () => {
    expect(componentStateSpecs.offline.tokens).not.toEqual(componentStateSpecs.error.tokens);
    expect(componentStateSpecs.offline.tokens).not.toEqual(componentStateSpecs.disabled.tokens);
  });
});
