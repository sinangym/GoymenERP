using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryCustomerReturnContractTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");

    [Fact]
    public void Contract_carries_only_the_source_needed_to_resolve_the_immutable_shipment_trace()
    {
        var request = Request();
        var quantity = InventoryCustomerReturnContractV1.Validate(request, Metadata(), Company);

        Assert.Equal(12.125m, quantity.Value);
        Assert.NotEqual(request.CustomerReturnId, request.InventoryMovementId);
        Assert.Contains(typeof(IAsyncDisposable), typeof(IInventoryCustomerReturnParticipantV1).GetInterfaces());
        var stage = typeof(IInventoryCustomerReturnParticipantV1)
            .GetMethod(nameof(IInventoryCustomerReturnParticipantV1.StageAsync))!;
        Assert.Equal(typeof(InventoryCustomerReturnRequestV1), stage.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(InventoryCommandMetadata), stage.GetParameters()[1].ParameterType);
        Assert.DoesNotContain(typeof(InventoryCustomerReturnRequestV1).GetProperties(), property =>
            property.Name.Contains("Label", StringComparison.Ordinal)
            || property.Name.Contains("Owner", StringComparison.Ordinal)
            || property.Name.Contains("Custodian", StringComparison.Ordinal)
            || property.Name.Contains("Location", StringComparison.Ordinal)
            || property.Name.Contains("Disposition", StringComparison.Ordinal));
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
        var error = Assert.Throws<InventoryCustomerReturnException>(() =>
            InventoryCustomerReturnContractV1.Validate(Request() with { QuantityKg = quantity }, Metadata(), Company));
        Assert.Equal((400, InventoryCustomerReturnCodesV1.InvalidQuantity), (error.Status, error.Code));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("0.125", 0.125)]
    [InlineData("1200.001", 1200.001)]
    public void Canonical_kilograms_are_lossless(string text, decimal expected) =>
        Assert.Equal(expected,
            InventoryCustomerReturnContractV1.Validate(Request() with { QuantityKg = text }, Metadata(), Company).Value);

    [Fact]
    public void Company_isolation_is_fail_closed_and_missing_identity_is_rejected()
    {
        var foreign = Assert.Throws<InventoryCustomerReturnException>(() =>
            InventoryCustomerReturnContractV1.Validate(Request(), Metadata(), Guid.NewGuid()));
        Assert.Equal((404, InventoryCustomerReturnCodesV1.InvalidReference), (foreign.Status, foreign.Code));

        foreach (var request in new[]
        {
            Request() with { CustomerReturnId = Guid.Empty },
            Request() with { SourceShipmentId = Guid.Empty },
            Request() with { LoadingInstructionLineId = Guid.Empty },
            Request() with { InventoryMovementId = Guid.Empty },
            Request() with { InventoryMovementId = Request().CustomerReturnId },
            Request() with { OccurredAtUtc = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.FromHours(3)) },
        })
        {
            var refused = Assert.Throws<InventoryCustomerReturnException>(() =>
                InventoryCustomerReturnContractV1.Validate(request, Metadata(), Company));
            Assert.Equal((400, InventoryCustomerReturnCodesV1.InvalidRequest), (refused.Status, refused.Code));
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
        var error = Assert.Throws<InventoryCustomerReturnException>(() =>
            InventoryCustomerReturnContractV1.Validate(Request(), metadata, Company));
        Assert.Equal((400, InventoryCustomerReturnCodesV1.InvalidRequest), (error.Status, error.Code));
    }

    [Fact]
    public void Refusal_channel_is_closed_and_every_code_has_one_status()
    {
        var declared = typeof(InventoryCustomerReturnCodesV1).GetFields()
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(field => Assert.IsType<string>(field.GetRawConstantValue()))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var mapped = InventoryCustomerReturnCodesV1.Status400Codes
            .Concat(InventoryCustomerReturnCodesV1.Status404Codes)
            .Concat(InventoryCustomerReturnCodesV1.Status409Codes)
            .Concat(InventoryCustomerReturnCodesV1.Status503Codes)
            .ToArray();

        Assert.Equal(mapped.Length, mapped.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(declared, mapped.Order(StringComparer.Ordinal));
        Assert.Throws<InvalidOperationException>(() =>
            InventoryCustomerReturnException.Refused(409, "inventory_customer_return_internal_detail"));
    }

    [Fact]
    public void Event_is_versioned_and_carries_the_original_physical_identity_without_authority_metadata()
    {
        Assert.Equal("inventory.customer-return.recorded.v1", InventoryCustomerReturnOutboxV1.EventType);
        Assert.Equal(1, InventoryCustomerReturnOutboxV1.SchemaVersion);
        Assert.Equal(InventoryCustomerReturnContractV1.CommandName, InventoryCustomerReturnOutboxV1.SourceOperation);
        Assert.Equal("Inventory.MaterialLabel", InventoryCustomerReturnOutboxV1.AggregateType);

        var payload = new InventoryCustomerReturnRecordedV1(
            Company, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "4", Guid.NewGuid(), StockPartyKind.Company, Company, StockPartyKind.Company, Company, Guid.NewGuid(),
            "12.125", new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero), "corr", "cause");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var names = json.RootElement.EnumerateObject().Select(property => property.Name).ToArray();

        Assert.Contains("originalShipmentMovementId", names);
        Assert.Contains("labelId", names);
        Assert.Contains("productId", names);
        Assert.Contains("ownerKind", names);
        Assert.Contains("custodianKind", names);
        Assert.Contains("locationId", names);
        Assert.DoesNotContain(names, name => name.Contains("permission", StringComparison.OrdinalIgnoreCase)
            || name.Contains("role", StringComparison.OrdinalIgnoreCase)
            || name.Contains("idempotency", StringComparison.OrdinalIgnoreCase)
            || name.Contains("replay", StringComparison.OrdinalIgnoreCase));
    }

    private static InventoryCustomerReturnRequestV1 Request() => new(
        Company,
        Guid.Parse("71000000-0000-0000-0000-000000000001"),
        Guid.Parse("71000000-0000-0000-0000-000000000002"),
        Guid.Parse("71000000-0000-0000-0000-000000000003"),
        Guid.Parse("71000000-0000-0000-0000-000000000004"),
        "12.125",
        new DateTimeOffset(2026, 10, 4, 9, 0, 0, TimeSpan.Zero));

    private static InventoryCommandMetadata Metadata() => new(
        "actor-1", "logistics.customer-returns.decide", ["logistics-user"],
        "corr-1", "cause-1", "key-1", "api");
}
