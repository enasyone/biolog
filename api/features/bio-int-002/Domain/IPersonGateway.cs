namespace Biolog.Features.Integrations.Domain;

/// <summary>
/// This feature's own narrow seam onto BIO-IDN-001's universal PERSON - implemented in
/// <c>Biolog.Api.Modules.BioInt002Module</c> by wrapping that story's own already-registered
/// (internal, <c>InternalsVisibleTo Biolog.Api</c>) <c>PersonService</c> directly, the same
/// cross-story gateway pattern every sibling story uses. Deliberately exposes ONLY what
/// BIO-IDN-001 itself exposes today - create and read - no update: see
/// <see cref="ImportBatchService"/>'s own header (AC04) for why the absence of a profile-mutator
/// method here is a load-bearing design decision, not an oversight.
/// </summary>
public interface IPersonGateway
{
    Task<PersonView> CreateAsync(CreatePersonFields fields, string actorId, string actorTypeWire, string correlationId, CancellationToken cancellationToken = default);

    Task<PersonView?> GetAsync(string personId, CancellationToken cancellationToken = default);
}

/// <summary>A read-only projection of BIO-IDN-001's own <c>Person</c> - this feature's own local type, never that story's own (cross-project types never cross a gateway interface boundary).</summary>
public sealed record PersonView
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public string? Title { get; init; }
    public required string FirstName { get; init; }
    public string? MiddleName { get; init; }
    public required string LastName { get; init; }
    public string? PreferredName { get; init; }
    public string? BornOn { get; init; }
    public required string StatusWire { get; init; }
}

/// <summary>The subset of a new PERSON's fields this story's import can ever populate at creation time - exactly the fields <c>PersonService.RegisterAsync</c> itself accepts.</summary>
public sealed record CreatePersonFields
{
    public string? Title { get; init; }
    public required string FirstName { get; init; }
    public string? MiddleName { get; init; }
    public required string LastName { get; init; }
    public string? PreferredName { get; init; }
    public string? BornOn { get; init; }
}
