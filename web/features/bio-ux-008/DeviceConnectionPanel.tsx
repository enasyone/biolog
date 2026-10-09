'use client';

import { useState } from 'react';
import type { BiometricDeviceKind, DeviceScenarioHint } from '../biometrics/ports';
import type { BiometricsPort } from '../biometrics/ports';
import { useTestScenarioPreset } from '../../dev-tools/use-test-scenario-preset';
import { AUTO_OPTION_VALUE, DEVICE_OPTIONS } from './scenarioOptions';
import styles from './biometric-journeys.module.css';

const DEVICE_HINT_VALUES: readonly DeviceScenarioHint[] = ['connected', 'offline'];

export interface DeviceConnectionPanelProps {
  port: BiometricsPort;
  kind: BiometricDeviceKind;
  label: string;
}

type ConnectionState = { status: 'disconnected' } | { status: 'connecting' } | { status: 'connected' } | { status: 'offline'; message: string };

/**
 * Simulated device connection (BIO-UX-008-AC03). A capture/verify/identify
 * action below is still callable while disconnected — this panel models
 * the device-connection step explicitly, it does not gate the rest of the
 * journey, since none of this story's ACs ask for that extra coupling.
 */
export function DeviceConnectionPanel({ port, kind, label }: DeviceConnectionPanelProps) {
  const [state, setState] = useState<ConnectionState>({ status: 'disconnected' });
  const [hint, setHint] = useTestScenarioPreset<DeviceScenarioHint | ''>('deviceHint', DEVICE_HINT_VALUES, '');

  async function connect() {
    setState({ status: 'connecting' });
    const result = await port.connectDevice(kind, hint === '' ? undefined : hint);
    setState(result.outcome === 'success' ? { status: 'connected' } : { status: 'offline', message: result.message });
  }

  return (
    <div className={styles.controlsRow}>
      <div className={styles.deviceStatus}>
        <span className={styles.dot} data-connected={state.status === 'connected'} aria-hidden="true" />
        <span data-testid="device-status">
          {label}:{' '}
          {state.status === 'connected' && 'Connected (SIMULATED)'}
          {state.status === 'connecting' && 'Connecting… (SIMULATED)'}
          {state.status === 'disconnected' && 'Not connected'}
          {state.status === 'offline' && state.message}
        </span>
      </div>
      <div className={styles.field}>
        <label htmlFor={`${kind}-connect-hint`}>Simulate connection outcome</label>
        <select
          id={`${kind}-connect-hint`}
          value={hint}
          onChange={(event) => setHint(event.target.value as DeviceScenarioHint | '')}
        >
          <option value={AUTO_OPTION_VALUE}>Auto (cycles)</option>
          {DEVICE_OPTIONS.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      </div>
      <button type="button" className="biolog-button-primary" onClick={connect} disabled={state.status === 'connecting'}>
        {state.status === 'connecting' ? 'Connecting…' : `Connect ${label.toLowerCase()}`}
      </button>
    </div>
  );
}
