/**
 * Fixed synthetic name/institution pools (BIO-UX-004-AC05).
 *
 * Every name below is invented for testing, drawn from common Nigerian
 * given/family-name patterns (Yoruba, Igbo, Hausa) without referring to any
 * real person, and every institution name is a fictional composite that does
 * not name a real BIOLOG customer or school. `SYNTHETIC_MARKER` is appended
 * to every display-facing label generated from these pools so a human tester
 * can never mistake one for real data — the same convention already shipped
 * in `search-fixtures.ts` and `session-context.tsx`.
 */

export const SYNTHETIC_MARKER = '(SYNTHETIC)';

/** `withMarker('Amara Adeleke')` -> `'Amara Adeleke (SYNTHETIC)'`. */
export function withMarker(label: string): string {
  return `${label} ${SYNTHETIC_MARKER}`;
}

export const FIRST_NAMES: readonly string[] = Object.freeze([
  'Amara', 'Kelechi', 'Ovie', 'Chiamaka', 'Emeka', 'Ngozi', 'Tunde', 'Busola',
  'Ifeanyi', 'Zainab', 'Damilola', 'Seyi', 'Femi', 'Chioma', 'Yusuf', 'Halima',
  'Chukwuemeka', 'Adaeze', 'Babatunde', 'Folake', 'Nnamdi', 'Aisha', 'Oluwaseun', 'Ebele',
]);

export const LAST_NAMES: readonly string[] = Object.freeze([
  'Adeleke', 'Okafor', 'Eferakeya', 'Balogun', 'Nwosu', 'Ibrahim', 'Okonkwo', 'Adeyemi',
  'Chukwu', 'Abubakar', 'Eze', 'Bello', 'Uzoma', 'Suleiman', 'Okoro', 'Fashola',
  'Nnadi', 'Garba', 'Obi', 'Lawal', 'Onyekwere', 'Musa', 'Anyanwu', 'Sani',
]);

/**
 * The two SYNTHETIC tenants already established by `session-context.tsx`
 * (BIO-FND-007/BIO-UX-002). Deliberately kept as a literal duplicate here
 * rather than importing that shell-owned module — this library stays
 * import-free of shared-shell context. `session-context.tsx`'s own tenant
 * list (`TENANTS`) is not exported, so `bio-ux-004-tenant-parity.test.ts`
 * can only cross-check the parts it does export (`DEFAULT_TENANT_ID`) plus
 * the three demo person names, which is documented there as a known,
 * partial guarantee — not full referential equality. Exporting `TENANTS`
 * so the two could be fully reconciled into one source is a shell change
 * requiring Damilola's approval, out of this story's scope.
 */
export const SYNTHETIC_TENANTS: readonly { id: string; name: string }[] = Object.freeze([
  Object.freeze({ id: 'alpha', name: withMarker('Alpha State University') }),
  Object.freeze({ id: 'beta', name: withMarker('Beta Polytechnic') }),
]);

/** Fictional route/bus names used by the transport generator. */
export const TRANSPORT_ROUTE_NAMES: readonly string[] = Object.freeze([
  'Route A — Riverside Loop',
  'Route B — Estate Express',
  'Route C — Hilltop Shuttle',
]);

/** Fictional device names by type, used by the device generator. */
export const DEVICE_NAME_TEMPLATES: readonly string[] = Object.freeze([
  'Main Gate Reader',
  'Library Entrance Scanner',
  'Hostel Block Turnstile',
  'Admin Block Camera',
  'Staff Room Reader',
]);

/** Fictional document types used by the document generator (BIO-UX-004-AC01). */
export const DOCUMENT_TYPES: readonly string[] = Object.freeze([
  'birth-certificate',
  'national-id',
  'admission-letter',
]);
