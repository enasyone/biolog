using System.Reflection;
using Biolog.Features.MrzEngine.Model;
using Xunit;

namespace Biolog.Features.MrzEngine.Tests;

/// <summary>
/// Standing regression coverage for BIO-DOC-002's trust-bearing result types
/// (<see cref="MrzFieldRead"/>, <see cref="MrzReadOutcome"/>): no external assembly - including
/// this test assembly - may construct or copy-modify one, only read it. This is the same
/// reflection-based pattern BIO-DOC-001's <c>ConstructionIsMandatoryTests</c> established after
/// QA-03 (a compile-error probe only protects the one line that failed to compile once;
/// reflection catches a future regression at every test run). Public accessibility is what is
/// asserted against here (not "internal specifically") because
/// <see cref="PropertyInfo.GetSetMethod(bool)"/> with <c>nonPublic: false</c> already treats
/// internal, protected and private identically as "not public" - which is exactly the guarantee
/// this suite (a separate assembly from the production code) needs: it cannot construct or copy
/// one of these either way.
/// </summary>
public sealed class MrzResultTypesAreLockedDownTests
{
    public static IEnumerable<object[]> LockedDownTypes() =>
    [
        [typeof(MrzFieldRead)],
        [typeof(MrzReadOutcome)],
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
