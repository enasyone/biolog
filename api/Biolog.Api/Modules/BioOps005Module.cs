using Biolog.Api.Tenancy;
using Biolog.Features.AuthenticityAssessment.Api;
using Biolog.Features.AuthenticityAssessment.Model;
using Biolog.Features.DocumentTemplatePack.Api;
using Biolog.Features.DocumentTemplatePack.Model;
using Biolog.Features.DocumentVerify.Storage;
using Biolog.Features.DocumentVerifyCoordination;
using Biolog.Features.DocumentVerifyCoordination.Api;
using Biolog.Features.DocumentVerifyCoordination.Gateways;
using Biolog.Features.DocumentVerifyCoordination.Tenancy;
using Biolog.Features.EvidenceComparison.Model;

namespace Biolog.Api.Modules;

/// <summary>
/// Document Verify evidence coordinator integration contract (BIO-OPS-005), registered the same
/// way every sibling module wraps its feature project.
///
/// Kemi's ruling, point 2: <see cref="CurrentTenantProviderAdapter"/> only reads the
/// already-shipped <see cref="ITenantContextAccessor"/> (BIO-TEN-002) - the identical shape
/// BIO-DOC-011's own module uses for its own <c>ICurrentTenantProvider</c>.
/// <see cref="AuthenticityAssessmentGatewayAdapter"/>/<see cref="DocumentIdentificationGatewayAdapter"/>
/// wrap BIO-DOC-009's/BIO-DOC-010's own already-registered <c>AuthenticityAssessmentService</c>/
/// <c>DocumentTemplatePackService</c> directly - reachable here only because
/// <c>api/Biolog.Api/Biolog.Api.csproj</c> already references both feature projects for their own
/// modules - the same cross-story gateway pattern <c>ICardIssueGateway</c> already established.
/// No <c>ProjectReference</c> is added from the coordinator's own feature project to BIO-DOC-009/010.
/// </summary>
public sealed class BioOps005Module : IBiologModule
{
    public string Name => "DocumentVerifyCoordination";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "Document", "AuthenticityAssessment", "DocumentTemplatePack"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentTenantProvider>(sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));

        services.AddSingleton<DocumentComparisonVersioningState>();
        services.AddSingleton<DocumentVerifyLockRegistry>();

        services.AddScoped<IAuthenticityAssessmentGateway>(
            sp => new AuthenticityAssessmentGatewayAdapter(sp.GetRequiredService<AuthenticityAssessmentService>()));
        services.AddScoped<IDocumentIdentificationGateway>(
            sp => new DocumentIdentificationGatewayAdapter(sp.GetRequiredService<DocumentTemplatePackService>()));

        services.AddScoped<DocumentVerifyCoordinatorService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        DocumentVerifyCoordinatorEndpoints.Map(endpoints);
    }

    private sealed class CurrentTenantProviderAdapter(ITenantContextAccessor accessor) : ICurrentTenantProvider
    {
        public string? TenantId => accessor.Context?.TenantId;
    }

    /// <summary>
    /// Translates between this coordinator's own plain <see cref="EngineCheckInput"/>/
    /// <see cref="CrossComparisonResult"/> shape and BIO-DOC-009's own <c>AuthenticityAssessmentInputs"/>/
    /// <c>AuthenticityDisposition</c> - the ONLY place either side's enum needs to be known to the
    /// other.
    /// </summary>
    private sealed class AuthenticityAssessmentGatewayAdapter(AuthenticityAssessmentService service) : IAuthenticityAssessmentGateway
    {
        public Task<AuthenticityAssessmentSnapshot> ComputeFromEvidenceAsync(
            string documentSessionId,
            EngineCheckInput mrz,
            EngineCheckInput barcode,
            EngineCheckInput ocr,
            CrossComparisonResult crossComparison,
            string actorId,
            string correlationId,
            CancellationToken cancellationToken = default)
        {
            var inputs = new AuthenticityAssessmentInputs
            {
                DocumentSessionId = documentSessionId,
                MrzOutcome = ToCheckOutcome(mrz.Outcome),
                MrzDetail = mrz.Detail,
                BarcodeOutcome = ToCheckOutcome(barcode.Outcome),
                BarcodeDetail = barcode.Detail,
                OcrOutcome = ToCheckOutcome(ocr.Outcome),
                OcrDetail = ocr.Detail,
                Classification = null, // BIO-DOC-006 classification is not this coordinator's own concern - see IdentifyDocumentGatewayAdapter.
                CrossComparison = crossComparison,
            };

            var assessment = service.ComputeFromEvidence(inputs, actorId, correlationId);
            var disposition = assessment.Result.PolicyDisposition;

            // "Confidence" here is how CERTAIN the system is about the reported state, not how
            // likely the document is authentic - a definitive Failed verdict is just as certain
            // as a definitive Authentic one; only Inconclusive is genuinely uncertain.
            var confidence = disposition == AuthenticityDisposition.Inconclusive ? 0.5 : 1.0;
            var summary = $"Authenticity assessment: {disposition.ToWireString()} ({assessment.Result.RulesetVersion}, evidence version {assessment.Result.EvidenceVersion}).";

            return Task.FromResult(new AuthenticityAssessmentSnapshot(disposition.ToEvidenceCheckState(), confidence, summary));
        }

        private static AuthenticityCheckOutcome ToCheckOutcome(EngineCheckOutcome outcome) => outcome switch
        {
            EngineCheckOutcome.NotPerformed => AuthenticityCheckOutcome.NotPerformed,
            EngineCheckOutcome.Passed => AuthenticityCheckOutcome.Passed,
            EngineCheckOutcome.Failed => AuthenticityCheckOutcome.Failed,
            EngineCheckOutcome.Inconclusive => AuthenticityCheckOutcome.Inconclusive,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "unreachable: exhaustive switch"),
        };
    }

    private sealed class DocumentIdentificationGatewayAdapter(DocumentTemplatePackService service) : IDocumentIdentificationGateway
    {
        public Task<IdentificationSnapshot> IdentifyFromEvidenceAsync(
            string documentSessionId,
            string entryId,
            IReadOnlyDictionary<string, string> observedFields,
            string actorId,
            string correlationId,
            CancellationToken cancellationToken = default)
        {
            var outcome = service.IdentifyFromEvidence(entryId, observedFields, actorId, correlationId);

            // Same "certainty, not likelihood" confidence scheme as the authenticity adapter.
            var confidence = outcome.State == Biolog.Features.DocumentTemplatePack.Model.DocumentTemplateVerificationState.Inconclusive ? 0.5 : 1.0;
            var summary = outcome.Violations.Count > 0
                ? $"Identified against {outcome.EntryId} ({outcome.Issuer}) with {outcome.Violations.Count} field violation(s)."
                : $"Identified against {outcome.EntryId} ({outcome.Issuer}) with no field violations.";

            return Task.FromResult(new IdentificationSnapshot(outcome.State.ToWireString(), confidence, summary));
        }
    }
}
