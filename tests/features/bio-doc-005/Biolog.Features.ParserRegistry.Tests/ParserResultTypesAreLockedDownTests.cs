using System.Reflection;
using Biolog.Features.ParserRegistry.Model;
using Xunit;

namespace Biolog.Features.ParserRegistry.Tests;

/// <summary>
/// Standing regression coverage for BIO-DOC-005's trust-bearing/governance result types
/// (<see cref="ParserVersionContent"/>, <see cref="ParserFieldResult"/>,
/// <see cref="ParserExecutionResult"/>): no external assembly can construct or copy-modify one,
/// only read it. A public <c>init</c> on <see cref="ParserVersionContent"/> in particular would
/// let a caller launder a Draft version into one that claims <see cref="ParserStatus.Published"/>
/// via <c>with</c>, bypassing the maker-checker workflow (<see cref="ParserRecord.Publish"/>)
/// entirely - the same QA-03 lesson BIO-DOC-002/003/004 all applied to their own result types.
/// </summary>
public sealed class ParserResultTypesAreLockedDownTests
{
    public static IEnumerable<object[]> LockedDownTypes() =>
    [
        [typeof(ParserVersionContent)],
        [typeof(ParserFieldResult)],
        [typeof(ParserExecutionResult)],
    ];

    [Theory]
    [MemberData(nameof(LockedDownTypes))]
    public void type_exposes_no_public_constructor(Type type)
    {
        var publicConstructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(publicConstructors);
    }

    [Theory]
    [MemberData(nameof(LockedDownTypes))]
    public void every_property_has_no_public_setter_or_init_accessor(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var setter = property.GetSetMethod(nonPublic: false);
            Assert.True(setter is null, $"{type.Name}.{property.Name} exposes a public setter/init accessor.");
        }
    }
}
