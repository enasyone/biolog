import { getPortMode, resolvePort } from '../../lib/feature-mode';
import type { ModulePort, PrimaryActionResult } from '../../lib/module-port';
import { attendanceFlagExceptionMockPort, attendanceMockPort, workforceAttendanceMockPort } from './mock';
import { attendanceFlagExceptionLivePort, attendanceLivePort, workforceAttendanceLivePort } from './live';
import type { AttendancePort, WorkforceAttendancePort } from './ports';

export type { AttendancePort, WorkforceAttendancePort } from './ports';

/**
 * The Attendance module's resolved ports (BIO-UX-010-AC01, AC05; BIO-ATT-003-AC05).
 * Pages/components import these, never `./mock` or `./live` directly.
 */
export const attendancePort: AttendancePort = resolvePort(attendanceMockPort, attendanceLivePort, 'primaryAction');

export const attendanceFlagExceptionPort: AttendancePort = resolvePort(
  attendanceFlagExceptionMockPort,
  attendanceFlagExceptionLivePort,
  'flagException',
);

/**
 * BIO-ATT-003-AC05: `workforceAttendancePort`'s shape (`WorkforceAttendancePort`) is not
 * `ModulePort`-shaped (no `primaryAction()`), so it cannot go through the generic
 * `resolvePort<T extends ModulePort>` helper above - resolved directly against the same
 * `AttendanceService:workforceAttendance` port key `getPortMode` already tracks, the same
 * fail-safe-to-SIMULATION default every other port in this module uses.
 */
export const workforceAttendancePort: WorkforceAttendancePort =
  getPortMode('AttendanceService', 'workforceAttendance') === 'LIVE' ? workforceAttendanceLivePort : workforceAttendanceMockPort;

/**
 * `ModulePort`-shaped proxy, solely to satisfy `module-registry.ts`'s own invariant
 * (BIO-UX-005-AC01: every key `MODULE_PORT_KEYS` lists for a module has a matching
 * `ModulePort` registered in `MODULE_PORTS`, cross-checked by
 * `bio-ux-005-module-registry.test.ts`) - found and fixed during BIO-ATT-003-F02's own
 * remediation, since `workforceAttendancePort` above cannot satisfy that shape itself.
 * `/attendance` is a real page, not `PlaceholderPage`-routed, so nothing actually calls this
 * through `module-registry.ts` at runtime; `primaryAction()` still does something genuinely
 * real (not a stub) rather than leaving a dead code path with a fabricated always-success
 * reading. `mode` matches the SAME `workforceAttendance` port key `getPortMode` already tracks,
 * read once at module load like every other port in this module.
 */
export const workforceAttendanceModulePort: ModulePort = {
  module: 'AttendanceService',
  mode: getPortMode('AttendanceService', 'workforceAttendance') === 'LIVE' ? 'live' : 'mock',
  async primaryAction(): Promise<PrimaryActionResult> {
    try {
      await workforceAttendancePort.listShifts();
      return { outcome: 'success', message: 'Workforce attendance: shifts loaded.' };
    } catch {
      return { outcome: 'failure', message: 'Workforce attendance: could not load shifts - try again.' };
    }
  },
};
