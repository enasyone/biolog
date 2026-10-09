// Developer tests for BIO-UX-007 AC01/AC03/AC04. SYNTHETIC: no real tenant/user data.
import { beforeEach, describe, expect, it } from 'vitest';
import {
  getCardsDataset,
  issueCard,
  listCredentials,
  markCardLost,
  recoverCard,
  replaceCard,
  resetCardsData,
  revokeCard,
} from '../../../web/features/cards/data-store';

const TENANT = 'alpha';

beforeEach(() => {
  resetCardsData();
});

describe('BIO-UX-007-AC01 issueCard', () => {
  it('issues an active card for a person with none', () => {
    // The baseline dataset generates one active credential per person, so this
    // exercises real code by first revoking a person's existing card (a
    // legitimate way to reach "no active card"), then issuing a fresh one.
    const dataset = getCardsDataset();
    const person = dataset.people.find((p) => p.tenantId === TENANT)!;
    revokeCard(TENANT, dataset.credentials.find((c) => c.personId === person.id)!.id, 'test setup');

    const result = issueCard(TENANT, person.id);
    expect(result.ok).toBe(true);
    expect(result.credential!.status).toBe('active');
    expect(result.credential!.personId).toBe(person.id);
  });

  it('refuses to issue a second active card for the same person', () => {
    const dataset = getCardsDataset();
    const person = dataset.people.find((p) => p.tenantId === TENANT)!;
    const result = issueCard(TENANT, person.id);
    expect(result.ok).toBe(false);
    expect(result.reason).toMatch(/already holds an active card/);
  });
});

describe('BIO-UX-007-AC03 lost/recovered/replaced/revoked transitions', () => {
  it('active -> lost -> recovered round trip', () => {
    const cardId = listCredentials(TENANT)[0]!.id;
    const lost = markCardLost(TENANT, cardId, 'left on bus');
    expect(lost.ok).toBe(true);
    expect(lost.credential!.status).toBe('lost');
    expect(lost.credential!.statusReason).toBe('left on bus');

    const recovered = recoverCard(TENANT, cardId);
    expect(recovered.ok).toBe(true);
    expect(recovered.credential!.status).toBe('recovered');
    expect(recovered.credential!.statusReason).toBeUndefined();
  });

  it('refuses to recover a card that was never lost', () => {
    const cardId = listCredentials(TENANT)[0]!.id;
    const result = recoverCard(TENANT, cardId);
    expect(result.ok).toBe(false);
    expect(result.reason).toMatch(/not lost/);
  });

  it('refuses to mark an already-lost card lost again', () => {
    const cardId = listCredentials(TENANT)[0]!.id;
    markCardLost(TENANT, cardId);
    const result = markCardLost(TENANT, cardId);
    expect(result.ok).toBe(false);
  });

  it('replace retires the old card and issues a linked new one', () => {
    const oldCard = listCredentials(TENANT)[0]!;
    const result = replaceCard(TENANT, oldCard.id, 'damaged');
    expect(result.ok).toBe(true);
    const newCard = result.credential!;
    expect(newCard.status).toBe('active');
    expect(newCard.replacesCardId).toBe(oldCard.id);
    expect(newCard.personId).toBe(oldCard.personId);
    expect(newCard.id).not.toBe(oldCard.id);

    const oldAfter = listCredentials(TENANT).find((c) => c.id === oldCard.id)!;
    expect(oldAfter.status).toBe('replaced');
    expect(oldAfter.statusReason).toBe('damaged');
    expect(oldAfter.replacedByCardId).toBe(newCard.id);
  });

  it('refuses to replace an already-replaced card', () => {
    const oldCard = listCredentials(TENANT)[0]!;
    replaceCard(TENANT, oldCard.id, 'damaged');
    const result = replaceCard(TENANT, oldCard.id, 'damaged again');
    expect(result.ok).toBe(false);
  });

  it('revoke is terminal and refuses a second revoke', () => {
    const cardId = listCredentials(TENANT)[0]!.id;
    const first = revokeCard(TENANT, cardId, 'disciplinary');
    expect(first.ok).toBe(true);
    expect(first.credential!.status).toBe('revoked');
    const second = revokeCard(TENANT, cardId, 'disciplinary');
    expect(second.ok).toBe(false);
  });

  it('a lost card can still be revoked directly (not only recovered/replaced)', () => {
    const cardId = listCredentials(TENANT)[0]!.id;
    markCardLost(TENANT, cardId);
    const result = revokeCard(TENANT, cardId, 'security concern');
    expect(result.ok).toBe(true);
    expect(result.credential!.status).toBe('revoked');
  });
});

describe('BIO-UX-007 tenant isolation', () => {
  it('mutations for one tenant never touch another tenant\'s credentials', () => {
    const otherTenantCard = listCredentials('beta')[0]!;
    const result = markCardLost(TENANT, otherTenantCard.id);
    expect(result.ok).toBe(false);
    expect(result.reason).toMatch(/No credential/);
    expect(listCredentials('beta').find((c) => c.id === otherTenantCard.id)!.status).toBe('active');
  });
});

describe('BIO-UX-007-AC04 credential history is durable, not overwritten', () => {
  it('replacing a card twice chains replacesCardId/replacedByCardId correctly', () => {
    const first = listCredentials(TENANT)[0]!;
    const secondResult = replaceCard(TENANT, first.id, 'lost');
    const second = secondResult.credential!;
    const thirdResult = replaceCard(TENANT, second.id, 'damaged');
    const third = thirdResult.credential!;

    expect(third.replacesCardId).toBe(second.id);
    const secondAfter = listCredentials(TENANT).find((c) => c.id === second.id)!;
    expect(secondAfter.status).toBe('replaced');
    expect(secondAfter.replacedByCardId).toBe(third.id);
    const firstAfter = listCredentials(TENANT).find((c) => c.id === first.id)!;
    expect(firstAfter.status).toBe('replaced');
    expect(firstAfter.replacedByCardId).toBe(second.id);
  });
});
