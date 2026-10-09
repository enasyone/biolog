'use client';

import { useMemo, useState } from 'react';
import { ActionStatus, type ActionState } from '../../components/action-status';
import { NotificationList } from '../../components/notification-list';
import { OPERATIONAL_SCENARIO_OPTIONS, ScenarioPicker } from '../../components/scenario-picker';
import { StatusBadge, type StatusBadgeTone } from '../../components/status-badge';
import { useSession } from '../../context/session-context';
import { useTestScenarioPreset } from '../../dev-tools/use-test-scenario-preset';
import {
  DEFAULT_SEED,
  DEVICE_SITES,
  DEVICE_VENDORS,
  deriveNotifications,
  generateDataset,
  type DeviceConnectivity,
  type DeviceSite,
  type DeviceVendor,
  type ScenarioId,
} from '../../lib/synthetic-data';
import { devicesPort, devicesViewConnectivityPort } from './index';
import { DeviceOperationsPanel } from '../bio-dev-004/DeviceOperationsPanel';

const CONNECTIVITY_TONE: Record<DeviceConnectivity, StatusBadgeTone> = {
  online: 'success',
  offline: 'error',
  degraded: 'warning',
};

interface SimulatedDevice {
  id: string;
  name: string;
}

interface RegisterDeviceFormState {
  serial: string;
  vendor: DeviceVendor;
  model: string;
  firmware: string;
  sdk: string;
  site: DeviceSite;
  ipAddress: string;
  macAddress: string;
  hostname: string;
  credentialReference: string;
}

/** Non-empty defaults, same convention as `access-page.tsx`'s pre-filled Zone ('Main Gate') — every required field has a plausible example value so the form is submittable as-is, not just after manual entry. */
const INITIAL_REGISTER_FORM: RegisterDeviceFormState = {
  serial: 'DEV-0001',
  vendor: DEVICE_VENDORS[0]!,
  model: 'FS-100',
  firmware: '1.0.0',
  sdk: '1.0.0',
  site: DEVICE_SITES[0]!,
  ipAddress: '',
  macAddress: '',
  hostname: '',
  credentialReference: '',
};

/**
 * Devices page (BIO-UX-010-AC01, AC02, AC05).
 *
 * "Register device" (AC01 primary action) adds a locally simulated device
 * after `devicesPort` confirms success; "View connectivity status" reveals
 * the tenant's device list with each device's `online`/`offline`/`degraded`
 * state, which the shared scenario picker (AC02) drives directly.
 */
export function DevicesPage() {
  const { tenant } = useSession();
  const [scenarioId, setScenarioId] = useTestScenarioPreset<ScenarioId>(
    'scenario',
    OPERATIONAL_SCENARIO_OPTIONS.map((option) => option.id),
    'normal',
  );
  const dataset = useMemo(() => generateDataset(DEFAULT_SEED, scenarioId), [scenarioId]);
  const notifications = useMemo(() => deriveNotifications(dataset), [dataset]);
  const tenantDevices = useMemo(
    () => dataset.devices.filter((device) => device.tenantId === tenant.id),
    [dataset, tenant.id],
  );

  const [form, setForm] = useState<RegisterDeviceFormState>(INITIAL_REGISTER_FORM);
  const [registeredDevices, setRegisteredDevices] = useState<readonly SimulatedDevice[]>([]);
  const [registerAction, setRegisterAction] = useState<ActionState>({ status: 'idle' });

  const [statusVisible, setStatusVisible] = useState(false);
  const [viewStatusAction, setViewStatusAction] = useState<ActionState>({ status: 'idle' });

  function updateForm<K extends keyof RegisterDeviceFormState>(key: K, value: RegisterDeviceFormState[K]) {
    setForm((prev) => ({ ...prev, [key]: value }));
  }

  async function registerDevice() {
    setRegisterAction({ status: 'loading' });
    const result = await devicesPort.registerDevice({
      serial: form.serial.trim(),
      vendor: form.vendor,
      model: form.model.trim(),
      firmware: form.firmware.trim(),
      sdk: form.sdk.trim(),
      site: form.site,
      network: {
        ipAddress: form.ipAddress.trim() || undefined,
        macAddress: form.macAddress.trim() || undefined,
        hostname: form.hostname.trim() || undefined,
      },
      credentialReference: form.credentialReference.trim() || undefined,
    });
    setRegisterAction({ status: 'done', ...result });
    if (result.outcome === 'success' && form.serial.trim().length > 0) {
      setRegisteredDevices((prev) => [
        ...prev,
        { id: `device-${prev.length}-${Date.now()}`, name: `${form.vendor} ${form.model.trim()} (${form.serial.trim()})` },
      ]);
    }
  }

  async function viewConnectivityStatus() {
    setViewStatusAction({ status: 'loading' });
    const result = await devicesViewConnectivityPort.primaryAction();
    setViewStatusAction({ status: 'done', ...result });
    if (result.outcome === 'success') {
      setStatusVisible(true);
    }
  }

  return (
    <div className="biolog-placeholder-page">
      <div className="biolog-op-page-header">
        <div>
          <h1>Devices</h1>
          <p className="biolog-placeholder-description">
            Registered capture and access devices, and their connectivity state, for {tenant.name}.
          </p>
        </div>
        <ScenarioPicker value={scenarioId} onChange={setScenarioId} options={OPERATIONAL_SCENARIO_OPTIONS} />
      </div>

      <NotificationList notifications={notifications} />

      <div className="biolog-op-grid">
        <section className="biolog-card">
          <h2>Register device</h2>
          <form
            className="biolog-op-form"
            onSubmit={(event) => {
              event.preventDefault();
              void registerDevice();
            }}
          >
            <label className="biolog-op-field">
              <span>Serial</span>
              <input
                aria-label="Serial"
                value={form.serial}
                onChange={(event) => updateForm('serial', event.target.value)}
                required
              />
            </label>
            <label className="biolog-op-field">
              <span>Vendor</span>
              <select
                aria-label="Vendor"
                value={form.vendor}
                onChange={(event) => updateForm('vendor', event.target.value as DeviceVendor)}
              >
                {DEVICE_VENDORS.map((vendor) => (
                  <option key={vendor} value={vendor}>
                    {vendor}
                  </option>
                ))}
              </select>
            </label>
            <label className="biolog-op-field">
              <span>Model</span>
              <input
                aria-label="Model"
                value={form.model}
                onChange={(event) => updateForm('model', event.target.value)}
                required
              />
            </label>
            <label className="biolog-op-field">
              <span>Firmware</span>
              <input
                aria-label="Firmware"
                value={form.firmware}
                onChange={(event) => updateForm('firmware', event.target.value)}
                required
              />
            </label>
            <label className="biolog-op-field">
              <span>SDK</span>
              <input
                aria-label="SDK"
                value={form.sdk}
                onChange={(event) => updateForm('sdk', event.target.value)}
                required
              />
            </label>
            <label className="biolog-op-field">
              <span>Site</span>
              <select
                aria-label="Site"
                value={form.site}
                onChange={(event) => updateForm('site', event.target.value as DeviceSite)}
              >
                {DEVICE_SITES.map((site) => (
                  <option key={site} value={site}>
                    {site}
                  </option>
                ))}
              </select>
            </label>
            <fieldset>
              <legend>Network (optional)</legend>
              <div className="biolog-op-form">
                <label className="biolog-op-field">
                  <span>IP address</span>
                  <input
                    aria-label="IP address"
                    value={form.ipAddress}
                    onChange={(event) => updateForm('ipAddress', event.target.value)}
                  />
                </label>
                <label className="biolog-op-field">
                  <span>MAC address</span>
                  <input
                    aria-label="MAC address"
                    value={form.macAddress}
                    onChange={(event) => updateForm('macAddress', event.target.value)}
                  />
                </label>
                <label className="biolog-op-field">
                  <span>Hostname</span>
                  <input
                    aria-label="Hostname"
                    value={form.hostname}
                    onChange={(event) => updateForm('hostname', event.target.value)}
                  />
                </label>
              </div>
            </fieldset>
            <label className="biolog-op-field">
              <span>Credential reference (optional)</span>
              <input
                aria-label="Credential reference"
                value={form.credentialReference}
                onChange={(event) => updateForm('credentialReference', event.target.value)}
              />
            </label>
            <div className="biolog-op-actions">
              <button type="submit" className="biolog-button-primary" disabled={registerAction.status === 'loading'}>
                {registerAction.status === 'loading' ? 'Working…' : 'Register device'}
              </button>
            </div>
            <ActionStatus action={registerAction} />
          </form>

          {registeredDevices.length > 0 && (
            <ul className="biolog-op-history-list">
              {registeredDevices.map((device) => (
                <li key={device.id}>{device.name} registered (SYNTHETIC).</li>
              ))}
            </ul>
          )}
        </section>

        <aside className="biolog-card">
          <h2>Connectivity status</h2>
          <div className="biolog-op-actions">
            <button
              type="button"
              className="biolog-button-secondary"
              onClick={() => void viewConnectivityStatus()}
              disabled={viewStatusAction.status === 'loading'}
            >
              {viewStatusAction.status === 'loading' ? 'Working…' : 'View connectivity status'}
            </button>
          </div>
          <ActionStatus action={viewStatusAction} />

          {statusVisible &&
            (tenantDevices.length === 0 ? (
              <p className="biolog-op-empty">No devices for this scenario.</p>
            ) : (
              <table className="biolog-table">
                <caption>Devices</caption>
                <thead>
                  <tr>
                    <th scope="col">Device</th>
                    <th scope="col">Connectivity</th>
                  </tr>
                </thead>
                <tbody>
                  {tenantDevices.map((device) => (
                    <tr key={device.id}>
                      <td>{device.name}</td>
                      <td>
                        <StatusBadge tone={CONNECTIVITY_TONE[device.connectivity]}>{device.connectivity}</StatusBadge>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            ))}
        </aside>
      </div>

      <DeviceOperationsPanel />
    </div>
  );
}
