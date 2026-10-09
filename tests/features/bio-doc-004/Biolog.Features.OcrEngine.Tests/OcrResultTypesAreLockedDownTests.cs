using System.Reflection;
using Biolog.Features.OcrEngine.Model;
using Xunit;

namespace Biolog.Features.OcrEngine.Tests;

/// <summary>
/// Standing regression coverage for BIO-DOC-004's trust-bearing result types
/// (<see cref="OcrFieldRead"/>, <see cref="TemplateOcrResult"/>, <see cref="GenericTextBlock"/>):
/// no external assembly can construct or copy-modify one, only read it - the same pattern
/// established by BIO-DOC-002's <c>MrzResultTypesAreLockedDownTests</c> and BIO-DOC-003's
/// <c>BarcodeResultTypesAreLockedDownTests</c> (both following BIO-DOC-001's QA-03 finding).
/// </summary>
public sealed class OcrResultTypesAreLockedDownTests
{
    public static IEnumerable<object[]> LockedDownTypes() =>
    [
        [typeof(OcrFieldRead)],
        [typeof(TemplateOcrResult)],
        [typeof(GenericTextBlock)],
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
