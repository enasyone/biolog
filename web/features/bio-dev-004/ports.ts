import type { ModulePort, PrimaryActionResult } from '../../lib/module-port';

/**
 * BIO-DEV-004's own dedicated connection: tenant-scoped device health polling/heartbeat and
 * capability-gated remote configuration jobs, backing a new "Device operations" panel on
 * `/devices` mounted ALONGSIDE the existing "Register device"/"Connectivity status" panels
 * (never replacing either) - the same "new keys, new panel, existing panel untouched"
 * precedent BIO-TRN-003's own notifications panel already established on `/transport`.
 */
export type RemoteJobTypeId = 'Reboot' | 'PushConfiguration' | 'UpdateFirmware';

export const KNOWN_REMOTE_JOB_TYPES: readonly RemoteJobTypeId[] = ['Reboot', 'PushConfiguration', 'UpdateFirmware'];

export interface DeviceOperationsSummary {
  id: string;
  vendor: string;
  model: string;
  status: string;
  health: 'Online' | 'Degraded' | 'Offline' | 'Unknown' | null;
  healthDetail: string | null;
  lastSeenAt: string | null;
  version: number;
}

export interface ListDevicesResult extends PrimaryActionResult {
  devices?: readonly DeviceOperationsSummary[];
}

export interface PollDeviceHealthInput {
  deviceId: string;
}

export interface PollDeviceHealthResult extends PrimaryActionResult {
  device?: DeviceOperationsSummary;
}

export interface GetRemoteJobCapabilitiesInput {
  deviceId: string;
}

export interface GetRemoteJobCapabilitiesResult extends PrimaryActionResult {
  supportedJobTypes?: readonly RemoteJobTypeId[];
}

export interface RunRemoteJobInput {
  deviceId: string;
  jobType: RemoteJobTypeId;
}

export interface RunRemoteJobResult extends PrimaryActionResult {
  /** The remote job's own outcome, distinct from `outcome` (the port call's own success/failure/offline) - a job can be a successful PORT CALL that reports a Failed job outcome (AC03: a failure is still recorded and surfaced, never swallowed). */
  jobOutcome?: 'Succeeded' | 'Failed';
}

export interface ListDevicesPort extends ModulePort {
  primaryAction(): Promise<ListDevicesResult>;
}

export interface PollDeviceHealthPort extends ModulePort {
  primaryAction(input?: PollDeviceHealthInput): Promise<PollDeviceHealthResult>;
}

export interface GetRemoteJobCapabilitiesPort extends ModulePort {
  primaryAction(input?: GetRemoteJobCapabilitiesInput): Promise<GetRemoteJobCapabilitiesResult>;
}

export interface RunRemoteJobPort extends ModulePort {
  primaryAction(input?: RunRemoteJobInput): Promise<RunRemoteJobResult>;
}
