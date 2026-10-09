using System.Reflection;
using Biolog.Features.DocumentTemplatePack.Model;
using Xunit;

namespace Biolog.Features.DocumentTemplatePack.Tests;

/// <summary>Standing reflection-based lockdown regression test, the same "no public constructor,
/// no public property setter" discipline every prior BIO-DOC-00x result type in this epic carries
/// its own copy of.</summary>
public sealed class DocumentTemplatePackResultTypesAreLockedDownTests
{
    [Fact]
    public void DocumentTemplateVerificationOutcome_has_no_public_constructor()
    {
        var publicConstructors = typeof(DocumentTemplateVerificationOutcome).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(publicConstructors);
    }

    [Fact]
    public void DocumentTemplateVerificationOutcome_every_property_setter_is_non_public()
    {
        foreach (var property in typeof(DocumentTemplateVerificationOutcome).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var setter = property.GetSetMethod(nonPublic: false);
            Assert.True(setter is null, $"{property.Name} has a public setter");
        }
    }

    [Fact]
    public void DocumentTemplatePackEntry_has_no_public_constructor()
    {
        var publicConstructors = typeof(DocumentTemplatePackEntry).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(publicConstructors);
    }
}
