'use client';

import { useState } from 'react';
import { auditFeatureModes } from '../lib/feature-mode';

/**
 * Development-only feature-mode indicator (BIO-UX-005-AC03).
 *
 * Displays every module's current SIMULATION/HYBRID/LIVE status and its
 * per-port breakdown — a read-only audit view (BIO-UX-005-AC06). Switching
 * a mode is set via `NEXT_PUBLIC_FEATURE_MODE_<MODULE>` env vars or
 * `setPortModeOverride` in tests (`feature-mode.ts`), not from this panel —
 * an interactive scenario switcher is BIO-UX-012's own "Human Test Console"
 * story, which this panel does not attempt to duplicate or pre-empt.
 *
 * `process.env.NODE_ENV === 'production'` is inlined to a literal at Next.js
 * build time, so in a production build this always returns `null` before
 * any hook runs and the branch below it is dead-code-eliminated entirely —
 * not a runtime check that could be bypassed (BIO-UX-005-AC03 "cannot be
 * enabled in production builds"). See `docs/features/bio-ux-005/README.md`
 * "Proving AC03" for how this is verified against a real production build,
 * not just asserted.
 */
export function FeatureModePanel() {
  if (process.env.NODE_ENV === 'production') {
    return null;
  }
  return <FeatureModePanelContent />;
}

function FeatureModePanelContent() {
  const [open, setOpen] = useState(false);
  const entries = auditFeatureModes();

  return (
    <div className="biolog-feature-mode-panel" data-testid="biolog-feature-mode-panel">
      <button
        type="button"
        className="biolog-feature-mode-toggle"
        onClick={() => setOpen((value) => !value)}
        aria-expanded={open}
        aria-controls="biolog-feature-mode-list"
      >
        DEV — feature modes
      </button>
      {open && (
        <div
          id="biolog-feature-mode-list"
          className="biolog-feature-mode-list"
          role="region"
          aria-label="Feature mode status by module"
        >
          <table>
            <thead>
              <tr>
                <th scope="col">Module</th>
                <th scope="col">Status</th>
                <th scope="col">Ports</th>
              </tr>
            </thead>
            <tbody>
              {entries.map((entry) => (
                <tr key={entry.module}>
                  <td>{entry.module}</td>
                  <td>
                    <span className={`biolog-feature-mode-badge biolog-feature-mode-badge--${entry.status.toLowerCase()}`}>
                      {entry.status}
                    </span>
                  </td>
                  <td>{entry.ports.map((p) => `${p.portKey}: ${p.mode}`).join(', ')}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
