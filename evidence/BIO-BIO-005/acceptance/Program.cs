using Biolog.Features.Biometrics.Domain;
using Biolog.Features.Biometrics.Matching;
using Biolog.Features.Biometrics.Providers;
using Biolog.Features.Biometrics.Synthetic;
using Biolog.Features.FaceEnrolment.Quality;
using Biolog.Features.FaceVerification.Domain;
using Biolog.Features.FaceVerification.Service;

namespace Biolog.Features.FaceVerification.Tests;

internal static class Program
{
    private const string TenantId = "ten_acceptance";
    private const string PersonId = "per_acceptance";
    private const string NowUtc = "2026-09-23T02:00:00Z";

    private static async Task<int> Main()
    {
        const int currentPhotoMasterVersion = 2;
        var payload = new byte[] { 1, 0, 9, 9, 9 };
        var staleReference = new FaceReferenceSnapshot
        {
            ReferenceId = "ref_from_photo_v1",
            SourcePhotoMasterVersion = 1,
            Template = BiometricTemplate.Create(
                "template_v1", TenantId, PersonId, BiometricModality.Face,
                "synthetic-face-v1", "synthetic-face-1.0.0",
                System.Security.Cryptography.SHA256.HashData(payload), NowUtc),
        };

        var service = new FaceVerificationService(
            new StaleReferenceGateway(staleReference, currentPhotoMasterVersion),
            new SyntheticFaceProvider(),
            new SyntheticBiometricMatcher(),
            new SyntheticFaceQualityChecker(),
            new SyntheticFaceVerificationThresholdPolicy(),
            new InMemoryFaceVerificationEvidenceStore(),
            newId: () => "acceptance-evidence-id",
            nowUtc: () => NowUtc);

        var evidence = await service.CompareAsync(
            TenantId,
            PersonId,
            new BiometricSample { Payload = payload, ContentType = "application/octet-stream", CapturedAtUtc = NowUtc },
            CancellationToken.None);

        if (evidence.ComparisonOutcome != FaceComparisonOutcome.ReferenceStale ||
            evidence.SourcePhotoMasterVersion != 1 ||
            evidence.CurrentPhotoMasterVersion != currentPhotoMasterVersion ||
            evidence.Score is not null)
        {
            Console.Error.WriteLine("FAIL: stale source-photo reference was not rejected deterministically.");
            return 1;
        }

        Console.WriteLine(
            $"PASS: reference from photo v{evidence.SourcePhotoMasterVersion} rejected as ReferenceStale while current photo is v{currentPhotoMasterVersion}.");
        Console.WriteLine("PASS: no biometric score was produced for the stale reference.");
        return 0;
    }

    private sealed class StaleReferenceGateway(FaceReferenceSnapshot reference, int currentPhotoMasterVersion) : IFaceReferenceGateway
    {
        public Task<FaceReferenceSnapshot?> GetLatestApprovedReferenceAsync(
            string tenantId, string personId, CancellationToken cancellationToken = default) =>
            Task.FromResult<FaceReferenceSnapshot?>(reference);

        public Task<int?> GetCurrentPhotoMasterVersionAsync(
            string tenantId, string personId, CancellationToken cancellationToken = default) =>
            Task.FromResult<int?>(currentPhotoMasterVersion);
    }
}
