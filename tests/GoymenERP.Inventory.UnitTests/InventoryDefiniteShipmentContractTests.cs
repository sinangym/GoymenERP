using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryDefiniteShipmentContractTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");

    [Fact]
    public void Exact_owner_contract_carries_all_atomic_shipment_identities()
    {
        var request = Request();
        var quantity = InventoryDefiniteShipmentContractV1.Validate(request, Metadata(), Company);

        Assert.Equal(12.125m, quantity.Value);
        Assert.Equal(Company, request.CompanyId);
        Assert.NotEqual(request.ShipmentId, request.MovementId);
        Assert.NotEqual(request.LoadingInstructionId, request.LoadingInstructionLineId);
        Assert.NotEqual(request.AllocationId, request.LabelId);
        Assert.Contains(typeof(IAsyncDisposable), typeof(IInventoryDefiniteShipmentParticipantV1).GetInterfaces());
        var stage = typeof(IInventoryDefiniteShipmentParticipantV1)
            .GetMethod(nameof(IInventoryDefiniteShipmentParticipantV1.StageAsync))!;
        Assert.Equal(typeof(InventoryDefiniteShipmentRequestV1), stage.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(InventoryCommandMetadata), stage.GetParameters()[1].ParameterType);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("01")]
    [InlineData("+1")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("9223372036854775808")]
    public void Expected_versions_are_canonical_positive_Int64_text(string version)
    {
        Refused(Request() with { ExpectedAllocationVersion = version },
            InventoryDefiniteShipmentCodesV1.InvalidVersion);
        Refused(Request() with { ExpectedLabelVersion = version },
            InventoryDefiniteShipmentCodesV1.InvalidVersion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("1.2345")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("+1")]
    [InlineData("1e0")]
    public void Quantity_is_positive_lossless_kg_text(string quantity)
    {
        Refused(Request() with { QuantityKg = quantity }, InventoryDefiniteShipmentCodesV1.InvalidQuantity);
    }

    [Theory]
    [InlineData("1.0", 1)]
    [InlineData("00.125", 0.125)]
    public void Existing_Inventory_decimal_shape_remains_accepted(string text, decimal expected)
    {
        Assert.Equal(expected,
            InventoryDefiniteShipmentContractV1.Validate(Request() with { QuantityKg = text }, Metadata(), Company).Value);
    }

    [Fact]
    public void Foreign_company_is_indistinguishable_from_an_invalid_reference()
    {
        var error = Assert.Throws<InventoryDefiniteShipmentException>(() =>
            InventoryDefiniteShipmentContractV1.Validate(Request(), Metadata(), Guid.NewGuid()));
        Assert.Equal((404, InventoryDefiniteShipmentCodesV1.InvalidReference), (error.Status, error.Code));
    }

    [Fact]
    public void Missing_identity_and_non_utc_time_are_refused_before_any_store_can_run()
    {
        Refused(Request() with { AllocationId = Guid.Empty }, InventoryDefiniteShipmentCodesV1.InvalidRequest);
        Refused(Request() with { OccurredAtUtc = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.FromHours(3)) },
            InventoryDefiniteShipmentCodesV1.InvalidRequest);
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
        var error = Assert.Throws<InventoryDefiniteShipmentException>(() =>
            InventoryDefiniteShipmentContractV1.Validate(Request(), metadata, Company));
        Assert.Equal((400, InventoryDefiniteShipmentCodesV1.InvalidRequest), (error.Status, error.Code));
    }

    [Fact]
    public void Closed_error_contract_keeps_stale_allocation_stale_label_and_unavailable_distinct()
    {
        Assert.Equal("inventory_allocation_version_conflict", InventoryDefiniteShipmentCodesV1.AllocationVersionConflict);
        Assert.Equal("inventory_label_version_conflict", InventoryDefiniteShipmentCodesV1.LabelVersionConflict);
        Assert.Equal("inventory_definite_shipment_unavailable", InventoryDefiniteShipmentCodesV1.Unavailable);
        Assert.NotEqual(InventoryDefiniteShipmentCodesV1.AllocationInsufficient,
            InventoryDefiniteShipmentCodesV1.PhysicalStockInsufficient);
        Assert.Throws<InvalidOperationException>(() =>
            InventoryDefiniteShipmentException.Refused(409, "inventory_arbitrary_internal_detail"));
        Assert.Throws<InvalidOperationException>(() =>
            InventoryDefiniteShipmentException.Refused(404, InventoryDefiniteShipmentCodesV1.Unavailable));
        Assert.Equal(InventoryDefiniteShipmentCodesV1.InvalidReference,
            InventoryDefiniteShipmentException.Refused(404, InventoryDefiniteShipmentCodesV1.InvalidReference).Code);
    }

    private static void Refused(InventoryDefiniteShipmentRequestV1 request, string code)
    {
        var error = Assert.Throws<InventoryDefiniteShipmentException>(() =>
            InventoryDefiniteShipmentContractV1.Validate(request, Metadata(), Company));
        Assert.Equal((400, code), (error.Status, error.Code));
    }

    private static InventoryDefiniteShipmentRequestV1 Request() => new(
        Company,
        Guid.Parse("71000000-0000-0000-0000-000000000001"),
        Guid.Parse("71000000-0000-0000-0000-000000000002"),
        Guid.Parse("71000000-0000-0000-0000-000000000003"),
        Guid.Parse("71000000-0000-0000-0000-000000000004"),
        "7",
        Guid.Parse("71000000-0000-0000-0000-000000000005"),
        "11",
        Guid.Parse("71000000-0000-0000-0000-000000000006"),
        "12.125",
        new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.Zero));

    private static InventoryCommandMetadata Metadata() => new(
        "actor-1", "logistics.shipments.record", ["logistics-user"], "corr-1", "cause-1", "key-1", "api");
}
