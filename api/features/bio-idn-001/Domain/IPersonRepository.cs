namespace Biolog.Features.PersonRegistry.Domain;

/// <summary>
/// Storage for <see cref="Person"/>. Deliberately has NO delete/remove method anywhere on
/// this interface (BIO-IDN-001-AC04, see <see cref="Person"/>'s class header) - a structural
/// test asserts this adversarially, by reflection, so a future addition cannot slip a hard
/// delete in without that test failing first.
///
/// Every read and write method takes <c>tenantId</c> explicitly and asserts a matching
/// record's own <see cref="Person.TenantId"/> before returning it - the same belt-and-braces
/// rule <c>InMemoryDeviceRepository</c> (BIO-DEV-001) applies on top of
/// <see cref="PersonService"/>'s own tenant-context check, so a bug that let one tenant's
/// person leak into a query meant for another is caught here too, not merely assumed away.
///
/// <b><c>internal</c> (BIO-IDN-001-QA-01, Seyi round 1).</b> Before this fix, this interface
/// and <see cref="InMemoryPersonRepository"/> were public, and <see cref="Person"/>'s
/// properties were publicly settable - so any code that could reference this assembly, not
/// just <see cref="PersonService"/>, could call <see cref="CreateAsync"/>/
/// <see cref="ReplaceAsync"/> directly with a hand-built <see cref="Person"/>, skipping the
/// tenant boundary, the governed lifecycle table and the audit trail entirely (an independent
/// probe reproduced exactly this: move a person to a different tenant and jump it straight to
/// <see cref="PersonStatus.Archived"/> with one direct call). This interface, its
/// implementation, and <see cref="Person"/>'s constructor are now all <c>internal</c>,
/// restricted to this assembly plus <c>Biolog.Api</c> (DI registration) and
/// <c>Biolog.Features.PersonRegistry.Tests</c> (an explicit, auditable test seam) via
/// <c>InternalsVisibleTo</c> - mirroring BIO-TEN-002-QA-03's identical fix for
/// <c>ITenantContextAccessor</c>/<c>TenantContext</c>. See <see cref="InMemoryPersonRepository"/>
/// for the additional belt-and-braces validation this alone does not replace.
/// </summary>
internal interface IPersonRepository
{
    /// <summary>Atomic by <see cref="Person.Id"/> (a freshly minted UUIDv7, so a collision is not a real-world outcome this needs to handle gracefully - only detect it, never silently overwrite).</summary>
    Task<Person> CreateAsync(Person person, CancellationToken cancellationToken = default);

    Task<Person?> GetAsync(string tenantId, string personId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Person>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Compare-and-set against <paramref name="expectedCurrent"/> (ADR-0001 Decision 3.2).</summary>
    Task<Person> ReplaceAsync(Person expectedCurrent, Person updated, CancellationToken cancellationToken = default);
}
