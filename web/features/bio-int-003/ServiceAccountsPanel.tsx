'use client';

import { useCallback, useEffect, useState } from 'react';
import { useSession } from '../../context/session-context';
import {
  KNOWN_SCOPES,
  createServiceAccountPort,
  listServiceAccountsPort,
  revokeServiceAccountPort,
  rotateServiceAccountSecretPort,
} from './index';
import type { ServiceAccountSummary } from './index';

type LoadState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'offline'; message: string }
  | { status: 'ready'; accounts: readonly ServiceAccountSummary[] };

/**
 * BIO-INT-003-AC05: the real "API Credentials" panel on `/administration/integrations`, mounted
 * ALONGSIDE BIO-INT-002's own `ImportConsole` (never replacing it - see
 * `web/app/administration/integrations/page.tsx`). Create a tenant-scoped service account,
 * see its one-time secret, rotate it, or revoke the account. SYNTHETIC data only.
 */
export function ServiceAccountsPanel() {
  const session = useSession();
  const [state, setState] = useState<LoadState>({ status: 'loading' });
  const [working, setWorking] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [revealedSecret, setRevealedSecret] = useState<{ accountId: string; secret: string } | null>(null);

  const [newDisplayName, setNewDisplayName] = useState('Student records sync');
  const [newScopes, setNewScopes] = useState<readonly string[]>(['ReadOnly']);

  const load = useCallback(() => {
    setState({ status: 'loading' });
    listServiceAccountsPort
      .primaryAction({ tenantId: session.tenant.id })
      .then((result) => {
        if (result.outcome === 'offline') {
          setState({ status: 'offline', message: result.message });
        } else if (result.outcome === 'failure') {
          setState({ status: 'error', message: result.message });
        } else {
          setState({ status: 'ready', accounts: result.accounts ?? [] });
        }
      })
      .catch(() => setState({ status: 'error', message: 'Could not load service accounts - try again.' }));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [session.tenant.id]);

  useEffect(() => {
    load();
  }, [load]);

  function toggleScope(scope: string) {
    setNewScopes((current) => (current.includes(scope) ? current.filter((s) => s !== scope) : [...current, scope]));
  }

  async function handleCreate() {
    setWorking(true);
    setNotice(null);
    setRevealedSecret(null);
    try {
      const result = await createServiceAccountPort.primaryAction({
        tenantId: session.tenant.id,
        displayName: newDisplayName,
        scopes: newScopes,
      });
      setNotice(result.message);
      if (result.outcome === 'success' && result.account && result.secret) {
        setRevealedSecret({ accountId: result.account.id, secret: result.secret });
        load();
      }
    } finally {
      setWorking(false);
    }
  }

  async function handleRotate(account: ServiceAccountSummary) {
    setWorking(true);
    setNotice(null);
    setRevealedSecret(null);
    try {
      const result = await rotateServiceAccountSecretPort.primaryAction({
        tenantId: session.tenant.id,
        id: account.id,
        expectedVersion: account.version,
      });
      setNotice(result.message);
      if (result.outcome === 'success' && result.account && result.secret) {
        setRevealedSecret({ accountId: result.account.id, secret: result.secret });
        load();
      }
    } finally {
      setWorking(false);
    }
  }

  async function handleRevoke(account: ServiceAccountSummary) {
    setWorking(true);
    setNotice(null);
    setRevealedSecret(null);
    try {
      const result = await revokeServiceAccountPort.primaryAction({
        tenantId: session.tenant.id,
        id: account.id,
        expectedVersion: account.version,
      });
      setNotice(result.message);
      if (result.outcome === 'success') {
        load();
      }
    } finally {
      setWorking(false);
    }
  }

  return (
    <section className="biolog-card">
      <h2>API credentials</h2>
      <p className="biolog-placeholder-description">
        Service accounts for integrators calling BIOLOG&apos;s REST APIs - OAuth2 client_credentials
        grant, scoped to BIOLOG roles, rate-limited and audited (BIO-INT-003). SYNTHETIC data only.
      </p>

      {state.status === 'loading' && <p role="status">Loading…</p>}
      {state.status === 'error' && <p role="alert">{state.message}</p>}
      {state.status === 'offline' && <p role="alert">{state.message}</p>}

      {state.status === 'ready' && (
        <table className="biolog-table" aria-label="Service accounts">
          <thead>
            <tr>
              <th>Display name</th>
              <th>Client ID</th>
              <th>Scopes</th>
              <th>Status</th>
              <th>
                <span className="biolog-visually-hidden">Actions</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {state.accounts.length === 0 && (
              <tr>
                <td colSpan={5}>No service accounts yet for this tenant.</td>
              </tr>
            )}
            {state.accounts.map((account) => (
              <tr key={account.id}>
                <td>{account.displayName}</td>
                <td>
                  <code>{account.clientId}</code>
                </td>
                <td>{account.scopes.join(', ')}</td>
                <td>{account.status}</td>
                <td>
                  <button
                    type="button"
                    disabled={working || account.status === 'Revoked'}
                    onClick={() => handleRotate(account)}
                  >
                    Rotate secret
                  </button>{' '}
                  <button
                    type="button"
                    disabled={working || account.status === 'Revoked'}
                    onClick={() => handleRevoke(account)}
                  >
                    Revoke
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {revealedSecret && (
        <p role="alert">
          New client secret for <code>{revealedSecret.accountId}</code>: <code>{revealedSecret.secret}</code> - copy it
          now, it will not be shown again.
        </p>
      )}

      <h3>Create a service account</h3>
      <div className="biolog-identity-toolbar">
        <input
          type="text"
          aria-label="Display name"
          value={newDisplayName}
          onChange={(e) => setNewDisplayName(e.target.value)}
        />
        <fieldset>
          <legend>Scopes</legend>
          {KNOWN_SCOPES.map((scope) => (
            <label key={scope}>
              <input type="checkbox" checked={newScopes.includes(scope)} onChange={() => toggleScope(scope)} />
              {scope}
            </label>
          ))}
        </fieldset>
        <button type="button" className="biolog-button-primary" disabled={working || newScopes.length === 0} onClick={handleCreate}>
          Create service account
        </button>
      </div>

      {notice && <p>{notice}</p>}
    </section>
  );
}
