using System.Data;
using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_real_inventory_generation_and_the_physical_close_rollback_together_when_the_later_host_step_fails()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var seed = await SeedGenerationOutputAsync(run, "SYN-COST-HOST-ROLLBACK");
        var audits = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        await using (var scope = GenerationHostScope.Create(run))
        {
            await using var transaction = await scope.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await scope.Store.CloseOrderAsync(seed.OrderId, seed.Version, run.Actor, At.AddHours(1), scope.Metadata);
            await scope.Host.StageClosedAsync(seed.OrderId, scope.Metadata, CancellationToken.None);
            var generation = Assert.Single(await scope.Store.ReadCostGenerationsAsync(seed.OrderId));
            Assert.Equal(ManufacturingCostGenerationStateV1.Unreadable, generation.State);
            Assert.DoesNotContain("\"originalTlAmount\":0", generation.ResultJson, StringComparison.Ordinal);
            await transaction.RollbackAsync(); // A failure after both real owners staged must keep the physical order open.
        }
        await using var fresh = run.Store(CompanyA);
        Assert.Equal(OrderStatus.Open, (await fresh.Orders.FindOrderAsync(seed.OrderId))!.Status);
        Assert.Empty(await fresh.Corrections.ReadCostGenerationsAsync(seed.OrderId));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.ManufacturingCostGenerations"));
        Assert.Equal(audits, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
    }

    [SqlServerFact]
    public async Task A_real_unreadable_inventory_generation_reverses_with_reopen_and_a_second_close_creates_a_new_generation()
    {
        await using var run = await StartManufacturingInventoryRunAsync(); var seed = await SeedGenerationOutputAsync(run, "SYN-COST-HOST-REOPEN");
        Guid firstId; long closedVersion;
        await using (var scope = GenerationHostScope.Create(run))
        {
            await using var transaction = await scope.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await scope.Store.CloseOrderAsync(seed.OrderId, seed.Version, run.Actor, At.AddHours(1), scope.Metadata);
            await scope.Host.StageClosedAsync(seed.OrderId, scope.Metadata, CancellationToken.None);
            var first = Assert.Single(await scope.Store.ReadCostGenerationsAsync(seed.OrderId)); firstId = first.GenerationId; closedVersion = first.ClosedOrderVersion;
            await transaction.CommitAsync();
        }
        await using (var scope = GenerationHostScope.Create(run))
        {
            var command = new ManufacturingOrderReopeningCommandV1(CompanyA, seed.OrderId, closedVersion, "Synthetic controlled reopening");
            var reopened = await scope.Host.ReopenAsync(command, token => scope.Store.ReopenOrderAsync(seed.OrderId, closedVersion, run.Actor,
                At.AddHours(2), command.Reason!, scope.Metadata, (_, _) => Task.FromResult<ManufacturingApiResult<object>?>(null), token),
                scope.Metadata, CancellationToken.None);
            await using var transaction = await scope.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await scope.Store.CloseOrderAsync(seed.OrderId, reopened.Version, run.Actor, At.AddHours(3), scope.Metadata);
            await scope.Host.StageClosedAsync(seed.OrderId, scope.Metadata, CancellationToken.None); await transaction.CommitAsync();
        }
        await using var fresh = run.Store(CompanyA); var history = await fresh.Corrections.ReadCostGenerationsAsync(seed.OrderId);
        Assert.Equal([ManufacturingCostGenerationStateV1.Unreadable, ManufacturingCostGenerationStateV1.Reversed, ManufacturingCostGenerationStateV1.Unreadable], history.Select(row => row.State));
        Assert.Equal(firstId, history[1].GenerationId); Assert.NotEqual(firstId, history[2].GenerationId);
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.ManufacturingCostGenerations"));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.ManufacturingCostGenerationHistory"));
    }

    private static async Task<(Guid OrderId, long Version)> SeedGenerationOutputAsync(Run run, string code)
    {
        var seed = await PrepareIssuedOrderAsync(run, code, 100m);
        await using var scope = ReturnOutputHostScope.Create(run, new ManufacturingHostAuthority(GrantedManufacturingScope()), new ManufacturingOutputAdmission());
        var output = await scope.Host.DeclareOutputAsync(OutputCommand(seed, seed.OrderVersion, seed.ParentLabelVersion, 100m),
            ManufacturingHostMetadata(run, "generation-output"));
        return (seed.OrderId, output.OrderVersion);
    }

    /// <summary>Real SQL stores/Inventory factory and audit owner; only the explicit cost-source outage and authority are synthetic.</summary>
    private sealed class GenerationHostScope(AsyncServiceScope services, ManufacturingDbContext context, ManufacturingStore store,
        ManufacturingCostGenerationHost host, ManufacturingCommandMetadata metadata) : IAsyncDisposable
    {
        public ManufacturingDbContext Context => context;
        public ManufacturingStore Store => store;
        public ManufacturingCostGenerationHost Host => host;
        public ManufacturingCommandMetadata Metadata => metadata;
        public static GenerationHostScope Create(Run run)
        {
            var services = run.Factory.Services.CreateAsyncScope(); var provider = services.ServiceProvider;
            var company = provider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
            var collector = provider.GetRequiredService<IAuditEventCollector>(); var writer = provider.GetRequiredService<IAuditTransactionWriterV1>();
            var context = new ManufacturingDbContext(new DbContextOptionsBuilder<ManufacturingDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", ManufacturingDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).Options, company);
            var store = new ManufacturingStore(context, company, collector, TimeProvider.System);
            var movements = new ManufacturingInventoryCostMovementReader(company, new ManufacturingHostCurrentUser(run.Actor, CompanyA),
                provider.GetRequiredService<IInventoryManufacturingCostReadPortV1>());
            var host = new ManufacturingCostGenerationHost(context, store, company, new ManufacturingHostCurrentUser(run.Actor, CompanyA),
                new GenerationHostAuthority(), new ExplicitCostSourceOutage(), movements, collector, writer, TimeProvider.System,
                provider.GetRequiredService<ManufacturingInventoryCostGenerationFactoryV1>());
            return new(services, context, store, host, ManufacturingHostMetadata(run, "cost-generation") with { PermissionCode = ErpPermissions.ManufacturingOrdersClose });
        }
        public async ValueTask DisposeAsync() { await context.DisposeAsync(); await services.DisposeAsync(); }
    }
    private sealed class GenerationHostAuthority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        { Assert.Equal(CompanyA, companyId); return Task.FromResult(GrantedManufacturingScope()); }
    }
    private sealed class ExplicitCostSourceOutage : IManufacturingScrapCostDistributionSourceV1
    {
        public Task<ManufacturingScrapCostDistributionSourceReadV1> ReadAsync(Guid companyId, Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult(new ManufacturingScrapCostDistributionSourceReadV1(ManufacturingScrapCostDistributionSourceStatusV1.Unavailable, companyId, orderId, null));
    }
}
