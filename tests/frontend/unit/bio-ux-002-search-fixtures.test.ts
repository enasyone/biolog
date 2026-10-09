// Developer tests for BIO-UX-002-AC01 (deterministic synthetic global search).
// SYNTHETIC: no real tenant/user data.
import { describe, expect, it } from 'vitest';
import { searchSynthetic } from '../../../web/lib/search-fixtures';

describe('BIO-UX-002-AC01 searchSynthetic is deterministic and case-insensitive', () => {
  it('returns no results for an empty or whitespace-only query', () => {
    expect(searchSynthetic('')).toEqual([]);
    expect(searchSynthetic('   ')).toEqual([]);
  });

  it('matches regardless of case', () => {
    const lower = searchSynthetic('amara');
    const upper = searchSynthetic('AMARA');
    expect(lower.length).toBeGreaterThan(0);
    expect(upper).toEqual(lower);
  });

  it('returns an empty array, not undefined, for a query with no matches', () => {
    expect(searchSynthetic('no-such-synthetic-record-zzz')).toEqual([]);
  });

  it('every result carries a category and an in-app href', () => {
    const results = searchSynthetic('card');
    expect(results.length).toBeGreaterThan(0);
    for (const result of results) {
      expect(['Person', 'Card', 'Device', 'Action']).toContain(result.category);
      expect(result.href.startsWith('/')).toBe(true);
    }
  });

  it('is a pure function: same query always returns an equal result set', () => {
    expect(searchSynthetic('device')).toEqual(searchSynthetic('device'));
  });
});
