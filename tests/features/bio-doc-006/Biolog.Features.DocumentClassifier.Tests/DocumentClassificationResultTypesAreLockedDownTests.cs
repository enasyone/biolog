using System.Reflection;
using Biolog.Features.DocumentClassifier.Model;
using Xunit;

namespace Biolog.Features.DocumentClassifier.Tests;

/// <summary>
/// Standing regression test for this story's own trust-bearing result types (the QA-03 lesson,
/// carried forward from BIO-DOC-001 through every sibling BIO-DOC-00x engine this epic):
/// <see cref="DocumentClassCandidate"/> and <see cref="DocumentClassificationOutcome"/> must have
/// no public constructor and every property must be <c>internal init</c>, never a plain public
/// <c>init</c> a caller could launder a result through via <c>with</c>.
/// </summary>
public sealed class DocumentClassificationResultTypesAreLockedDownTests
{
    public static IEnumerable<object[]> LockedDownTypes =>
    [
        [typeof(DocumentClassCandidate)],
        [typeof(DocumentClassificationOutcome)],
    ];

    [Theory]
    [MemberData(nameof(LockedDownTypes))]
    public void has_no_public_constructor(Type type)
    {
        Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    [Theory]
    [MemberData(nameof(LockedDownTypes))]
    public void every_property_setter_is_non_public(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Assert.True(
                property.GetSetMethod(nonPublic: false) is null,
                $"{type.Name}.{property.Name} has a public setter");
        }
    }
}
