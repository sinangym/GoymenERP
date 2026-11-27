using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Infrastructure.WorkInbox;
using F = GoymenERP.Logistics.UnitTests.LogisticsWorkInboxTestFixture;

namespace GoymenERP.Logistics.UnitTests;

public sealed class LogisticsCustomerReturnDecisionSourceTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Purpose_preserves_all_recorded_request_facts_and_immutable_maker_after_rejection(bool rejected)
    {
        var f = Fixture(); var row = f.Rows.CustomerReturnRows[0];
        if (rejected) { row.Status = 3; row.Version = 2; row.DecidedBy = F.Approver; row.DecidedAtUtc = F.T0.ToUniversalTime(); }
        var snapshot = await new LogisticsCustomerReturnWorkInboxSourceV1(f.Rows, f.Fence).ReadDecisionAsync(f.Context(), row.CustomerReturnId, default);
        Assert.NotNull(snapshot); Assert.Equal(!rejected, snapshot.Pending); Assert.Equal(rejected ? "2" : "1", snapshot.SourceVersion);
        var human = snapshot.LogisticsCustomerReturnProof!.Return;
        Assert.Equal((row.SourceDeliveryId, row.SourceShipmentId, row.SourceOrderLineId, row.UnitId),
            (human.SourceDeliveryId, human.SourceShipmentId, human.SourceOrderLineId, human.UnitId));
        Assert.Equal(("1.250", "100", "Kilogram", row.Reason, F.Maker),
            (human.Quantity, human.SourceDeliveredQuantity, human.QuantityUnit, human.Reason, human.RequestedBy));
        Assert.Equal("/onaylarim/logistics.customer-return.v1/" + row.CustomerReturnId.ToString("D"), snapshot.Item.Link);
        Assert.Equal(LogisticsWorkInboxFence.InstructionLink(row.LoadingInstructionId), snapshot.Item.References[0].Link);
        Assert.Equal(F.Maker, snapshot.Item.PreparedBy); Assert.DoesNotContain(row.Reason, JsonSerializer.Serialize(snapshot));
        Assert.DoesNotContain("logisticsCustomerReturnProof", JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(2, f.Rows.Reads);
    }

    [Theory]
    [InlineData("company")] [InlineData("missing")]
    public async Task Unknown_or_foreign_return_has_no_purpose_proof(string fault)
    {
        var f = Fixture(); if (fault == "company") f.Rows.CustomerReturnRows[0].CompanyId = F.CompanyB;
        Assert.Null(await new LogisticsCustomerReturnWorkInboxSourceV1(f.Rows, f.Fence).ReadDecisionAsync(f.Context(), fault == "missing" ? F.Id(999) : F.Id(31), default));
    }

    [Theory]
    [InlineData("unit")] [InlineData("source")] [InlineData("quantity")] [InlineData("state")]
    [InlineData("permission")] [InlineData("changing")]
    public async Task Incomplete_changing_or_unauthorized_source_is_never_released(string fault)
    {
        var f = Fixture(); var row = f.Rows.CustomerReturnRows[0];
        if (fault == "unit") row.QuantityUnit = 9;
        if (fault == "source") row.SourceDeliveryId = Guid.Empty;
        if (fault == "quantity") row.Quantity = 101;
        if (fault == "state") row.Status = 9;
        if (fault == "permission") f.Authority.Answer = (_, _) => PermissionScope.Denied;
        if (fault == "changing") f.Rows.AfterRead = read => { if (read == 2) row.Version++; };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new LogisticsCustomerReturnWorkInboxSourceV1(f.Rows, f.Fence).ReadDecisionAsync(f.Context(), F.Id(31), default));
        if (fault == "permission") Assert.Equal(0, f.Rows.Reads);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Native_minimum_supplied_occurrence_date_is_preserved_for_pending_and_rejected_returns(bool rejected)
    {
        var f = Fixture(); var row = f.Rows.CustomerReturnRows[0];
        row.OccurredAtUtc = DateTimeOffset.MinValue;
        if (rejected) { row.Status = 3; row.Version = 2; row.DecidedBy = F.Approver; row.DecidedAtUtc = F.T0.ToUniversalTime(); }
        var snapshot = await new LogisticsCustomerReturnWorkInboxSourceV1(f.Rows, f.Fence)
            .ReadDecisionAsync(f.Context(), row.CustomerReturnId, default);
        Assert.NotNull(snapshot); Assert.Equal(!rejected, snapshot.Pending);
        Assert.Equal(DateTimeOffset.MinValue, snapshot.LogisticsCustomerReturnProof!.Return.OccurredAtUtc);
    }

    private static F Fixture()
    {
        var f = new F();
        f.Rows.CustomerReturnRows.Add(new CustomerReturnRow { CompanyId = F.CompanyA, CustomerReturnId = F.Id(31), LoadingInstructionId = F.Id(1),
            LoadingInstructionLineId = F.Id(2), SourceDeliveryId = F.Id(3), SourceShipmentId = F.Id(4), SourceOrderId = F.Id(5), SourceOrderLineId = F.Id(6), UnitId = F.Id(7),
            QuantityUnit = 1, Quantity = 1.250m, SourceDeliveredQuantity = 100m, Reason = "SENTETIK-private-human-reason", RequestedBy = F.Maker,
            OccurredAtUtc = F.T0.ToUniversalTime(), RequestedAtUtc = F.T0.ToUniversalTime(), Status = 1, Version = 1 });
        return f;
    }
}
