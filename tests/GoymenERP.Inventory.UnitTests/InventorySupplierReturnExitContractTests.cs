using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventorySupplierReturnExitContractTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");

    [Fact]
    public void Contract_names_the_original_acceptance_and_lets_Inventory_resolve_the_label()
    {
        var request = Request();
        var input = InventorySupplierReturnExitContractV1.Validate(request, Metadata(), Company);

        Assert.Equal(12.125m, input.Quantity.Value);
        Assert.Equal(41.5m, input.OriginalUnitCostTryPerKg);
        Assert.Contains(typeof(IAsyncDisposable), typeof(IInventorySupplierReturnExitParticipantV1).GetInterfaces());
        var stage = typeof(IInventorySupplierReturnExitParticipantV1)
            .GetMethod(nameof(IInventorySupplierReturnExitParticipantV1.StageAsync))!;
        Assert.Equal(typeof(InventorySupplierReturnExitRequestV1), stage.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(InventoryCommandMetadata), stage.GetParameters()[1].ParameterType);
        // AK-8 (SAT-D433/SAT-D445): the only label the caller may name is the optional SourceLabelId, honoured only behind the
        // golden gate and re-checked against Inventory's own lineage; the receipt still fixes product, owner and ceiling.
        Assert.Equal(typeof(Guid?), typeof(InventorySupplierReturnExitRequestV1).GetProperty("SourceLabelId")!.PropertyType);
        Assert.Null(request.SourceLabelId);
        Assert.DoesNotContain(typeof(InventorySupplierReturnExitRequestV1).GetProperties().Where(property => property.Name != "SourceLabelId"), property =>
            property.Name.Contains("Label", StringComparison.Ordinal)
            || property.Name.Contains("Product", StringComparison.Ordinal)
            || property.Name.Contains("Owner", StringComparison.Ordinal)
            || property.Name.Contains("Custodian", StringComparison.Ordinal)
            || property.Name.Contains("Location", StringComparison.Ordinal));
        Assert.Equal("purchasing.supplier-return", InventorySupplierReturnExitContractV1.SourceKind);
        Assert.Equal("inventory.supplier-return-exit.stage.v1", InventorySupplierReturnExitContractV1.CommandName);
        Assert.Equal(15, (int)StockMovementKind.SupplierReturnExit);
    }

    [Fact]
    public void Both_free_and_blocked_kilograms_may_leave_but_no_other_block_state()
    {
        Assert.Null(Record.Exception(() => InventorySupplierReturnExitContractV1.Validate(
            Request() with { SourceBlock = BlockState.Blocked }, Metadata(), Company)));
        Assert.Null(Record.Exception(() => InventorySupplierReturnExitContractV1.Validate(
            Request() with { SourceBlock = BlockState.Free }, Metadata(), Company)));
        var refused = Assert.Throws<InventorySupplierReturnExitException>(() =>
            InventorySupplierReturnExitContractV1.Validate(
                Request() with { SourceBlock = (BlockState)99 }, Metadata(), Company));
        Assert.Equal((400, InventorySupplierReturnExitCodesV1.InvalidRequest), (refused.Status, refused.Code));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("01")]
    [InlineData("1.0")]
    [InlineData("1.2345")]
    [InlineData(" 1")]
    [InlineData("+1")]
    [InlineData("1e0")]
    public void Quantity_is_positive_canonical_kilogram_text(string quantity)
    {
        var error = Assert.Throws<InventorySupplierReturnExitException>(() =>
            InventorySupplierReturnExitContractV1.Validate(Request() with { QuantityKg = quantity }, Metadata(), Company));
        Assert.Equal((400, InventorySupplierReturnExitCodesV1.InvalidQuantity), (error.Status, error.Code));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.0000")]
    [InlineData("1.00")]
    [InlineData("1.23456")]
    [InlineData("-1")]
    [InlineData(" 1")]
    [InlineData("")]
    public void An_original_cost_is_positive_canonical_base_currency_text_and_zero_is_not_a_cost(string cost)
    {
        var error = Assert.Throws<InventorySupplierReturnExitException>(() =>
            InventorySupplierReturnExitContractV1.Validate(
                Request() with { OriginalUnitCostTryPerKg = cost }, Metadata(), Company));
        Assert.Equal((400, InventorySupplierReturnExitCodesV1.InvalidCost), (error.Status, error.Code));
    }

    [Fact]
    public void An_unavailable_original_cost_stays_unavailable_and_never_becomes_zero()
    {
        var input = InventorySupplierReturnExitContractV1.Validate(
            Request() with { OriginalUnitCostTryPerKg = null }, Metadata(), Company);

        Assert.Null(input.OriginalUnitCostTryPerKg);
        Assert.Equal(1.2345m, InventorySupplierReturnExitContractV1.Validate(
            Request() with { OriginalUnitCostTryPerKg = "1.2345" }, Metadata(), Company).OriginalUnitCostTryPerKg);
    }

    [Fact]
    public void Company_isolation_is_fail_closed_and_missing_identity_is_rejected()
    {
        var foreign = Assert.Throws<InventorySupplierReturnExitException>(() =>
            InventorySupplierReturnExitContractV1.Validate(Request(), Metadata(), Guid.NewGuid()));
        Assert.Equal((404, InventorySupplierReturnExitCodesV1.InvalidReference), (foreign.Status, foreign.Code));

        foreach (var request in new[]
        {
            Request() with { CompanyId = Guid.Empty },
            Request() with { SupplierReturnId = Guid.Empty },
            Request() with { SupplierReturnLineId = Guid.Empty },
            Request() with { GoodsAcceptanceId = Guid.Empty },
            Request() with { InventoryMovementId = Guid.Empty },
            Request() with { InventoryMovementId = Request().SupplierReturnId },
            Request() with { InventoryMovementId = Request().SupplierReturnLineId },
            Request() with { OccurredAtUtc = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.FromHours(3)) },
        })
        {
            var refused = Assert.Throws<InventorySupplierReturnExitException>(() =>
                InventorySupplierReturnExitContractV1.Validate(request, Metadata(), Company));
            Assert.Equal((400, InventorySupplierReturnExitCodesV1.InvalidRequest), (refused.Status, refused.Code));
        }
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("permission")]
    [InlineData("correlation")]
    [InlineData("idempotency")]
    [InlineData("source")]
    public void Audit_and_replay_metadata_are_required_and_canonical(string field)
    {
        var metadata = Metadata();
        metadata = field switch
        {
            "actor" => metadata with { ActorId = "" },
            "permission" => metadata with { PermissionCode = " permission" },
            "correlation" => metadata with { CorrelationId = new string('x', 129) },
            "idempotency" => metadata with { IdempotencyKey = "key\n" },
            _ => metadata with { SourceChannel = " " },
        };
        var error = Assert.Throws<InventorySupplierReturnExitException>(() =>
            InventorySupplierReturnExitContractV1.Validate(Request(), metadata, Company));
        Assert.Equal((400, InventorySupplierReturnExitCodesV1.InvalidRequest), (error.Status, error.Code));
    }

    [Fact]
    public void Refusal_channel_is_closed_and_every_code_has_one_status()
    {
        var declared = typeof(InventorySupplierReturnExitCodesV1).GetFields()
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(field => Assert.IsType<string>(field.GetRawConstantValue()))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var mapped = InventorySupplierReturnExitCodesV1.Status400Codes
            .Concat(InventorySupplierReturnExitCodesV1.Status404Codes)
            .Concat(InventorySupplierReturnExitCodesV1.Status409Codes)
            .Concat(InventorySupplierReturnExitCodesV1.Status503Codes)
            .ToArray();

        Assert.Equal(mapped.Length, mapped.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(declared, mapped.Order(StringComparer.Ordinal));
        Assert.All(declared, code => Assert.StartsWith("inventory_supplier_return_exit_", code, StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() =>
            InventorySupplierReturnExitException.Refused(409, "inventory_supplier_return_exit_internal_detail"));
    }

    [Fact]
    public void An_allocated_label_and_an_unposted_cost_difference_are_explicit_conflicts_of_their_own()
    {
        // KLN-D93 and SAT-D371: each refusal has its own code; neither is the generic stock-not-available answer.
        Assert.Equal("inventory_supplier_return_exit_stock_allocated", InventorySupplierReturnExitCodesV1.StockAllocated);
        Assert.Equal(
            "inventory_supplier_return_exit_cost_difference_unposted",
            InventorySupplierReturnExitCodesV1.CostDifferenceUnposted);
        foreach (var code in new[]
                 {
                     InventorySupplierReturnExitCodesV1.StockAllocated,
                     InventorySupplierReturnExitCodesV1.CostDifferenceUnposted,
                 })
        {
            Assert.True(InventorySupplierReturnExitCodesV1.IsDefined(409, code));
            Assert.False(InventorySupplierReturnExitCodesV1.IsDefined(503, code));
            Assert.NotEqual(InventorySupplierReturnExitCodesV1.StockNotAvailable, code);
            var refusal = InventorySupplierReturnExitException.Refused(409, code);
            Assert.Equal((409, code), (refusal.Status, refusal.Code));
        }
    }

    [Fact]
    public void Event_is_versioned_and_carries_the_physical_identity_and_original_cost_without_authority_metadata()
    {
        Assert.Equal("inventory.supplier-return-exit.recorded.v1", InventorySupplierReturnExitOutboxV1.EventType);
        Assert.Equal(1, InventorySupplierReturnExitOutboxV1.SchemaVersion);
        Assert.Equal(InventorySupplierReturnExitContractV1.CommandName, InventorySupplierReturnExitOutboxV1.SourceOperation);
        Assert.Equal("Inventory.MaterialLabel", InventorySupplierReturnExitOutboxV1.AggregateType);

        var payload = new InventorySupplierReturnExitRecordedV1(
            Company, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "4", Guid.NewGuid(), StockPartyKind.Company, Company, StockPartyKind.Company, Company, Guid.NewGuid(),
            BlockState.Blocked, "12.125", null, new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero), "corr", "cause");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var names = json.RootElement.EnumerateObject().Select(property => property.Name).ToArray();

        Assert.Contains("originalReceiptMovementId", names);
        Assert.Contains("goodsAcceptanceId", names);
        Assert.Contains("labelId", names);
        Assert.Contains("sourceBlock", names);
        Assert.Contains("originalUnitCostTryPerKg", names);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("originalUnitCostTryPerKg").ValueKind);
        Assert.DoesNotContain(names, name => name.Contains("permission", StringComparison.OrdinalIgnoreCase)
            || name.Contains("role", StringComparison.OrdinalIgnoreCase)
            || name.Contains("idempotency", StringComparison.OrdinalIgnoreCase)
            || name.Contains("replay", StringComparison.OrdinalIgnoreCase));
    }

    private static InventorySupplierReturnExitRequestV1 Request() => new(
        Company,
        Guid.Parse("72000000-0000-0000-0000-000000000001"),
        Guid.Parse("72000000-0000-0000-0000-000000000002"),
        Guid.Parse("72000000-0000-0000-0000-000000000003"),
        Guid.Parse("72000000-0000-0000-0000-000000000004"),
        BlockState.Free,
        "12.125",
        "41.5",
        new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero));

    private static InventoryCommandMetadata Metadata() => new(
        "actor-1", "purchasing.supplier-returns.decide", ["purchasing-user"],
        "corr-1", "cause-1", "key-1", "api");
}
