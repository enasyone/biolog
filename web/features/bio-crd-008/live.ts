import type {
  CardTemplateActionResult,
  CardTemplateElement,
  CardTemplateLineageSummary,
  CardTemplateSnapshot,
  CardTemplateSnapshotRecord,
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
 * Card-template lifecycle live adapters (BIO-CRD-008-AC05). Like `features/reporting/live.ts`
 * (BIO-RPT-002), `createLiveAdapter` (a fixed zero-arg `POST <path>`) cannot serve these - every
 * action here sends real JSON input or interpolates a real path segment - so each is a
 * hand-written `ModulePort`, the same shape that story established.
 */
const ACTOR_HEADERS = { 'X-Actor-Id': 'web-ui', 'X-Actor-Type': 'user' } as const;
const BASE_PATH = '/api/v1/card-templates';

function toWireElement(element: CardTemplateElement) {
  return {
    id: element.id,
    kind: element.kind,
    label: element.label,
    x_px: element.xPx,
    y_px: element.yPx,
    width_px: element.widthPx,
    height_px: element.heightPx,
    rotation_deg: element.rotationDeg ?? 0,
    z_index: element.zIndex,
    locked: element.locked ?? false,
    font_family: element.fontFamily,
    font_size_px: element.fontSizePx,
    text: element.text,
  };
}

function toWireSnapshot(snapshot: CardTemplateSnapshot) {
  return {
    format_kind: snapshot.formatKind,
    orientation: snapshot.orientation,
    custom_size_mm: snapshot.customSizeMm
      ? { long_edge_mm: snapshot.customSizeMm.longEdgeMm, short_edge_mm: snapshot.customSizeMm.shortEdgeMm }
      : undefined,
    front_elements: snapshot.frontElements.map(toWireElement),
    back_elements: snapshot.backElements.map(toWireElement),
    watermark: snapshot.watermark,
    photo: snapshot.photo,
    barcode: snapshot.barcode,
    signature: snapshot.signature,
  };
}

function fromWireElement(dto: Record<string, unknown>): CardTemplateElement {
  return {
    id: String(dto.id),
    kind: dto.kind as CardTemplateElement['kind'],
    label: String(dto.label),
    xPx: Number(dto.x_px),
    yPx: Number(dto.y_px),
    widthPx: Number(dto.width_px),
    heightPx: Number(dto.height_px),
    rotationDeg: Number(dto.rotation_deg ?? 0),
    zIndex: Number(dto.z_index),
    locked: Boolean(dto.locked),
    fontFamily: dto.font_family as string | undefined,
    fontSizePx: dto.font_size_px as number | undefined,
    text: dto.text as string | undefined,
  };
}

function mapTemplate(body: Record<string, unknown>): CardTemplateSnapshotRecord {
  const snapshot = body.snapshot as Record<string, unknown>;
  const customSizeMm = snapshot.custom_size_mm as { long_edge_mm: number; short_edge_mm: number } | undefined;
  return {
    templateId: String(body.template_id),
    version: Number(body.version),
    revision: Number(body.revision),
    status: body.status as CardTemplateSnapshotRecord['status'],
    name: String(body.name),
    snapshot: {
      formatKind: snapshot.format_kind as CardTemplateSnapshot['formatKind'],
      orientation: snapshot.orientation as CardTemplateSnapshot['orientation'],
      customSizeMm: customSizeMm ? { longEdgeMm: customSizeMm.long_edge_mm, shortEdgeMm: customSizeMm.short_edge_mm } : undefined,
      frontElements: (snapshot.front_elements as Record<string, unknown>[]).map(fromWireElement),
      backElements: (snapshot.back_elements as Record<string, unknown>[]).map(fromWireElement),
      watermark: snapshot.watermark as CardTemplateSnapshot['watermark'],
      photo: snapshot.photo as CardTemplateSnapshot['photo'],
      barcode: snapshot.barcode as CardTemplateSnapshot['barcode'],
      signature: snapshot.signature as CardTemplateSnapshot['signature'],
    },
    createdAt: String(body.created_at),
    updatedAt: String(body.updated_at),
    publishedAt: body.published_at as string | undefined,
  };
}

async function problemMessage(response: Response, fallback: string): Promise<string> {
  try {
    const problem = (await response.json()) as { message?: string };
    return problem.message ?? fallback;
  } catch {
    return fallback;
  }
}

export const cardTemplateCreateDraftLivePort: CreateTemplateDraftPort = {
  module: 'CredentialService',
  mode: 'live',
  async primaryAction(input?: CreateTemplateDraftInput): Promise<CardTemplateActionResult> {
    if (!input) return { outcome: 'failure', message: 'No template draft was provided.' };
    try {
      const response = await fetch(BASE_PATH, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...ACTOR_HEADERS },
        body: JSON.stringify({ name: input.name, snapshot: toWireSnapshot(input.snapshot) }),
      });
      if (!response.ok) {
        return { outcome: 'failure', message: `Create draft failed: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const template = mapTemplate((await response.json()) as Record<string, unknown>);
      return { outcome: 'success', message: `Template ${template.templateId} v${template.version} created as draft (LIVE).`, template };
    } catch {
      return { outcome: 'offline', message: `No live card-template service reachable at ${BASE_PATH} yet.` };
    }
  },
};

export const cardTemplateListLineagesLivePort: ListTemplateLineagesPort = {
  module: 'CredentialService',
  mode: 'live',
  async primaryAction(): Promise<ListTemplateLineagesResult> {
    try {
      const response = await fetch(BASE_PATH, { headers: ACTOR_HEADERS });
      if (!response.ok) {
        return { outcome: 'failure', message: `Listing templates failed: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const body = (await response.json()) as { lineages: Record<string, unknown>[] };
      const lineages: CardTemplateLineageSummary[] = body.lineages.map((l) => ({
        templateId: String(l.template_id),
        name: String(l.name),
        latestVersion: Number(l.latest_version),
        latestStatus: l.latest_status as CardTemplateLineageSummary['latestStatus'],
        publishedVersion: l.published_version as number | undefined,
      }));
      return { outcome: 'success', message: `${lineages.length} template lineage(s) (LIVE).`, lineages };
    } catch {
      return { outcome: 'offline', message: `No live card-template service reachable at ${BASE_PATH} yet.` };
    }
  },
};

/**
 * The backend's own request DTOs use `[JsonPropertyName("expected_revision")]`-style snake_case
 * wire fields (`CardTemplateDtos.cs`), never the frontend's own camelCase `TemplateVersionInput`
 * shape - `toWireBody` builds the exact body each endpoint expects rather than re-serialising
 * `input` as-is (a real bug self-caught: an early draft of this file did exactly that, silently
 * dropping `expected_revision`/`passed`/`note` entirely - every transition failed with a 422
 * `RevisionOnlyRequest`-missing-property error, caught only by the LIVE dual-server proof, never
 * by SIMULATION-mode tests since the mock adapters never round-trip through JSON at all).
 */
function transitionAction<TInput extends TemplateVersionInput>(path: (input: TInput) => string, toWireBody: (input: TInput) => object, verb: string) {
  return async (input: TInput | undefined): Promise<CardTemplateActionResult> => {
    if (!input) return { outcome: 'failure', message: 'No template version was provided.' };
    const url = path(input);
    try {
      const response = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...ACTOR_HEADERS },
        body: JSON.stringify(toWireBody(input)),
      });
      if (!response.ok) {
        return { outcome: 'failure', message: `${verb} failed: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const template = mapTemplate((await response.json()) as Record<string, unknown>);
      return { outcome: 'success', message: `Template ${template.templateId} v${template.version} is now ${template.status} (LIVE).`, template };
    } catch {
      return { outcome: 'offline', message: `No live card-template service reachable at ${url} yet.` };
    }
  };
}

const versionPath = (input: TemplateVersionInput) => `${BASE_PATH}/${encodeURIComponent(input.templateId)}/versions/${input.version}`;
const revisionOnlyBody = (input: TemplateVersionInput) => ({ expected_revision: input.expectedRevision });

export const cardTemplateSubmitForValidationLivePort: SubmitTemplateForValidationPort = {
  module: 'CredentialService',
  mode: 'live',
  primaryAction: transitionAction((i: TemplateVersionInput) => `${versionPath(i)}/submit`, revisionOnlyBody, 'Submit for validation'),
};

export const cardTemplateRecordValidationLivePort: RecordTemplateValidationPort = {
  module: 'CredentialService',
  mode: 'live',
  primaryAction: transitionAction(
    (i: RecordTemplateValidationInput) => `${versionPath(i)}/validate`,
    (i: RecordTemplateValidationInput) => ({ passed: i.passed, note: i.note, expected_revision: i.expectedRevision }),
    'Record validation',
  ),
};

export const cardTemplateRejectLivePort: RejectTemplatePort = {
  module: 'CredentialService',
  mode: 'live',
  primaryAction: transitionAction(
    (i: RejectTemplateInput) => `${versionPath(i)}/reject`,
    (i: RejectTemplateInput) => ({ note: i.note, expected_revision: i.expectedRevision }),
    'Reject',
  ),
};

export const cardTemplatePublishLivePort: PublishTemplatePort = {
  module: 'CredentialService',
  mode: 'live',
  primaryAction: transitionAction((i: TemplateVersionInput) => `${versionPath(i)}/publish`, revisionOnlyBody, 'Publish'),
};

export const cardTemplateRetireLivePort: RetireTemplatePort = {
  module: 'CredentialService',
  mode: 'live',
  primaryAction: transitionAction((i: TemplateVersionInput) => `${versionPath(i)}/retire`, revisionOnlyBody, 'Retire'),
};

export const cardTemplateCreateNextVersionLivePort: CreateNextTemplateVersionPort = {
  module: 'CredentialService',
  mode: 'live',
  async primaryAction(input?: CreateNextTemplateVersionInput): Promise<CardTemplateActionResult> {
    if (!input) return { outcome: 'failure', message: 'No source template version was provided.' };
    const url = `${BASE_PATH}/${encodeURIComponent(input.templateId)}/versions/${input.fromVersion}/new-version`;
    try {
      const response = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json', ...ACTOR_HEADERS }, body: JSON.stringify({}) });
      if (!response.ok) {
        return { outcome: 'failure', message: `Create next version failed: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const template = mapTemplate((await response.json()) as Record<string, unknown>);
      return { outcome: 'success', message: `Template ${template.templateId} v${template.version} created from v${input.fromVersion} (LIVE).`, template };
    } catch {
      return { outcome: 'offline', message: `No live card-template service reachable at ${url} yet.` };
    }
  },
};
