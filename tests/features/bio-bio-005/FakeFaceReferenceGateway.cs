using Biolog.Features.Biometrics.Domain;
using Biolog.Features.FaceVerification.Domain;

namespace Biolog.Features.FaceVerification.Tests;

/// <summary>Test double for <see cref="IFaceReferenceGateway"/> - the production implementation
/// lives in <c>Biolog.Api.Modules.BioBio005Module</c> and wraps BIO-BIO-003's real
/// <c>FaceEnrolmentService</c> and BIO-PHO-001's real <c>PhotoMasterService</c>, neither of which
/// this feature project has any way to construct (internal/Scoped-registered - see this
/// feature's csproj remarks).</summary>
public sealed class FakeFaceReferenceGateway : IFaceReferenceGateway
{
    private readonly Dictionary<(string TenantId, string PersonId), FaceReferenceSnapshot> _references = [];
    private readonly Dictionary<(string TenantId, string PersonId), int?> _currentPhotoVersions = [];

    /// <summary>Adds an approved reference. Unless <see cref="SetCurrentPhotoMasterVersion"/> was
    /// already called for this (tenant, person), the current photo master version defaults to
    /// <paramref name="sourcePhotoMasterVersion"/> itself - i.e. a freshly added reference is NOT
    /// stale by default, matching the "photo just captured, reference just derived from it"
    /// happy path. Call <see cref="SetCurrentPhotoMasterVersion"/> to simulate a photo lifecycle
    /// change that has superseded this reference's source photo.</summary>
    public FakeFaceReferenceGateway Add(
        string tenantId, string personId, byte[] payload, int sourcePhotoMasterVersion = 1,
        string providerId = "synthetic-face-v1", string providerVersion = "synthetic-face-1.0.0")
    {
        var digest = System.Security.Cryptography.SHA256.HashData(payload);
        var template = BiometricTemplate.Create(
            $"template-{Guid.NewGuid()}", tenantId, personId, BiometricModality.Face, providerId, providerVersion, digest,
            "2026-01-01T00:00:00Z");
        _references[(tenantId, personId)] = new FaceReferenceSnapshot
        {
            ReferenceId = $"ref-{Guid.NewGuid()}",
            Template = template,
            SourcePhotoMasterVersion = sourcePhotoMasterVersion,
        };
        _currentPhotoVersions.TryAdd((tenantId, personId), sourcePhotoMasterVersion);
        return this;
    }

    /// <summary>Simulates BIO-PHO-001 appending a new photo master (or having none at all, via
    /// <c>version: null</c>) independently of whether a face reference has been re-captured.</summary>
    public FakeFaceReferenceGateway SetCurrentPhotoMasterVersion(string tenantId, string personId, int? version)
    {
        _currentPhotoVersions[(tenantId, personId)] = version;
        return this;
    }

    public Task<FaceReferenceSnapshot?> GetLatestApprovedReferenceAsync(
        string tenantId, string personId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_references.TryGetValue((tenantId, personId), out var reference) ? reference : null);

    public Task<int?> GetCurrentPhotoMasterVersionAsync(
        string tenantId, string personId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_currentPhotoVersions.TryGetValue((tenantId, personId), out var version) ? version : null);
}
