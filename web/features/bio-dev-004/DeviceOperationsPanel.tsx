'use client';

import { useCallback, useEffect, useState } from 'react';
import {
  getRemoteJobCapabilitiesPort,
  KNOWN_REMOTE_JOB_TYPES,
  listDevicesPort,
  pollDeviceHealthPort,
  runRemoteJobPort,
} from './index';
import type { DeviceOperationsSummary, RemoteJobTypeId } from './index';

type LoadState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'offline'; message: string }
  | { status: 'ready'; devices: readonly DeviceOperationsSummary[] };

const HEALTH_LABEL: Record<NonNullable<DeviceOperationsSummary['health']> | 'none', string> = {
  Online: 'Online',
  Degraded: 'Degraded',
  Offline: 'Offline',
  Unknown: 'Unknown',
  none: 'Never polled',
};

/**
 * BIO-DEV-004-AC05: the real "Device operations" panel on `/devices`, mounted ALONGSIDE the
 * existing "Register device"/"Connectivity status" sections (never replacing either - see
 * `web/features/devices/devices-page.tsx`). "Poll health" (AC01) updates a device's own
 * last-seen/status/health in place; the remote-job dropdown is populated per-device from this
 * story's own capability-gated job-type list (AC02), so a visible primary action never offers
 * a job the device cannot run - the same "no visible primary action may be dead" rule this
 * codebase's own UI-first contract requires, applied here via disabling the unsupported
 * option rather than hiding the button, so AC04's explicit failure is still reachable and
 * visible (a rejected job surfaces as a genuinely distinct, accessible `role="alert"` state -
 * the same convention `web/features/bio-trn-003/TransportNotificationsPanel.tsx`'s own
 * delivery-outcome rendering already established). SYNTHETIC data only.
 */
export function DeviceOperationsPanel() {
  const [state, setState] = useState<LoadState>({ status: 'loading' });
  const [workingDeviceId, setWorkingDeviceId] = useState<string | null>(null);
  const [notice, setNotice] = useState<{ message: string; isError: boolean } | null>(null);
  const [capabilitiesByDevice, setCapabilitiesByDevice] = useState<Record<string, readonly RemoteJobTypeId[]>>({});
  const [selectedJobByDevice, setSelectedJobByDevice] = useState<Record<string, RemoteJobTypeId>>({});

  const load = useCallback(() => {
    setState({ status: 'loading' });
    listDevicesPort
      .primaryAction()
      .then((result) => {
        if (result.outcome === 'offline') {
          setState({ status: 'offline', message: result.message });
        } else if (result.outcome === 'failure') {
          setState({ status: 'error', message: result.message });
        } else {
          const devices = result.devices ?? [];
          setState({ status: 'ready', devices });
          for (const device of devices) {
            void getRemoteJobCapabilitiesPort.primaryAction({ deviceId: device.id }).then((capResult) => {
              if (capResult.outcome === 'success') {
                setCapabilitiesByDevice((prev) => ({ ...prev, [device.id]: capResult.supportedJobTypes ?? [] }));
              }
            });
          }
        }
      })
      .catch(() => setState({ status: 'error', message: 'Could not load devices - try again.' }));
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  async function handlePollHealth(deviceId: string) {
    setWorkingDeviceId(deviceId);
    setNotice(null);
    try {
      const result = await pollDeviceHealthPort.primaryAction({ deviceId });
      setNotice({ message: result.message, isError: result.outcome !== 'success' });
      if (result.outcome === 'success') {
        load();
      }
    } finally {
      setWorkingDeviceId(null);
    }
  }

  async function handleRunJob(deviceId: string) {
    const jobType = selectedJobByDevice[deviceId] ?? KNOWN_REMOTE_JOB_TYPES[0]!;
    setWorkingDeviceId(deviceId);
    setNotice(null);
    try {
      const result = await runRemoteJobPort.primaryAction({ deviceId, jobType });
      setNotice({ message: result.message, isError: result.outcome !== 'success' || result.jobOutcome === 'Failed' });
    } finally {
      setWorkingDeviceId(null);
    }
  }

  return (
    <section className="biolog-card" data-testid="device-operations-panel">
      <h2>Device operations</h2>
      <p className="biolog-placeholder-description">
        Health polling/heartbeat and capability-gated remote configuration jobs (BIO-DEV-004). SYNTHETIC data only.
      </p>

      {state.status === 'loading' && <p role="status">Loading…</p>}
      {state.status === 'error' && <p role="alert">{state.message}</p>}
      {state.status === 'offline' && <p role="alert">{state.message}</p>}

      {state.status === 'ready' && (
        <table className="biolog-table" aria-label="Device operations" data-testid="device-operations-table">
          <thead>
            <tr>
              <th>Device</th>
              <th>Status</th>
              <th>Health</th>
              <th>Last seen</th>
              <th>
                <span className="biolog-visually-hidden">Actions</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {state.devices.length === 0 && (
              <tr>
                <td colSpan={5} data-testid="device-operations-empty">No devices registered for this tenant yet.</td>
              </tr>
            )}
            {state.devices.map((device) => {
              const supportedJobs = capabilitiesByDevice[device.id] ?? [];
              const selectedJob = selectedJobByDevice[device.id] ?? KNOWN_REMOTE_JOB_TYPES[0]!;
              const busy = workingDeviceId === device.id;
              return (
                <tr key={device.id} data-testid="device-operations-row">
                  <td>{device.vendor} {device.model}</td>
                  <td>{device.status}</td>
                  <td data-testid="device-operations-health">{HEALTH_LABEL[device.health ?? 'none']}</td>
                  <td data-testid="device-operations-last-seen">{device.lastSeenAt ? new Date(device.lastSeenAt).toLocaleString() : 'Never'}</td>
                  <td>
                    <div className="biolog-op-actions">
                      <button
                        type="button"
                        data-testid="device-operations-poll-health"
                        disabled={busy}
                        onClick={() => handlePollHealth(device.id)}
                      >
                        {busy ? 'Working…' : 'Poll health'}
                      </button>
                      <select
                        aria-label={`Remote job for ${device.vendor} ${device.model}`}
                        data-testid="device-operations-job-select"
                        value={selectedJob}
                        onChange={(e) =>
                          setSelectedJobByDevice((prev) => ({ ...prev, [device.id]: e.target.value as RemoteJobTypeId }))
                        }
                      >
                        {KNOWN_REMOTE_JOB_TYPES.map((jobType) => (
                          <option key={jobType} value={jobType} disabled={!supportedJobs.includes(jobType)}>
                            {jobType}
                            {!supportedJobs.includes(jobType) ? ' (unsupported)' : ''}
                          </option>
                        ))}
                      </select>
                      <button
                        type="button"
                        className="biolog-button-primary"
                        data-testid="device-operations-run-job"
                        disabled={busy || !supportedJobs.includes(selectedJob)}
                        onClick={() => handleRunJob(device.id)}
                      >
                        {busy ? 'Working…' : 'Run job'}
                      </button>
                    </div>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}

      {notice && (
        <p role={notice.isError ? 'alert' : undefined} data-testid="device-operations-notice">
          {notice.message}
        </p>
      )}
    </section>
  );
}
