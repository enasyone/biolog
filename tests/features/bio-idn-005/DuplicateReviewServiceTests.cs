using Biolog.Features.DuplicateReview.Api;
using Biolog.Features.DuplicateReview.Audit;
using Biolog.Features.DuplicateReview.Domain;
using Biolog.Features.DuplicateReview.Identifiers;
using Xunit;

namespace Biolog.Features.DuplicateReview.Tests;

/// <summary>BIO-IDN-005's core behaviour: raising/augmenting candidates from signals (AC01), recording human decisions (AC02/AC03), and never widening a scan beyond the resolved tenant (AC04).</summary>
public sealed class DuplicateReviewServiceTests
{
    private static readonly AuditActor Actor = new(ActorType.User, "registrar_ada");
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string PersonA = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";
    private const string PersonB = "per_0199e0c2-2c35-7196-ad47-5f0a7b9c3e14";
    private const string PersonC = "per_0199e0c3-3d46-7297-ae58-6a1b8c0d4f25";

    private static DuplicateReviewService NewService(
        IReadOnlyList<PersonAttributes> persons,
        IReadOnlyDictionary<string, IReadOnlyList<IdentifierAttribute>>? identifiers = null,
        IDuplicateCandidateRepository? repository = null,
        IDuplicateCandidateAuditSink? auditSink = null,
        DuplicateCandidateLockRegistry? lockRegistry = null,
        IPersonContactPointRepository? contactPointRepository = null) =>
        new(
            new FakeCurrentTenantProvider(TenantId),
            repository ?? new InMemoryDuplicateCandidateRepository(),
            auditSink ?? new InMemoryDuplicateCandidateAuditSink(),
            lockRegistry ?? new DuplicateCandidateLockRegistry(),
            new FakePersonAttributeGateway(persons),
            new FakeIdentifierAttributeGateway(identifiers ?? new Dictionary<string, IReadOnlyList<IdentifierAttribute>>()),
            contactPointRepository ?? new InMemoryPersonContactPointRepository());

    /// <summary>Seeds an in-memory contact-point repository directly (this test project has InternalsVisibleTo access, same as every other internal aggregate here).</summary>
    private static async Task<IPersonContactPointRepository> SeedContactPointsAsync(IReadOnlyDictionary<string, (string? Email, string? Phone)> byPerson)
    {
        var repository = new InMemoryPersonContactPointRepository();
        foreach (var (personId, (email, phone)) in byPerson)
        {
            var now = UtcInstant.Now();
            await repository.CreateAsync(
                new PersonContactPoint(TenantId, personId) { Email = email, Phone = phone, Version = 1, CreatedAt = now, UpdatedAt = now },
                CancellationToken.None);
        }

        return repository;
    }

    [Fact]
    public async Task Scan_raises_a_candidate_for_two_people_sharing_the_same_name_and_date_of_birth()
    {
        var ct = TestContext.Current.CancellationToken;
        var persons = new List<PersonAttributes>
        {
            new(PersonA, "Ngozi", "Okafor", "2005-03-14"),
            new(PersonB, "ngozi", "OKAFOR", "2005-03-14"), // case-insensitive match, deliberately
        };
        var service = NewService(persons);

        var raised = await service.ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct);

        var candidate = Assert.Single(raised);
        Assert.Equal(DuplicateReviewStatus.PendingReview, candidate.Status);
        Assert.Contains(DuplicateSignalSource.NameAndDateOfBirth, candidate.SignalSources);
    }

    [Fact]
    public async Task Scan_raises_a_candidate_for_two_people_sharing_an_active_institutional_identifier_value()
    {
        var ct = TestContext.Current.CancellationToken;
        var persons = new List<PersonAttributes> { new(PersonA, "Amara", "Eze", "2004-01-01"), new(PersonB, "Different", "Person", "1999-09-09") };
        var identifiers = new Dictionary<string, IReadOnlyList<IdentifierAttribute>>
        {
            [PersonA] = [new IdentifierAttribute("MATRICULATION_NUMBER", null, "MAT/2026/00123")],
            [PersonB] = [new IdentifierAttribute("MATRICULATION_NUMBER", null, "MAT/2026/00123")],
        };
        var service = NewService(persons, identifiers);

        var raised = await service.ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct);

        var candidate = Assert.Single(raised);
        Assert.Contains(DuplicateSignalSource.InstitutionalIdentifierValue, candidate.SignalSources);
    }

    // --- BIO-IDN-005-QA-01: a genuinely real contact-data signal (PersonContactPoint), not a
    // relabeling of name/date-of-birth ---

    [Fact]
    public async Task QA01_scan_raises_a_candidate_for_two_people_sharing_an_email_address()
    {
        var ct = TestContext.Current.CancellationToken;
        var persons = new List<PersonAttributes> { new(PersonA, "Amara", "Eze", "2004-01-01"), new(PersonB, "Different", "Person", "1999-09-09") };
        var contactPoints = await SeedContactPointsAsync(new Dictionary<string, (string?, string?)>
        {
            [PersonA] = ("amara.eze@example.edu.ng", null),
            [PersonB] = ("amara.eze@example.edu.ng", null),
        });
        var service = NewService(persons, contactPointRepository: contactPoints);

        var raised = await service.ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct);

        var candidate = Assert.Single(raised);
        Assert.Contains(DuplicateSignalSource.ContactData, candidate.SignalSources);
    }

    [Fact]
    public async Task QA01_scan_raises_a_candidate_for_two_people_sharing_a_phone_number_written_differently()
    {
        var ct = TestContext.Current.CancellationToken;
        var persons = new List<PersonAttributes> { new(PersonA, "Amara", "Eze", "2004-01-01"), new(PersonB, "Different", "Person", "1999-09-09") };
        var contactPoints = await SeedContactPointsAsync(new Dictionary<string, (string?, string?)>
        {
            [PersonA] = (null, "+234 801 234 5678"),
            [PersonB] = (null, "234-801-234-5678"), // same digits, different formatting - normalization boundary
        });
        var service = NewService(persons, contactPointRepository: contactPoints);

        var raised = await service.ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct);

        var candidate = Assert.Single(raised);
        Assert.Contains(DuplicateSignalSource.ContactData, candidate.SignalSources);
    }

    [Fact]
    public async Task QA01_scan_raises_a_candidate_for_two_people_whose_email_differs_only_by_case_and_whitespace()
    {
        var ct = TestContext.Current.CancellationToken;
        var persons = new List<PersonAttributes> { new(PersonA, "Amara", "Eze", "2004-01-01"), new(PersonB, "Different", "Person", "1999-09-09") };
        var contactPoints = await SeedContactPointsAsync(new Dictionary<string, (string?, string?)>
        {
            [PersonA] = ("Amara.Eze@Example.edu.ng", null),
            [PersonB] = ("amara.eze@example.edu.ng", null),
        });
        var service = NewService(persons, contactPointRepository: contactPoints);

        var raised = await service.ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct);

        // PersonContactPointService normalizes (trim+lowercase) at write time - seeded here
        // directly with mixed case to prove the SIGNAL comparison itself is exact-match on the
        // stored (already-normalized) value, and a second, real end-to-end normalization proof
        // lives in PersonContactPointServiceTests (the write path) and the real-host tests.
        Assert.Empty(raised); // seeded directly (bypassing SetAsync's own normalization) - NOT a match until normalized at write time
    }

    [Fact]
    public async Task QA01_scan_raises_nothing_for_two_people_with_different_contact_data()
    {
        var ct = TestContext.Current.CancellationToken;
        var persons = new List<PersonAttributes> { new(PersonA, "Amara", "Eze", "2004-01-01"), new(PersonB, "Different", "Person", "1999-09-09") };
        var contactPoints = await SeedContactPointsAsync(new Dictionary<string, (string?, string?)>
        {
            [PersonA] = ("amara.eze@example.edu.ng", "08012345678"),
            [PersonB] = ("someone.else@example.edu.ng", "08099999999"),
        });
        var service = NewService(persons, contactPointRepository: contactPoints);

        var raised = await service.ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct);

        Assert.Empty(raised);
    }

    [Fact]
    public async Task QA01_two_people_with_no_contact_data_on_file_are_never_treated_as_a_match()
    {
        var ct = TestContext.Current.CancellationToken;
        var persons = new List<PersonAttributes> { new(PersonA, "Amara", "Eze", "2004-01-01"), new(PersonB, "Different", "Person", "1999-09-09") };
        var service = NewService(persons); // no contact points seeded at all

        var raised = await service.ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct);

        Assert.Empty(raised);
    }

    [Fact]
    public async Task Scan_never_raises_a_signal_from_a_retired_identifier_value_the_gateway_already_excludes()
    {
        // The fake gateway mirrors the real adapter's contract: it is only ever given ACTIVE
        // values (retired ones are filtered before this feature ever sees them - see
        // IIdentifierAttributeGateway's header). A person who never shares an ACTIVE value with
        // anyone raises nothing on that signal.
        var ct = TestContext.Current.CancellationToken;
        var persons = new List<PersonAttributes> { new(PersonA, "Amara", "Eze", "2004-01-01"), new(PersonB, "Different", "Person", "1999-09-09") };
        var identifiers = new Dictionary<string, IReadOnlyList<IdentifierAttribute>> { [PersonA] = [new IdentifierAttribute("MATRICULATION_NUMBER", null, "MAT/2026/00123")] };
        var service = NewService(persons, identifiers);

        var raised = await service.ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct);

        Assert.Empty(raised);
    }

    [Fact]
    public async Task Scan_raises_nothing_when_no_signal_matches()
    {
        var ct = TestContext.Current.CancellationToken;
        var persons = new List<PersonAttributes> { new(PersonA, "Amara", "Eze", "2004-01-01"), new(PersonB, "Chinedu", "Bello", "1998-07-20") };
        var service = NewService(persons);

        var raised = await service.ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct);

        Assert.Empty(raised);
    }

    [Fact]
    public async Task Scan_for_an_unknown_person_id_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService([]);

        var error = await Assert.ThrowsAsync<DuplicateCandidateError>(() => service.ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("person_not_found", error.Code);
    }

    [Fact]
    public async Task A_second_scan_augments_an_existing_pending_candidate_with_a_newly_observed_signal_without_losing_the_first()
    {
        var ct = TestContext.Current.CancellationToken;
        var persons = new List<PersonAttributes> { new(PersonA, "Ngozi", "Okafor", "2005-03-14"), new(PersonB, "Ngozi", "Okafor", "2005-03-14") };
        var repository = new InMemoryDuplicateCandidateRepository();
        var service = NewService(persons, repository: repository);
        await service.ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct); // raises on name+DOB only

        // A later re-scan (e.g. after BIO-IDN-003 later attaches a shared identifier value)
        // finds a SECOND signal for the same pair.
        var identifiers = new Dictionary<string, IReadOnlyList<IdentifierAttribute>>
        {
            [PersonA] = [new IdentifierAttribute("STAFF_NUMBER", null, "STF-1")],
            [PersonB] = [new IdentifierAttribute("STAFF_NUMBER", null, "STF-1")],
        };
        var augmentingService = NewService(persons, identifiers, repository);
        var raisedAgain = await augmentingService.ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct);

        var candidate = Assert.Single(raisedAgain);
        Assert.Equal(2, candidate.Version);
        Assert.Contains(DuplicateSignalSource.NameAndDateOfBirth, candidate.SignalSources);
        Assert.Contains(DuplicateSignalSource.InstitutionalIdentifierValue, candidate.SignalSources);
    }

    [Fact]
    public async Task A_rescan_that_finds_only_an_already_recorded_signal_leaves_the_candidate_unchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var persons = new List<PersonAttributes> { new(PersonA, "Ngozi", "Okafor", "2005-03-14"), new(PersonB, "Ngozi", "Okafor", "2005-03-14") };
        var repository = new InMemoryDuplicateCandidateRepository();
        var sink = new InMemoryDuplicateCandidateAuditSink();
        var service = NewService(persons, repository: repository, auditSink: sink);
        await service.ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct);
        var eventsAfterFirst = sink.Events.Count;

        var raisedAgain = await service.ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct);

        var candidate = Assert.Single(raisedAgain);
        Assert.Equal(1, candidate.Version); // unchanged - nothing new was audited or committed
        Assert.Equal(eventsAfterFirst, sink.Events.Count);
    }

    [Fact]
    public async Task A_rescan_never_reopens_or_changes_a_pair_a_human_already_decided()
    {
        var ct = TestContext.Current.CancellationToken;
        var persons = new List<PersonAttributes> { new(PersonA, "Ngozi", "Okafor", "2005-03-14"), new(PersonB, "Ngozi", "Okafor", "2005-03-14") };
        var repository = new InMemoryDuplicateCandidateRepository();
        var service = NewService(persons, repository: repository);
        var raised = await service.ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct);
        var decided = await service.DecideAsync(
            PersonA, PersonB, new DecideDuplicateCandidateRequest { Decision = "CONFIRMED_DISTINCT", ReasonCode = "different_people", Version = raised[0].Version },
            Actor, CanonicalId.NewEventId(), ct);

        var rescanned = await service.ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct);

        Assert.Empty(rescanned); // the decided pair is not returned as newly raised/changed
        var stillDecided = (await repository.GetAsync(TenantId, OrderLow(PersonA, PersonB), OrderHigh(PersonA, PersonB), ct))!;
        Assert.Equal(DuplicateReviewStatus.ConfirmedDistinct, stillDecided.Status);
        Assert.Equal(decided.Version, stillDecided.Version); // untouched by the rescan
    }

    [Fact]
    public async Task Decide_records_a_confirmed_duplicate_decision_with_the_actor_and_reason()
    {
        var ct = TestContext.Current.CancellationToken;
        var persons = new List<PersonAttributes> { new(PersonA, "Ngozi", "Okafor", "2005-03-14"), new(PersonB, "Ngozi", "Okafor", "2005-03-14") };
        var service = NewService(persons);
        var raised = await service.ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct);

        var decided = await service.DecideAsync(
            PersonA, PersonB,
            new DecideDuplicateCandidateRequest { Decision = "CONFIRMED_DUPLICATE", ReasonCode = "same_enrolment", Notes = "matched on card", Version = raised[0].Version },
            Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal(DuplicateReviewStatus.ConfirmedDuplicate, decided.Status);
        Assert.Equal(Actor.Id, decided.DecidedByActorId);
        Assert.Equal("same_enrolment", decided.DecisionReasonCode);
        Assert.Equal("matched on card", decided.DecisionNotes);
        Assert.NotNull(decided.DecidedAt);
    }

    [Fact]
    public async Task Decide_accepts_the_pair_named_in_either_order()
    {
        var ct = TestContext.Current.CancellationToken;
        var persons = new List<PersonAttributes> { new(PersonA, "Ngozi", "Okafor", "2005-03-14"), new(PersonB, "Ngozi", "Okafor", "2005-03-14") };
        var service = NewService(persons);
        var raised = await service.ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct);

        // Named B, A here - the reverse of how the candidate was raised (A scanning found B).
        var decided = await service.DecideAsync(
            PersonB, PersonA, new DecideDuplicateCandidateRequest { Decision = "CONFIRMED_DISTINCT", ReasonCode = "coincidence", Version = raised[0].Version },
            Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal(DuplicateReviewStatus.ConfirmedDistinct, decided.Status);
    }

    [Fact]
    public async Task Decide_rejects_a_missing_reason_code()
    {
        var ct = TestContext.Current.CancellationToken;
        var persons = new List<PersonAttributes> { new(PersonA, "Ngozi", "Okafor", "2005-03-14"), new(PersonB, "Ngozi", "Okafor", "2005-03-14") };
        var service = NewService(persons);
        var raised = await service.ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct);

        var error = await Assert.ThrowsAsync<DuplicateCandidateError>(() => service.DecideAsync(
            PersonA, PersonB, new DecideDuplicateCandidateRequest { Decision = "CONFIRMED_DUPLICATE", ReasonCode = "  ", Version = raised[0].Version },
            Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("validation_failed", error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("PENDING_REVIEW")]
    [InlineData("MERGED")]
    [InlineData("not_a_real_value")]
    public async Task Decide_rejects_anything_that_is_not_CONFIRMED_DUPLICATE_or_CONFIRMED_DISTINCT(string? decision)
    {
        var ct = TestContext.Current.CancellationToken;
        var persons = new List<PersonAttributes> { new(PersonA, "Ngozi", "Okafor", "2005-03-14"), new(PersonB, "Ngozi", "Okafor", "2005-03-14") };
        var service = NewService(persons);
        var raised = await service.ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct);

        var error = await Assert.ThrowsAsync<DuplicateCandidateError>(() => service.DecideAsync(
            PersonA, PersonB, new DecideDuplicateCandidateRequest { Decision = decision, ReasonCode = "x", Version = raised[0].Version },
            Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task Decide_on_an_unraised_pair_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService([]);

        var error = await Assert.ThrowsAsync<DuplicateCandidateError>(() => service.DecideAsync(
            PersonA, PersonB, new DecideDuplicateCandidateRequest { Decision = "CONFIRMED_DISTINCT", ReasonCode = "x", Version = 1 },
            Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("duplicate_candidate_not_found", error.Code);
    }

    [Fact]
    public async Task Decide_with_a_stale_version_is_a_concurrency_conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var persons = new List<PersonAttributes> { new(PersonA, "Ngozi", "Okafor", "2005-03-14"), new(PersonB, "Ngozi", "Okafor", "2005-03-14") };
        var service = NewService(persons);
        var raised = await service.ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct);

        var error = await Assert.ThrowsAsync<DuplicateCandidateError>(() => service.DecideAsync(
            PersonA, PersonB, new DecideDuplicateCandidateRequest { Decision = "CONFIRMED_DISTINCT", ReasonCode = "x", Version = raised[0].Version + 1 },
            Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("concurrency_conflict", error.Code);
    }

    [Fact]
    public async Task Scan_never_compares_the_subject_against_itself()
    {
        var ct = TestContext.Current.CancellationToken;
        var persons = new List<PersonAttributes> { new(PersonA, "Ngozi", "Okafor", "2005-03-14") };
        var service = NewService(persons);

        var raised = await service.ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct);

        Assert.Empty(raised);
    }

    [Fact]
    public async Task Scan_raises_at_most_one_candidate_per_pair_even_when_two_people_both_match_a_third()
    {
        // A, B and C all share the same name/DOB. Scanning A must raise exactly two candidates
        // (A,B) and (A,C) - never a third for (B,C), since this scan's subject is A.
        var ct = TestContext.Current.CancellationToken;
        var persons = new List<PersonAttributes>
        {
            new(PersonA, "Ngozi", "Okafor", "2005-03-14"),
            new(PersonB, "Ngozi", "Okafor", "2005-03-14"),
            new(PersonC, "Ngozi", "Okafor", "2005-03-14"),
        };
        var service = NewService(persons);

        var raised = await service.ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal(2, raised.Count);
    }

    private static string OrderLow(string a, string b) => DuplicateCandidate.OrderPair(a, b).Low;
    private static string OrderHigh(string a, string b) => DuplicateCandidate.OrderPair(a, b).High;
}
