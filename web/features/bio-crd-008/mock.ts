import type {
  CardTemplateActionResult,
  CardTemplateLineageSummary,
  CardTemplateSnapshotRecord,
  CardTemplateStatus,
  CreateNextTemplateVersionInput,
  CreateNextTemplateVersionPort,
  CreateTemplateDraftInput,
  CreateTemplateDraftPort,
  ListTemplateLineagesPort,
  ListTemplateLineagesResult,
  PublishTemplatePort,
  RecordTemplateValidationInput,
  RecordTemplateValidationPort,
  RejectTemplateInput,
  RejectTemplatePort,
  RetireTemplatePort,
  SubmitTemplateForValidationPort,
  TemplateVersionInput,
} from './ports';

/**
 * Card-template lifecycle mock adapters (BIO-CRD-008-AC01/AC02/AC05). Same deterministic,
 * real-precondition-checked discipline `web/features/reporting/mock.ts` (BIO-RPT-002) and
 * `web/features/cards/mock.ts` (BIO-UX-007) both already establish: `failure` always reflects a
 * genuine, checkable state against this module's own in-memory store, never an unrelated
 * scripted outcome. Re-implements the backend's own Draft/Validation/Approval/Published/Retired
 * state machine client-side (SIMULATION mode has no server to defer to).
 */
const MISSING_INPUT: CardTemplateActionResult = { outcome: 'failure', message: 'No input was provided to this action.' };

function key(templateId: string, version: number): string {
  return `${templateId}::${version}`;
}

const store = new Map<string, CardTemplateSnapshotRecord>();
let templateCounter = 0;

function nowIso(): string {
  return new Date().toISOString().replace(/\.\d+Z$/, '.000Z');
}

function latestVersion(templateId: string): number {
  let max = 0;
  for (const record of store.values()) {
    if (record.templateId === templateId) max = Math.max(max, record.version);
  }
  return max;
}

function published(templateId: string): CardTemplateSnapshotRecord | undefined {
  for (const record of store.values()) {
    if (record.templateId === templateId && record.status === 'published') return record;
  }
  return undefined;
}

function transition(
  input: TemplateVersionInput | undefined,
  requiredStatus: CardTemplateStatus,
  nextStatus: CardTemplateStatus,
): CardTemplateActionResult {
  if (!input) return MISSING_INPUT;
  const record = store.get(key(input.templateId, input.version));
  if (!record) {
    return { outcome: 'failure', message: `No card template '${input.templateId}' v${input.version} found (SIMULATION).` };
  }
  if (record.revision !== input.expectedRevision) {
    return { outcome: 'failure', message: `Template ${input.templateId} v${input.version} was modified concurrently (SIMULATION).`, template: record };
  }
  if (record.status !== requiredStatus) {
    return { outcome: 'failure', message: `Template ${input.templateId} v${input.version} is ${record.status}, not ${requiredStatus} - cannot transition (SIMULATION).`, template: record };
  }

  const updated: CardTemplateSnapshotRecord = {
    ...record,
    status: nextStatus,
    revision: record.revision + 1,
    updatedAt: nowIso(),
    publishedAt: nextStatus === 'published' ? nowIso() : record.publishedAt,
  };
  store.set(key(input.templateId, input.version), updated);
  return { outcome: 'success', message: `Template ${input.templateId} v${input.version} is now ${nextStatus} (SIMULATION).`, template: updated };
}

export const cardTemplateCreateDraftMockPort: CreateTemplateDraftPort = {
  module: 'CredentialService',
  mode: 'mock',
  async primaryAction(input?: CreateTemplateDraftInput): Promise<CardTemplateActionResult> {
    if (!input) return MISSING_INPUT;
    if (!input.name.trim()) {
      return { outcome: 'failure', message: 'A template needs a name (SIMULATION).' };
    }
    templateCounter += 1;
    const templateId = `tpl-sim-${String(templateCounter).padStart(6, '0')}`;
    const now = nowIso();
    const record: CardTemplateSnapshotRecord = {
      templateId,
      version: 1,
      revision: 1,
      status: 'draft',
      name: input.name,
      snapshot: input.snapshot,
      createdAt: now,
      updatedAt: now,
    };
    store.set(key(templateId, 1), record);
    return { outcome: 'success', message: `Template ${templateId} v1 created as draft (SIMULATION).`, template: record };
  },
};

export const cardTemplateListLineagesMockPort: ListTemplateLineagesPort = {
  module: 'CredentialService',
  mode: 'mock',
  async primaryAction(): Promise<ListTemplateLineagesResult> {
    const byLineage = new Map<string, CardTemplateSnapshotRecord[]>();
    for (const record of store.values()) {
      const list = byLineage.get(record.templateId) ?? [];
      list.push(record);
      byLineage.set(record.templateId, list);
    }
    const lineages: CardTemplateLineageSummary[] = [...byLineage.entries()].map(([templateId, records]) => {
      const latest = records.reduce((a, b) => (b.version > a.version ? b : a));
      const pub = records.find((r) => r.status === 'published');
      return { templateId, name: latest.name, latestVersion: latest.version, latestStatus: latest.status, publishedVersion: pub?.version };
    });
    return { outcome: 'success', message: `${lineages.length} template lineage(s) (SIMULATION).`, lineages };
  },
};

export const cardTemplateSubmitForValidationMockPort: SubmitTemplateForValidationPort = {
  module: 'CredentialService',
  mode: 'mock',
  async primaryAction(input?: TemplateVersionInput): Promise<CardTemplateActionResult> {
    return transition(input, 'draft', 'validation');
  },
};

export const cardTemplateRecordValidationMockPort: RecordTemplateValidationPort = {
  module: 'CredentialService',
  mode: 'mock',
  async primaryAction(input?: RecordTemplateValidationInput): Promise<CardTemplateActionResult> {
    if (!input) return MISSING_INPUT;
    return transition(input, 'validation', input.passed ? 'approval' : 'draft');
  },
};

export const cardTemplateRejectMockPort: RejectTemplatePort = {
  module: 'CredentialService',
  mode: 'mock',
  async primaryAction(input?: RejectTemplateInput): Promise<CardTemplateActionResult> {
    return transition(input, 'approval', 'draft');
  },
};

export const cardTemplatePublishMockPort: PublishTemplatePort = {
  module: 'CredentialService',
  mode: 'mock',
  async primaryAction(input?: TemplateVersionInput): Promise<CardTemplateActionResult> {
    if (!input) return MISSING_INPUT;
    // AC02: only one Published version per lineage - auto-retire whichever one currently holds it.
    const currentlyPublished = published(input.templateId);
    if (currentlyPublished && currentlyPublished.version !== input.version) {
      store.set(key(currentlyPublished.templateId, currentlyPublished.version), {
        ...currentlyPublished,
        status: 'retired',
        revision: currentlyPublished.revision + 1,
        updatedAt: nowIso(),
      });
    }
    return transition(input, 'approval', 'published');
  },
};

export const cardTemplateRetireMockPort: RetireTemplatePort = {
  module: 'CredentialService',
  mode: 'mock',
  async primaryAction(input?: TemplateVersionInput): Promise<CardTemplateActionResult> {
    return transition(input, 'published', 'retired');
  },
};

export const cardTemplateCreateNextVersionMockPort: CreateNextTemplateVersionPort = {
  module: 'CredentialService',
  mode: 'mock',
  async primaryAction(input?: CreateNextTemplateVersionInput): Promise<CardTemplateActionResult> {
    if (!input) return MISSING_INPUT;
    const source = store.get(key(input.templateId, input.fromVersion));
    if (!source) {
      return { outcome: 'failure', message: `No card template '${input.templateId}' v${input.fromVersion} found (SIMULATION).` };
    }
    if (source.status !== 'published' && source.status !== 'retired') {
      return { outcome: 'failure', message: `Template ${input.templateId} v${input.fromVersion} is ${source.status} - only a published or retired version can start a new one (SIMULATION).`, template: source };
    }
    const nextVersion = latestVersion(input.templateId) + 1;
    const now = nowIso();
    const record: CardTemplateSnapshotRecord = {
      templateId: input.templateId,
      version: nextVersion,
      revision: 1,
      status: 'draft',
      name: source.name,
      snapshot: source.snapshot,
      createdAt: now,
      updatedAt: now,
    };
    store.set(key(input.templateId, nextVersion), record);
    return { outcome: 'success', message: `Template ${input.templateId} v${nextVersion} created from v${input.fromVersion} (SIMULATION).`, template: record };
  },
};
