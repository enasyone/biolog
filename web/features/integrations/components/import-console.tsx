'use client';

import { useCallback, useEffect, useState } from 'react';
import { useSession } from '../../../context/session-context';
import { integrationsPort } from '../index';
import type { FieldMappingEntry, ImportBatchResult, ImportRowInput, MappingSummary, SourceSystem, SyncDirection } from '../types';

type LoadState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'offline'; message: string }
  | { status: 'ready'; mappings: readonly MappingSummary[] };

interface EditableRow {
  externalKey: string;
  firstName: string;
  lastName: string;
}

const DEFAULT_FIELD_MAPPINGS: readonly FieldMappingEntry[] = [
  { sourceField: 'ext_first_name', personField: 'first_name' },
  { sourceField: 'ext_last_name', personField: 'last_name' },
];

const DEFAULT_ROWS: readonly EditableRow[] = [
  { externalKey: 'SIS-1001', firstName: 'Ada', lastName: 'Lovelace' },
  { externalKey: 'SIS-1002', firstName: 'Grace', lastName: 'Hopper' },
];

function toRowInputs(rows: readonly EditableRow[]): ImportRowInput[] {
  return rows.map((r) => ({
    externalKey: r.externalKey,
    fields: { ext_first_name: r.firstName, ext_last_name: r.lastName },
  }));
}

/**
 * BIO-INT-002-AC06: the real `/administration/integrations` console - create a versioned
 * source mapping (AC01/AC02), edit a handful of synthetic rows, preview the dry-run conflict
 * breakdown (AC03), then apply it idempotently (AC04/AC05). SYNTHETIC data only, per this
 * story's own constraints - never a real SIS/HR/ERP connection.
 */
export function ImportConsole() {
  const session = useSession();
  const [state, setState] = useState<LoadState>({ status: 'loading' });
  const [selectedMappingId, setSelectedMappingId] = useState<string | null>(null);
  const [rows, setRows] = useState<readonly EditableRow[]>(DEFAULT_ROWS);
  const [batchResult, setBatchResult] = useState<ImportBatchResult | null>(null);
  const [working, setWorking] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  const [newDisplayName, setNewDisplayName] = useState('Main SIS');
  const [newSourceSystem, setNewSourceSystem] = useState<SourceSystem>('sis');
  const [newSyncDirection, setNewSyncDirection] = useState<SyncDirection>('source_is_master');

  const load = useCallback(() => {
    setState({ status: 'loading' });
    integrationsPort
      .listMappings({ tenantId: session.tenant.id })
      .then((result) => {
        setState({ status: 'ready', mappings: result.mappings });
        if (result.mappings.length > 0 && !selectedMappingId) {
          setSelectedMappingId(result.mappings[0]!.id);
        }
      })
      .catch(() => setState({ status: 'error', message: 'Could not load integration mappings - try again.' }));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [session.tenant.id]);

  useEffect(() => {
    load();
  }, [load]);

  async function handleCreateMapping() {
    setWorking(true);
    setNotice(null);
    try {
      const mapping = await integrationsPort.createMapping({
        tenantId: session.tenant.id,
        sourceSystem: newSourceSystem,
        syncDirection: newSyncDirection,
        displayName: newDisplayName,
        fieldMappings: DEFAULT_FIELD_MAPPINGS,
      });
      setNotice(`Created mapping "${mapping.displayName}" (version ${mapping.version}).`);
      setSelectedMappingId(mapping.id);
      load();
    } catch {
      setNotice('Could not create the mapping — the live service may be offline (SIMULATION mode is always reachable).');
    } finally {
      setWorking(false);
    }
  }

  function updateRow(index: number, patch: Partial<EditableRow>) {
    setRows((current) => current.map((r, i) => (i === index ? { ...r, ...patch } : r)));
  }

  function addRow() {
    setRows((current) => [...current, { externalKey: `SIS-${1000 + current.length + 1}`, firstName: '', lastName: '' }]);
  }

  function removeRow(index: number) {
    setRows((current) => current.filter((_, i) => i !== index));
  }

  async function handlePreview() {
    if (!selectedMappingId) return;
    setWorking(true);
    setNotice(null);
    try {
      const result = await integrationsPort.previewImport({
        tenantId: session.tenant.id,
        mappingId: selectedMappingId,
        rows: toRowInputs(rows),
      });
      setBatchResult(result);
    } catch {
      setNotice('Preview failed — the live service may be offline (SIMULATION mode is always reachable).');
    } finally {
      setWorking(false);
    }
  }

  async function handleApply() {
    if (!selectedMappingId) return;
    setWorking(true);
    setNotice(null);
    try {
      const idempotencyKey = `apply-${selectedMappingId}-${Date.now()}`;
      const result = await integrationsPort.applyImport({
        tenantId: session.tenant.id,
        mappingId: selectedMappingId,
        rows: toRowInputs(rows),
        idempotencyKey,
      });
      setBatchResult(result);
      setNotice(result.replayed ? 'Replayed a prior apply for this exact request (idempotent).' : 'Applied.');
    } catch {
      setNotice('Apply failed — the live service may be offline (SIMULATION mode is always reachable).');
    } finally {
      setWorking(false);
    }
  }

  const selectedMapping = state.status === 'ready' ? state.mappings.find((m) => m.id === selectedMappingId) ?? null : null;

  return (
    <div className="biolog-placeholder-page">
      <h1>Integrations</h1>
      <p className="biolog-placeholder-description">
        SIS/HR/ERP/CSV import framework — versioned source mappings, dry-run conflict preview, idempotent apply.
        SYNTHETIC data only.
      </p>

      <section className="biolog-card">
        <h2>Source mappings</h2>
        {state.status === 'loading' && <p role="status">Loading…</p>}
        {state.status === 'error' && <p role="alert">{state.message}</p>}
        {state.status === 'offline' && <p role="alert">{state.message}</p>}
        {state.status === 'ready' && state.mappings.length === 0 && <p>No mappings configured yet for this tenant.</p>}
        {state.status === 'ready' && state.mappings.length > 0 && (
          <label>
            Active mapping:{' '}
            <select value={selectedMappingId ?? ''} onChange={(e) => setSelectedMappingId(e.target.value)}>
              {state.mappings.map((m) => (
                <option key={m.id} value={m.id}>
                  {m.displayName} ({m.sourceSystem}, {m.syncDirection}, v{m.version})
                </option>
              ))}
            </select>
          </label>
        )}

        <h3>Create a new mapping</h3>
        <div className="biolog-identity-toolbar">
          <input
            type="text"
            aria-label="Display name"
            value={newDisplayName}
            onChange={(e) => setNewDisplayName(e.target.value)}
          />
          <select aria-label="Source system" value={newSourceSystem} onChange={(e) => setNewSourceSystem(e.target.value as SourceSystem)}>
            <option value="sis">SIS</option>
            <option value="hr">HR</option>
            <option value="payroll">Payroll</option>
            <option value="erp">ERP</option>
            <option value="csv_excel">CSV/Excel</option>
          </select>
          <select
            aria-label="Sync direction"
            value={newSyncDirection}
            onChange={(e) => setNewSyncDirection(e.target.value as SyncDirection)}
          >
            <option value="source_is_master">Source is master (normal import)</option>
            <option value="biolog_is_master">BIOLOG is master (reject inbound data)</option>
          </select>
          <button type="button" className="biolog-button-primary" disabled={working} onClick={handleCreateMapping}>
            Create mapping
          </button>
        </div>
        <p className="biolog-placeholder-description">
          Every new mapping maps <code>ext_first_name</code>/<code>ext_last_name</code> to PERSON&apos;s own
          <code> first_name</code>/<code>last_name</code> (BIO-INT-002-AC01).
        </p>
      </section>

      {selectedMapping && (
        <section className="biolog-card">
          <h2>Rows to import</h2>
          <table className="biolog-table" aria-label="Rows to import (SYNTHETIC)">
            <thead>
              <tr>
                <th>External key</th>
                <th>First name</th>
                <th>Last name</th>
                <th>
                  <span className="biolog-visually-hidden">Remove</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row, index) => (
                <tr key={index}>
                  <td>
                    <input
                      aria-label={`External key for row ${index + 1}`}
                      value={row.externalKey}
                      onChange={(e) => updateRow(index, { externalKey: e.target.value })}
                    />
                  </td>
                  <td>
                    <input
                      aria-label={`First name for row ${index + 1}`}
                      value={row.firstName}
                      onChange={(e) => updateRow(index, { firstName: e.target.value })}
                    />
                  </td>
                  <td>
                    <input
                      aria-label={`Last name for row ${index + 1}`}
                      value={row.lastName}
                      onChange={(e) => updateRow(index, { lastName: e.target.value })}
                    />
                  </td>
                  <td>
                    <button type="button" className="biolog-button-secondary" onClick={() => removeRow(index)}>
                      Remove
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <div className="biolog-identity-toolbar">
            <button type="button" className="biolog-button-secondary" onClick={addRow}>
              Add row
            </button>
            <button type="button" className="biolog-button-secondary" disabled={working} onClick={handlePreview}>
              Preview (dry-run)
            </button>
            <button type="button" className="biolog-button-primary" disabled={working} onClick={handleApply}>
              Apply
            </button>
          </div>
        </section>
      )}

      {notice && (
        <p role="status" className="biolog-identity-status biolog-identity-status--inline">
          {notice}
        </p>
      )}

      {batchResult && (
        <section className="biolog-card">
          <h2>{batchResult.mode === 'dry_run' ? 'Preview result' : 'Apply result'}</h2>
          {batchResult.rowOutcomes.length === 0 && <p>This integration rejects inbound data (BIOLOG is configured as master).</p>}
          {batchResult.rowOutcomes.length > 0 && (
            <table className="biolog-table" aria-label="Row outcomes">
              <thead>
                <tr>
                  <th>External key</th>
                  <th>Outcome</th>
                  <th>Person id</th>
                  <th>Conflicts</th>
                </tr>
              </thead>
              <tbody>
                {batchResult.rowOutcomes.map((outcome) => (
                  <tr key={outcome.externalKey}>
                    <td>{outcome.externalKey}</td>
                    <td>{outcome.outcome}</td>
                    <td>{outcome.personId ?? '—'}</td>
                    <td>
                      {outcome.conflicts.length === 0
                        ? '—'
                        : outcome.conflicts
                            .map((c) => `${c.personField}: "${c.existingValue}" ≠ "${c.incomingValue}" (not written)`)
                            .join('; ')}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
      )}
    </div>
  );
}
