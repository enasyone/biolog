'use client';

import { useState } from 'react';
import { StatusBadge, type StatusBadgeTone } from '../../components/status-badge';
import { searchAuditEventsPort, exportAuditEventsPort, type AuditInvestigationFilterInput, type AuditInvestigationRecord } from '.';

const ACTOR_TYPES = ['user', 'service_account', 'device', 'system', 'anonymous', 'unknown'] as const;

const CLASSIFICATION_TONE: Readonly<Record<string, StatusBadgeTone>> = {
  public: 'neutral',
  internal: 'info',
  personal: 'error',
  sensitive: 'error',
  secret: 'error',
};

type SearchState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'success'; message: string; records: readonly AuditInvestigationRecord[]; totalCount: number }
  | { status: 'error'; message: string };

type ExportState = { status: 'idle' } | { status: 'loading' } | { status: 'done'; message: string; outcome: 'success' | 'failure' | 'offline' };

/**
 * BIO-RPT-003-AC01-AC05: searchable audit investigation view, mounted on `/audit` ALONGSIDE (not
 * replacing) BIO-RPT-001's own "Recent access events" card and its `viewAuditTrail` button - both
 * remain untouched. Filters: actor/person/action/time/device/reason (AC01). No mutation action of
 * any kind is offered (AC02). Export is a real network call through `exportAuditEventsPort`,
 * permissioned and audited server-side (AC03); a 403 surfaces here as an ordinary error message,
 * not a broken button. `classification`/`withheld` render per record so a viewer can see when
 * detail was withheld (AC04) without this component re-implementing that decision itself. Reuses
 * `.biolog-op-form`/`.biolog-op-field`/`.biolog-op-actions` (BIO-UX-010's own filter-form pattern,
 * already the approved visual language for a filter-and-list screen) rather than inventing new
 * classes.
 */
export default function AuditInvestigationPanel() {
  const [filter, setFilter] = useState<AuditInvestigationFilterInput>({});
  const [search, setSearch] = useState<SearchState>({ status: 'idle' });
  const [exportState, setExportState] = useState<ExportState>({ status: 'idle' });

  function updateFilter<K extends keyof AuditInvestigationFilterInput>(key: K, value: string) {
    setFilter((current) => ({ ...current, [key]: value || undefined }));
  }

  async function runSearch() {
    setSearch({ status: 'loading' });
    const result = await searchAuditEventsPort.primaryAction(filter);
    if (result.outcome === 'success') {
      setSearch({ status: 'success', message: result.message, records: result.records ?? [], totalCount: result.totalCount ?? 0 });
    } else {
      setSearch({ status: 'error', message: result.message });
    }
  }

  async function runExport() {
    setExportState({ status: 'loading' });
    const result = await exportAuditEventsPort.primaryAction(filter);
    if (result.outcome === 'success' && result.csv) {
      const blob = new Blob([result.csv], { type: 'text/csv' });
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = 'audit-investigation-export.csv';
      link.click();
      URL.revokeObjectURL(url);
    }
    setExportState({ status: 'done', message: result.message, outcome: result.outcome });
  }

  return (
    <section className="biolog-card" data-testid="audit-investigation-panel">
      <h2>
        Investigate audit events{' '}
        <StatusBadge tone={searchAuditEventsPort.mode === 'live' ? 'success' : 'neutral'}>
          {searchAuditEventsPort.mode === 'live' ? 'LIVE' : 'SIMULATION'}
        </StatusBadge>
      </h2>
      <p className="biolog-placeholder-description">Searchable, read-only view across every feature&apos;s own audit trail (SYNTHETIC).</p>

      <form
        className="biolog-op-form"
        onSubmit={(event) => {
          event.preventDefault();
          void runSearch();
        }}
      >
        <label className="biolog-op-field">
          <span>Actor type</span>
          <select aria-label="Actor type" value={filter.actorType ?? ''} onChange={(event) => updateFilter('actorType', event.target.value)}>
            <option value="">Any</option>
            {ACTOR_TYPES.map((type) => (
              <option key={type} value={type}>
                {type}
              </option>
            ))}
          </select>
        </label>
        <label className="biolog-op-field">
          <span>Actor id</span>
          <input aria-label="Actor id" value={filter.actorId ?? ''} onChange={(event) => updateFilter('actorId', event.target.value)} />
        </label>
        <label className="biolog-op-field">
          <span>Person id</span>
          <input aria-label="Person id" value={filter.personId ?? ''} onChange={(event) => updateFilter('personId', event.target.value)} />
        </label>
        <label className="biolog-op-field">
          <span>Action</span>
          <input aria-label="Action" value={filter.action ?? ''} onChange={(event) => updateFilter('action', event.target.value)} />
        </label>
        <label className="biolog-op-field">
          <span>Device id</span>
          <input aria-label="Device id" value={filter.deviceId ?? ''} onChange={(event) => updateFilter('deviceId', event.target.value)} />
        </label>
        <label className="biolog-op-field">
          <span>Reason code</span>
          <input aria-label="Reason code" value={filter.reasonCode ?? ''} onChange={(event) => updateFilter('reasonCode', event.target.value)} />
        </label>
        <label className="biolog-op-field">
          <span>Occurred from</span>
          <input
            aria-label="Occurred from"
            placeholder="2026-09-01T00:00:00.000Z"
            value={filter.occurredFrom ?? ''}
            onChange={(event) => updateFilter('occurredFrom', event.target.value)}
          />
        </label>
        <label className="biolog-op-field">
          <span>Occurred to</span>
          <input
            aria-label="Occurred to"
            placeholder="2026-09-30T00:00:00.000Z"
            value={filter.occurredTo ?? ''}
            onChange={(event) => updateFilter('occurredTo', event.target.value)}
          />
        </label>

        <div className="biolog-op-actions">
          <button type="submit" className="biolog-button-primary" disabled={search.status === 'loading'} data-testid="audit-investigation-search">
            {search.status === 'loading' ? 'Searching…' : 'Search'}
          </button>
          <button
            type="button"
            className="biolog-button-secondary"
            onClick={runExport}
            disabled={exportState.status === 'loading'}
            data-testid="audit-investigation-export"
          >
            {exportState.status === 'loading' ? 'Exporting…' : 'Export CSV'}
          </button>
        </div>
      </form>

      {search.status === 'error' && <p className={`biolog-placeholder-status biolog-placeholder-status--failure`}>{search.message}</p>}
      {exportState.status === 'done' && (
        <p className={`biolog-placeholder-status biolog-placeholder-status--${exportState.outcome === 'success' ? 'success' : 'failure'}`}>
          {exportState.message}
        </p>
      )}

      {search.status === 'success' && (
        <>
          <p data-testid="audit-investigation-summary">{search.message}</p>
          {search.records.length === 0 ? (
            <p className="biolog-op-empty">No matching audit events (SYNTHETIC).</p>
          ) : (
            <table className="biolog-table" aria-label="Audit investigation results (SYNTHETIC)" tabIndex={0}>
              <thead>
                <tr>
                  <th scope="col">Time</th>
                  <th scope="col">Source</th>
                  <th scope="col">Actor</th>
                  <th scope="col">Person</th>
                  <th scope="col">Action</th>
                  <th scope="col">Reason</th>
                  <th scope="col">Classification</th>
                </tr>
              </thead>
              <tbody>
                {search.records.map((record) => (
                  <tr key={record.eventId}>
                    <td>{record.occurredAt}</td>
                    <td>{record.sourceFeature}</td>
                    <td>
                      {record.actor.type}:{record.actor.id}
                    </td>
                    <td>{record.personId ?? '—'}</td>
                    <td>{record.action}</td>
                    <td>{record.reasonCode}</td>
                    <td>
                      <StatusBadge tone={CLASSIFICATION_TONE[record.classification] ?? 'neutral'}>{record.classification}</StatusBadge>
                      {record.withheld.length > 0 && <span className="biolog-op-empty"> ({record.withheld.length} field(s) withheld)</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </>
      )}
    </section>
  );
}
