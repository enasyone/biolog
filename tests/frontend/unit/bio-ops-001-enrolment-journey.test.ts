// BIO-OPS-001-AC01-AC05: local-only synthetic enrolment acceptance journey.
import { describe, expect, it, vi } from 'vitest';
import { identityMockPort } from '../../../web/features/identity/mock';
import { photosMockPort } from '../../../web/features/photos/mock';
import { biometricsMockPort } from '../../../web/features/biometrics/mock';
import { runEvidenceScenario } from '../../../web/features/documents/evidence';
import { cardsIssueMockPort } from '../../../web/features/cards/mock';
import { resetCardsData } from '../../../web/features/cards/data-store';
import {
  EnrolmentJourneyBlockedError,
  EnrolmentJourneyService,
  EnrolmentJourneyStore,
  LocalArtifactStore,
  type StageContext,
} from '../../../web/features/bio-ops-001/enrolment-journey';

describe('BIO-OPS-001 local end-to-end enrolment acceptance journey', () => {
  it('AC01/AC02/AC03 persists every produced artifact against one tenant/PERSON', async () => {
    resetCardsData();
    const tenantId = 'tenant-ops-001-synthetic';
    const enrolment = await identityMockPort.submitEnrolment({
      tenantId, firstName: 'Ada', lastName: 'Synthetic', dateOfBirth: '2000-01-01', category: 'student',
      photoCaptured: false, biometricCaptured: false, credentialAssigned: false,
    });
    expect(enrolment.outcome).toBe('success');
    const personId = enrolment.personId!;
    const store = new EnrolmentJourneyStore();
    const artifacts = new LocalArtifactStore();
    const journey = new EnrolmentJourneyService(store, artifacts);

    await journey.executeStage(tenantId, personId, 'photo', async (context) => {
      const result = await photosMockPort.primaryAction();
      const artifact = result.outcome === 'success' ? artifacts.persist(context) : undefined;
      return { outcome: result.outcome, artifactReference: artifact?.artifactReference };
    });
    await journey.executeStage(tenantId, personId, 'fingerprint', async (context) => {
      const result = await biometricsMockPort.captureFingerprint('right_index', 'good');
      const outcome = result.outcome === 'good' ? 'success' : 'failure';
      const artifact = outcome === 'success' ? artifacts.persist(context) : undefined;
      return { outcome, artifactReference: artifact?.artifactReference };
    });
    await journey.executeStage(tenantId, personId, 'face', async (context) => {
      const result = await biometricsMockPort.captureFace('good');
      const outcome = result.outcome === 'good' ? 'success' : 'failure';
      const artifact = outcome === 'success' ? artifacts.persist(context) : undefined;
      return { outcome, artifactReference: artifact?.artifactReference };
    });
    await journey.executeStage(tenantId, personId, 'document-verification', async (context) => {
      const result = runEvidenceScenario('authenticity', 2);
      const artifact = result.outcome === 'success' ? artifacts.persist(context) : undefined;
      return { outcome: result.outcome, artifactReference: artifact?.artifactReference };
    });
    await journey.executeStage(tenantId, personId, 'card-issue', async (context) => {
      const result = await cardsIssueMockPort.primaryAction({ tenantId, personId });
      const artifact = result.outcome === 'success' ? artifacts.persist(context) : undefined;
      return { outcome: result.outcome, artifactReference: artifact?.artifactReference };
    });

    const persisted = store.listForPerson(tenantId, personId);
    expect(persisted.map((item) => item.stage)).toEqual([
      'photo', 'fingerprint', 'face', 'document-verification', 'card-issue',
    ]);
    expect(persisted.every((item) => item.tenantId === tenantId && item.personId === personId)).toBe(true);
    expect(persisted.every((item) => item.artifactReference.length > 0)).toBe(true);
    expect(store.blockersForPerson(tenantId, personId)).toEqual([]);
  });

  it.each(['failure', 'offline'] as const)('AC05 %s stops the journey and card issue is never invoked', async (outcome) => {
    const store = new EnrolmentJourneyStore();
    const artifacts = new LocalArtifactStore();
    const journey = new EnrolmentJourneyService(store, artifacts);
    const issueCard = vi.fn(async (context: StageContext) => ({ outcome: 'success' as const, artifactReference: artifacts.persist(context).artifactReference }));

    await expect(journey.executeStage('tenant-a', 'per_synthetic', 'document-verification', async () => ({ outcome })))
      .rejects.toBeInstanceOf(EnrolmentJourneyBlockedError);
    await expect(journey.executeStage('tenant-a', 'per_synthetic', 'card-issue', issueCard))
      .rejects.toBeInstanceOf(EnrolmentJourneyBlockedError);

    expect(store.listForPerson('tenant-a', 'per_synthetic')).toEqual([]);
    expect(store.blockersForPerson('tenant-a', 'per_synthetic')).toEqual([
      { tenantId: 'tenant-a', personId: 'per_synthetic', stage: 'document-verification', outcome },
    ]);
    expect(issueCard).not.toHaveBeenCalled();
  });

  it('AC03 rejects a reference owned by a different PERSON', async () => {
    const store = new EnrolmentJourneyStore();
    const artifacts = new LocalArtifactStore();
    const journey = new EnrolmentJourneyService(store, artifacts);
    const wrongOwner = artifacts.persist({ tenantId: 'tenant-a', personId: 'per_other', stage: 'photo' });

    await expect(journey.executeStage('tenant-a', 'per_expected', 'photo', async () => ({
      outcome: 'success', artifactReference: wrongOwner.artifactReference,
    }))).rejects.toThrow('artifact ownership does not match');
    expect(store.listForPerson('tenant-a', 'per_expected')).toEqual([]);
  });
});
