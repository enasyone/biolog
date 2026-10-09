using System.Reflection;
using Biolog.Features.AuthenticityAssessment.Model;
using Xunit;

namespace Biolog.Features.AuthenticityAssessment.Tests;

/// <summary>Standing reflection-based lockdown regression test, the same "no public constructor,
/// no public property setter" discipline every prior BIO-DOC-00x result type in this epic carries
/// its own copy of (e.g. <c>EvidenceComparisonResultTypesAreLockedDownTests</c>) - applied here
/// from the start rather than waiting for a reviewer to find the gap.</summary>
public sealed class AuthenticityAssessmentResultTypesAreLockedDownTests
{
    private static readonly Type[] LockedDownRecordTypes =
    [
        typeof(AuthenticityCheckResult),
        typeof(AuthenticityAssessmentResult),
        typeof(AuthenticityManualOverride),
    ];

    [Theory]
    [MemberData(nameof(LockedDownTypesData))]
    public void has_no_public_constructor(Type type)
    {
        var publicConstructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(publicConstructors);
    }

    [Theory]
    [MemberData(nameof(LockedDownTypesData))]
    public void every_property_setter_is_non_public(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var setter = property.GetSetMethod(nonPublic: false);
            Assert.True(setter is null, $"{type.Name}.{property.Name} has a public setter");
        }
    }

    public static TheoryData<Type> LockedDownTypesData()
    {
        var data = new TheoryData<Type>();
        foreach (var type in LockedDownRecordTypes)
        {
            data.Add(type);
        }

        return data;
    }

    [Fact]
    public void AuthenticityAssessment_the_mutable_aggregate_has_no_public_constructor_either()
    {
        // A mutable class, not a record, but the same discipline applies for the same reason -
        // only FromResult (a factory over an already-Compute()'d result) may create one.
        var publicConstructors = typeof(Model.AuthenticityAssessment).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(publicConstructors);
    }

    [Fact]
    public void AuthenticityCheckResult_rejects_a_blank_detail()
    {
        // Two non-public constructors exist: this type's own 3-arg one, and the compiler-generated
        // record copy constructor (also non-public) - select by parameter count, not .Single().
        var ctor = typeof(AuthenticityCheckResult)
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(c => c.GetParameters().Length == 3);
        var ex = Assert.Throws<TargetInvocationException>(
            () => ctor.Invoke([AuthenticityCheckId.MrzStructuralValidity, AuthenticityCheckOutcome.Passed, "  "]));
        Assert.IsType<ArgumentException>(ex.InnerException);
    }
}
