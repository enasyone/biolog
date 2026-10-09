using Biolog.Api.Tenancy;
using Biolog.Features.Liveness.Domain;
using Biolog.Features.Liveness.Service;

namespace Biolog.Api.Modules;

/// <summary>
/// BIO-BIO-008: provider-neutral liveness evidence (AC01), a distinct-from-face-match status
/// vocabulary (AC02), tenant/workflow policy hooks (AC03) and adversarial test-sample factories
/// (AC04). Registers <see cref="LivenessEvaluationService"/> plus the synthetic provider/policy.
///
/// <b>AC05 (web wiring).</b> Maps <c>POST /api/v1/biometrics/face/liveness</c> - the EXACT path
/// `web/features/biometrics/live.ts` (BIO-UX-008) already targets, with the exact
/// <c>{ outcome, message, simulated }</c> response shape that same file's
/// <c>LivenessResult</c> type already expects. No change to any file under
/// <c>web/features/biometrics/**</c> or <c>web/lib/**</c> was made or is needed for this endpoint
/// to exist and be callable - see docs/features/bio-bio-008/README.md's "AC05" section for the
/// documented HYBRID state this lands in (the whole <c>BiometricService</c> module still resolves
/// as one mock/live switch today - BIO-UX-008 built it that way before any of its actions had a
/// real backend - splitting <c>checkLiveness</c> onto its own independently-switchable port
/// requires editing BIO-UX-005-owned shared files (`web/lib/feature-mode.ts`/`module-registry.ts`)
/// and needs Kemi's sign-off, requested but not yet received as of this submission).
/// </summary>
public sealed class BioBio008Module : IBiologModule
{
    public string Name => "Liveness";
    public IReadOnlyCollection<string> DependsOn => ["Tenant"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ILivenessPolicy, InMemoryLivenessPolicy>();
        services.AddSingleton<ILivenessProvider, SyntheticLivenessProvider>();
        services.AddSingleton<LivenessEvaluationService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/biometrics/face/liveness", async (
            LivenessCheckRequest? request, LivenessEvaluationService service, ITenantContextAccessor tenantAccessor, CancellationToken cancellationToken) =>
        {
            try
            {
                RequireTenant(tenantAccessor);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }

            // BIO-UX-008's own web contract sends only an optional scenario hint, never a real
            // capture (the web layer has no camera/getUserMedia call anywhere - see that story's
            // own README "Security requirement" note) - this endpoint demonstrates the REAL
            // policy/provider pipeline against a synthetic demonstration sample chosen from the
            // hint, the same honest "real mechanism, not a hardcoded response" framing
            // BIO-RPT-001's own /primary-action endpoint already documents for itself.
            var sample = request?.Hint switch
            {
                "spoof_suspected" => AdversarialSampleFactory.Create(AdversarialCaseKind.ScreenPresentation),
                "live" => AdversarialSampleFactory.Genuine(KnownLiveDemonstrationSeed),
                _ => AdversarialSampleFactory.Genuine(KnownLiveDemonstrationSeed),
            };

            LivenessEvidence evidence;
            try
            {
                evidence = await service.EvaluateAsync(sample, cancellationToken);
            }
            catch (LivenessException ex)
            {
                return Results.Problem(title: "liveness_evaluation_failed", detail: ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            // The web's own LivenessOutcome is binary (live/spoof_suspected) - it has no
            // "inconclusive" concept. Mapped conservatively: only a confident Live evidence
            // reports "live"; both SpoofDetected and Inconclusive report "spoof_suspected" (the
            // fail-safe direction for a high-assurance signal - "not confidently live" reads the
            // same as "treat with suspicion" to the UI today). Extending the web's own outcome
            // vocabulary to a genuine three-way split is a BIO-UX-005-owned shared-contract
            // change out of this backend story's scope.
            var outcome = evidence.Status == LivenessStatus.Live ? "live" : "spoof_suspected";
            var message = evidence.Status switch
            {
                LivenessStatus.Live => $"Live (provider={evidence.ProviderId}, confidence={evidence.Confidence:0.00}).",
                LivenessStatus.SpoofDetected => $"Spoof suspected (provider={evidence.ProviderId}, confidence={evidence.Confidence:0.00}, reason={evidence.Reason}).",
                _ => $"Inconclusive, treated as spoof-suspected (provider={evidence.ProviderId}, confidence={evidence.Confidence:0.00}, reason={evidence.Reason}).",
            };

            return Results.Ok(new { outcome, message, simulated = true });
        });
    }

    /// <summary>A fixed byte sequence whose SHA-256 digest deterministically lands
    /// <see cref="SyntheticLivenessProvider"/>'s hash-bucket rule on "Live" - see that type's own
    /// three-way split. Verified by <c>SyntheticLivenessProviderTests</c>.</summary>
    private static readonly byte[] KnownLiveDemonstrationSeed = [0x00, 0x00, 0x00, 0x01];

    private static string RequireTenant(ITenantContextAccessor tenantAccessor) =>
        tenantAccessor.Context?.TenantId ?? throw new TenantContextMissingException();

    private static IResult TenantContextProblem(TenantContextMissingException ex) => Results.Problem(
        title: "tenant_context_missing", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>Request body for the liveness endpoint - mirrors `web/features/biometrics/live.ts`'s
/// own `{ hint }` POST body exactly (BIO-UX-008-AC05).</summary>
public sealed record LivenessCheckRequest(string? Hint);
