'use client';

import Link from 'next/link';
import { useId, useState } from 'react';
import { searchSynthetic, type SearchResult } from '../lib/search-fixtures';

/**
 * Global search (BIO-UX-002-AC01). A real, working primary action: submitting
 * a query renders deterministic synthetic results (or an explicit
 * "no matches" state) rather than doing nothing — no live search service
 * exists yet, so this is the approved SIMULATION behaviour, clearly labelled.
 */
export function SearchBar() {
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<readonly SearchResult[] | null>(null);
  const listId = useId();

  function runSearch(value: string) {
    setQuery(value);
    setResults(value.trim() ? searchSynthetic(value) : null);
  }

  return (
    <div className="biolog-search">
      <label htmlFor="biolog-search-input" className="biolog-visually-hidden">
        Search people, cards, devices or actions
      </label>
      <input
        id="biolog-search-input"
        type="search"
        className="biolog-search-input"
        placeholder="Search people, cards, devices or actions…"
        value={query}
        onChange={(event) => runSearch(event.target.value)}
        autoComplete="off"
      />
      {results !== null && (
        <div
          id={listId}
          className="biolog-search-results"
          role="region"
          aria-label="Search results"
          aria-live="polite"
        >
          {results.length === 0 ? (
            <p className="biolog-search-empty">
              No matches for &ldquo;{query}&rdquo; (SIMULATION — search is not yet connected to a live service).
            </p>
          ) : (
            <ul>
              {results.map((result) => (
                <li key={result.id}>
                  <Link href={result.href}>
                    <span className="biolog-search-result-category">{result.category}</span>
                    {result.label}
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}
