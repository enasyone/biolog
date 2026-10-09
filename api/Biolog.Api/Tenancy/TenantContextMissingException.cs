namespace Biolog.Api.Tenancy;

/// <summary>
/// ADR-0002 Decision 2: "there is no default tenant... if no channel supplies a tenant,
/// the request fails with <c>tenant_context_missing</c>. It does not proceed with an
/// empty, null or wildcard tenant." Any feature that reads <see cref="ITenantContextAccessor.Context"/>
/// and finds it <c>null</c> throws this rather than substituting a default or skipping the
/// tenant check - the fail-closed rule this codebase's tenant hook exists to make
/// unrepresentable to get wrong by accident.
///
/// Every device-registry endpoint throws this today, because BIO-FND-008's
/// <see cref="TenantContextMiddleware"/> deliberately resolves no tenant yet (no host,
/// principal or service channel exists in .NET yet) - that is the correct, honest current
/// behaviour per ADR-0002, not a defect in this story. A future story that implements real
/// tenant resolution populates the context this exception is checking for, and every
/// tenant-scoped feature built against <see cref="ITenantContextAccessor"/> - this one
/// included - starts working with no code change.
/// </summary>
public sealed class TenantContextMissingException() : Exception(
    "tenant_context_missing: no tenant channel has resolved a context for this request");
