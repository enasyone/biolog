namespace Biolog.Features.DocumentVerifyCoordination.Gateways;

/// <summary>Already projected onto <c>web/features/documents/evidence.ts</c>'s own
/// <c>EvidenceCheckState</c> wire vocabulary ("consistent" | "inconclusive" | "failed" - the
/// exact three BIO-DOC-010's own <c>DocumentTemplateVerificationState.ToWireString</c> already
/// uses) by the adapter that implements this gateway.</summary>
public sealed record IdentificationSnapshot(string EvidenceCheckState, double Confidence, string Summary);

/// <summary>
/// This feature's own narrow seam onto BIO-DOC-010's real, already-registered
/// <c>DocumentTemplatePackService</c> - deliberately NOT a <c>ProjectReference</c> to
/// <c>api/features/bio-doc-010</c> (Kemi's ruling, point 2). The real implementation is an
/// adapter in <c>Biolog.Api.Modules.BioOps005Module</c> wrapping that service's new
/// <c>IdentifyFromEvidence</c> overload directly.
/// </summary>
public interface IDocumentIdentificationGateway
{
    /// <param name="entryId">The governed pack entry to verify against - a plain string, not
    /// BIO-DOC-010's own <c>NigeriaDocumentPackV1.PassportEntryId</c> constant (no
    /// <c>ProjectReference</c> to see it with). This coordinator's own synthetic fixture always
    /// targets the Nigerian ePassport entry, <c>"nga-passport-2021"</c>.</param>
    Task<IdentificationSnapshot> IdentifyFromEvidenceAsync(
        string documentSessionId,
        string entryId,
        IReadOnlyDictionary<string, string> observedFields,
        string actorId,
        string correlationId,
        CancellationToken cancellationToken = default);
}
