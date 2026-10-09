namespace Biolog.Features.PersonRegistry.Domain;

/// <summary>
/// Computes the display "Full Name" the Master Technical Specification §7 names as one of
/// PERSON's six dynamic name fields, from the four stored components
/// (<see cref="Person.Title"/>, <see cref="Person.FirstName"/>, <see cref="Person.MiddleName"/>,
/// <see cref="Person.LastName"/>) - see <see cref="Person"/>'s class header for why this is a
/// derived value computed on read (here, and in <c>Api/PersonResponse.cs</c>), never a stored
/// field of its own.
/// </summary>
public static class PersonNameFormatter
{
    public static string FullName(Person person) =>
        string.Join(' ', new[] { person.Title, person.FirstName, person.MiddleName, person.LastName }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
}
