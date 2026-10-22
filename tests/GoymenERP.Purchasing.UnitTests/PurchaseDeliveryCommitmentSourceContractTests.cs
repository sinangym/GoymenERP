using System.Reflection;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseDeliveryCommitmentSourceContractTests
{
    [Fact]
    public void Source_schema_is_closed_and_contains_only_expected_supply_facts()
    {
        Assert.Equal(
            ["CompanyId", "PurchaseOrderId", "PurchaseOrderLineId", "PurchaseOrderVersion", "Slices"],
            PropertiesOf<PurchaseDeliveryCommitmentSourceV1>());

        Assert.Equal(
            ["DeliverySliceId", "ExpectedOn", "QuantityText", "SliceNo", "SupplierConfirmedAtUtc"],
            PropertiesOf<PurchaseDeliveryCommitmentSliceV1>());
        Assert.Equal(
            typeof(string),
            typeof(PurchaseDeliveryCommitmentSourceV1)
                .GetProperty(nameof(PurchaseDeliveryCommitmentSourceV1.PurchaseOrderVersion))!
                .PropertyType);

        string[] forbidden =
        [
            "accepted", "actor", "amount", "currency", "discount", "idempotency", "intransit",
            "payment", "price", "receipt", "stock", "tax"
        ];

        var names = PropertiesOf<PurchaseDeliveryCommitmentSourceV1>()
            .Concat(PropertiesOf<PurchaseDeliveryCommitmentSliceV1>())
            .Select(name => name.ToLowerInvariant())
            .ToArray();

        foreach (var term in forbidden)
            Assert.DoesNotContain(names, name => name.Contains(term, StringComparison.Ordinal));
    }

    [Fact]
    public void Read_contract_is_line_scoped_and_returns_nullable_source_without_authority_input()
    {
        var method = typeof(IPurchaseDeliveryCommitmentSourceV1)
            .GetMethod(nameof(IPurchaseDeliveryCommitmentSourceV1.ReadAsync));

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<PurchaseDeliveryCommitmentSourceV1>), method.ReturnType);
        Assert.Equal(
            [typeof(Guid), typeof(Guid), typeof(CancellationToken)],
            method.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        Assert.Equal(
            ["purchaseOrderId", "purchaseOrderLineId", "cancellationToken"],
            method.GetParameters().Select(parameter => parameter.Name).ToArray());

        var nullability = new NullabilityInfoContext();
        var returnNullability = nullability.Create(method.ReturnParameter);
        Assert.Equal(NullabilityState.NotNull, returnNullability.ReadState);
        Assert.Equal(
            NullabilityState.Nullable,
            Assert.Single(returnNullability.GenericTypeArguments).ReadState);
        Assert.Equal(
            NullabilityState.Nullable,
            nullability.Create(typeof(PurchaseDeliveryCommitmentSliceV1)
                .GetProperty(nameof(PurchaseDeliveryCommitmentSliceV1.SupplierConfirmedAtUtc))!).ReadState);
    }

    [Fact]
    public void Multiple_slices_preserve_exact_quantity_text_identity_order_and_confirmation_fact()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var confirmedAt = DateTimeOffset.Parse("2026-10-22T09:30:00+00:00");
        const string exactOpaqueVersion = "po-revision/0007:accepted-token";
        var source = new PurchaseDeliveryCommitmentSourceV1(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), exactOpaqueVersion,
            [
                new(firstId, 1, "400.000", new DateOnly(2026, 10, 30), confirmedAt),
                new(secondId, 2, "600", new DateOnly(2026, 11, 6), null),
            ]);

        Assert.Equal(exactOpaqueVersion, source.PurchaseOrderVersion);
        Assert.Collection(source.Slices,
            slice =>
            {
                Assert.Equal(firstId, slice.DeliverySliceId);
                Assert.Equal(1, slice.SliceNo);
                Assert.Equal("400.000", slice.QuantityText);
                Assert.Equal(confirmedAt, slice.SupplierConfirmedAtUtc);
            },
            slice =>
            {
                Assert.Equal(secondId, slice.DeliverySliceId);
                Assert.Equal(2, slice.SliceNo);
                Assert.Equal("600", slice.QuantityText);
                Assert.Null(slice.SupplierConfirmedAtUtc);
            });
    }

    private static string[] PropertiesOf<T>() =>
        typeof(T).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
}
