/**
 * Deterministic synthetic global-search fixture (BIO-UX-002-AC01).
 *
 * The shell's search box is a real primary action (UI-first rule 3: "no dead
 * primary buttons"), not yet backed by a live search API. Matching this tiny,
 * fixed fixture list is the deterministic simulation until a search story
 * connects a live service port behind the same `searchSynthetic` function —
 * callers never see the difference, per the SIMULATION/LIVE port contract.
 */
export interface SearchResult {
  id: string;
  label: string;
  category: 'Person' | 'Card' | 'Device' | 'Action';
  href: string;
}

const FIXTURES: readonly SearchResult[] = Object.freeze([
  Object.freeze({ id: 'p-amara', label: 'Amara Adeleke (SYNTHETIC)', category: 'Person', href: '/people' }),
  Object.freeze({ id: 'p-kelechi', label: 'Kelechi Okafor (SYNTHETIC)', category: 'Person', href: '/staff' }),
  Object.freeze({ id: 'p-ovie', label: 'Ovie Eferakeya (SYNTHETIC)', category: 'Person', href: '/people' }),
  Object.freeze({ id: 'c-0001', label: 'Card BIO-0001 (SYNTHETIC)', category: 'Card', href: '/cards' }),
  Object.freeze({ id: 'd-gate1', label: 'Device: Main Gate Reader (SYNTHETIC)', category: 'Device', href: '/devices' }),
  Object.freeze({ id: 'a-issue', label: 'Issue Card', category: 'Action', href: '/cards/issue' }),
  Object.freeze({ id: 'a-enrol', label: 'New Enrolment', category: 'Action', href: '/enrolment' }),
]);

/** Case-insensitive substring match against `label`, deterministic order. Empty query returns none. */
export function searchSynthetic(query: string): readonly SearchResult[] {
  const trimmed = query.trim().toLowerCase();
  if (!trimmed) return [];
  return FIXTURES.filter((result) => result.label.toLowerCase().includes(trimmed));
}
