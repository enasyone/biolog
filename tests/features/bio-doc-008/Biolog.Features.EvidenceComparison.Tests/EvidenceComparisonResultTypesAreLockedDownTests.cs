using System.Reflection;
using Biolog.Features.EvidenceComparison.Audit;
using Biolog.Features.EvidenceComparison.Model;
using Xunit;

namespace Biolog.Features.EvidenceComparison.Tests;

/// <summary>
/// Standing regression test for this story's own trust-bearing result types (the QA-03 lesson,
/// carried forward from BIO-DOC-001 through every sibling BIO-DOC-00x engine this epic):
/// <see cref="FieldComparison"/>, <see cref="CrossComparisonResult"/> and
/// <see cref="NfcCorroboration"/> must have no public constructor and every property must be
/// <c>internal init</c>, never a plain public <c>init</c> a caller could launder a result through
/// via <c>with</c>. <see cref="SourceFieldValue"/> and <see cref="SourcePrecedencePolicy"/> are
/// deliberately excluded - both are caller-supplied INPUT types with a public <c>Create</c>
/// factory, not engine-computed trust decisions (see their own remarks for why).
/// </summary>
public sealed class EvidenceComparisonResultTypesAreLockedDownTests
{
    public static IEnumerable<object[]> LockedDownTypes =>
    [
        [typeof(FieldComparison)],
        [typeof(CrossComparisonResult)],
        [typeof(NfcCorroboration)],
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

    [Fact]
    public void FieldComparison_and_CrossComparisonResult_collections_cannot_be_downcast_back_to_a_mutable_list_or_dictionary()
    {
        // Proactively applying the BIO-DOC-007-QA-02/QA-03 lesson's own regression shape here.
        var comparison = ComparisonEngine.Compare("surname", new Dictionary<EvidenceSourceKind, SourceFieldValue>
        {
            [EvidenceSourceKind.Mrz] = SourceFieldValue.Create(EvidenceSourceKind.Mrz, "X", "X", 0.9),
            [EvidenceSourceKind.Visual] = SourceFieldValue.Create(EvidenceSourceKind.Visual, "X", "X", 0.9),
        });
        var result = CrossComparisonResult.Compute(
            "session-1", 1, "actor", "2026-09-23T12:00:00.000Z", "corr-1",
            [comparison], NfcCorroboration.NotAvailable, new InMemoryEvidenceComparisonAuditSink(),
            new InMemoryVersionStore());

        Assert.IsNotType<Dictionary<EvidenceSourceKind, SourceFieldValue>>(comparison.ValuesBySource);
        Assert.IsNotType<List<FieldComparison>>(result.FieldComparisons);
        Assert.IsNotType<List<string>>(result.FieldsRequiringReview);
    }
}
