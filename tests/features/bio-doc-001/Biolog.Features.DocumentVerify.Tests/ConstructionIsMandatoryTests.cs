using System.Reflection;
using Biolog.Features.DocumentVerify.Model;
using Xunit;

namespace Biolog.Features.DocumentVerify.Tests;

/// <summary>
/// QA-03 (Chioma acceptance audit): a compile error is what actually proves a bypass is closed
/// (see the rewritten immutability tests and evidence/BIO-DOC-001/acceptance/InvalidEvidenceProbe),
/// but a compile error is not something the regular executable test suite can assert going
/// forward - it only protects the exact line that failed to compile once, at the moment someone
/// happened to try it. These reflection-based tests are the standing regression coverage: they
/// fail loudly, at test-run time, if a future change reintroduces a public constructor or a
/// public/internal property setter on any of the four types the finding named plus the one this
/// story locked down proactively for the same reason (<see cref="PersonFieldChangeProposal"/>).
/// </summary>
public sealed class ConstructionIsMandatoryTests
{
    public static IEnumerable<object[]> LockedDownTypes() => new[]
    {
        new object[] { typeof(DocumentEvidence) },
        new object[] { typeof(DocumentFieldEvidence) },
        new object[] { typeof(BoundingBox) },
        new object[] { typeof(PersonFieldChangeProposal) },
    };

    [Theory]
    [MemberData(nameof(LockedDownTypes))]
    public void QA03_type_exposes_no_public_constructor(Type type)
    {
        var publicConstructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(publicConstructors);
    }

    [Theory]
    [MemberData(nameof(LockedDownTypes))]
    public void QA03_every_property_has_no_public_or_internal_setter_or_init_accessor(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var setter = property.GetSetMethod(nonPublic: false);
            Assert.True(setter is null, $"{type.Name}.{property.Name} exposes a public setter/init accessor.");
        }
    }
}
