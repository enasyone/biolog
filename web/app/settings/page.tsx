'use client';

import Link from 'next/link';
import { useState, type ReactNode } from 'react';
import { DEFAULT_FEATURE_FLAGS, useSession } from '../../context/session-context';
import { useModuleAction } from '../../lib/use-module-action';
import { ModuleActionStatus } from '../../components/module-action-status';

/**
 * System Settings (BIO-UX-011-AC01, AC02, AC04, AC05). Six sections named
 * exactly per AC02: General, Notification, Retention/Privacy, Security &
 * Access, Feature Flags, Offline/Device Sync. Every section's Save *and*
 * Reset works through a mock adapter (`TenantService`'s port — the same one
 * `route-manifest.ts` already assigned this route) and is labelled
 * SIMULATION (AC05) — Reset restores that section's field defaults
 * synchronously (a real, unconditional UI action) and then calls the same
 * adapter Save uses, so its outcome is equally visible and equally backed by
 * the mock adapter, not merely a local-only affordance.
 *
 * Security & Access's MFA control is gated to the System Administrator
 * role (AC04), without inventing a new placeholder permission string — see
 * `docs/features/bio-ux-011/README.md` "nav-items.ts placeholder
 * permissions" for why that catalog itself is left untouched. Feature
 * Flags (also AC04) reads/writes `session.featureFlags` directly — toggling
 * Card Designer/Document Verify/Mobile Credentials here immediately shows
 * or hides that item in the primary sidebar (`app-shell.tsx`'s
 * `filterNavByFeatureFlags`), a real navigation change, not a local-only
 * checkbox.
 */
export default function SettingsPage() {
  const session = useSession();
  const isAdmin = session.role.id === 'admin';

  return (
    <div className="biolog-placeholder-page">
      <h1>System Settings</h1>
      <p className="biolog-placeholder-description">
        {session.tenant.name} — general, notification, retention/privacy, security/access,
        feature-flag and offline/device-sync settings.
      </p>

      <GeneralSection tenantName={session.tenant.name} />
      <NotificationSection />
      <RetentionPrivacySection />
      <SecurityAccessSection isAdmin={isAdmin} />
      <FeatureFlagSection />
      <OfflineDeviceSyncSection />

      <nav aria-label="System Settings related pages" className="biolog-placeholder-related">
        <ul>
          <li>
            <Link href="/settings/authentication">Authentication</Link>
          </li>
        </ul>
      </nav>
    </div>
  );
}

function SettingsSection({
  id,
  title,
  description,
  children,
  onSave,
  onReset,
  saveState,
}: {
  id: string;
  title: string;
  description: string;
  children: ReactNode;
  onSave: () => void;
  onReset: () => void;
  saveState: ReturnType<typeof useModuleAction>['state'];
}) {
  return (
    <section className="biolog-card biolog-settings-section" aria-labelledby={`${id}-heading`}>
      <h2 id={`${id}-heading`}>{title}</h2>
      <p className="biolog-placeholder-description">{description}</p>
      {children}
      <div className="biolog-settings-actions">
        <button
          type="button"
          className="biolog-button-primary"
          onClick={onSave}
          disabled={saveState.status === 'loading'}
        >
          {saveState.status === 'loading' ? 'Saving…' : 'Save (SIMULATION)'}
        </button>
        <button type="button" className="biolog-button-secondary" onClick={onReset}>
          Reset
        </button>
      </div>
      <ModuleActionStatus state={saveState} />
    </section>
  );
}

function GeneralSection({ tenantName }: { tenantName: string }) {
  const DEFAULTS = { displayName: tenantName, timeZone: 'Africa/Lagos' };
  const [displayName, setDisplayName] = useState(DEFAULTS.displayName);
  const [timeZone, setTimeZone] = useState(DEFAULTS.timeZone);
  const action = useModuleAction('TenantService');

  return (
    <SettingsSection
      id="general"
      title="General"
      description="Tenant display name and locale defaults."
      onSave={action.run}
      onReset={() => {
        setDisplayName(DEFAULTS.displayName);
        setTimeZone(DEFAULTS.timeZone);
        action.run();
      }}
      saveState={action.state}
    >
      <div className="biolog-field-row">
        <label htmlFor="settings-display-name">Tenant display name</label>
        <input
          id="settings-display-name"
          type="text"
          value={displayName}
          onChange={(e) => setDisplayName(e.target.value)}
        />
      </div>
      <div className="biolog-field-row">
        <label htmlFor="settings-timezone">Time zone</label>
        <select id="settings-timezone" value={timeZone} onChange={(e) => setTimeZone(e.target.value)}>
          <option value="Africa/Lagos">Africa/Lagos</option>
          <option value="Africa/Accra">Africa/Accra</option>
          <option value="Etc/UTC">UTC</option>
        </select>
      </div>
    </SettingsSection>
  );
}

function NotificationSection() {
  const DEFAULTS = { email: true, sms: false, notifyDuplicate: true, notifyDeviceOffline: true };
  const [email, setEmail] = useState(DEFAULTS.email);
  const [sms, setSms] = useState(DEFAULTS.sms);
  const [notifyDuplicate, setNotifyDuplicate] = useState(DEFAULTS.notifyDuplicate);
  const [notifyDeviceOffline, setNotifyDeviceOffline] = useState(DEFAULTS.notifyDeviceOffline);
  const action = useModuleAction('TenantService');

  return (
    <SettingsSection
      id="notification"
      title="Notification"
      description="Channels and events that trigger a notification."
      onSave={action.run}
      onReset={() => {
        setEmail(DEFAULTS.email);
        setSms(DEFAULTS.sms);
        setNotifyDuplicate(DEFAULTS.notifyDuplicate);
        setNotifyDeviceOffline(DEFAULTS.notifyDeviceOffline);
        action.run();
      }}
      saveState={action.state}
    >
      <div className="biolog-toggle-row">
        <label>
          <input type="checkbox" checked={email} onChange={(e) => setEmail(e.target.checked)} /> Email
          notifications
        </label>
      </div>
      <div className="biolog-toggle-row">
        <label>
          <input type="checkbox" checked={sms} onChange={(e) => setSms(e.target.checked)} /> SMS
          notifications
        </label>
      </div>
      <div className="biolog-toggle-row">
        <label>
          <input
            type="checkbox"
            checked={notifyDuplicate}
            onChange={(e) => setNotifyDuplicate(e.target.checked)}
          />{' '}
          Duplicate identity detected
        </label>
      </div>
      <div className="biolog-toggle-row">
        <label>
          <input
            type="checkbox"
            checked={notifyDeviceOffline}
            onChange={(e) => setNotifyDeviceOffline(e.target.checked)}
          />{' '}
          Device goes offline
        </label>
      </div>
    </SettingsSection>
  );
}

function RetentionPrivacySection() {
  const DEFAULTS = { recordRetentionDays: '365', biometricRetentionDays: '90', anonymizeOnExit: true };
  const [recordRetentionDays, setRecordRetentionDays] = useState(DEFAULTS.recordRetentionDays);
  const [biometricRetentionDays, setBiometricRetentionDays] = useState(DEFAULTS.biometricRetentionDays);
  const [anonymizeOnExit, setAnonymizeOnExit] = useState(DEFAULTS.anonymizeOnExit);
  const action = useModuleAction('TenantService');

  return (
    <SettingsSection
      id="retention-privacy"
      title="Retention & Privacy"
      description="How long records and biometric templates are retained."
      onSave={action.run}
      onReset={() => {
        setRecordRetentionDays(DEFAULTS.recordRetentionDays);
        setBiometricRetentionDays(DEFAULTS.biometricRetentionDays);
        setAnonymizeOnExit(DEFAULTS.anonymizeOnExit);
        action.run();
      }}
      saveState={action.state}
    >
      <div className="biolog-field-row">
        <label htmlFor="settings-record-retention">Record retention</label>
        <select
          id="settings-record-retention"
          value={recordRetentionDays}
          onChange={(e) => setRecordRetentionDays(e.target.value)}
        >
          <option value="90">90 days</option>
          <option value="365">1 year</option>
          <option value="1825">5 years</option>
        </select>
      </div>
      <div className="biolog-field-row">
        <label htmlFor="settings-biometric-retention">Biometric template retention</label>
        <select
          id="settings-biometric-retention"
          value={biometricRetentionDays}
          onChange={(e) => setBiometricRetentionDays(e.target.value)}
        >
          <option value="30">30 days</option>
          <option value="90">90 days</option>
          <option value="365">1 year</option>
        </select>
      </div>
      <div className="biolog-toggle-row">
        <label>
          <input
            type="checkbox"
            checked={anonymizeOnExit}
            onChange={(e) => setAnonymizeOnExit(e.target.checked)}
          />{' '}
          Anonymize identity on exit/graduation
        </label>
      </div>
    </SettingsSection>
  );
}

function SecurityAccessSection({ isAdmin }: { isAdmin: boolean }) {
  const DEFAULTS = { requireMfa: false, sessionTimeoutMinutes: '30', passwordPolicy: 'standard' };
  const [requireMfa, setRequireMfa] = useState(DEFAULTS.requireMfa);
  const [sessionTimeoutMinutes, setSessionTimeoutMinutes] = useState(DEFAULTS.sessionTimeoutMinutes);
  const [passwordPolicy, setPasswordPolicy] = useState(DEFAULTS.passwordPolicy);
  const action = useModuleAction('TenantService');

  return (
    <SettingsSection
      id="security-access"
      title="Security & Access"
      description="Sign-in and session security policy for this tenant."
      onSave={action.run}
      onReset={() => {
        setRequireMfa(DEFAULTS.requireMfa);
        setSessionTimeoutMinutes(DEFAULTS.sessionTimeoutMinutes);
        setPasswordPolicy(DEFAULTS.passwordPolicy);
        action.run();
      }}
      saveState={action.state}
    >
      <div className="biolog-toggle-row">
        <label>
          <input
            type="checkbox"
            checked={requireMfa}
            disabled={!isAdmin}
            onChange={(e) => setRequireMfa(e.target.checked)}
          />{' '}
          Require multi-factor authentication
        </label>
        {!isAdmin && (
          <p className="biolog-field-note">
            Only a System Administrator can change this — switch role in the header to edit.
          </p>
        )}
      </div>
      <div className="biolog-field-row">
        <label htmlFor="settings-session-timeout">Session timeout</label>
        <select
          id="settings-session-timeout"
          value={sessionTimeoutMinutes}
          onChange={(e) => setSessionTimeoutMinutes(e.target.value)}
        >
          <option value="15">15 minutes</option>
          <option value="30">30 minutes</option>
          <option value="60">1 hour</option>
        </select>
      </div>
      <div className="biolog-field-row">
        <label htmlFor="settings-password-policy">Password policy</label>
        <select
          id="settings-password-policy"
          value={passwordPolicy}
          onChange={(e) => setPasswordPolicy(e.target.value)}
        >
          <option value="standard">Standard</option>
          <option value="strict">Strict</option>
        </select>
      </div>
    </SettingsSection>
  );
}

function FeatureFlagSection() {
  const session = useSession();
  const action = useModuleAction('TenantService');

  return (
    <SettingsSection
      id="feature-flags"
      title="Feature Flags"
      description="Modules enabled for this tenant. Toggling one immediately shows or hides its item in the primary sidebar — try turning Mobile Credentials on."
      onSave={action.run}
      onReset={() => {
        for (const key of Object.keys(DEFAULT_FEATURE_FLAGS) as (keyof typeof DEFAULT_FEATURE_FLAGS)[]) {
          session.setFeatureFlag(key, DEFAULT_FEATURE_FLAGS[key]);
        }
        action.run();
      }}
      saveState={action.state}
    >
      <div className="biolog-toggle-row">
        <label>
          <input
            type="checkbox"
            checked={session.featureFlags.cardDesigner}
            onChange={(e) => session.setFeatureFlag('cardDesigner', e.target.checked)}
          />{' '}
          Card Designer
        </label>
      </div>
      <div className="biolog-toggle-row">
        <label>
          <input
            type="checkbox"
            checked={session.featureFlags.documentVerify}
            onChange={(e) => session.setFeatureFlag('documentVerify', e.target.checked)}
          />{' '}
          Document Verify
        </label>
      </div>
      <div className="biolog-toggle-row">
        <label>
          <input
            type="checkbox"
            checked={session.featureFlags.mobileCredentials}
            onChange={(e) => session.setFeatureFlag('mobileCredentials', e.target.checked)}
          />{' '}
          Mobile Credentials
        </label>
      </div>
    </SettingsSection>
  );
}

function OfflineDeviceSyncSection() {
  const DEFAULTS = { bufferSize: '500', syncIntervalMinutes: '15' };
  const [bufferSize, setBufferSize] = useState(DEFAULTS.bufferSize);
  const [syncIntervalMinutes, setSyncIntervalMinutes] = useState(DEFAULTS.syncIntervalMinutes);
  const saveAction = useModuleAction('TenantService');
  const syncNowAction = useModuleAction('DeviceService');

  return (
    <SettingsSection
      id="offline-device-sync"
      title="Offline & Device Sync"
      description="How devices buffer and resynchronise events while offline."
      onSave={saveAction.run}
      onReset={() => {
        setBufferSize(DEFAULTS.bufferSize);
        setSyncIntervalMinutes(DEFAULTS.syncIntervalMinutes);
        saveAction.run();
      }}
      saveState={saveAction.state}
    >
      <div className="biolog-field-row">
        <label htmlFor="settings-buffer-size">Offline event buffer size</label>
        <select id="settings-buffer-size" value={bufferSize} onChange={(e) => setBufferSize(e.target.value)}>
          <option value="100">100 events</option>
          <option value="500">500 events</option>
          <option value="2000">2,000 events</option>
        </select>
      </div>
      <div className="biolog-field-row">
        <label htmlFor="settings-sync-interval">Auto-sync interval</label>
        <select
          id="settings-sync-interval"
          value={syncIntervalMinutes}
          onChange={(e) => setSyncIntervalMinutes(e.target.value)}
        >
          <option value="5">5 minutes</option>
          <option value="15">15 minutes</option>
          <option value="60">1 hour</option>
        </select>
      </div>
      <div className="biolog-settings-actions">
        <button
          type="button"
          className="biolog-button-secondary"
          onClick={syncNowAction.run}
          disabled={syncNowAction.state.status === 'loading'}
        >
          {syncNowAction.state.status === 'loading' ? 'Syncing…' : 'Sync now (SIMULATION)'}
        </button>
      </div>
      <ModuleActionStatus state={syncNowAction.state} />
    </SettingsSection>
  );
}
