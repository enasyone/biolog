// Local mirror of the frozen shared contract `contracts/js/clock.mjs`
// (ADR-0001 Decision 2, `contracts/schemas/utc-instant.schema.json`). Every
// stored/queued timestamp on this device is a UTC instant: RFC 3339, literal
// Z, exactly three fractional digits. Read-only reference: do not diverge
// from the shared algorithm.
library;

const int minYear = 1000;
const int maxYear = 9999;

final RegExp _instantPattern = RegExp(
  r'^(\d{4})-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])T([01]\d|2[0-3]):[0-5]\d:[0-5]\d\.\d{3}Z$',
);

bool _isLeapYear(int year) => (year % 4 == 0 && year % 100 != 0) || year % 400 == 0;

const List<int> _daysInMonth = [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

bool _isRealCalendarDate(int year, int month, int day) {
  if (year < minYear || year > maxYear) return false;
  final max = (month == 2 && _isLeapYear(year)) ? 29 : _daysInMonth[month - 1];
  return day >= 1 && day <= max;
}

/// True when [value] is a well-formed, calendar-valid BIOLOG UTC instant.
bool isUtcInstant(String value) {
  final match = _instantPattern.firstMatch(value);
  if (match == null) return false;
  final year = int.parse(match.group(1)!);
  final month = int.parse(match.group(2)!);
  final day = int.parse(match.group(3)!);
  return _isRealCalendarDate(year, month, day);
}

/// Format [value] (defaults to now) as the canonical wire instant.
String toInstant([DateTime? value]) {
  final utc = (value ?? DateTime.now()).toUtc();
  final formatted = _format(utc);
  if (!isUtcInstant(formatted)) {
    throw ArgumentError(
      '$formatted is outside the supported year range $minYear..$maxYear',
    );
  }
  return formatted;
}

String _format(DateTime utc) {
  String pad(int v, int width) => v.toString().padLeft(width, '0');
  final ms = utc.millisecond.toString().padLeft(3, '0');
  return '${pad(utc.year, 4)}-${pad(utc.month, 2)}-${pad(utc.day, 2)}'
      'T${pad(utc.hour, 2)}:${pad(utc.minute, 2)}:${pad(utc.second, 2)}.${ms}Z';
}

/// The only sanctioned "now" for records this device writes.
String nowUtc() => toInstant(DateTime.now());
