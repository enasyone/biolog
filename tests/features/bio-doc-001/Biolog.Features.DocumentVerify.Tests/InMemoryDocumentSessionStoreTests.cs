using Biolog.Features.DocumentVerify.Model;
using Biolog.Features.DocumentVerify.Storage;
using Xunit;

namespace Biolog.Features.DocumentVerify.Tests;

/// <summary>Integration-style developer tests exercising <see cref="InMemoryDocumentSessionStore"/>
/// end to end across BIO-DOC-001-AC01 through AC05, including the ADR-0002 cross-tenant
/// not-found obligation every story touching tenant data ships.</summary>
public sealed class InMemoryDocumentSessionStoreTests
{
    private const string TenantA = "ten_a";
    private const string TenantB = "ten_b";
    private const string T0 = "2026-09-21T12:00:00.000Z";
    private const string T1 = "2026-09-21T12:00:01.000Z";
    private const string T2 = "2026-09-21T12:00:02.000Z";
    private static readonly string ValidHash = new('a', 64);

    private static InMemoryDocumentSessionStore NewStore() => new();

    [Fact]
    public void AC01_creates_a_pre_person_session_and_later_links_it()
    {
        var store = NewStore();
        var session = store.CreateSession("dcs_1", TenantA, personId: null, T0);
        Assert.Null(session.PersonId);

        var linked = store.LinkPerson(TenantA, "dcs_1", "per_1", session.Version, T1);

        Assert.Equal("per_1", linked.PersonId);
        Assert.Equal(DocumentSessionStatus.PersonLinked, linked.Status);
    }

    [Fact]
    public void ADR0002_S4_3_a_session_id_that_belongs_to_a_different_tenant_is_reported_as_not_found_not_forbidden()
    {
        var store = NewStore();
        store.CreateSession("dcs_1", TenantA, personId: null, T0);

        var ex = Assert.Throws<DocumentSessionNotFoundException>(() => store.GetSession(TenantB, "dcs_1"));
        Assert.Equal("dcs_1", ex.SessionId);
    }

    [Fact]
    public void ADR0002_S4_3_a_genuinely_unknown_session_id_reports_the_identical_not_found_error()
    {
        var store = NewStore();

        var ex = Assert.Throws<DocumentSessionNotFoundException>(() => store.GetSession(TenantA, "dcs_missing"));
        Assert.Equal("dcs_missing", ex.SessionId);
    }

    [Fact]
    public void AC02_AC03_evidence_and_field_evidence_append_with_auto_incrementing_sequence_numbers()
    {
        var store = NewStore();
        store.CreateSession("dcs_1", TenantA, personId: "per_1", T0);

        var evidence1 = store.AppendEvidence(TenantA, "dcs_1", ValidHash, "image/jpeg", 1, DocumentSide.Front, EvidenceSource.Capture, T0, T1, EvidenceClassification.Personal);
        var evidence2 = store.AppendEvidence(TenantA, "dcs_1", ValidHash, "image/jpeg", 2, DocumentSide.Back, EvidenceSource.Capture, T0, T1, EvidenceClassification.Personal);

        Assert.Equal(1, evidence1.SequenceNumber);
        Assert.Equal(2, evidence2.SequenceNumber);
        Assert.Equal(2, store.ListEvidence(TenantA, "dcs_1").Count);

        var field = store.AppendFieldEvidence(TenantA, "dcs_1", evidence1.SequenceNumber, "surname", "OKAFOR", "Okafor", 0.9, FieldEvidenceSource.Mrz, null, T2, EvidenceClassification.Personal);
        Assert.Equal(1, field.SequenceNumber);
        Assert.Equal(evidence1.SequenceNumber, field.SourceEvidenceSequenceNumber);
    }

    [Fact]
    public void field_evidence_referencing_an_unknown_source_evidence_sequence_is_rejected()
    {
        var store = NewStore();
        store.CreateSession("dcs_1", TenantA, personId: "per_1", T0);

        Assert.Throws<ArgumentException>(() => store.AppendFieldEvidence(
            TenantA, "dcs_1", sourceEvidenceSequenceNumber: 99, "surname", "OKAFOR", "Okafor",
            0.9, FieldEvidenceSource.Mrz, null, T2, EvidenceClassification.Personal));
    }

    [Fact]
    public void a_closed_session_refuses_further_evidence()
    {
        var store = NewStore();
        var session = store.CreateSession("dcs_1", TenantA, personId: "per_1", T0);
        store.CloseSession(TenantA, "dcs_1", session.Version, T1);

        Assert.Throws<InvalidOperationException>(() => store.AppendEvidence(
            TenantA, "dcs_1", ValidHash, "image/jpeg", 1, DocumentSide.Front, EvidenceSource.Capture, T0, T1, EvidenceClassification.Personal));
    }

    // QA-01 (Seyi review): a cited sequence number alone proved nothing - RaiseConflict did not
    // check that it actually belonged to the named field, or that the cited records actually
    // disagreed, so a caller could fabricate manual-review provenance.
    [Fact]
    public void AC05_raising_a_conflict_with_evidence_from_an_unrelated_field_is_refused()
    {
        var store = NewStore();
        store.CreateSession("dcs_1", TenantA, personId: "per_1", T0);
        var evidence = store.AppendEvidence(TenantA, "dcs_1", ValidHash, "image/jpeg", 1, DocumentSide.DataPage, EvidenceSource.Capture, T0, T1, EvidenceClassification.Personal);
        var givenNames = store.AppendFieldEvidence(TenantA, "dcs_1", evidence.SequenceNumber, "given_names", "CHIOMA", "Chioma", 0.9, FieldEvidenceSource.Mrz, null, T2, EvidenceClassification.Personal);
        var documentNumber = store.AppendFieldEvidence(TenantA, "dcs_1", evidence.SequenceNumber, "document_number", "A1", "A1", 0.9, FieldEvidenceSource.Mrz, null, T2, EvidenceClassification.Personal);

        var ex = Assert.Throws<ArgumentException>(() => store.RaiseConflict(
            TenantA, "dcs_1", "date_of_birth", new[] { givenNames.SequenceNumber, documentNumber.SequenceNumber }, T2));
        Assert.Contains("field_name_mismatch", ex.Message);
    }

    [Fact]
    public void AC05_raising_a_conflict_with_equal_values_is_refused_there_is_no_real_disagreement()
    {
        var store = NewStore();
        store.CreateSession("dcs_1", TenantA, personId: "per_1", T0);
        var evidence = store.AppendEvidence(TenantA, "dcs_1", ValidHash, "image/jpeg", 1, DocumentSide.DataPage, EvidenceSource.Capture, T0, T1, EvidenceClassification.Personal);
        var mrz = store.AppendFieldEvidence(TenantA, "dcs_1", evidence.SequenceNumber, "surname", "OKAFOR", "Okafor", 0.9, FieldEvidenceSource.Mrz, null, T2, EvidenceClassification.Personal);
        var ocr = store.AppendFieldEvidence(TenantA, "dcs_1", evidence.SequenceNumber, "surname", "OKAFOR", "Okafor", 0.8, FieldEvidenceSource.Ocr, null, T2, EvidenceClassification.Personal);

        var ex = Assert.Throws<ArgumentException>(() => store.RaiseConflict(
            TenantA, "dcs_1", "surname", new[] { mrz.SequenceNumber, ocr.SequenceNumber }, T2));
        Assert.Contains("does_not_disagree", ex.Message);
    }

    [Fact]
    public void AC05_raising_a_conflict_moves_the_session_into_manual_review_and_resolving_the_last_open_conflict_returns_it()
    {
        var store = NewStore();
        var session = store.CreateSession("dcs_1", TenantA, personId: "per_1", T0);
        var evidence = store.AppendEvidence(TenantA, "dcs_1", ValidHash, "image/jpeg", 1, DocumentSide.DataPage, EvidenceSource.Capture, T0, T1, EvidenceClassification.Personal);
        var fieldA = store.AppendFieldEvidence(TenantA, "dcs_1", evidence.SequenceNumber, "date_of_birth", "140301", "2001-03-14", 0.7, FieldEvidenceSource.Mrz, null, T2, EvidenceClassification.Personal);
        var fieldB = store.AppendFieldEvidence(TenantA, "dcs_1", evidence.SequenceNumber, "date_of_birth", "140302", "2002-03-14", 0.65, FieldEvidenceSource.Ocr, null, T2, EvidenceClassification.Personal);

        store.RaiseConflict(TenantA, "dcs_1", "date_of_birth", new[] { fieldA.SequenceNumber, fieldB.SequenceNumber }, T2);
        Assert.Equal(DocumentSessionStatus.ManualReviewPending, store.GetSession(TenantA, "dcs_1").Status);

        store.ResolveConflict(TenantA, "dcs_1", "date_of_birth", "2001-03-14", "reviewer_amina", "2026-09-21T12:10:00.000Z");

        var resolvedSession = store.GetSession(TenantA, "dcs_1");
        Assert.Equal(DocumentSessionStatus.PersonLinked, resolvedSession.Status);
        Assert.Equal(session.PersonId, resolvedSession.PersonId);
    }

    [Fact]
    public void AC05_session_stays_in_manual_review_while_a_second_conflict_on_a_different_field_is_still_open()
    {
        var store = NewStore();
        store.CreateSession("dcs_1", TenantA, personId: "per_1", T0);
        var evidence = store.AppendEvidence(TenantA, "dcs_1", ValidHash, "image/jpeg", 1, DocumentSide.DataPage, EvidenceSource.Capture, T0, T1, EvidenceClassification.Personal);
        var dobA = store.AppendFieldEvidence(TenantA, "dcs_1", evidence.SequenceNumber, "date_of_birth", "a", "2001-03-14", 0.7, FieldEvidenceSource.Mrz, null, T2, EvidenceClassification.Personal);
        var dobB = store.AppendFieldEvidence(TenantA, "dcs_1", evidence.SequenceNumber, "date_of_birth", "b", "2002-03-14", 0.6, FieldEvidenceSource.Ocr, null, T2, EvidenceClassification.Personal);
        var surA = store.AppendFieldEvidence(TenantA, "dcs_1", evidence.SequenceNumber, "surname", "OKAFOR", "Okafor", 0.7, FieldEvidenceSource.Mrz, null, T2, EvidenceClassification.Personal);
        var surB = store.AppendFieldEvidence(TenantA, "dcs_1", evidence.SequenceNumber, "surname", "0KAFOR", "0kafor", 0.6, FieldEvidenceSource.Ocr, null, T2, EvidenceClassification.Personal);

        store.RaiseConflict(TenantA, "dcs_1", "date_of_birth", new[] { dobA.SequenceNumber, dobB.SequenceNumber }, T2);
        store.RaiseConflict(TenantA, "dcs_1", "surname", new[] { surA.SequenceNumber, surB.SequenceNumber }, T2);

        store.ResolveConflict(TenantA, "dcs_1", "date_of_birth", "2001-03-14", "reviewer_amina", "2026-09-21T12:10:00.000Z");

        Assert.Equal(DocumentSessionStatus.ManualReviewPending, store.GetSession(TenantA, "dcs_1").Status);
    }

    [Fact]
    public void AC04_a_field_with_an_open_conflict_produces_a_proposal_flagged_as_requiring_manual_review_and_it_cannot_be_accepted()
    {
        var store = NewStore();
        store.CreateSession("dcs_1", TenantA, personId: "per_1", T0);
        var evidence = store.AppendEvidence(TenantA, "dcs_1", ValidHash, "image/jpeg", 1, DocumentSide.DataPage, EvidenceSource.Capture, T0, T1, EvidenceClassification.Personal);
        var dobA = store.AppendFieldEvidence(TenantA, "dcs_1", evidence.SequenceNumber, "date_of_birth", "a", "2001-03-14", 0.7, FieldEvidenceSource.Mrz, null, T2, EvidenceClassification.Personal);
        var dobB = store.AppendFieldEvidence(TenantA, "dcs_1", evidence.SequenceNumber, "date_of_birth", "b", "2002-03-14", 0.6, FieldEvidenceSource.Ocr, null, T2, EvidenceClassification.Personal);
        store.RaiseConflict(TenantA, "dcs_1", "date_of_birth", new[] { dobA.SequenceNumber, dobB.SequenceNumber }, T2);

        var proposal = store.ProposePersonFieldChange(TenantA, "dcs_1", "date_of_birth", "2001-03-14", dobA.SequenceNumber, T2);

        Assert.True(proposal.RequiresManualReview);
        Assert.Throws<InvalidOperationException>(() => proposal.Accept("reviewer_amina", "2026-09-21T12:10:00.000Z"));
    }

    [Fact]
    public void AC04_a_field_with_no_conflict_produces_an_acceptable_proposal_but_it_is_never_auto_applied()
    {
        var store = NewStore();
        store.CreateSession("dcs_1", TenantA, personId: "per_1", T0);
        var evidence = store.AppendEvidence(TenantA, "dcs_1", ValidHash, "image/jpeg", 1, DocumentSide.DataPage, EvidenceSource.Capture, T0, T1, EvidenceClassification.Personal);
        var field = store.AppendFieldEvidence(TenantA, "dcs_1", evidence.SequenceNumber, "surname", "OKAFOR", "Okafor", 0.9, FieldEvidenceSource.Mrz, null, T2, EvidenceClassification.Personal);

        var proposal = store.ProposePersonFieldChange(TenantA, "dcs_1", "surname", "Okafor", field.SequenceNumber, T2);

        Assert.False(proposal.RequiresManualReview);
        Assert.Null(proposal.AppliedAt);
        var accepted = proposal.Accept("reviewer_amina", "2026-09-21T12:10:00.000Z");
        Assert.NotNull(accepted.AppliedAt);
        // The store's own record of the proposal is untouched by accepting the returned copy -
        // proving, again, that nothing about accepting a proposal reaches back and mutates
        // anything this store holds.
        Assert.Null(store.ListPersonFieldChangeProposals(TenantA, "dcs_1").Single().AppliedAt);
    }

    [Fact]
    public void proposing_a_person_field_change_on_a_pre_person_session_is_refused()
    {
        var store = NewStore();
        store.CreateSession("dcs_1", TenantA, personId: null, T0);
        var evidence = store.AppendEvidence(TenantA, "dcs_1", ValidHash, "image/jpeg", 1, DocumentSide.DataPage, EvidenceSource.Capture, T0, T1, EvidenceClassification.Personal);
        var field = store.AppendFieldEvidence(TenantA, "dcs_1", evidence.SequenceNumber, "surname", "OKAFOR", "Okafor", 0.9, FieldEvidenceSource.Mrz, null, T2, EvidenceClassification.Personal);

        Assert.Throws<InvalidOperationException>(() => store.ProposePersonFieldChange(TenantA, "dcs_1", "surname", "Okafor", field.SequenceNumber, T2));
    }
}
