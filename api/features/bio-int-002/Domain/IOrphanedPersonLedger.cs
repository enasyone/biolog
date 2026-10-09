namespace Biolog.Features.Integrations.Domain;

/// <summary>
/// BIO-INT-002-F01's own remediation (Zainab round-1 FAIL, ZAINAB-INT-002-F01). Mirrors the exact
/// "adoption" backstop BIO-VIS-002 established for its own analogous problem (its F03/F04 remediation
/// of <c>VisitorCredentialService</c>): a resource can be durably created with no durable local
/// reference to it yet, and the step that would have recorded that reference can itself fail.
///
/// <b>The gap this closes.</b> <see cref="ImportBatchService.ApplyAsync"/> creates a brand-new PERSON
/// via <c>IPersonGateway.CreateAsync</c> for a <see cref="RowOutcome.Created"/> row, then immediately
/// creates an <see cref="ExternalIdentityLink"/> pointing at it. BIO-IDN-001's PERSON aggregate has no
/// delete - so if the PERSON create durably succeeds but the immediately-following link write then
/// fails (or the idempotency claim is released for any other reason before the link is durably
/// recorded), a retry's own <c>ClassifyRowAsync</c> finds no link for that external key, classifies
/// the row as <see cref="RowOutcome.Created"/> AGAIN, and creates a SECOND, different, orphaned
/// PERSON for the same external identity. Every retry compounds another orphan.
///
/// <b>The fix.</b> <see cref="RecordAsync"/> is called immediately once a new PERSON's id is known,
/// BEFORE the link-write that could fail - the same "attach the durable reference before the next
/// risky step" discipline BIO-VIS-002's own card-sequence attachment uses. A retry's row processing
/// checks <see cref="FindAsync"/> FIRST: if an entry already exists for this exact
/// (tenant, mapping, external key), it ADOPTS that PersonId (creates the link against it) instead of
/// minting a second PERSON. "Resolved" here just means a link now exists for that key - there is
/// nothing further to reconcile once that happens, unlike BIO-VIS-002's ledger (which also tracks a
/// physical-access device's own live state); this ledger only ever needs to answer "was a PERSON
/// already created for this external key that a link write never confirmed."
///
/// <b>Where this proportionately stops.</b> Same stance as every other in-memory store in this
/// codebase (a dictionary, no external dependency, no backstop-for-the-backstop regress) - if THIS
/// write also failed, there is nothing further this story's own scope can reasonably do.
/// </summary>
internal interface IOrphanedPersonLedger
{
    Task RecordAsync(string tenantId, string mappingId, string externalKey, string personId, CancellationToken cancellationToken = default);

    Task<string?> FindAsync(string tenantId, string mappingId, string externalKey, CancellationToken cancellationToken = default);
}
