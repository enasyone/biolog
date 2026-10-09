// Developer tests for BIO-RPT-002. SYNTHETIC fixtures only.
using Biolog.Features.ReportBuilder.Domain;
using Xunit;

namespace Biolog.Features.ReportBuilder.Tests;

public sealed class ReportGenerationServiceTests
{
    private const string TenantA = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string TenantB = "ten_018f2e5a-1234-7abc-89ab-0123456789ac";

    [Fact]
    public async Task AC01_groups_and_counts_card_production_by_status()
    {
        var cardGateway = new FakeCardProductionGateway();
        cardGateway.Seed(TenantA,
            new CardProductionRecord("passed", "connected", "staff"),
            new CardProductionRecord("passed", "connected", "staff"),
            new CardProductionRecord("failed", "connected", "visitor"));
        var service = new ReportGenerationService(cardGateway, new FakeAttendanceGateway());

        var result = await service.GenerateAsync(TenantA, new ReportDefinition { Dataset = ReportDataset.CardProduction, Dimension = ReportDimension.Status, Measure = ReportMeasure.Count }, TestContext.Current.CancellationToken);

        Assert.Equal(["status", "count"], result.Columns);
        Assert.Equal(3, result.SourceRecordCount);
        Assert.Contains(result.Rows, r => r[0] == "passed" && r[1] == "2");
        Assert.Contains(result.Rows, r => r[0] == "failed" && r[1] == "1");
    }

    [Fact]
    public async Task AC01_groups_and_counts_attendance_by_mode()
    {
        var attendanceGateway = new FakeAttendanceGateway();
        attendanceGateway.Seed(TenantA,
            new AttendanceRecord("check_in", "device", "biometric"),
            new AttendanceRecord("check_in", "device", "biometric"),
            new AttendanceRecord("check_out", "manual", "unverified"));
        var service = new ReportGenerationService(new FakeCardProductionGateway(), attendanceGateway);

        var result = await service.GenerateAsync(TenantA, new ReportDefinition { Dataset = ReportDataset.Attendance, Dimension = ReportDimension.Mode, Measure = ReportMeasure.Count }, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.SourceRecordCount);
        Assert.Contains(result.Rows, r => r[0] == "check_in" && r[1] == "2");
        Assert.Contains(result.Rows, r => r[0] == "check_out" && r[1] == "1");
    }

    [Fact]
    public async Task AC03_a_tenant_never_sees_another_tenants_records_in_its_own_report()
    {
        var cardGateway = new FakeCardProductionGateway();
        cardGateway.Seed(TenantA, new CardProductionRecord("passed", "connected", "staff"));
        cardGateway.Seed(TenantB, new CardProductionRecord("failed", "connected", "staff"), new CardProductionRecord("failed", "connected", "staff"));
        var service = new ReportGenerationService(cardGateway, new FakeAttendanceGateway());

        var resultA = await service.GenerateAsync(TenantA, new ReportDefinition { Dataset = ReportDataset.CardProduction, Dimension = ReportDimension.Status, Measure = ReportMeasure.Count }, TestContext.Current.CancellationToken);

        Assert.Equal(1, resultA.SourceRecordCount);
        Assert.DoesNotContain(resultA.Rows, r => r[0] == "failed");
    }

    [Fact]
    public async Task AC01_an_ungoverned_definition_is_rejected_before_any_gateway_is_ever_called()
    {
        var cardGateway = new FakeCardProductionGateway();
        cardGateway.Seed(TenantA, new CardProductionRecord("passed", "connected", "staff"));
        var service = new ReportGenerationService(cardGateway, new FakeAttendanceGateway());

        await Assert.ThrowsAsync<ReportError>(() =>
            service.GenerateAsync(TenantA, new ReportDefinition { Dataset = ReportDataset.CardProduction, Dimension = ReportDimension.Mode, Measure = ReportMeasure.Count }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task an_empty_dataset_produces_a_zero_row_result_not_an_error()
    {
        var service = new ReportGenerationService(new FakeCardProductionGateway(), new FakeAttendanceGateway());

        var result = await service.GenerateAsync(TenantA, new ReportDefinition { Dataset = ReportDataset.CardProduction, Dimension = ReportDimension.Status, Measure = ReportMeasure.Count }, TestContext.Current.CancellationToken);

        Assert.Empty(result.Rows);
        Assert.Equal(0, result.SourceRecordCount);
    }
}
