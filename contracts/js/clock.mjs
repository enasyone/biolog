// BIOLOG clocks — ADR-0001 Decision 2.
// Every stored, transported, compared, logged or audited timestamp is a UTC instant.
// Local time exists only at the moment of rendering, and only with an explicit zone.

/**
 * Supported year range, inclusive: the intersection of what every BIOLOG surface can
 * actually store and round-trip. MySQL's documented DATE/DATETIME range is
 * 1000-01-01..9999-12-31 and .NET's DateTime covers 0001..9999, so 1000..9999 is what
 * survives a write to the tenant database. A value outside it would be accepted at the
 * boundary and then fail at the database — the opposite of the fail-closed rule this
 * contract exists to enforce. Assumption on record: no BIOLOG business timestamp
 * legitimately precedes year 1000 (see ADR-0001 §2.1).
 */
export const MIN_YEAR = 1000;
export const MAX_YEAR = 9999;

/**
 * RFC 3339, literal Z, exactly three fractional digits. Fixed width, so string order ==
 * time order.
 *
 * These match any four-digit year on purpose. The year bound is applied afterwards by
 * `isRealCalendarDate`, so an out-of-range year can be reported as exactly that instead
 * of as a malformed string — a caller sending 0000-06-15 has a range problem, not a
 * format problem. The published schemas cannot make that distinction (a pattern only
 * says yes or no), so they encode the bound directly as `[1-9]\d{3}`. The two layers are
 * held to the same verdict by an exhaustive code-vs-schema sweep over every four-digit
 * year in the test suite.
 */
const INSTANT = /^(\d{4})-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])T([01]\d|2[0-3]):[0-5]\d:[0-5]\d\.\d{3}Z$/;
const CALENDAR_DATE = /^(\d{4})-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])$/;

/**
 * Proleptic Gregorian leap year: divisible by 4, except centuries, except every 400th.
 * 2000 is a leap year; 1900 and 2100 are not.
 */
function isLeapYear(year) {
  return (year % 4 === 0 && year % 100 !== 0) || year % 400 === 0;
}

const DAYS_IN_MONTH = [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

/**
 * True when the day actually exists in that month of that year.
 * The shape regexes only bound the digits; they cannot tell 2026-02-30 from 2026-02-28.
 * Without this check `Date.parse` rolls the value forward silently, turning a supplied
 * audit or device time into a different instant with no error — the one thing a canonical
 * time contract must never do.
 */
function isRealCalendarDate(year, month, day) {
  if (year < MIN_YEAR || year > MAX_YEAR) return false;
  const max = month === 2 && isLeapYear(year) ? 29 : DAYS_IN_MONTH[month - 1];
  return day >= 1 && day <= max;
}

/** Pull Y/M/D out of a matched instant or calendar date. */
function datePartsOf(match) {
  return { year: Number(match[1]), month: Number(match[2]), day: Number(match[3]) };
}

export class ClockError extends Error {
  constructor(message, code) {
    super(message);
    this.name = 'ClockError';
    this.code = code;
  }
}

export { isLeapYear };

export function isUtcInstant(value) {
  if (typeof value !== 'string') return false;
  const match = INSTANT.exec(value);
  if (match === null) return false;
  const { year, month, day } = datePartsOf(match);
  return isRealCalendarDate(year, month, day) && !Number.isNaN(Date.parse(value));
}

export function isCalendarDate(value) {
  if (typeof value !== 'string') return false;
  const match = CALENDAR_DATE.exec(value);
  if (match === null) return false;
  const { year, month, day } = datePartsOf(match);
  return isRealCalendarDate(year, month, day);
}

/** Format a Date (or epoch ms) as the canonical wire instant. */
export function toInstant(value = new Date()) {
  const d = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(d.getTime())) throw new ClockError('not a valid time value', 'invalid_instant');
  const formatted = d.toISOString().replace(/\.(\d{3})\d*Z$/, '.$1Z');
  // Formatting and parsing must agree, or a value could be written that cannot be read
  // back. Outside the supported years toISOString also switches to the expanded
  // ±YYYYYY form, which is not the canonical wire shape.
  if (!isUtcInstant(formatted)) {
    throw new ClockError(
      `${formatted} is outside the supported year range ${MIN_YEAR}..${MAX_YEAR}`,
      'instant_out_of_range',
    );
  }
  return formatted;
}

/** The only sanctioned "now". */
export function nowUtc() {
  return toInstant(new Date());
}

/**
 * Parse a wire instant. Rejects anything ambiguous rather than guessing:
 * a naive value, a bare date, an offset other than Z, or a day that does not exist in
 * that month. A "+01:00" value is refused because an offset is not a time zone — it has
 * lost the rule that produced it. 2026-02-30 is refused because it is not a date at all,
 * and silently becoming 2026-03-02 would be a data-integrity failure, not a convenience.
 */
export function parseInstant(value) {
  if (typeof value !== 'string') throw new ClockError('instant must be a string', 'invalid_instant');
  const match = INSTANT.exec(value);
  if (match === null) {
    throw new ClockError(
      `instant must be RFC 3339 UTC with a literal Z and 3 fractional digits, got ${JSON.stringify(value)}`,
      'invalid_instant',
    );
  }
  const { year, month, day } = datePartsOf(match);
  if (year < MIN_YEAR || year > MAX_YEAR) {
    throw new ClockError(
      `${value} is outside the supported year range ${MIN_YEAR}..${MAX_YEAR}`,
      'instant_out_of_range',
    );
  }
  if (!isRealCalendarDate(year, month, day)) {
    throw new ClockError(
      `${value} is not a real date: ${String(month).padStart(2, '0')} of ${year} has no day ${day}`,
      'invalid_calendar_date',
    );
  }
  const ms = Date.parse(value);
  if (Number.isNaN(ms)) throw new ClockError(`unparseable instant: ${value}`, 'invalid_instant');
  return new Date(ms);
}

/**
 * Render a UTC instant for a human. The IANA zone is required and must come from tenant,
 * site or user preference — never from the host, or a report changes meaning depending on
 * which machine ran it. The result is self-describing: it carries the zone and the
 * resolved offset, so it can be traced back to the instant it came from.
 */
export function renderLocal(instant, timeZone, { locale = 'en-GB' } = {}) {
  const at = parseInstant(instant);
  if (typeof timeZone !== 'string' || timeZone.length === 0) {
    throw new ClockError('an explicit IANA time zone is required to render local time', 'time_zone_required');
  }
  let parts;
  try {
    parts = new Intl.DateTimeFormat(locale, {
      timeZone,
      year: 'numeric', month: '2-digit', day: '2-digit',
      hour: '2-digit', minute: '2-digit', second: '2-digit',
      hour12: false, timeZoneName: 'longOffset',
    }).formatToParts(at);
  } catch {
    throw new ClockError(`unknown IANA time zone: ${JSON.stringify(timeZone)}`, 'unknown_time_zone');
  }
  const get = (t) => parts.find((p) => p.type === t)?.value ?? '';
  const offset = get('timeZoneName').replace(/^(GMT|UTC)$/, 'GMT+00:00').replace(/^(GMT|UTC)/, '');
  return {
    instant,
    time_zone: timeZone,
    utc_offset: offset === '' ? '+00:00' : offset,
    local_date: `${get('year')}-${get('month')}-${get('day')}`,
    local_time: `${get('hour')}:${get('minute')}:${get('second')}`,
  };
}

/**
 * Compare a device's claimed occurred_at against the server's received_at.
 * ADR-0001 Decision 2.4: skew is evidence. It is recorded and flagged, never silently
 * corrected, and `received_at` stays authoritative for ordering either way.
 */
export function assessClockSkew(occurredAt, receivedAt, { toleranceMs = 5 * 60 * 1000 } = {}) {
  const skewMs = parseInstant(occurredAt).getTime() - parseInstant(receivedAt).getTime();
  return { skew_ms: skewMs, within_tolerance: Math.abs(skewMs) <= toleranceMs, authoritative: 'received_at' };
}
