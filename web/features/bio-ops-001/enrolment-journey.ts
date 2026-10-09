export type EnrolmentStage = 'photo' | 'fingerprint' | 'face' | 'document-verification' | 'card-issue';
export type StageOutcome = 'success' | 'failure' | 'offline';

export interface StageContext {
  readonly tenantId: string;
  readonly personId: string;
  readonly stage: EnrolmentStage;
}

export interface PersistedArtifact extends StageContext {
  readonly artifactReference: string;
}

export interface StageResult {
  readonly outcome: StageOutcome;
  readonly artifactReference?: string;
}

export interface PersistedJourneyEvidence extends PersistedArtifact {
  readonly sequence: number;
}

export interface JourneyBlocker extends StageContext {
  readonly outcome: Exclude<StageOutcome, 'success'>;
}

/** A distinct owning repository: references returned to the journey must resolve here. */
export class LocalArtifactStore {
  readonly #artifacts = new Map<string, PersistedArtifact>();
  #sequence = 0;

  persist(context: StageContext): PersistedArtifact {
    this.#sequence += 1;
    const artifact = Object.freeze({
      ...context,
      artifactReference: `local://${context.stage}/${this.#sequence}`,
    });
    this.#artifacts.set(artifact.artifactReference, artifact);
    return artifact;
  }

  get(artifactReference: string): PersistedArtifact | undefined {
    return this.#artifacts.get(artifactReference);
  }
}

export class EnrolmentJourneyStore {
  readonly #evidence: PersistedJourneyEvidence[] = [];
  readonly #blockers: JourneyBlocker[] = [];

  persist(evidence: PersistedJourneyEvidence): void {
    this.#evidence.push(Object.freeze({ ...evidence }));
  }

  block(blocker: JourneyBlocker): void {
    this.#blockers.push(Object.freeze({ ...blocker }));
  }

  listForPerson(tenantId: string, personId: string): readonly PersistedJourneyEvidence[] {
    return this.#evidence.filter((item) => item.tenantId === tenantId && item.personId === personId);
  }

  blockersForPerson(tenantId: string, personId: string): readonly JourneyBlocker[] {
    return this.#blockers.filter((item) => item.tenantId === tenantId && item.personId === personId);
  }
}

export class EnrolmentJourneyBlockedError extends Error {
  constructor(readonly blocker: JourneyBlocker) {
    super(`${blocker.stage} blocked with ${blocker.outcome}`);
  }
}

export class EnrolmentJourneyService {
  #sequence = 0;

  constructor(
    private readonly store: EnrolmentJourneyStore,
    private readonly artifacts: LocalArtifactStore,
  ) {}

  async executeStage(
    tenantId: string,
    personId: string,
    stage: EnrolmentStage,
    action: (context: StageContext) => Promise<StageResult>,
  ): Promise<PersistedJourneyEvidence> {
    if (!tenantId.trim() || !personId.trim()) throw new Error('tenantId and personId are required');
    const priorBlocker = this.store.blockersForPerson(tenantId, personId)[0];
    if (priorBlocker) throw new EnrolmentJourneyBlockedError(priorBlocker);

    const context = Object.freeze({ tenantId, personId, stage });
    const result = await action(context);
    if (result.outcome !== 'success') {
      const blocker = { ...context, outcome: result.outcome } as const;
      this.store.block(blocker);
      throw new EnrolmentJourneyBlockedError(blocker);
    }
    if (!result.artifactReference) throw new Error(`${stage} returned no persisted artifact reference`);

    const owned = this.artifacts.get(result.artifactReference);
    if (!owned || owned.tenantId !== tenantId || owned.personId !== personId || owned.stage !== stage) {
      throw new Error(`${stage} artifact ownership does not match the journey tenant/PERSON`);
    }

    this.#sequence += 1;
    const evidence = { ...owned, sequence: this.#sequence };
    this.store.persist(evidence);
    return evidence;
  }
}
