using Biolog.Features.AuthenticityAssessment.Model;

namespace Biolog.Features.DocumentTemplatePack.Model;

/// <summary>
/// AC04's own record: the result of one <see cref="DocumentTemplateVerification.Verify"/> call,
/// stamped with exactly which governed <see cref="PackId"/>/<see cref="PackVersion"/>/<see cref="EntryId"/>
/// it was verified against - "pack version is recorded on every verification" is this type
/// existing and being durably audited (<see cref="Audit.DocumentTemplatePackAuditEventBuilder.BuildVerificationRecorded"/>)
/// every time <see cref="DocumentTemplateVerification.Verify"/> returns, never only sometimes.
///
/// <para>
/// Locked down (<c>internal init</c>, no public constructor) for the same reason every prior
/// BIO-DOC-00x result type in this epic is: a public <c>init</c> would let a caller launder a
/// result with real violations into one that claims <see cref="DocumentTemplateVerificationState.Consistent"/>.
/// </para>
/// </summary>
public sealed record DocumentTemplateVerificationOutcome
{
    public string RecordId { get; internal init; } = null!;
    public string PackId { get; internal init; } = null!;
    public string PackVersion { get; internal init; } = null!;
    public string EntryId { get; internal init; } = null!;
    public string Country { get; internal init; } = null!;
    public string Issuer { get; internal init; } = null!;
    public string DocumentClass { get; internal init; } = null!;
    public string Series { get; internal init; } = null!;
    public DocumentTemplateVerificationState State { get; internal init; }
    public IReadOnlyList<DocumentTemplatePackFieldViolation> Violations { get; internal init; } = [];
    public string CorrelationId { get; internal init; } = null!;
    public string RecordedAt { get; internal init; } = null!;
    public AuthenticityDisposition? CorrelatedAuthenticityDisposition { get; internal init; }

    internal DocumentTemplateVerificationOutcome(
        string recordId,
        string packId,
        string packVersion,
        string entryId,
        string country,
        string issuer,
        string documentClass,
        string series,
        DocumentTemplateVerificationState state,
        IReadOnlyList<DocumentTemplatePackFieldViolation> violations,
        string correlationId,
        string recordedAt,
        AuthenticityDisposition? correlatedAuthenticityDisposition)
    {
        RecordId = recordId;
        PackId = packId;
        PackVersion = packVersion;
        EntryId = entryId;
        Country = country;
        Issuer = issuer;
        DocumentClass = documentClass;
        Series = series;
        State = state;
        Violations = new List<DocumentTemplatePackFieldViolation>(violations).AsReadOnly();
        CorrelationId = correlationId;
        RecordedAt = recordedAt;
        CorrelatedAuthenticityDisposition = correlatedAuthenticityDisposition;
    }
}
