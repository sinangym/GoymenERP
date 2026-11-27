using System.Text.Json;
using GoymenERP.Api.Ai.Gateway;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

public sealed class AiAcceptanceCountsContractTests
{
    [Theory]
    [InlineData(null)] [InlineData(0)] [InlineData(7)]
    public void Optional_unreviewed_coverage_preserves_legacy_absence_and_real_zero(int? count)
    {
        var usage = new AiGatewayUsageViewV1(TestCompanyIds.CompanyA, 202611, 0, 0, []);
        var response = AiGatewayUsageResponseV1.From(usage, [new(AiFeatureV1.Panel, 3, 1, 0, 0, count)]);
        Assert.True(response.AcceptanceMeasured);
        var row = Assert.Single(response.Acceptance);
        Assert.Equal(count, row.Unreviewed);
        Assert.Equal((3, 1, 0, 0), (row.Accepted, row.Corrected, row.Rejected, row.Reverted));
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(row, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(count.HasValue, wire.RootElement.TryGetProperty("unreviewed", out var measured));
        if (count.HasValue) Assert.Equal(count.Value, measured.GetInt32());
        else Assert.Equal(5, wire.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public void A_negative_unreviewed_owner_count_is_unavailable_instead_of_a_measured_result()
    {
        var usage = new AiGatewayUsageViewV1(TestCompanyIds.CompanyA, 202611, 0, 0, []);
        Assert.Throws<SettingsPrerequisiteException>(() => AiGatewayUsageResponseV1.From(usage,
            [new(AiFeatureV1.Panel, 0, 0, 0, 0, -1)]));
    }
}
