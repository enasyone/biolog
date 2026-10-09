using Biolog.Features.Biometrics.Domain;

namespace Biolog.Features.FingerprintSearch.Domain;

/// <summary>
/// AC01/AC05: the searchable POPULATION a 1:N search compares a probe against - deliberately a
/// separate concept from BIO-BIO-001's own <c>Matching.IBiometricTemplateStore</c> (see this
/// project's csproj remarks for why: that store cannot enumerate every enrolled identity in a
/// tenant, only fetch one identity's own templates, and this project cannot modify it to add
/// that capability). One entry per (tenant, person) - the LATEST indexed template replaces any
/// prior one for that identity, the same "one authorised reference" resolution every prior
/// BIO-BIO verification story uses.
///
/// <b>Population is caller-driven, not automatic.</b> Nothing in this feature hooks into
/// BIO-BIO-002's enrolment completion to auto-populate this gallery - doing so would require a
/// query BIO-BIO-002 does not expose and modifying files outside this story's write scope (see
/// csproj remarks). <see cref="Service.FingerprintSearchService.IndexAsync"/> is the explicit
/// registration step a caller (an operator, or a later orchestration story) invokes to add an
/// identity to the searchable gallery.
///
/// Internal - reachable only from this assembly, <c>Biolog.Api</c> (DI registration) and this
/// feature's own test project (BIO-IDN-001-QA-01's governance shape).
/// </summary>
internal interface IFingerprintGalleryStore
{
    Task IndexAsync(BiometricTemplate template, CancellationToken cancellationToken);

    /// <summary>AC05: every indexed template for one tenant, and ONLY that tenant - never
    /// another tenant's. Empty (never null) when nothing has been indexed.</summary>
    Task<IReadOnlyList<BiometricTemplate>> GetAllForTenantAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>BIO-BIO-009-AC02/AC04: removes this identity's own indexed entry, if any. Returns
    /// <c>true</c> only when an entry genuinely existed for this tenant and was removed;
    /// <c>false</c> for "nothing indexed" (never an exception). A plain persistence operation -
    /// no lock, no audit - matching <see cref="IndexAsync"/>'s own directness.</summary>
    Task<bool> RemoveAsync(string tenantId, string personId, CancellationToken cancellationToken);
}
