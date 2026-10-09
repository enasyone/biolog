'use client';

import type { ScenarioId } from '../lib/synthetic-data';

/**
 * Scenario selector shared by the five BIO-UX-010 operational pages
 * (BIO-UX-010-AC02: "online, offline, degraded-device, denied-access and
 * notification scenarios are available"). Purely presentational — the
 * calling page owns the `SyntheticDataStore` and re-renders from it when
 * `onChange` fires; this component holds no data of its own.
 */
export interface ScenarioOption {
  id: ScenarioId;
  label: string;
}

export interface ScenarioPickerProps {
  value: ScenarioId;
  onChange: (id: ScenarioId) => void;
  options: readonly ScenarioOption[];
  label?: string;
}

/** The five scenarios BIO-UX-010-AC02 names, in the order they appear in every page's picker. */
export const OPERATIONAL_SCENARIO_OPTIONS: readonly ScenarioOption[] = Object.freeze([
  { id: 'normal', label: 'Online' },
  { id: 'offline', label: 'Offline' },
  { id: 'degraded-device', label: 'Degraded device' },
  { id: 'denied-access', label: 'Denied access' },
  { id: 'notification', label: 'Notification' },
]);

export function ScenarioPicker({ value, onChange, options, label = 'Scenario' }: ScenarioPickerProps) {
  return (
    <label className="biolog-op-scenario-picker">
      <span>{label}</span>
      {/* Explicit aria-label, not the implicit wrapping label alone: a wrapping <label>'s
          computed accessible name concatenates every descendant <option>'s text too, which
          both misleads assistive tech and made getByLabel(label) ambiguous in
          bio-ux-010-operational-journeys.spec.ts wherever an option's own text happened to
          contain another field's label as a substring. */}
      <select aria-label={label} value={value} onChange={(event) => onChange(event.target.value as ScenarioId)}>
        {options.map((option) => (
          <option key={option.id} value={option.id}>
            {option.label}
          </option>
        ))}
      </select>
    </label>
  );
}
