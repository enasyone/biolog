namespace Biolog.Features.Transport.Domain;

/// <summary>Storage for <see cref="PersonAssignment"/>. Same tenant-scoping and <c>internal</c> discipline as <see cref="IRouteRepository"/>. <see cref="ListByPersonAsync"/> exists specifically so <c>PersonAssignmentService</c> can check AC02's overlap invariant under its own <c>(TenantId, PersonId)</c> lock.</summary>
internal interface IPersonAssignmentRepository
{
    Task<PersonAssignment> CreateAsync(PersonAssignment record, CancellationToken cancellationToken = default);

    Task<PersonAssignment?> GetAsync(string tenantId, string assignmentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PersonAssignment>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PersonAssignment>> ListByPersonAsync(string tenantId, string personId, CancellationToken cancellationToken = default);

    Task<PersonAssignment> ReplaceAsync(PersonAssignment expectedCurrent, PersonAssignment updated, CancellationToken cancellationToken = default);
}
