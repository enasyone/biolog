using Biolog.Api.Tenancy;
using Biolog.Features.AccessDecisions;
using Biolog.Features.AccessDecisions.Api;
using Biolog.Features.AccessDecisions.Audit;
using Biolog.Features.AccessDecisions.Demo;
using Biolog.Features.AccessDecisions.Domain;
using Biolog.Features.AccessDecisions.Tenancy;

namespace Biolog.Api.Modules;

/// <summary>
/// Online access decision service (BIO-ACC-002), registered the same way every sibling
/// identity-wave module wraps its feature project - written directly here, in
/// <c>Biolog.Api.Modules</c>, never inside <c>api/features/bio-acc-002/**</c>, so the dependency
/// stays one-way.
///
/// <see cref="CurrentTenantProviderAdapter"/> only reads the already-shipped
/// <see cref="ITenantContextAccessor"/> (identical shape to every sibling module's own adapter).
///
/// <b>Four cross-story gateways, all wrapping an already-registered sibling service directly
/// (InternalsVisibleTo Biolog.Api), never a ProjectReference.</b> <see cref="PersonExistenceGatewayAdapter"/>
/// wraps BIO-IDN-001's own <c>PersonService.GetAsync</c>. <see cref="PolicyDecisionGatewayAdapter"/>
/// wraps BIO-ACC-001's own already-Scoped-registered <c>PolicyEvaluationService.EvaluateAsync</c>
/// directly (that service's own ADR-0006 permission check and audit trail are used unchanged -
/// this adapter only translates the wire shapes at the boundary, see AC01's own header on
/// <c>IPolicyDecisionGateway</c> for why). <see cref="FingerprintEvidenceGatewayAdapter"/>/
/// <see cref="FaceEvidenceGatewayAdapter"/> wrap BIO-BIO-004's/BIO-BIO-005's own already-
/// Scoped-registered <c>FingerprintVerificationService</c>/<c>FaceVerificationService</c> directly,
/// each catching that sibling's own "no authorised reference enrolled" exception and reporting
/// <see cref="BiometricComparisonOutcome.NotEnrolled"/> instead of letting it propagate as an
/// unhandled 500 (see that outcome's own header).
///
/// <b>AC05's demo seam.</b> <see cref="AccessDecisionDemoBridgeService"/> is registered Singleton
/// (headerless UI bridge, takes over <c>/api/v1/access/primary-action</c> and
/// <c>/api/v1/access/view-access-events</c> - both previously genuinely unwired, see
/// docs/features/bio-acc-002/README.md "AC05 web wiring"). <see cref="AccessDecisionDemoSeedProvisioner"/>
/// is a SEPARATE Singleton adapter from the Scoped <see cref="PolicyDecisionGatewayAdapter"/> (the
/// same captive-dependency avoidance <c>BioTrn002Module</c>'s own <c>TransportDemoSeedProvisioner</c>
/// documents) - it calls BIO-ACC-001's own PURE <c>PolicyEvaluator.Evaluate</c> directly with a
/// fixed <c>IdentityStatus.Active</c>, bypassing the RBAC/provenance gate entirely for the demo
/// path only (see <see cref="IAccessDecisionDemoSeedProvisioner"/>'s own header).
///
/// Depends on <see cref="TenantModule"/>, <see cref="BioIdn001Module"/> (registered as
/// <c>"PersonRegistry"</c>), <see cref="BioAcc001Module"/> (registered as <c>"AccessPolicy"</c>),
/// <see cref="BioBio004Module"/> (registered as <c>"FingerprintVerification"</c>) and
/// <see cref="BioBio005Module"/> (registered as <c>"FaceVerification"</c>).
/// </summary>
public sealed class BioAcc002Module : IBiologModule
{
    public string Name => "AccessDecisions";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "PersonRegistry", "AccessPolicy", "FingerprintVerification", "FaceVerification"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentTenantProvider>(sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));

        services.AddSingleton<IAccessDecisionEventRepository, InMemoryAccessDecisionEventRepository>();
        services.AddSingleton<IAccessDecisionAuditSink, InMemoryAccessDecisionAuditSink>();
        services.AddSingleton<AccessDecisionLockRegistry>();

        services.AddScoped<IPersonExistenceGateway>(
            sp => new PersonExistenceGatewayAdapter(sp.GetRequiredService<Biolog.Features.PersonRegistry.PersonService>()));
        services.AddScoped<IPolicyDecisionGateway>(
            sp => new PolicyDecisionGatewayAdapter(sp.GetRequiredService<Biolog.Features.AccessPolicy.PolicyEvaluationService>()));
        services.AddScoped<IFingerprintEvidenceGateway>(
            sp => new FingerprintEvidenceGatewayAdapter(
                sp.GetRequiredService<Biolog.Features.FingerprintVerification.Service.FingerprintVerificationService>(),
                sp.GetRequiredService<ITenantContextAccessor>()));
        services.AddScoped<IFaceEvidenceGateway>(
            sp => new FaceEvidenceGatewayAdapter(
                sp.GetRequiredService<Biolog.Features.FaceVerification.Service.FaceVerificationService>(),
                sp.GetRequiredService<ITenantContextAccessor>()));

        services.AddSingleton<IAccessDecisionDemoSeedProvisioner>(
            sp => new AccessDecisionDemoSeedProvisioner(sp.GetRequiredService<Biolog.Features.AccessPolicy.Domain.IAccessPolicyRepository>()));

        services.AddSingleton(_ => ActorProvenanceProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<ActorProvenanceGate>();

        services.AddScoped<AccessDecisionService>();
        services.AddSingleton<AccessDecisionDemoBridgeService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        AccessDecisionEndpoints.Map(endpoints);
    }

    private sealed class CurrentTenantProviderAdapter(ITenantContextAccessor accessor) : ICurrentTenantProvider
    {
        public string? TenantId => accessor.Context?.TenantId;
    }

    private sealed class PersonExistenceGatewayAdapter(Biolog.Features.PersonRegistry.PersonService personService) : IPersonExistenceGateway
    {
        public async Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default) =>
            await personService.GetAsync(personId, cancellationToken) is not null;
    }

    /// <summary>AC01/AC02/AC03's real evaluation seam onto BIO-ACC-001 - see this file's own class header.</summary>
    private sealed class PolicyDecisionGatewayAdapter(Biolog.Features.AccessPolicy.PolicyEvaluationService policyEvaluationService) : IPolicyDecisionGateway
    {
        public async Task<PolicyDecisionResult> EvaluateAsync(
            PolicyDecisionQuery query, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
        {
            var request = new Biolog.Features.AccessPolicy.Api.EvaluateAccessPolicyRequest
            {
                PersonId = query.PersonId,
                ProfileKey = query.ProfileKey,
                CredentialClass = query.CredentialClass,
                LocationId = query.LocationId,
                OccurredAt = query.OccurredAtUtc,
                PresentedCardStatus = query.PresentedCardStatus,
                PresentedAssuranceLevel = query.PresentedAssuranceLevel,
            };

            Biolog.Features.AccessPolicy.Domain.PolicyEvaluationResult result;
            try
            {
                result = await policyEvaluationService.EvaluateAsync(query.PolicyKey, request, ToTheirActor(actor), correlationId, cancellationToken);
            }
            catch (Biolog.Features.AccessPolicy.Domain.AccessPolicyError theirError)
            {
                // BIO-ACC-001's own RBAC/validation/audit failures surface through this gateway
                // boundary translated into this feature's own error type (same Code/Status/Detail)
                // rather than propagating an un-caught foreign exception type past this feature's
                // own endpoint's catch block.
                throw new AccessDecisionError(theirError.Message, theirError.Code, theirError.Status, theirError.Detail);
            }

            return new PolicyDecisionResult
            {
                Decision = result.Decision == Biolog.Features.AccessPolicy.Domain.PolicyEvaluationDecision.Allow ? AccessDecisionOutcome.Allow : AccessDecisionOutcome.Deny,
                ReasonCode = result.ReasonCode,
                PolicyKey = result.PolicyKey,
                PolicyVersionNumber = result.PolicyVersionNumber,
                MatchedRuleId = result.MatchedRuleId,
            };
        }

        private static Biolog.Features.AccessPolicy.Audit.AuditActor ToTheirActor(AuditActor actor) => new(
            actor.Type switch
            {
                ActorType.User => Biolog.Features.AccessPolicy.Audit.ActorType.User,
                ActorType.ServiceAccount => Biolog.Features.AccessPolicy.Audit.ActorType.ServiceAccount,
                ActorType.Device => Biolog.Features.AccessPolicy.Audit.ActorType.Device,
                ActorType.System => Biolog.Features.AccessPolicy.Audit.ActorType.System,
                ActorType.Anonymous => Biolog.Features.AccessPolicy.Audit.ActorType.Anonymous,
                _ => Biolog.Features.AccessPolicy.Audit.ActorType.Unknown,
            },
            actor.Id);
    }

    /// <summary>AC01's fingerprint evidence seam onto BIO-BIO-004 - see this file's own class header.</summary>
    private sealed class FingerprintEvidenceGatewayAdapter(
        Biolog.Features.FingerprintVerification.Service.FingerprintVerificationService verificationService,
        ITenantContextAccessor tenantAccessor) : IFingerprintEvidenceGateway
    {
        public async Task<BiometricEvidenceEntry> VerifyAsync(string personId, BiometricSampleInput sample, CancellationToken cancellationToken = default)
        {
            var tenantId = RequireTenant();
            var biometricSample = new Biolog.Features.Biometrics.Providers.BiometricSample
            {
                Payload = sample.Payload, ContentType = sample.ContentType, CapturedAtUtc = sample.CapturedAtUtc,
            };

            try
            {
                var record = await verificationService.VerifyAsync(tenantId, personId, biometricSample, cancellationToken);
                var outcome = record.ComparisonOutcome switch
                {
                    Biolog.Features.FingerprintVerification.Domain.BiometricComparisonOutcome.Matched => BiometricComparisonOutcome.Matched,
                    Biolog.Features.FingerprintVerification.Domain.BiometricComparisonOutcome.NotMatched => BiometricComparisonOutcome.NotMatched,
                    _ => BiometricComparisonOutcome.AcquisitionFailed,
                };
                return new BiometricEvidenceEntry { Modality = BiometricModality.Fingerprint, ComparisonOutcome = outcome, Score = record.Score };
            }
            catch (Biolog.Features.FingerprintVerification.Domain.FingerprintVerificationException)
            {
                return new BiometricEvidenceEntry { Modality = BiometricModality.Fingerprint, ComparisonOutcome = BiometricComparisonOutcome.NotEnrolled };
            }
        }

        private string RequireTenant() => tenantAccessor.Context?.TenantId ?? throw AccessDecisionError.TenantContextMissing();
    }

    /// <summary>AC01's face evidence seam onto BIO-BIO-005 - see this file's own class header.</summary>
    private sealed class FaceEvidenceGatewayAdapter(
        Biolog.Features.FaceVerification.Service.FaceVerificationService verificationService,
        ITenantContextAccessor tenantAccessor) : IFaceEvidenceGateway
    {
        public async Task<BiometricEvidenceEntry> VerifyAsync(string personId, BiometricSampleInput sample, CancellationToken cancellationToken = default)
        {
            var tenantId = RequireTenant();
            var biometricSample = new Biolog.Features.Biometrics.Providers.BiometricSample
            {
                Payload = sample.Payload, ContentType = sample.ContentType, CapturedAtUtc = sample.CapturedAtUtc,
            };

            try
            {
                var evidence = await verificationService.CompareAsync(tenantId, personId, biometricSample, cancellationToken);
                var outcome = evidence.ComparisonOutcome switch
                {
                    Biolog.Features.FaceVerification.Domain.FaceComparisonOutcome.Matched => BiometricComparisonOutcome.Matched,
                    Biolog.Features.FaceVerification.Domain.FaceComparisonOutcome.NotMatched => BiometricComparisonOutcome.NotMatched,
                    Biolog.Features.FaceVerification.Domain.FaceComparisonOutcome.ReferenceStale => BiometricComparisonOutcome.ReferenceStale,
                    _ => BiometricComparisonOutcome.AcquisitionFailed,
                };
                return new BiometricEvidenceEntry { Modality = BiometricModality.Face, ComparisonOutcome = outcome, Score = evidence.Score };
            }
            catch (Biolog.Features.FaceVerification.Domain.FaceVerificationException)
            {
                return new BiometricEvidenceEntry { Modality = BiometricModality.Face, ComparisonOutcome = BiometricComparisonOutcome.NotEnrolled };
            }
        }

        private string RequireTenant() => tenantAccessor.Context?.TenantId ?? throw AccessDecisionError.TenantContextMissing();
    }

    /// <summary>AC05's demo-only seam onto BIO-ACC-001's own policy aggregate/evaluator - see <see cref="IAccessDecisionDemoSeedProvisioner"/>'s own header for why this bypasses RBAC/provenance.</summary>
    private sealed class AccessDecisionDemoSeedProvisioner(Biolog.Features.AccessPolicy.Domain.IAccessPolicyRepository repository) : IAccessDecisionDemoSeedProvisioner
    {
        private const string AuthorActorId = "svc_access_decisions_demo_author";
        private const string PublisherActorId = "svc_access_decisions_demo_publisher";

        private readonly SemaphoreSlim _demoPolicyGate = new(1, 1);

        public async Task<DemoPolicySnapshot> EnsureDemoPolicyAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            await _demoPolicyGate.WaitAsync(cancellationToken);
            try
            {
                var policyKey = AccessDecisionDemoBridgeService.DemoPolicyKey;
                var existing = await repository.GetAsync(tenantId, policyKey, cancellationToken);
                var now = Biolog.Features.AccessPolicy.Identifiers.UtcInstant.Now();

                if (existing is null)
                {
                    var rules = new[] { PermissiveAllowRule() };
                    var draft = Biolog.Features.AccessPolicy.Domain.AccessPolicy.Draft(tenantId, policyKey, rules, AuthorActorId, now);
                    var created = await repository.CreateAsync(draft, cancellationToken);
                    var published = created.Publish(PublisherActorId, created.ConcurrencyVersion, now);
                    var replaced = await repository.ReplaceAsync(created, published, cancellationToken);
                    return Snapshot(replaced);
                }

                if (existing.PublishedVersionNumber is null)
                {
                    var published = existing.Publish(PublisherActorId, existing.ConcurrencyVersion, now);
                    var replaced = await repository.ReplaceAsync(existing, published, cancellationToken);
                    return Snapshot(replaced);
                }

                return Snapshot(existing);
            }
            finally
            {
                _demoPolicyGate.Release();
            }
        }

        public async Task<PolicyDecisionResult> EvaluateDemoPolicyAsync(
            string tenantId, string policyKey, PolicyDecisionQuery query, CancellationToken cancellationToken = default)
        {
            var policy = await repository.GetAsync(tenantId, policyKey, cancellationToken);
            var publishedVersion = policy?.GetPublishedVersion();

            DateTime.TryParse(
                query.OccurredAtUtc, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var occurredAtUtc);
            Biolog.Features.AccessPolicy.Domain.CardStatusExtensions.TryParse(query.PresentedCardStatus, out var presentedCardStatus);
            Biolog.Features.AccessPolicy.Domain.AssuranceLevelExtensions.TryParse(query.PresentedAssuranceLevel, out var presentedAssuranceLevel);

            var context = new Biolog.Features.AccessPolicy.Domain.PolicyEvaluationContext
            {
                PersonId = query.PersonId,
                ProfileKey = query.ProfileKey,
                CredentialClass = query.CredentialClass,
                LocationId = query.LocationId,
                OccurredAtUtc = occurredAtUtc,
                PresentedCardStatus = presentedCardStatus,
                PresentedAssuranceLevel = presentedAssuranceLevel,
            };

            var result = Biolog.Features.AccessPolicy.Domain.PolicyEvaluator.Evaluate(
                policyKey, publishedVersion, context, Biolog.Features.AccessPolicy.Domain.IdentityStatus.Active);

            return new PolicyDecisionResult
            {
                Decision = result.Decision == Biolog.Features.AccessPolicy.Domain.PolicyEvaluationDecision.Allow ? AccessDecisionOutcome.Allow : AccessDecisionOutcome.Deny,
                ReasonCode = result.ReasonCode,
                PolicyKey = result.PolicyKey,
                PolicyVersionNumber = result.PolicyVersionNumber,
                MatchedRuleId = result.MatchedRuleId,
            };
        }

        private static Biolog.Features.AccessPolicy.Domain.PolicyRule PermissiveAllowRule() => new()
        {
            RuleId = 1,
            Effect = Biolog.Features.AccessPolicy.Domain.PolicyRuleEffect.Allow,
            Subject = Biolog.Features.AccessPolicy.Domain.SubjectMatch.Any,
            Credential = Biolog.Features.AccessPolicy.Domain.CredentialClassMatch.Any,
            Location = Biolog.Features.AccessPolicy.Domain.LocationMatch.Any,
            Schedule = Biolog.Features.AccessPolicy.Domain.ScheduleMatch.Always,
            IdentityStatus = Biolog.Features.AccessPolicy.Domain.IdentityStatusMatch.Any,
            CardStatus = Biolog.Features.AccessPolicy.Domain.CardStatusMatch.Any,
            RequiredAssuranceLevel = Biolog.Features.AccessPolicy.Domain.AssuranceLevel.Basic,
        };

        private static DemoPolicySnapshot Snapshot(Biolog.Features.AccessPolicy.Domain.AccessPolicy policy) => new()
        {
            PolicyKey = policy.PolicyKey,
            PublishedVersionNumber = policy.PublishedVersionNumber ?? 0,
            RuleCount = policy.GetPublishedVersion()?.Rules.Count ?? 0,
        };
    }
}
