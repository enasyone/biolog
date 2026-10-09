using Biolog.Features.EvidenceComparison.Model;
using Xunit;

namespace Biolog.Features.EvidenceComparison.Tests;

/// <summary>Developer tests for BIO-DOC-008-AC01 ("comparable fields show source-by-source values
/// and match/mismatch/unknown").</summary>
public sealed class ComparisonEngineTests
{
    private static SourceFieldValue Value(EvidenceSourceKind source, string normalized) =>
        SourceFieldValue.Create(source, normalized, normalized, 0.9);

    [Fact]
    public void AC01_two_agreeing_sources_report_a_match()
    {
        var values = new Dictionary<EvidenceSourceKind, SourceFieldValue>
        {
            [EvidenceSourceKind.Mrz] = Value(EvidenceSourceKind.Mrz, "OKAFOR"),
            [EvidenceSourceKind.Visual] = Value(EvidenceSourceKind.Visual, "OKAFOR"),
        };

        var comparison = ComparisonEngine.Compare("surname", values);

        Assert.Equal(FieldMatchStatus.Match, comparison.Status);
        Assert.Equal(2, comparison.ValuesBySource.Count);
    }

    [Fact]
    public void AC01_two_disagreeing_sources_report_a_mismatch()
    {
        var values = new Dictionary<EvidenceSourceKind, SourceFieldValue>
        {
            [EvidenceSourceKind.Mrz] = Value(EvidenceSourceKind.Mrz, "OKAFOR"),
            [EvidenceSourceKind.Visual] = Value(EvidenceSourceKind.Visual, "OKAFOR-JONES"),
        };

        var comparison = ComparisonEngine.Compare("surname", values);

        Assert.Equal(FieldMatchStatus.Mismatch, comparison.Status);
    }

    [Fact]
    public void AC01_fewer_than_two_sources_reports_unknown_never_a_trivial_match()
    {
        var values = new Dictionary<EvidenceSourceKind, SourceFieldValue>
        {
            [EvidenceSourceKind.Mrz] = Value(EvidenceSourceKind.Mrz, "OKAFOR"),
        };

        var comparison = ComparisonEngine.Compare("surname", values);

        Assert.Equal(FieldMatchStatus.Unknown, comparison.Status);
    }

    [Fact]
    public void AC01_zero_sources_reports_unknown()
    {
        var comparison = ComparisonEngine.Compare("surname", new Dictionary<EvidenceSourceKind, SourceFieldValue>());

        Assert.Equal(FieldMatchStatus.Unknown, comparison.Status);
    }

    [Fact]
    public void AC01_three_sources_where_only_two_agree_is_still_a_mismatch()
    {
        var values = new Dictionary<EvidenceSourceKind, SourceFieldValue>
        {
            [EvidenceSourceKind.Mrz] = Value(EvidenceSourceKind.Mrz, "OKAFOR"),
            [EvidenceSourceKind.Visual] = Value(EvidenceSourceKind.Visual, "OKAFOR"),
            [EvidenceSourceKind.Barcode] = Value(EvidenceSourceKind.Barcode, "0KAFOR"), // zero, not letter O
        };

        var comparison = ComparisonEngine.Compare("surname", values);

        Assert.Equal(FieldMatchStatus.Mismatch, comparison.Status);
    }
}
