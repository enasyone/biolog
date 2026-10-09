// Developer tests for BIO-RPT-003-AC04. SYNTHETIC fixtures only.
using Biolog.Features.AuditInvestigation.Domain;
using Xunit;

namespace Biolog.Features.AuditInvestigation.Tests;

public sealed class AuditViewRedactionTests
{
    [Theory]
    [InlineData("public")]
    [InlineData("internal")]
    public void Public_and_internal_classified_detail_is_never_hidden(string classification)
    {
        var record = TestData.Record("rbac", "evt_1", "rbac.role_assign", "role_assigned", "2026-09-01T00:00:00.000Z", classification,
            detail: new Dictionary<string, object?> { ["role"] = "AUDITOR" });

        var withPermission = AuditViewRedaction.Apply(record, canViewSensitiveDetail: true);
        var withoutPermission = AuditViewRedaction.Apply(record, canViewSensitiveDetail: false);

        Assert.Single(withPermission.Detail);
        Assert.Single(withoutPermission.Detail);
    }

    [Theory]
    [InlineData("personal")]
    [InlineData("sensitive")]
    public void Personal_and_sensitive_classified_detail_is_hidden_without_permission(string classification)
    {
        var record = TestData.Record("rbac", "evt_1", "rbac.role_assign", "role_assigned", "2026-09-01T00:00:00.000Z", classification,
            detail: new Dictionary<string, object?> { ["person_id"] = "per_1", ["role"] = "AUDITOR" });

        var result = AuditViewRedaction.Apply(record, canViewSensitiveDetail: false);

        Assert.Empty(result.Detail);
        Assert.Contains("person_id", result.Withheld);
        Assert.Contains("role", result.Withheld);
        Assert.Equal(classification, result.Classification);
    }

    [Theory]
    [InlineData("personal")]
    [InlineData("sensitive")]
    public void Personal_and_sensitive_classified_detail_is_revealed_with_permission(string classification)
    {
        var record = TestData.Record("rbac", "evt_1", "rbac.role_assign", "role_assigned", "2026-09-01T00:00:00.000Z", classification,
            detail: new Dictionary<string, object?> { ["person_id"] = "per_1" });

        var result = AuditViewRedaction.Apply(record, canViewSensitiveDetail: true);

        Assert.Single(result.Detail);
        Assert.Empty(result.Withheld);
    }

    [Fact]
    public void Secret_classified_detail_is_hidden_even_with_permission()
    {
        // ADR-0003: no producer in this codebase ever registers "secret" - this is defence in
        // depth against a future source adapter's own bug, not a reachable case today.
        var record = TestData.Record("rbac", "evt_1", "rbac.role_assign", "role_assigned", "2026-09-01T00:00:00.000Z", "secret",
            detail: new Dictionary<string, object?> { ["token"] = "should-never-appear" });

        var result = AuditViewRedaction.Apply(record, canViewSensitiveDetail: true);

        Assert.Empty(result.Detail);
        Assert.Contains("token", result.Withheld);
    }

    [Fact]
    public void An_unregistered_classification_string_is_treated_as_secret_and_always_hidden()
    {
        var record = TestData.Record("rbac", "evt_1", "rbac.role_assign", "role_assigned", "2026-09-01T00:00:00.000Z", "top-secret-typo",
            detail: new Dictionary<string, object?> { ["role"] = "AUDITOR" });

        var result = AuditViewRedaction.Apply(record, canViewSensitiveDetail: true);

        Assert.Empty(result.Detail);
    }

    [Fact]
    public void A_record_with_no_detail_at_all_is_returned_unchanged_even_when_sensitive_and_ungranted()
    {
        var record = TestData.Record("rbac", "evt_1", "rbac.role_assign", "role_assigned", "2026-09-01T00:00:00.000Z", "sensitive");

        var result = AuditViewRedaction.Apply(record, canViewSensitiveDetail: false);

        Assert.Empty(result.Detail);
        Assert.Empty(result.Withheld);
    }

    [Fact]
    public void Withheld_from_the_producer_is_preserved_alongside_view_time_withholding()
    {
        var record = TestData.Record("rbac", "evt_1", "rbac.role_assign", "role_assigned", "2026-09-01T00:00:00.000Z", "sensitive",
            detail: new Dictionary<string, object?> { ["role"] = "AUDITOR" },
            withheld: ["already_withheld_by_producer"]);

        var result = AuditViewRedaction.Apply(record, canViewSensitiveDetail: false);

        Assert.Contains("already_withheld_by_producer", result.Withheld);
        Assert.Contains("role", result.Withheld);
    }
}
