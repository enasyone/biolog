using Biolog.Features.MrzEngine.Model;
using Xunit;

namespace Biolog.Features.MrzEngine.Tests;

/// <summary>Developer tests for BIO-DOC-002-AC02's "configured" formats: format recognition is
/// data-driven from <see cref="MrzFormatRegistry"/>, not a closed switch statement over three
/// hard-coded shapes.</summary>
public sealed class MrzFormatRegistryTests
{
    [Fact]
    public void the_three_icao_standard_formats_are_always_registered()
    {
        Assert.Contains(MrzFormatRegistry.All, f => f.Kind == MrzFormatKind.Td1);
        Assert.Contains(MrzFormatRegistry.All, f => f.Kind == MrzFormatKind.Td2);
        Assert.Contains(MrzFormatRegistry.All, f => f.Kind == MrzFormatKind.Td3);
    }

    [Fact]
    public void the_two_visa_formats_are_registered_as_configuration_not_hard_coded_into_the_engine()
    {
        Assert.Contains(MrzFormatRegistry.All, f => f.Kind == MrzFormatKind.MrvA);
        Assert.Contains(MrzFormatRegistry.All, f => f.Kind == MrzFormatKind.MrvB);
    }

    [Fact]
    public void a_newly_registered_format_becomes_resolvable_by_its_shape()
    {
        // A shape (2 lines x 20 columns) no ICAO/MRV format uses - proves Register() is the real
        // extension seam and does not collide with any other test's detection/parsing coverage.
        var descriptor = new MrzFormatDescriptor(
            Kind: MrzFormatKind.Td2, // reusing a Kind enum value is fine - Kind is not the lookup key.
            LineCount: 2,
            LineLength: 20,
            HasCompositeCheckDigit: false,
            DocumentCodeDiscriminator: c => c == 'Z',
            Fields: [new MrzFieldSpec(MrzFieldKind.DocumentNumber, Line: 0, Start: 0, Length: 20, HasCheckDigit: false)]);

        MrzFormatRegistry.Register(descriptor);

        var resolved = MrzFormatRegistry.ResolveByShape(2, 20);
        Assert.Contains(resolved, f => ReferenceEquals(f, descriptor));
    }

    [Fact]
    public void resolving_by_document_code_never_guesses_between_ambiguous_or_unmatched_candidates()
    {
        var matchesEverything = new MrzFormatDescriptor(
            MrzFormatKind.Td3, 2, 44, true, _ => true, Fields: []);
        var alsoMatchesEverything = new MrzFormatDescriptor(
            MrzFormatKind.MrvA, 2, 44, false, _ => true, Fields: []);
        var matchesNothing = new MrzFormatDescriptor(
            MrzFormatKind.Td2, 2, 44, true, _ => false, Fields: []);

        Assert.Null(MrzFormatRegistry.ResolveByDocumentCode([matchesEverything, alsoMatchesEverything], 'P'));
        Assert.Null(MrzFormatRegistry.ResolveByDocumentCode([matchesNothing], 'P'));
    }
}
