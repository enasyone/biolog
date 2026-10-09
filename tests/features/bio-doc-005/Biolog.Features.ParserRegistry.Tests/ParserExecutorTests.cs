using System.Text;
using Biolog.Features.BarcodeEngine.Model;
using Biolog.Features.ParserRegistry.Model;
using Xunit;

namespace Biolog.Features.ParserRegistry.Tests;

/// <summary>Developer tests for BIO-DOC-005-AC03 ("a parser specifies document/issuer
/// applicability, field mapping and validation").</summary>
public sealed class ParserExecutorTests
{
    private const string Now = "2026-09-22T12:00:00.000Z";
    private static readonly ParserCaller.Bts Bts = new();

    private static ParserVersionContent PublishSample(
        IReadOnlyList<ParserFieldMapping> fields,
        IReadOnlyList<ParserValidationRule> rules)
    {
        var catalog = new ParserCatalog(new TenantParserAuthorityRegistry());
        var applicability = ParserApplicability.Create(BarcodeSymbology.Pdf417, "US-CA-DMV");
        var record = catalog.Draft(Bts, "p1", new ParserOwnerScope.Global(), applicability, fields, rules, "author", Now);
        catalog.RecordTestResult(Bts, "p1", true, "tester", record.ConcurrencyVersion, Now);
        catalog.Publish(Bts, "p1", "approver", record.ConcurrencyVersion, Now);
        return record.Latest;
    }

    [Fact]
    public void AC03_maps_fixed_offset_fields_from_the_raw_payload()
    {
        var version = PublishSample(
            [ParserFieldMapping.Create("surname", 0, 6), ParserFieldMapping.Create("given_names", 6, 5)],
            []);
        var payload = Encoding.UTF8.GetBytes("OKAFORCHIOM");

        var result = ParserExecutor.Apply(version, payload);

        Assert.True(result.Success);
        Assert.Equal("OKAFOR", result.Fields[0].RawValue);
        Assert.Equal("CHIOM", result.Fields[1].RawValue);
    }

    [Fact]
    public void AC03_a_required_field_that_is_blank_fails_validation()
    {
        var version = PublishSample(
            [ParserFieldMapping.Create("surname", 0, 6)],
            [ParserValidationRule.Create("surname", required: true)]);
        var payload = Encoding.UTF8.GetBytes("      "); // blank

        var result = ParserExecutor.Apply(version, payload);

        Assert.False(result.Success);
        Assert.Equal("required_field_missing", result.Fields[0].FailureReason);
    }

    [Fact]
    public void AC03_a_pattern_mismatch_fails_validation_without_altering_the_raw_value()
    {
        var version = PublishSample(
            [ParserFieldMapping.Create("dob", 0, 6)],
            [ParserValidationRule.Create("dob", required: true, pattern: "^[0-9]{6}$")]);
        var payload = Encoding.UTF8.GetBytes("ABCDEF"); // not digits

        var result = ParserExecutor.Apply(version, payload);

        Assert.False(result.Success);
        Assert.Equal("ABCDEF", result.Fields[0].RawValue); // preserved, never "fixed"
        Assert.Equal("pattern_mismatch", result.Fields[0].FailureReason);
    }

    [Fact]
    public void AC03_a_field_extending_past_the_payload_length_fails_cleanly_never_throws()
    {
        var version = PublishSample([ParserFieldMapping.Create("field", 0, 20)], []);
        var payload = Encoding.UTF8.GetBytes("SHORT");

        var result = ParserExecutor.Apply(version, payload);

        Assert.False(result.Success);
        Assert.Equal("field_out_of_bounds", result.Fields[0].FailureReason);
    }

    [Fact]
    public void refuses_to_apply_a_draft_version_never_used_before_approval()
    {
        var catalog = new ParserCatalog(new TenantParserAuthorityRegistry());
        var applicability = ParserApplicability.Create(BarcodeSymbology.Pdf417, "US-CA-DMV");
        var record = catalog.Draft(Bts, "p2", new ParserOwnerScope.Global(), applicability,
            [ParserFieldMapping.Create("f", 0, 1)], [], "author", Now);

        Assert.Throws<InvalidOperationException>(() => ParserExecutor.Apply(record.Latest, [0x41]));
    }

    [Fact]
    public void SEC_F01_a_catastrophic_backtracking_pattern_times_out_instead_of_hanging_the_thread()
    {
        // Seyi's round-1 finding (BIO-DOC-005-F01, High): a tenant-authored validation pattern
        // matched with no timeout lets a crafted pattern + input pair consume unbounded CPU
        // (classic ReDoS). This field's pattern is exactly that shape; the input is chosen so the
        // backtracking engine cannot find a match and would otherwise search exponentially.
        var version = PublishSample(
            [ParserFieldMapping.Create("field", 0, 40)],
            [ParserValidationRule.Create("field", required: false, pattern: "^(a+)+$")]);
        var hostilePayload = Encoding.UTF8.GetBytes(new string('a', 39) + "!");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = ParserExecutor.Apply(version, hostilePayload);
        stopwatch.Stop();

        // Bounded by ParserExecutor.MatchTimeout plus normal overhead - proof this returns
        // promptly rather than hanging, not just that it eventually returns *some* answer.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"took {stopwatch.Elapsed} - timeout was not enforced");
        Assert.False(result.Success);
        Assert.Equal("pattern_timeout", result.Fields[0].FailureReason);
        // The raw value is preserved exactly as read, never hidden because validation timed out.
        Assert.Equal(new string('a', 39) + "!", result.Fields[0].RawValue);
    }

    [Fact]
    public void AC03_applicability_matches_symbology_issuer_and_optional_document_code()
    {
        var narrow = ParserApplicability.Create(BarcodeSymbology.Pdf417, "US-CA-DMV", "v2024");
        var wide = ParserApplicability.Create(BarcodeSymbology.Pdf417, "US-CA-DMV");

        Assert.True(narrow.Matches(BarcodeSymbology.Pdf417, "US-CA-DMV", "v2024"));
        Assert.False(narrow.Matches(BarcodeSymbology.Pdf417, "US-CA-DMV", "v2020"));
        Assert.True(wide.Matches(BarcodeSymbology.Pdf417, "US-CA-DMV", "any-version-at-all"));
        Assert.False(narrow.Matches(BarcodeSymbology.Code39, "US-CA-DMV", "v2024"));
    }
}
