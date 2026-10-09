// Developer tests for BIO-CRD-008-AC01, AC02. SYNTHETIC fixed data only. Boundary behaviours a
// Playwright click-through cannot reach cleanly (an unknown template id, a stale-revision
// concurrency conflict, an out-of-order transition attempt) - reproduced at the port level
// directly, the same way bio-rpt-002-reporting-mock.test.ts does for its own module.
import { describe, expect, it } from 'vitest';
import {
  cardTemplateCreateDraftMockPort,
  cardTemplateCreateNextVersionMockPort,
  cardTemplateListLineagesMockPort,
  cardTemplatePublishMockPort,
  cardTemplateRecordValidationMockPort,
  cardTemplateRejectMockPort,
  cardTemplateRetireMockPort,
  cardTemplateSubmitForValidationMockPort,
} from '../../../web/features/bio-crd-008/mock';
import type { CardTemplateSnapshot } from '../../../web/features/bio-crd-008/ports';

const MINIMAL_SNAPSHOT: CardTemplateSnapshot = {
  formatKind: 'cr80',
  orientation: 'portrait',
  frontElements: [],
  backElements: [],
  watermark: 'staff',
  photo: { required: false },
  barcode: 'qr',
  signature: { required: false },
};

describe('BIO-CRD-008 card-template lifecycle mock ports', () => {
  it('AC01 a missing input is a handled failure, not a thrown exception, for every action', async () => {
    await expect(cardTemplateCreateDraftMockPort.primaryAction(undefined)).resolves.toMatchObject({ outcome: 'failure' });
    await expect(cardTemplateSubmitForValidationMockPort.primaryAction(undefined)).resolves.toMatchObject({ outcome: 'failure' });
    await expect(cardTemplateRecordValidationMockPort.primaryAction(undefined)).resolves.toMatchObject({ outcome: 'failure' });
    await expect(cardTemplateRejectMockPort.primaryAction(undefined)).resolves.toMatchObject({ outcome: 'failure' });
    await expect(cardTemplatePublishMockPort.primaryAction(undefined)).resolves.toMatchObject({ outcome: 'failure' });
    await expect(cardTemplateRetireMockPort.primaryAction(undefined)).resolves.toMatchObject({ outcome: 'failure' });
    await expect(cardTemplateCreateNextVersionMockPort.primaryAction(undefined)).resolves.toMatchObject({ outcome: 'failure' });
  });

  it('AC01 the full happy path: draft -> validation -> approval -> published -> retired', async () => {
    const created = await cardTemplateCreateDraftMockPort.primaryAction({ name: 'Staff ID v1', snapshot: MINIMAL_SNAPSHOT });
    expect(created.outcome).toBe('success');
    const t0 = created.template!;
    expect(t0.status).toBe('draft');

    const submitted = await cardTemplateSubmitForValidationMockPort.primaryAction({ templateId: t0.templateId, version: t0.version, expectedRevision: t0.revision });
    expect(submitted.template!.status).toBe('validation');

    const approved = await cardTemplateRecordValidationMockPort.primaryAction({ templateId: t0.templateId, version: t0.version, expectedRevision: submitted.template!.revision, passed: true });
    expect(approved.template!.status).toBe('approval');

    const published = await cardTemplatePublishMockPort.primaryAction({ templateId: t0.templateId, version: t0.version, expectedRevision: approved.template!.revision });
    expect(published.outcome).toBe('success');
    expect(published.template!.status).toBe('published');

    const retired = await cardTemplateRetireMockPort.primaryAction({ templateId: t0.templateId, version: t0.version, expectedRevision: published.template!.revision });
    expect(retired.template!.status).toBe('retired');
  });

  it('AC01 a published template refuses a second publish - immutability enforced, not silently accepted', async () => {
    const created = await cardTemplateCreateDraftMockPort.primaryAction({ name: 'Immutable test', snapshot: MINIMAL_SNAPSHOT });
    const t0 = created.template!;
    const submitted = await cardTemplateSubmitForValidationMockPort.primaryAction({ templateId: t0.templateId, version: t0.version, expectedRevision: t0.revision });
    const approved = await cardTemplateRecordValidationMockPort.primaryAction({ templateId: t0.templateId, version: t0.version, expectedRevision: submitted.template!.revision, passed: true });
    const published = await cardTemplatePublishMockPort.primaryAction({ templateId: t0.templateId, version: t0.version, expectedRevision: approved.template!.revision });

    const secondPublish = await cardTemplatePublishMockPort.primaryAction({ templateId: t0.templateId, version: t0.version, expectedRevision: published.template!.revision });
    expect(secondPublish.outcome).toBe('failure');
  });

  it('AC02 creating a next version from a published template makes a brand-new draft row, the source untouched', async () => {
    const created = await cardTemplateCreateDraftMockPort.primaryAction({ name: 'v1', snapshot: MINIMAL_SNAPSHOT });
    const t0 = created.template!;
    const submitted = await cardTemplateSubmitForValidationMockPort.primaryAction({ templateId: t0.templateId, version: t0.version, expectedRevision: t0.revision });
    const approved = await cardTemplateRecordValidationMockPort.primaryAction({ templateId: t0.templateId, version: t0.version, expectedRevision: submitted.template!.revision, passed: true });
    const published = await cardTemplatePublishMockPort.primaryAction({ templateId: t0.templateId, version: t0.version, expectedRevision: approved.template!.revision });

    const next = await cardTemplateCreateNextVersionMockPort.primaryAction({ templateId: t0.templateId, fromVersion: t0.version });
    expect(next.outcome).toBe('success');
    expect(next.template!.version).toBe(2);
    expect(next.template!.status).toBe('draft');

    const lineages = await cardTemplateListLineagesMockPort.primaryAction();
    const lineage = lineages.lineages!.find((l) => l.templateId === t0.templateId)!;
    expect(lineage.publishedVersion).toBe(1); // v1 still published - v2 is a separate, unpublished draft
    expect(published.template!.status).toBe('published'); // the source object itself never mutated
  });

  it("AC02 publishing a new version auto-retires the lineage's previously published one", async () => {
    const created = await cardTemplateCreateDraftMockPort.primaryAction({ name: 'v1', snapshot: MINIMAL_SNAPSHOT });
    const t0 = created.template!;
    let t = await cardTemplateSubmitForValidationMockPort.primaryAction({ templateId: t0.templateId, version: t0.version, expectedRevision: t0.revision });
    t = await cardTemplateRecordValidationMockPort.primaryAction({ templateId: t0.templateId, version: t0.version, expectedRevision: t.template!.revision, passed: true });
    t = await cardTemplatePublishMockPort.primaryAction({ templateId: t0.templateId, version: t0.version, expectedRevision: t.template!.revision });

    const next = await cardTemplateCreateNextVersionMockPort.primaryAction({ templateId: t0.templateId, fromVersion: t0.version });
    const v2 = next.template!;
    let t2 = await cardTemplateSubmitForValidationMockPort.primaryAction({ templateId: v2.templateId, version: v2.version, expectedRevision: v2.revision });
    t2 = await cardTemplateRecordValidationMockPort.primaryAction({ templateId: v2.templateId, version: v2.version, expectedRevision: t2.template!.revision, passed: true });
    t2 = await cardTemplatePublishMockPort.primaryAction({ templateId: v2.templateId, version: v2.version, expectedRevision: t2.template!.revision });
    expect(t2.template!.status).toBe('published');

    const lineages = await cardTemplateListLineagesMockPort.primaryAction();
    const lineage = lineages.lineages!.find((l) => l.templateId === t0.templateId)!;
    expect(lineage.publishedVersion).toBe(2); // only the newer version is published now
  });

  it('a validation-failed record sends the template back to draft, not a dead end', async () => {
    const created = await cardTemplateCreateDraftMockPort.primaryAction({ name: 'rework', snapshot: MINIMAL_SNAPSHOT });
    const t0 = created.template!;
    const submitted = await cardTemplateSubmitForValidationMockPort.primaryAction({ templateId: t0.templateId, version: t0.version, expectedRevision: t0.revision });
    const failed = await cardTemplateRecordValidationMockPort.primaryAction({ templateId: t0.templateId, version: t0.version, expectedRevision: submitted.template!.revision, passed: false });
    expect(failed.template!.status).toBe('draft');
  });

  it('an unknown template id is refused honestly, not treated as any default template', async () => {
    const result = await cardTemplateSubmitForValidationMockPort.primaryAction({ templateId: 'tpl-sim-does-not-exist', version: 1, expectedRevision: 1 });
    expect(result.outcome).toBe('failure');
  });

  it('a stale expected revision is a concurrency conflict, not a silent overwrite', async () => {
    const created = await cardTemplateCreateDraftMockPort.primaryAction({ name: 'concurrency', snapshot: MINIMAL_SNAPSHOT });
    const t0 = created.template!;
    await cardTemplateSubmitForValidationMockPort.primaryAction({ templateId: t0.templateId, version: t0.version, expectedRevision: t0.revision });

    const stale = await cardTemplateSubmitForValidationMockPort.primaryAction({ templateId: t0.templateId, version: t0.version, expectedRevision: t0.revision });
    expect(stale.outcome).toBe('failure');
  });
});
