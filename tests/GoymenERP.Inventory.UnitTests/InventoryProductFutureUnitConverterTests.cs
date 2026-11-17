using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryProductFutureUnitConverterTests
{
    [Fact]
    public async Task Owner_conversion_keeps_the_actual_quantity_and_explicit_kg_remainder()
    {
        var owner = new Owner(new(InventoryProductUnitConversionOutcomeV1.Converted, 3m, 0.001m));
        var result = await new InventoryProductFutureUnitConverterV1(owner).ConvertAsync(Guid.NewGuid(), "different-card-revision",
            1m, Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal((InventoryFutureUnitConversionOutcomeV1.Converted, 3m, 0.001m),
            (result.Outcome, result.Quantity, result.RemainderKg));
        Assert.Equal(1, owner.Calls);
    }

    [Fact]
    public async Task Same_unit_identity_preserves_the_existing_contract_without_an_owner_coefficient_read()
    {
        var owner = new Owner(new(InventoryProductUnitConversionOutcomeV1.Unavailable, null));
        var unit = Guid.NewGuid();
        var result = await new InventoryProductFutureUnitConverterV1(owner).ConvertAsync(Guid.NewGuid(), "1", 4m, unit, unit);
        Assert.Equal(4m, result.Quantity); Assert.Equal(0, owner.Calls);
    }

    [Theory]
    [InlineData(InventoryProductUnitConversionOutcomeV1.Unavailable)]
    [InlineData(InventoryProductUnitConversionOutcomeV1.Converted)]
    public async Task Technical_or_malformed_source_throws_transient_503_instead_of_recordable_no_source(InventoryProductUnitConversionOutcomeV1 outcome)
    {
        var owner = new Owner(new(outcome, null));
        var error = await Assert.ThrowsAsync<InventoryAllocationException>(() => new InventoryProductFutureUnitConverterV1(owner)
            .ConvertToKilogramsAsync(Guid.NewGuid(), "1", 1m, Guid.NewGuid()));
        Assert.Equal((503, InventoryFutureAllocationCodesV1.ServiceUnavailable), (error.Status, error.Code));
    }

    [Fact]
    public async Task Real_no_source_stays_no_source_and_company_access_loss_is_not_a_success()
    {
        var owner = new Owner(new(InventoryProductUnitConversionOutcomeV1.NoSource, null));
        var converter = new InventoryProductFutureUnitConverterV1(owner);
        Assert.Equal(InventoryFutureUnitConversionOutcomeV1.NoSource,
            (await converter.ConvertToKilogramsAsync(Guid.NewGuid(), "1", 1m, Guid.NewGuid())).Outcome);
        owner.Answer = new(InventoryProductUnitConversionOutcomeV1.Denied, null);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => converter.ConvertToKilogramsAsync(Guid.NewGuid(), "1", 1m, Guid.NewGuid()));
    }

    private sealed class Owner(InventoryProductUnitConversionAnswerV1 answer) : IInventoryProductUnitConversionSourceV1
    {
        public InventoryProductUnitConversionAnswerV1 Answer { get; set; } = answer;
        public int Calls { get; private set; }
        public Task<InventoryProductUnitConversionAnswerV1> ConvertAsync(Guid productId, decimal quantity, Guid fromUnitId,
            Guid? toUnitId, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(Answer); }
    }
}
