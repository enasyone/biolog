namespace Biolog.Features.CardTemplates.Domain;

/// <summary>
/// The one genuine cross-story runtime dependency this story has (BIO-CRD-004's own
/// <c>ISignatureAssetRepository</c>), reached through a narrow gateway this project defines -
/// never a ProjectReference - implemented by an adapter in
/// <c>api/Biolog.Api/Modules/BioCrd008Module.cs</c>, the same shape every prior cross-story
/// dependency in this epic already established (see BIO-CRD-007's own <c>ICardIssueGateway</c>).
/// </summary>
public interface ISignatureAssetGateway
{
    /// <summary>True only if this exact (tenantId, lineageId, version) signature asset exists.</summary>
    bool Exists(string tenantId, string lineageId, int version);
}
