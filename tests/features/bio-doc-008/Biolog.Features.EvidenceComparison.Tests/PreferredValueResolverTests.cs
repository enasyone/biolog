using Biolog.Features.EvidenceComparison.Model;
using Xunit;

namespace Biolog.Features.EvidenceComparison.Tests;

/// <summary>Developer tests for BIO-DOC-008-AC04 ("no source is silently preferred without
/// documented policy").</summary>
public sealed class PreferredValueResolverTests
{
    private static SourceFieldValue Value(EvidenceSourceKind source, string normalized) =>
        SourceFieldValue.Create(source, normalized, normalized, 0.9);

    [Fact]
    public void AC04_resolves_the_value_from_the_highest_precedence_source_present()
    {
        var comparison = ComparisonEngine.Compare("surname", new Dictionary<EvidenceSourceKind, SourceFieldValue>
        {
            [EvidenceSourceKind.Visual] = Value(EvidenceSourceKind.Visual, "OKAFOR"),
            [EvidenceSourceKind.Mrz] = Value(EvidenceSourceKind.Mrz, "OKAFOR-M"),
        });
        var policy = SourcePrecedencePolicy.Create([EvidenceSourceKind.Mrz, EvidenceSourceKind.Visual]);

        var preferred = PreferredValueResolver.ResolvePreferredValue(comparison, policy);

        Assert.NotNull(preferred);
        Assert.Equal(EvidenceSourceKind.Mrz, preferred!.Source);
    }

    [Fact]
    public void AC04_falls_through_to_the_next_named_source_when_the_top_preference_is_absent()
    {
        var comparison = ComparisonEngine.Compare("surname", new Dictionary<EvidenceSourceKind, SourceFieldValue>
        {
            [EvidenceSourceKind.Visual] = Value(EvidenceSourceKind.Visual, "OKAFOR"),
        });
        var policy = SourcePrecedencePolicy.Create([EvidenceSourceKind.Mrz, EvidenceSourceKind.Visual]);

        var preferred = PreferredValueResolver.ResolvePreferredValue(comparison, policy);

        Assert.NotNull(preferred);
        Assert.Equal(EvidenceSourceKind.Visual, preferred!.Source);
    }

    [Fact]
    public void AC04_returns_null_when_no_source_named_in_the_policy_contributed_a_value_never_a_guess()
    {
        var comparison = ComparisonEngine.Compare("surname", new Dictionary<EvidenceSourceKind, SourceFieldValue>
        {
            [EvidenceSourceKind.Barcode] = Value(EvidenceSourceKind.Barcode, "OKAFOR"),
        });
        var policy = SourcePrecedencePolicy.Create([EvidenceSourceKind.Mrz, EvidenceSourceKind.Visual]);

        var preferred = PreferredValueResolver.ResolvePreferredValue(comparison, policy);

        Assert.Null(preferred);
    }

    [Fact]
    public void AC04_a_policy_must_name_at_least_one_source()
    {
        Assert.Throws<ArgumentException>(() => SourcePrecedencePolicy.Create([]));
    }

    [Fact]
    public void AC04_a_policy_must_not_repeat_a_source()
    {
        Assert.Throws<ArgumentException>(() => SourcePrecedencePolicy.Create([EvidenceSourceKind.Mrz, EvidenceSourceKind.Mrz]));
    }

    [Fact]
    public void AC04_no_method_picks_a_value_out_of_a_comparison_without_requiring_an_explicit_precedence_policy()
    {
        // Structural proof: no public method anywhere in this assembly takes a FieldComparison and
        // returns a single SourceFieldValue from it UNLESS it also requires a SourcePrecedencePolicy
        // parameter - the only path to "the" value for a field is through this one, explicitly
        // policy-driven method. This targets the actual AC04 shape (picking one value out of a
        // multi-source comparison), not every unrelated method that happens to construct or return
        // a SourceFieldValue (e.g. SourceFieldValue.Create/FromMrz, which are plain input factories).
        var assembly = typeof(ComparisonEngine).Assembly;
        var offendingMethods = assembly.GetTypes()
            .Where(t => t.IsPublic)
            .SelectMany(t => t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly))
            .Where(m => m.ReturnType == typeof(SourceFieldValue))
            .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(FieldComparison)))
            .Where(m => !m.GetParameters().Any(p => p.ParameterType == typeof(SourcePrecedencePolicy)))
            .ToList();

        Assert.Empty(offendingMethods);
    }
}
