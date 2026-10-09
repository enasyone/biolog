using Biolog.Features.Biometrics.Domain;
using Biolog.Features.FaceSearch.Domain;

namespace Biolog.Features.FaceSearch.Tests;

/// <summary>Test double for <see cref="IFaceGalleryApprovalGateway"/> - the production
/// implementation lives in <c>Biolog.Api.Modules.BioBio007Module</c> and wraps BIO-BIO-003's real
/// <c>FaceEnrolmentService</c>, which this feature project deliberately has no reference to (see
/// this feature's csproj remarks).</summary>
public sealed class FakeFaceGalleryApprovalGateway : IFaceGalleryApprovalGateway
{
    private readonly Dictionary<(string TenantId, string PersonId), BiometricTemplate> _approved = [];

    public FakeFaceGalleryApprovalGateway Approve(string tenantId, string personId, byte[] payload)
    {
        var digest = System.Security.Cryptography.SHA256.HashData(payload);
        var template = BiometricTemplate.Create(
            $"template-{Guid.NewGuid()}", tenantId, personId, BiometricModality.Face, "synthetic-face-v1", "synthetic-face-1.0.0", digest,
            "2026-01-01T00:00:00Z");
        _approved[(tenantId, personId)] = template;
        return this;
    }

    public Task<BiometricTemplate?> GetLatestApprovedReferenceAsync(
        string tenantId, string personId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_approved.TryGetValue((tenantId, personId), out var template) ? template : null);
}
