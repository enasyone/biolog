// Developer tests for BIO-ARC-001. Every test name carries the AC it proves.
// Run: node --test contracts/js/   (no dependencies, offline-safe)
// All identifiers, names and times here are SYNTHETIC.

import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import {
  TYPES, newId, parseId, isCanonicalId, uuidV7, toStorageBytes, fromStorageBytes,
  orderingTimestampMs, IdentifierError,
  nowUtc, toInstant, parseInstant, isUtcInstant, isCalendarDate, renderLocal,
  assessClockSkew, isLeapYear, MIN_YEAR, MAX_YEAR, ClockError,
  INITIAL_VERSION, nextVersion, checkPrecondition, assertUpdatable, requestFingerprint,
  resolveIdempotency, classifySyncIntake, isBreakingSchemaChange, ConcurrencyError,
} from './index.mjs';

const schema = (name) =>
  JSON.parse(readFileSync(fileURLToPath(new URL(`../schemas/${name}`, import.meta.url)), 'utf8'));

const REGISTRY = schema('identifier-registry.json');
const ADR = readFileSync(
  fileURLToPath(new URL('../../docs/adr/ADR-0001-canonical-identifiers-clocks-and-versioning.md', import.meta.url)),
  'utf8',
);

// ---------------------------------------------------------------------------
// BIO-ARC-001-AC01 — canonical ID strategy for tenant, person, credential,
// device, document session and events.
// ---------------------------------------------------------------------------

test('AC01: every entity named by the story is registered with a distinct prefix', () => {
  const required = ['tenant', 'person', 'credential', 'device', 'document_session', 'event'];
  assert.deepEqual(Object.keys(TYPES).sort(), [...required].sort());
  const prefixes = REGISTRY.types.map((t) => t.prefix);
  assert.equal(new Set(prefixes).size, prefixes.length, 'prefixes must be unique');
  for (const type of required) assert.match(ADR, new RegExp(`\`${TYPES[type].prefix}\``), `ADR documents ${type}`);
});

test('AC01: the tenant is the only control-plane identifier', () => {
  assert.equal(TYPES.tenant.plane, 'control');
  for (const t of REGISTRY.types.filter((x) => x.type !== 'tenant')) assert.equal(t.plane, 'tenant');
});

test('AC01: minted identifiers are typed, well formed and round-trip through parse', () => {
  for (const type of Object.keys(TYPES)) {
    const id = newId(type);
    assert.ok(isCanonicalId(id), `${id} is canonical`);
    const parsed = parseId(id, type);
    assert.equal(parsed.type, type);
    assert.equal(parsed.prefix, TYPES[type].prefix);
    assert.equal(id.length, 40);
  }
});

test('AC01: minted identifiers are UUIDv7 — version and RFC 9562 variant are pinned', () => {
  for (let i = 0; i < 200; i += 1) {
    const uuid = uuidV7();
    assert.equal(uuid[14], '7', 'version nibble');
    assert.ok('89ab'.includes(uuid[19]), 'variant nibble');
  }
});

test('AC01: identifiers sort in creation order, which is why UUIDv7 was chosen', () => {
  const ids = [1_700_000_000_000, 1_700_000_000_001, 1_700_000_001_000, 1_800_000_000_000]
    .map((ms) => `per_${uuidV7(ms)}`);
  assert.deepEqual([...ids].sort(), ids, 'lexical order matches chronological order');
});

test('AC01: identifiers are mintable offline with no coordination and do not collide', () => {
  const minted = new Set(Array.from({ length: 5000 }, () => newId('event')));
  assert.equal(minted.size, 5000);
});

test('AC01: a wrong-type identifier is rejected at the boundary', () => {
  const credential = newId('credential');
  assert.throws(() => parseId(credential, 'person'), (e) => {
    assert.ok(e instanceof IdentifierError);
    assert.equal(e.code, 'identifier_type_mismatch');
    return true;
  });
});

test('AC01: malformed, foreign and unregistered identifiers are refused', () => {
  const bad = [
    '',
    'per_',
    'per-0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d',            // wrong separator
    'xyz_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d',            // unregistered prefix
    '0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d',                // bare uuid, no type
    'per_0199e0c1-9d40-4e52-b103-7c9a4e2f6b1d',            // UUIDv4, not v7
    'per_0199e0c1-9d40-7e52-c103-7c9a4e2f6b1d',            // wrong variant nibble
    'PER_0199E0C1-9D40-7E52-B103-7C9A4E2F6B1D',            // uppercase
    'per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d ',           // trailing space
    'per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d; DROP TABLE person',
    null, undefined, 42, {},
  ];
  for (const value of bad) {
    assert.equal(isCanonicalId(value), false, `${JSON.stringify(value)} must not be canonical`);
    assert.throws(() => parseId(value), IdentifierError);
  }
});

test('AC01: an unregistered type cannot be minted locally by a feature module', () => {
  assert.throws(() => newId('visitor'), (e) => e.code === 'unregistered_type');
});

test('AC01: storage form is 16 bytes without the prefix and round-trips exactly', () => {
  const id = newId('document_session');
  const bytes = toStorageBytes(id);
  assert.equal(bytes.length, 16);
  assert.equal(fromStorageBytes('document_session', bytes), id);
  assert.throws(() => fromStorageBytes('document_session', Buffer.alloc(15)), IdentifierError);
});

test('AC01: stored bytes carry no type, so the column type must be reasserted on read', () => {
  const person = newId('person');
  const asDevice = fromStorageBytes('device', toStorageBytes(person));
  assert.equal(asDevice.slice(4), person.slice(4), 'same 16 bytes');
  assert.notEqual(asDevice, person, 'different type prefix');
});

test('AC01: the embedded timestamp is ordering metadata, and the ADR says not to trust it', () => {
  assert.equal(orderingTimestampMs(`per_${uuidV7(1_700_000_000_123)}`), 1_700_000_000_123);
  assert.match(ADR, /\*\*It is not a\s+business timestamp\*\*/);
});

test('AC01: registry examples satisfy the published canonical-id schema pattern', () => {
  const pattern = new RegExp(schema('canonical-id.schema.json').pattern);
  for (const t of REGISTRY.types) {
    assert.match(t.example, pattern, `${t.type} example`);
    assert.equal(parseId(t.example).type, t.type);
  }
  assert.equal(REGISTRY.canonical_pattern, schema('canonical-id.schema.json').pattern, 'registry and schema agree');
});

test('AC01: business identifiers are documented as attributes, never keys', () => {
  assert.match(ADR, /business\s*\n?identifiers/i);
  assert.match(ADR, /never its primary key/);
  assert.ok(REGISTRY.business_identifiers.examples.includes('institutional identifier'));
});

// ---------------------------------------------------------------------------
// BIO-ARC-001-AC02 — UTC internally, explicit local rendering.
// ---------------------------------------------------------------------------

test('AC02: the canonical instant is RFC 3339 UTC, literal Z, three fractional digits', () => {
  const now = nowUtc();
  assert.match(now, /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/);
  assert.equal(now.length, 24);
  assert.ok(isUtcInstant(now));
  assert.match(now, new RegExp(schema('utc-instant.schema.json').pattern));
});

test('AC02: instants are fixed width, so string comparison equals chronological order', () => {
  const times = [0, 1, 1_700_000_000_000, 1_900_000_000_000].map(toInstant);
  assert.deepEqual([...times].sort(), times);
});

test('AC02: naive, offset and low-precision timestamps are rejected, not guessed at', () => {
  const bad = [
    '2026-09-20T21:05:00',            // naive, no zone
    '2026-09-20 21:05:00.000Z',       // space separator
    '2026-09-20T21:05:00Z',           // no fractional digits
    '2026-09-20T21:05:00.00Z',        // two digits
    '2026-09-20T22:05:00.000+01:00',  // offset, not Z
    '2026-09-20T21:05:00.000z',       // lowercase z
    '2026-09-20',                     // bare date
    '2026-09-20T25:05:00.000Z',       // impossible hour
    1_700_000_000_000, null, undefined,
  ];
  for (const value of bad) {
    assert.equal(isUtcInstant(value), false, `${JSON.stringify(value)} must not be a valid instant`);
    assert.throws(() => parseInstant(value), ClockError);
  }
});

test('AC02: a valid instant parses back to the same point on the timeline', () => {
  const instant = '2026-09-20T21:05:00.000Z';
  assert.equal(parseInstant(instant).getTime(), Date.UTC(2026, 8, 20, 21, 5, 0, 0));
  assert.equal(toInstant(parseInstant(instant)), instant);
});

test('AC02: local rendering requires an explicit IANA zone and is never implicit', () => {
  assert.throws(() => renderLocal(nowUtc(), undefined), (e) => e.code === 'time_zone_required');
  assert.throws(() => renderLocal(nowUtc(), ''), (e) => e.code === 'time_zone_required');
  assert.throws(() => renderLocal(nowUtc(), 'Mars/Olympus'), (e) => e.code === 'unknown_time_zone');
});

test('AC02: a rendered local time carries its zone and offset, so it stays reversible', () => {
  const lagos = renderLocal('2026-09-20T21:05:00.000Z', 'Africa/Lagos');
  assert.deepEqual(lagos, {
    instant: '2026-09-20T21:05:00.000Z',
    time_zone: 'Africa/Lagos',
    utc_offset: '+01:00',
    local_date: '2026-09-20',
    local_time: '22:05:00',
  });
  const utc = renderLocal('2026-09-20T21:05:00.000Z', 'UTC');
  assert.equal(utc.utc_offset, '+00:00');
  assert.equal(utc.local_time, '21:05:00');
});

test('AC02: one instant renders differently per zone while the stored value never changes', () => {
  const instant = '2026-01-15T23:30:00.000Z';
  const lagos = renderLocal(instant, 'Africa/Lagos');
  const london = renderLocal(instant, 'Europe/London');
  const tokyo = renderLocal(instant, 'Asia/Tokyo');
  assert.equal(lagos.local_date, '2026-01-16');
  assert.equal(london.local_date, '2026-01-15');
  assert.equal(tokyo.local_date, '2026-01-16');
  assert.equal(tokyo.local_time, '08:30:00');
  for (const r of [lagos, london, tokyo]) assert.equal(r.instant, instant);
});

test('AC02: daylight-saving transitions resolve from the zone rules, not a stored offset', () => {
  const winter = renderLocal('2026-01-15T12:00:00.000Z', 'Europe/London');
  const summer = renderLocal('2026-07-15T12:00:00.000Z', 'Europe/London');
  assert.equal(winter.utc_offset, '+00:00');
  assert.equal(summer.utc_offset, '+01:00');
  assert.equal(winter.local_time, '12:00:00');
  assert.equal(summer.local_time, '13:00:00');
});

// --- BIO-ARC-001-QA-01 regression (Seyi review, 2026-09-20) --------------------------
// The shape regex bounds the digits but cannot tell 2026-02-30 from 2026-02-28, and
// Date.parse rolls an impossible day forward silently. That turned a supplied audit or
// device time into a different instant with no error. Guard both directions.

test('AC02/QA-01: impossible calendar instants are rejected, never normalized', () => {
  for (const value of ['2026-02-29T12:00:00.000Z', '2026-02-30T12:00:00.000Z', '2026-04-31T12:00:00.000Z']) {
    assert.equal(isUtcInstant(value), false, `${value} must not be a valid instant`);
    assert.throws(() => parseInstant(value), (e) => {
      assert.ok(e instanceof ClockError);
      assert.equal(e.code, 'invalid_calendar_date');
      return true;
    }, `${value} must be rejected`);
  }
});

test('AC02/QA-01: the rejected values are exactly the ones Date.parse would have moved', () => {
  // Proves the defect was real and that the fix targets it: the platform still rolls
  // these forward, so the contract — not the platform — is what stops it.
  assert.equal(new Date(Date.parse('2026-02-29T12:00:00.000Z')).toISOString(), '2026-03-01T12:00:00.000Z');
  assert.equal(new Date(Date.parse('2026-02-30T12:00:00.000Z')).toISOString(), '2026-03-02T12:00:00.000Z');
  assert.equal(new Date(Date.parse('2026-04-31T12:00:00.000Z')).toISOString(), '2026-05-01T12:00:00.000Z');
  for (const value of ['2026-02-29T12:00:00.000Z', '2026-02-30T12:00:00.000Z', '2026-04-31T12:00:00.000Z']) {
    assert.throws(() => parseInstant(value), ClockError);
  }
});

test('AC02/QA-01: genuine leap days are still accepted, including the 400-year rule', () => {
  for (const value of ['2028-02-29T12:00:00.000Z', '2024-02-29T00:00:00.000Z', '2000-02-29T23:59:59.999Z']) {
    assert.ok(isUtcInstant(value), `${value} is a real instant`);
    assert.equal(toInstant(parseInstant(value)), value, 'round-trips unchanged');
  }
  assert.equal(isUtcInstant('1900-02-29T12:00:00.000Z'), false, '1900 is not a leap year');
  assert.equal(isUtcInstant('2100-02-29T12:00:00.000Z'), false, '2100 is not a leap year');
});

test('AC02/QA-01: impossible calendar dates are rejected and real ones kept', () => {
  for (const value of ['2026-02-29', '2026-02-30', '2026-04-31', '2026-06-31', '2026-09-31', '1900-02-29']) {
    assert.equal(isCalendarDate(value), false, `${value} must not be a calendar date`);
  }
  for (const value of ['2024-02-29', '2000-02-29', '2026-02-28', '2026-04-30', '2026-12-31']) {
    assert.ok(isCalendarDate(value), `${value} is a real date`);
  }
});

test('AC02/QA-01: the leap rule is the proleptic Gregorian one', () => {
  assert.equal(isLeapYear(2024), true);
  assert.equal(isLeapYear(2026), false);
  assert.equal(isLeapYear(1900), false, 'century, not divisible by 400');
  assert.equal(isLeapYear(2000), true, 'divisible by 400');
  assert.equal(isLeapYear(2100), false);
  assert.equal(isLeapYear(2400), true);
});

test('AC02/QA-01: every day of a leap year and a non-leap year is classified correctly', () => {
  for (const year of [2024, 2026, 1900, 2000]) {
    let accepted = 0;
    for (let month = 1; month <= 12; month += 1) {
      for (let day = 1; day <= 31; day += 1) {
        const value = `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
        if (isCalendarDate(value)) accepted += 1;
      }
    }
    assert.equal(accepted, isLeapYear(year) ? 366 : 365, `${year} has the right number of days`);
  }
});

test('AC02/QA-01: clock-skew assessment refuses an impossible claimed time', () => {
  // An edge device reporting a date that does not exist is a defect or an attack, not a
  // value to silently reinterpret before comparing it with received_at.
  assert.throws(() => assessClockSkew('2026-02-30T12:00:00.000Z', nowUtc()), (e) => e.code === 'invalid_calendar_date');
  assert.throws(() => renderLocal('2026-04-31T12:00:00.000Z', 'Africa/Lagos'), (e) => e.code === 'invalid_calendar_date');
});

test('AC04/QA-01: schema patterns and code agree on impossible dates', () => {
  const instantPattern = new RegExp(schema('utc-instant.schema.json').pattern);
  const datePattern = new RegExp(schema('calendar-date.schema.json').pattern);
  const probes = ['2026-02-29', '2026-02-30', '2026-04-31', '2028-02-29', '2000-02-29', '1900-02-29', '2026-12-31'];
  for (const date of probes) {
    assert.equal(datePattern.test(date), isCalendarDate(date), `calendar-date schema agrees on ${date}`);
    const instant = `${date}T12:00:00.000Z`;
    assert.equal(instantPattern.test(instant), isUtcInstant(instant), `utc-instant schema agrees on ${instant}`);
  }
  // Sweep four years, including both century cases, so the two definitions cannot drift.
  for (const year of [1900, 2000, 2024, 2026]) {
    for (let month = 1; month <= 12; month += 1) {
      for (let day = 28; day <= 31; day += 1) {
        const date = `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
        assert.equal(datePattern.test(date), isCalendarDate(date), `schema and code agree on ${date}`);
      }
    }
  }
});

// --- BIO-ARC-001-QA-02 regression (Seyi re-review round 2, 2026-09-20) --------------
// The code applied the leap rule to every four-digit year and accepted 0000-02-29, while
// both schema patterns rejected it: bindings generated from the schemas would have been
// stricter than the reference implementation. Closed by giving both layers the same year
// domain, and by sweeping every four-digit year so no boundary can be omitted again.

test('AC02/QA-02: the supported year range is 1000..9999 and both ends hold', () => {
  assert.equal(MIN_YEAR, 1000);
  assert.equal(MAX_YEAR, 9999);
  assert.ok(isCalendarDate('1000-01-01'), 'lower bound accepted');
  assert.ok(isCalendarDate('9999-12-31'), 'upper bound accepted');
  assert.ok(isUtcInstant('1000-01-01T00:00:00.000Z'));
  assert.ok(isUtcInstant('9999-12-31T23:59:59.999Z'));
  assert.equal(isCalendarDate('0999-12-31'), false, 'one day below the lower bound');
});

test('AC02/QA-02: year zero is rejected everywhere, including its leap day', () => {
  for (const date of ['0000-02-29', '0000-01-01', '0000-12-31', '0000-02-28', '0000-03-01']) {
    assert.equal(isCalendarDate(date), false, `${date} must be rejected`);
    const instant = `${date}T12:00:00.000Z`;
    assert.equal(isUtcInstant(instant), false, `${instant} must be rejected`);
    assert.throws(() => parseInstant(instant), (e) => {
      assert.ok(e instanceof ClockError);
      assert.equal(e.code, 'instant_out_of_range');
      return true;
    });
  }
});

test('AC02/QA-02: years below the range are rejected whether or not they are leap years', () => {
  for (const date of ['0004-02-29', '0400-02-29', '0001-01-01', '0100-02-28', '0999-02-28']) {
    assert.equal(isCalendarDate(date), false, `${date} is below the supported range`);
    assert.equal(isUtcInstant(`${date}T12:00:00.000Z`), false);
  }
});

test('AC02/QA-02: formatting cannot produce a value that parsing would refuse', () => {
  // toInstant and parseInstant must agree, or a record could be written that cannot be
  // read back. Both ends of the range round-trip; outside it, formatting fails loudly.
  for (const instant of ['1000-01-01T00:00:00.000Z', '9999-12-31T23:59:59.999Z', '2026-09-20T21:05:00.000Z']) {
    assert.equal(toInstant(parseInstant(instant)), instant);
  }
  assert.throws(() => toInstant(new Date(Date.UTC(999, 11, 31))), (e) => e.code === 'instant_out_of_range');
  assert.throws(() => toInstant(new Date('0000-01-01T00:00:00.000Z')), (e) => e.code === 'instant_out_of_range');
  assert.throws(() => toInstant(new Date(8.64e15)), (e) => e.code === 'instant_out_of_range', 'year 275760');
});

test('AC02/QA-02: an out-of-range year is reported differently from an impossible day', () => {
  // Two distinct defects deserve two distinct machine codes; a caller fixing a bad device
  // clock should not be told its calendar is wrong.
  assert.throws(() => parseInstant('0000-06-15T12:00:00.000Z'), (e) => e.code === 'instant_out_of_range');
  assert.throws(() => parseInstant('2026-06-31T12:00:00.000Z'), (e) => e.code === 'invalid_calendar_date');
});

test('AC04/QA-02: code and schema agree on every four-digit year, exhaustively', () => {
  // 7,440,000 comparisons, ~2s. Deliberately exhaustive rather than sampled: the two
  // findings against this contract were both boundaries a sampled sweep had skipped.
  const datePattern = new RegExp(schema('calendar-date.schema.json').pattern);
  const instantPattern = new RegExp(schema('utc-instant.schema.json').pattern);
  const divergences = [];
  for (let year = 0; year <= 9999; year += 1) {
    const ys = String(year).padStart(4, '0');
    for (let month = 1; month <= 12; month += 1) {
      const ms = String(month).padStart(2, '0');
      for (let day = 1; day <= 31; day += 1) {
        const date = `${ys}-${ms}-${String(day).padStart(2, '0')}`;
        if (datePattern.test(date) !== isCalendarDate(date)) divergences.push(date);
        const instant = `${date}T12:00:00.000Z`;
        if (instantPattern.test(instant) !== isUtcInstant(instant)) divergences.push(instant);
      }
    }
  }
  assert.deepEqual(divergences, [], 'schema pattern and reference code must classify every date identically');
});

test('AC04/QA-02: the schema patterns avoid lookahead so generated bindings can use them', () => {
  // RE2-backed validators (Go) reject lookahead. The year bound is encoded as [1-9]\d{3}
  // precisely so the published pattern survives translation to every target stack.
  for (const name of ['calendar-date.schema.json', 'utc-instant.schema.json']) {
    const { pattern } = schema(name);
    assert.equal(/\(\?[=!<]/.test(pattern), false, `${name} must not use lookahead or lookbehind`);
    assert.ok(pattern.includes('[1-9]\\d{3}'), `${name} encodes the year bound directly`);
  }
});

test('AC02: calendar dates are a separate type from instants', () => {
  assert.ok(isCalendarDate('2004-11-02'));
  assert.equal(isCalendarDate('2026-09-20T21:05:00.000Z'), false);
  assert.equal(isUtcInstant('2004-11-02'), false);
  assert.match('2004-11-02', new RegExp(schema('calendar-date.schema.json').pattern));
});

test('AC02: a device clock is a claim; received_at stays authoritative', () => {
  const ok = assessClockSkew('2026-09-20T21:05:00.000Z', '2026-09-20T21:05:02.000Z');
  assert.equal(ok.within_tolerance, true);
  assert.equal(ok.authoritative, 'received_at');

  const wrong = assessClockSkew('2026-09-20T09:05:00.000Z', '2026-09-20T21:05:00.000Z');
  assert.equal(wrong.within_tolerance, false);
  assert.equal(wrong.skew_ms, -12 * 60 * 60 * 1000);
  assert.equal(wrong.authoritative, 'received_at');
});

test('AC02: the event envelope requires both the claimed and the received instant', () => {
  const envelope = schema('event-envelope.schema.json');
  assert.ok(envelope.required.includes('occurred_at'));
  assert.ok(envelope.required.includes('received_at'));
  assert.match(envelope.properties.received_at.description, /[Aa]uthoritative/);
});

// ---------------------------------------------------------------------------
// BIO-ARC-001-AC03 — optimistic concurrency and version rules.
// ---------------------------------------------------------------------------

test('AC03: versions start at 1 and advance by exactly one', () => {
  assert.equal(INITIAL_VERSION, 1);
  assert.equal(nextVersion(1), 2);
  assert.equal(nextVersion(41), 42);
  for (const bad of [0, -1, 1.5, '3', null, undefined]) assert.throws(() => nextVersion(bad), ConcurrencyError);
});

test('AC03: a matching precondition commits and yields the next version', () => {
  assert.deepEqual(checkPrecondition({ storedVersion: 7, expectedVersion: 7 }), { ok: true, next_version: 8 });
});

test('AC03: a stale writer is rejected with 409 and told the current version', () => {
  assert.throws(() => checkPrecondition({ storedVersion: 9, expectedVersion: 7 }), (e) => {
    assert.equal(e.code, 'concurrency_conflict');
    assert.equal(e.status, 409);
    assert.equal(e.detail.current_version, 9);
    return true;
  });
});

test('AC03: a missing precondition fails closed with 428 rather than last-write-wins', () => {
  for (const expectedVersion of [undefined, null]) {
    assert.throws(() => checkPrecondition({ storedVersion: 4, expectedVersion }), (e) => {
      assert.equal(e.code, 'precondition_required');
      assert.equal(e.status, 428);
      return true;
    });
  }
  assert.throws(() => checkPrecondition({ storedVersion: 4, expectedVersion: '4' }), (e) => e.status === 400);
});

test('AC03: two concurrent writers from the same read — exactly one wins', () => {
  let stored = 3;
  const commit = (expected) => { const r = checkPrecondition({ storedVersion: stored, expectedVersion: expected }); stored = r.next_version; return r; };
  commit(3);
  assert.equal(stored, 4);
  assert.throws(() => commit(3), (e) => e.code === 'concurrency_conflict');
  assert.equal(stored, 4, 'the loser changed nothing');
});

test('AC03: append-only records reject updates instead of carrying a version', () => {
  for (const type of ['photo_master', 'audit_event', 'event', 'card_template_version']) {
    assert.throws(() => assertUpdatable(type), (e) => {
      assert.equal(e.code, 'immutable_record');
      assert.equal(e.status, 409);
      return true;
    });
  }
  assert.equal(assertUpdatable('person'), true);
  const envelope = schema('event-envelope.schema.json');
  assert.deepEqual(envelope.not.required, ['version'], 'events must not carry an aggregate version');
});

test('AC03: the mutable-aggregate envelope requires id, tenant, version and both instants', () => {
  const v = schema('versioned-resource.schema.json');
  assert.deepEqual(v.required.sort(), ['created_at', 'id', 'tenant_id', 'updated_at', 'version'].sort());
  assert.equal(v.properties.version.minimum, 1);
});

test('AC03: an idempotent retry replays the stored result without executing again', () => {
  const body = { credential_id: 'crd_0199e0c2-0a13-7f64-8b25-3d8e5f7a1c92', action: 'activate' };
  const fingerprint = requestFingerprint(body);
  assert.deepEqual(resolveIdempotency({ storedRecord: null, fingerprint }), { outcome: 'EXECUTE' });

  const stored = { key: 'k-1', fingerprint, result: { status: 'ACTIVATED', card_sequence: '001' } };
  const replay = resolveIdempotency({ storedRecord: stored, fingerprint });
  assert.equal(replay.outcome, 'REPLAY');
  assert.deepEqual(replay.result, stored.result, 'no second card is minted');
});

test('AC03: request fingerprints ignore key order but not values', () => {
  assert.equal(requestFingerprint({ a: 1, b: 2 }), requestFingerprint({ b: 2, a: 1 }));
  assert.notEqual(requestFingerprint({ a: 1, b: 2 }), requestFingerprint({ a: 1, b: 3 }));
  assert.notEqual(requestFingerprint({ a: '1' }), requestFingerprint({ a: 1 }));
});

test('AC03: reusing an idempotency key with a different body is surfaced, not absorbed', () => {
  const stored = { key: 'k-1', fingerprint: requestFingerprint({ action: 'activate' }), result: { status: 'ACTIVATED' } };
  assert.throws(() => resolveIdempotency({ storedRecord: stored, fingerprint: requestFingerprint({ action: 'revoke' }) }), (e) => {
    assert.equal(e.code, 'idempotency_key_reuse');
    assert.equal(e.status, 409);
    return true;
  });
});

test('AC03: offline sync applies only on an exact base-version match', () => {
  assert.deepEqual(classifySyncIntake({ aggregateType: 'person', baseVersion: 5, storedVersion: 5 }),
    { outcome: 'APPLY', next_version: 6 });
});

test('AC03: security-sensitive sync conflicts go to the human queue, never last-write-wins', () => {
  for (const aggregateType of ['person', 'credential', 'biometric']) {
    const r = classifySyncIntake({ aggregateType, baseVersion: 2, storedVersion: 5 });
    assert.equal(r.outcome, 'CONFLICT');
    assert.equal(r.sync_state, 'CONFLICT');
    assert.equal(r.queue, 'sync-resolution');
  }
  // A merge rule must not be able to override a security-sensitive conflict.
  const forced = classifySyncIntake({ aggregateType: 'credential', baseVersion: 2, storedVersion: 5, mergeRule: () => 'merged' });
  assert.equal(forced.outcome, 'CONFLICT');
});

test('AC03: without a declared merge rule, any version mismatch defaults to CONFLICT', () => {
  assert.equal(classifySyncIntake({ aggregateType: 'attendance_event', baseVersion: 2, storedVersion: 3 }).outcome, 'CONFLICT');
  assert.equal(classifySyncIntake({ aggregateType: 'attendance_event', baseVersion: 2, storedVersion: 3, mergeRule: () => 'merged' }).outcome, 'MERGE');
});

test('AC03: sync states match the offline architecture', () => {
  assert.deepEqual(schema('offline-mutation.schema.json').properties.sync_state.enum,
    ['PENDING', 'UPLOADING', 'ACKNOWLEDGED', 'SYNCED', 'CONFLICT', 'FAILED']);
});

test('AC03: event schema versioning is separate from aggregate versioning', () => {
  assert.equal(isBreakingSchemaChange({ addedOptionalFields: ['assurance_level'] }), false);
  assert.equal(isBreakingSchemaChange({ removedFields: ['device_serial'] }), true);
  assert.equal(isBreakingSchemaChange({ retypedFields: ['occurred_at'] }), true);
});

// ---------------------------------------------------------------------------
// BIO-ARC-001-AC04 — contracts represented in shared code/schema and tests.
// ---------------------------------------------------------------------------

test('AC04: every published schema is valid JSON Schema 2020-12 with an id and title', () => {
  const files = ['canonical-id', 'utc-instant', 'calendar-date', 'versioned-resource', 'event-envelope', 'offline-mutation'];
  for (const f of files) {
    const s = schema(`${f}.schema.json`);
    assert.equal(s.$schema, 'https://json-schema.org/draft/2020-12/schema', f);
    assert.ok(s.$id && s.title && s.description, `${f} is self-describing`);
  }
});

test('AC04: code and schema share one definition of a canonical identifier', () => {
  const pattern = new RegExp(schema('canonical-id.schema.json').pattern);
  for (let i = 0; i < 100; i += 1) {
    const id = newId('credential');
    assert.equal(pattern.test(id), isCanonicalId(id), 'schema and code agree');
  }
  for (const bad of ['per_0199e0c1-9d40-4e52-b103-7c9a4e2f6b1d', 'xyz_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d']) {
    assert.equal(pattern.test(bad), false);
    assert.equal(isCanonicalId(bad), false);
  }
});

test('AC04: code and schema share one definition of a UTC instant', () => {
  const pattern = new RegExp(schema('utc-instant.schema.json').pattern);
  for (const value of [nowUtc(), '2026-09-20T21:05:00.000Z', '1970-01-01T00:00:00.000Z']) {
    assert.equal(pattern.test(value), isUtcInstant(value));
  }
  for (const value of ['2026-09-20T21:05:00Z', '2026-09-20T22:05:00.000+01:00']) {
    assert.equal(pattern.test(value), false);
    assert.equal(isUtcInstant(value), false);
  }
});

test('AC04: the ADR is the recorded decision and names its owner and freeze policy', () => {
  assert.match(ADR, /# ADR-0001/);
  assert.match(ADR, /\*\*Status:\*\* Accepted \(BIO-ARC-001/);
  assert.match(ADR, /Kemi/);
  assert.match(ADR, /freeze at W01 exit/);
  for (const ac of ['BIO-ARC-001-AC01', 'BIO-ARC-001-AC02', 'BIO-ARC-001-AC03']) {
    assert.ok(ADR.includes(ac), `${ac} is traceable to a decision section`);
  }
});

test('AC04: a full envelope built from the contracts validates against its schema shape', () => {
  const tenantId = newId('tenant');
  const person = {
    id: newId('person'), tenant_id: tenantId, version: INITIAL_VERSION,
    created_at: nowUtc(), updated_at: nowUtc(),
  };
  const v = schema('versioned-resource.schema.json');
  for (const field of v.required) assert.ok(person[field] !== undefined, `${field} present`);
  assert.match(person.tenant_id, new RegExp(v.properties.tenant_id.pattern));
  assert.match(person.created_at, new RegExp(schema('utc-instant.schema.json').pattern));

  const event = {
    event_id: newId('event'), schema_version: 1, tenant_id: tenantId,
    aggregate_type: 'person', aggregate_id: person.id,
    // aggregate_version added to this envelope by ADR-0003 Decision 2: the version of the
    // aggregate the event describes, which is a fact about the state, not a version of
    // the event. The prohibition on a bare `version` field below still stands.
    aggregate_version: person.version,
    correlation_id: newId('event'),
    // causation_id is required by ADR-0003 Decision 6.1 / AC02 and explicitly null for a
    // root event, so an unstated cause cannot be mistaken for no cause.
    causation_id: null,
    occurred_at: nowUtc(), received_at: nowUtc(), producer: 'enrolment-service',
    payload_classification: 'personal', payload: { state: 'PRE_ENROLLED' },
  };
  const e = schema('event-envelope.schema.json');
  for (const field of e.required) assert.ok(event[field] !== undefined, `${field} present`);
  assert.ok(e.properties.aggregate_type.enum.includes(event.aggregate_type));
  assert.equal(event.version, undefined, 'an event still carries no version of its own');
  assert.equal(event.aggregate_version, 1, 'but it records the aggregate version it describes');
});
