using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

// ChangeTracker.Local source isolation only; the existing InMemory provider is not SQL acceptance.
public sealed class InventoryTrackedCostPoolIsolationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Tracked_cost_pools_from_another_company_never_supply_or_receive_the_active_company_cost(bool foreignCurrency)
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        var otherCompany = Guid.NewGuid(); var location = Guid.NewGuid(); var product = Guid.NewGuid();
        var currency = foreignCurrency ? ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD") : SettingsCurrencyIdentitiesV1.TurkishLira;
        await using var context = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, company);
        var own = new ExactCostPoolRow { CompanyId = company.CompanyId, LocationId = location, ProductId = product,
            CurrencyId = currency, Version = 7, Status = 1, AverageCostPerKg = 10m,
            QuantityRatioJson = "{\"numerator\":\"0\",\"denominator\":\"1\"}" };
        var foreign = new ExactCostPoolRow { CompanyId = otherCompany, LocationId = location, ProductId = product,
            CurrencyId = currency, Version = 99, Status = 1, AverageCostPerKg = 999m,
            QuantityRatioJson = "{\"numerator\":\"9\",\"denominator\":\"1\"}" };
        context.ExactCostPools.AddRange(foreign, own);
        var ownTl = new AverageCostLedgerRow { Id = Guid.NewGuid(), CompanyId = company.CompanyId,
            LocationId = location, ProductId = product, Version = 7, QuantityKg = 0m, AverageCostPerKg = 10m, Status = 1 };
        var foreignTl = new AverageCostLedgerRow { Id = Guid.NewGuid(), CompanyId = otherCompany,
            LocationId = location, ProductId = product, Version = 99, QuantityKg = 9m, AverageCostPerKg = 999m, Status = 1 };
        var ownFx = new CurrencyAverageCostLedgerRow { Id = Guid.NewGuid(), CompanyId = company.CompanyId,
            LocationId = location, ProductId = product, CurrencyId = currency, Version = 7,
            QuantityKg = 0m, AverageCostPerKg = 10m, Status = 1 };
        var foreignFx = new CurrencyAverageCostLedgerRow { Id = Guid.NewGuid(), CompanyId = otherCompany,
            LocationId = location, ProductId = product, CurrencyId = currency, Version = 99,
            QuantityKg = 9m, AverageCostPerKg = 999m, Status = 1 };
        if (foreignCurrency) context.CurrencyAverageCostLedger.AddRange(foreignFx, ownFx);
        else context.AverageCostLedger.AddRange(foreignTl, ownTl);

        var before = await InventoryStore.ReadCostPoolAsync(context, company, location, product, currency, default);
        Assert.True(before.IsConsistent);
        Assert.Same(own, before.ExactRow);
        Assert.Equal(10m, before.ExactPosition!.AverageCostPerKg);
        Assert.Equal(7, before.Version);
        if (foreignCurrency) { Assert.Same(ownFx, before.ForeignRow); Assert.Null(before.TlRow); }
        else { Assert.Same(ownTl, before.TlRow); Assert.Null(before.ForeignRow); }
        await InventoryStore.WriteCostPoolAsync(context, company, TimeProvider.System, location, product, currency,
            before, new ExactAverageCostPool(ExactCostRatio.Of(0m), 12.5m), default);
        Assert.Equal(12.5m, own.AverageCostPerKg);
        Assert.Equal(12.5m, foreignCurrency ? ownFx.AverageCostPerKg : ownTl.AverageCostPerKg);
        Assert.Equal(999m, foreign.AverageCostPerKg);
        Assert.Equal(99, foreign.Version);
        Assert.Equal(999m, foreignCurrency ? foreignFx.AverageCostPerKg : foreignTl.AverageCostPerKg);
        Assert.Equal(9m, foreignCurrency ? foreignFx.QuantityKg : foreignTl.QuantityKg);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Foreign_tracked_recognition_or_generation_cannot_erase_an_unreadable_active_company_pending_cost(bool foreignGeneration)
    {
        var company = new CompanyContext(); company.Set(Guid.NewGuid());
        var otherCompany = Guid.NewGuid(); var movement = Guid.NewGuid(); var generation = Guid.NewGuid();
        var location = Guid.NewGuid(); var product = Guid.NewGuid(); var currency = SettingsCurrencyIdentitiesV1.TurkishLira;
        await using var context = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, company);
        // No actual persisted lineage proves this pending entry's cost. A foreign tracked proof must not hide it.
        context.PendingMovementCosts.Add(new PendingMovementCostRow { CompanyId = company.CompanyId,
            MovementId = movement, LineNumber = 1, CurrencyId = currency, LabelId = Guid.NewGuid(),
            ProductId = product, LocationId = location, QuantityKg = 1m, Reason = "SENTETIK-source-unreadable" });
        context.MovementCostRecognitions.Add(new MovementCostRecognitionRow { Id = Guid.NewGuid(),
            CompanyId = foreignGeneration ? company.CompanyId : otherCompany, MovementId = movement, LineNumber = 1,
            CurrencyId = currency, GenerationId = foreignGeneration ? generation : null });
        if (foreignGeneration) context.ManufacturingCostGenerations.Add(new ManufacturingCostGenerationRow {
            CompanyId = otherCompany, Id = generation, ManufacturingOrderId = Guid.NewGuid(),
            State = (int)InventoryManufacturingCostGenerationStateV1.Recognized });

        var pool = await InventoryStore.ReadCostPoolAsync(context, company, location, product, currency, default);
        Assert.False(pool.SourceReadable);
        Assert.False(pool.IsConsistent);
        Assert.Null(pool.ExactPosition);
        Assert.Single(context.PendingMovementCosts.Local);
    }

}
