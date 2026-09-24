using GoymenERP.Api.ManufacturingRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Lots;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using InventoryKilograms = GoymenERP.Modules.Inventory.Domain.Quantities.Kilograms;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class ManufacturingSqlServerRelationalAcceptanceTests
{
    private static readonly Guid InventoryHostProduct =
        Guid.Parse("9b000000-0000-4000-8000-000000000451");
    private static readonly Guid InventoryHostLotA =
        Guid.Parse("9b000000-0000-4000-8000-000000000452");
    private static readonly Guid InventoryHostLotB =
        Guid.Parse("9b000000-0000-4000-8000-000000000453");

    [SqlServerFact]
    public async Task Manufacturing_input_issue_commits_one_fact_one_physical_effect_and_replays_across_transport_keys()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var parent = await SeedManufacturingParentAsync(run, "SYN-MFG-HOST-COMMIT", 100m);
        var authority = new ManufacturingHostAuthority(GrantedManufacturingScope());
        await using var scope = ManufacturingHostScope.Create(run, CompanyA, authority);
        var command = await SeedManufacturingOrderAsync(run, scope.Store, parent.Label.Id, expectedLabelVersion: 2,
            quantity: 25m);
        var firstMetadata = ManufacturingHostMetadata(run, "commit-first");

        var recorded = await scope.Host.IssueAsync(command, firstMetadata);
        var sameKeyReplay = await scope.Host.IssueAsync(command,
            firstMetadata with { CorrelationId = $"mfg-host-same-key-{Guid.NewGuid():N}" });
        var differentKeyReplay = await scope.Host.IssueAsync(command,
            ManufacturingHostMetadata(run, "commit-different-key"));

        Assert.False(recorded.IsReplay);
        Assert.True(sameKeyReplay.IsReplay);
        Assert.True(differentKeyReplay.IsReplay);
        Assert.Equal(2, recorded.OrderVersion);
        Assert.Equal(3, recorded.ParentLabelVersion);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Facts WHERE Id = @id", ("@id", command.IssueId)));
        Assert.Equal(2L, await run.ScalarAsync<long>(
            "SELECT Version FROM manufacturing.Orders WHERE Id = @id", ("@id", command.OrderId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Id = @id", ("@id", command.MovementId)));
        Assert.Equal(75m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @label AND Block = 1",
            ("@label", command.ParentLabelId)));
        Assert.Equal(3L, await run.ScalarAsync<long>(
            "SELECT Version FROM inventory.Labels WHERE Id = @label", ("@label", command.ParentLabelId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE TargetId = @id", ("@id", command.IssueId)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = @correlation",
            ("@correlation", firstMetadata.CorrelationId)));

        // A fact written before this host existed must not be silently completed later. The
        // participant reaches its staged callback, the host rejects the incomplete pair, and the
        // attempted Inventory movement rolls back while the old Manufacturing fact remains.
        var legacy = await SeedManufacturingOrderAsync(run, scope.Store, parent.Label.Id, 3, 5m);
        var legacyMetadata = ManufacturingHostMetadata(run, "legacy-fact");
        Assert.Equal(ManufacturingWriteOutcome.Recorded, await scope.Store.IssueInputAsync(
            legacy.OrderId, legacy.ExpectedOrderVersion, legacy.IssueId, legacy.ParentLabelId,
            legacy.Quantity, legacy.OccurredAtUtc, legacyMetadata));
        var legacyError = await Assert.ThrowsAsync<ManufacturingInventoryInputIssueHostException>(() =>
            scope.Host.IssueAsync(legacy, ManufacturingHostMetadata(run, "legacy-retry")));
        Assert.Equal(ManufacturingInventoryInputIssueHostCodes.IncompleteRealization, legacyError.Code);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Facts WHERE Id = @id", ("@id", legacy.IssueId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Id = @id", ("@id", legacy.MovementId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE TargetId = @id", ("@id", legacy.IssueId)));
    }

    [SqlServerFact]
    public async Task Manufacturing_input_issue_rolls_back_both_owners_when_inventory_stage_fails()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var parent = await SeedManufacturingParentAsync(run, "SYN-MFG-HOST-STAGE-ROLLBACK", 100m);
        var authority = new ManufacturingHostAuthority(GrantedManufacturingScope());
        await using var scope = ManufacturingHostScope.Create(run, CompanyA, authority,
            (stage, _) => stage == ManufacturingInventoryIssueStage.InventoryStaged
                ? Task.FromException(new SyntheticManufacturingHostFailure())
                : Task.CompletedTask);
        var command = await SeedManufacturingOrderAsync(run, scope.Store, parent.Label.Id, 2, 25m);
        var metadata = ManufacturingHostMetadata(run, "inventory-stage-failure");

        await Assert.ThrowsAsync<SyntheticManufacturingHostFailure>(() => scope.Host.IssueAsync(command, metadata));

        await AssertNoManufacturingInventoryIssueEffectAsync(run, command, metadata.CorrelationId, 100m, 2L);
    }

    [SqlServerFact]
    public async Task Manufacturing_input_issue_rolls_back_both_owners_when_final_authority_is_revoked()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var parent = await SeedManufacturingParentAsync(run, "SYN-MFG-HOST-AUTH-ROLLBACK", 100m);
        var authority = new ManufacturingHostAuthority(GrantedManufacturingScope(), PermissionScope.Denied);
        await using var scope = ManufacturingHostScope.Create(run, CompanyA, authority);
        var command = await SeedManufacturingOrderAsync(run, scope.Store, parent.Label.Id, 2, 25m);
        var metadata = ManufacturingHostMetadata(run, "authority-revoked");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => scope.Host.IssueAsync(command, metadata));

        Assert.Equal(2, authority.Calls);
        await AssertNoManufacturingInventoryIssueEffectAsync(run, command, metadata.CorrelationId, 100m, 2L);
    }

    [SqlServerFact]
    public async Task Manufacturing_input_issue_refuses_stale_and_foreign_parent_references_without_partial_fact()
    {
        await using var run = await StartManufacturingInventoryRunAsync();
        var stale = await SeedManufacturingParentAsync(run, "SYN-MFG-HOST-STALE", 100m);
        var authority = new ManufacturingHostAuthority(GrantedManufacturingScope());
        await using var scope = ManufacturingHostScope.Create(run, CompanyA, authority);
        var staleCommand = await SeedManufacturingOrderAsync(run, scope.Store, stale.Label.Id, 2, 10m);
        await using (var inventory = InventoryHostStore(run, CompanyA))
        {
            await inventory.Movements.RecordMovementAsync(
                InventoryHostMovement(CompanyA, StockMovementKind.Issue,
                    new StockSourceReference("synthetic.manufacturing-host-stale", Guid.NewGuid()),
                    InventoryHostOut(InventoryHostPosition(
                        CompanyA, stale.LocationId, stale.Label, BlockState.Free), 1m)),
                InventoryHostMetadata(run) with
                {
                    CorrelationId = $"mfg-host-stale-prerequisite-{Guid.NewGuid():N}",
                });
        }

        var staleError = await Assert.ThrowsAsync<InventoryManufacturingRealizationException>(() =>
            scope.Host.IssueAsync(staleCommand, ManufacturingHostMetadata(run, "stale-parent")));
        Assert.Equal((409, InventoryManufacturingRealizationCodesV1.LabelVersionConflict),
            (staleError.Status, staleError.Code));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Facts WHERE Id = @id", ("@id", staleCommand.IssueId)));
        Assert.Equal(1L, await run.ScalarAsync<long>(
            "SELECT Version FROM manufacturing.Orders WHERE Id = @id", ("@id", staleCommand.OrderId)));
        Assert.Equal(99m, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @label AND Block = 1",
            ("@label", stale.Label.Id)));

        var foreign = await SeedManufacturingParentForCompanyAsync(run, CompanyB,
            "SYN-MFG-HOST-FOREIGN", 100m);
        var foreignCommand = await SeedManufacturingOrderAsync(run, scope.Store, foreign.Label.Id, 2, 10m);
        var foreignError = await Assert.ThrowsAsync<InventoryManufacturingRealizationException>(() =>
            scope.Host.IssueAsync(foreignCommand, ManufacturingHostMetadata(run, "foreign-parent")));
        Assert.Equal((404, InventoryManufacturingRealizationCodesV1.InvalidReference),
            (foreignError.Status, foreignError.Code));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Facts WHERE Id = @id", ("@id", foreignCommand.IssueId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Id = @id", ("@id", foreignCommand.MovementId)));
    }

    private static async Task<ManufacturingInventoryInputIssueCommand> SeedManufacturingOrderAsync(
        Run run,
        ManufacturingStore store,
        Guid parentLabelId,
        long expectedLabelVersion,
        decimal quantity)
    {
        var workCenterId = await run.ScalarAsync<Guid>(
            "SELECT TOP (1) Id FROM organization.WorkCenters WHERE CompanyId = @company ORDER BY Id",
            ("@company", CompanyA));
        var plan = new ProductionPlan(Guid.NewGuid(), CompanyA, workCenterId, ManufacturingProcess.Slitting,
            [new PlannedInput(parentLabelId, new Kilograms(quantity), expectedLabelVersion)],
            new ProductionTarget(TargetBasis.InputMass, new Kilograms(quantity)));
        var orderId = Guid.NewGuid();
        await store.SavePlanAsync(plan, ManufacturingHostMetadata(run, "seed-plan"));
        await store.ApprovePlanAsync(plan.Id, plan.Version, run.Actor, At,
            ManufacturingHostMetadata(run, "seed-approval"));
        await store.CreateOrderAsync(orderId, plan.Id, ManufacturingHostMetadata(run, "seed-order"));
        return new ManufacturingInventoryInputIssueCommand(orderId, 1, Guid.NewGuid(), parentLabelId,
            Guid.NewGuid(), new Kilograms(quantity), At.AddMinutes(1));
    }

    private static async Task<Run> StartManufacturingInventoryRunAsync()
    {
        var run = await Run.StartAsync();
        try
        {
            await using (var context = InventoryHostContext(run, CompanyA))
            {
                await context.Database.MigrateAsync();
            }

            foreach (var (companyId, lotId) in new[]
                     {
                         (CompanyA, InventoryHostLotA),
                         (CompanyB, InventoryHostLotB),
                     })
            {
                await using var store = InventoryHostStore(run, companyId);
                await store.Lots.SaveLotAsync(
                    new MaterialLot(lotId, companyId, InventoryHostProduct),
                    [],
                    InventoryHostMetadata(run) with
                    {
                        CorrelationId = $"mfg-host-lot-{companyId:N}-{Guid.NewGuid():N}",
                    });
            }

            return run;
        }
        catch
        {
            await run.DisposeAsync();
            throw;
        }
    }

    private static async Task<ManufacturingParentSeed> SeedManufacturingParentAsync(
        Run run, string code, decimal quantity) =>
        await SeedManufacturingParentForCompanyAsync(run, CompanyA, code, quantity);

    private static async Task<ManufacturingParentSeed> SeedManufacturingParentForCompanyAsync(
        Run run, Guid companyId, string code, decimal quantity)
    {
        var location = await InventoryHostLocationOfAsync(run, companyId);
        var label = InventoryHostLabel(companyId, code);
        await using var store = InventoryHostStore(run, companyId);
        await store.Labels.SaveLabelAsync(label,
            InventoryHostMetadata(run) with
            {
                CorrelationId = $"mfg-host-foreign-label-{Guid.NewGuid():N}",
            });
        await store.Movements.RecordMovementAsync(
            InventoryHostMovement(companyId, StockMovementKind.Receipt,
                new StockSourceReference("synthetic.manufacturing-host-parent", Guid.NewGuid()),
                InventoryHostIn(InventoryHostPosition(
                    companyId, location, label, BlockState.Free), quantity)),
            InventoryHostMetadata(run) with
            {
                CorrelationId = $"mfg-host-foreign-stock-{Guid.NewGuid():N}",
            });
        return new ManufacturingParentSeed(label, location);
    }

    private static async Task<Guid> InventoryHostLocationOfAsync(Run run, Guid companyId) =>
        await run.ScalarAsync<Guid>(
            "SELECT TOP (1) Id FROM organization.Locations WHERE CompanyId = @company ORDER BY Id",
            ("@company", companyId));

    private static InventoryDbContext InventoryHostContext(Run run, Guid companyId)
    {
        var company = new CompanyContext();
        company.Set(companyId);
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.Schema))
            .Options;
        return new InventoryDbContext(options, company);
    }

    private static InventoryHostStoreScope InventoryHostStore(Run run, Guid companyId)
    {
        var company = new CompanyContext();
        company.Set(companyId);
        var audit = new AuditEventCollector();
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(audit, company))
            .Options;
        var context = new InventoryDbContext(options, company);
        var settings = new ApprovedSettingsPrerequisiteProvider(
            company, new ManufacturingHostCurrentUser(run.Actor, companyId));
        var store = new InventoryStore(
            context, company, audit, new InventoryKilogramScale(settings), TimeProvider.System);
        return new InventoryHostStoreScope(context, store);
    }

    private static MaterialLabel InventoryHostLabel(Guid companyId, string code) =>
        new(Guid.NewGuid(), companyId, code, InventoryHostProduct,
            companyId == CompanyA ? InventoryHostLotA : InventoryHostLotB);

    private static StockPosition InventoryHostPosition(
        Guid companyId, Guid locationId, MaterialLabel label, BlockState block) =>
        new(companyId,
            new StockOwner(StockPartyKind.Company, companyId),
            new StockCustodian(StockPartyKind.Company, companyId),
            locationId,
            label,
            block);

    private static StockMovementLine InventoryHostIn(StockPosition position, decimal kilograms) =>
        new(position, StockDirection.In, InventoryKilograms.Positive(kilograms));

    private static StockMovementLine InventoryHostOut(StockPosition position, decimal kilograms) =>
        new(position, StockDirection.Out, InventoryKilograms.Positive(kilograms));

    private static StockMovement InventoryHostMovement(
        Guid companyId, StockMovementKind kind, StockSourceReference source, params StockMovementLine[] lines) =>
        new(Guid.NewGuid(), companyId, kind, At, source, lines);

    private static InventoryCommandMetadata InventoryHostMetadata(Run run) => new(
        run.Actor.ToString("D"),
        ErpPermissions.ManufacturingFactsRecord,
        ["SYNTHETIC-MANUFACTURING"],
        $"manufacturing-host-inventory-{Guid.NewGuid():N}",
        null,
        $"manufacturing-host-inventory-{Guid.NewGuid():N}",
        "Integration");

    private static ISettingsPrerequisiteProvider ManufacturingSettings(Run run)
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        return new ApprovedSettingsPrerequisiteProvider(
            company, new ManufacturingHostCurrentUser(run.Actor, CompanyA));
    }

    private static async Task AssertNoManufacturingInventoryIssueEffectAsync(
        Run run,
        ManufacturingInventoryInputIssueCommand command,
        string correlationId,
        decimal expectedBalance,
        long expectedLabelVersion)
    {
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM manufacturing.Facts WHERE Id = @id", ("@id", command.IssueId)));
        Assert.Equal(1L, await run.ScalarAsync<long>(
            "SELECT Version FROM manufacturing.Orders WHERE Id = @id", ("@id", command.OrderId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements WHERE Id = @id", ("@id", command.MovementId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE TargetId = @id", ("@id", command.IssueId)));
        Assert.Equal(expectedBalance, await run.ScalarAsync<decimal>(
            "SELECT QuantityKg FROM inventory.Balances WHERE LabelId = @label AND Block = 1",
            ("@label", command.ParentLabelId)));
        Assert.Equal(expectedLabelVersion, await run.ScalarAsync<long>(
            "SELECT Version FROM inventory.Labels WHERE Id = @label", ("@label", command.ParentLabelId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = @correlation",
            ("@correlation", correlationId)));
    }

    private static ManufacturingCommandMetadata ManufacturingHostMetadata(Run run, string operation) => new(
        run.Actor.ToString("D"), ErpPermissions.ManufacturingFactsRecord, ["SYNTHETIC-MANUFACTURING"],
        $"manufacturing-host-{operation}-{Guid.NewGuid():N}", null,
        $"manufacturing-host-{operation}-{Guid.NewGuid():N}", "Integration");

    private static PermissionScope GrantedManufacturingScope() => new(
        true, true, new HashSet<string>(["SYNTHETIC-MANUFACTURING"], StringComparer.Ordinal),
        new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());

    private sealed class ManufacturingHostAuthority(params PermissionScope[] decisions)
        : IErpAuthorityRevalidationService
    {
        private int calls;
        public int Calls => calls;

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(ErpPermissions.ManufacturingFactsRecord, permission);
            Assert.Equal(CompanyA, companyId);
            cancellationToken.ThrowIfCancellationRequested();
            var index = Math.Min(Interlocked.Increment(ref calls) - 1, decisions.Length - 1);
            return Task.FromResult(decisions[index]);
        }
    }

    private sealed class ManufacturingHostScope(
        ManufacturingDbContext context,
        ManufacturingStore store,
        ManufacturingInventoryInputIssueHost host) : IAsyncDisposable
    {
        public ManufacturingStore Store => store;
        public ManufacturingInventoryInputIssueHost Host => host;

        public static ManufacturingHostScope Create(
            Run run,
            Guid companyId,
            IErpAuthorityRevalidationService authority,
            Func<ManufacturingInventoryIssueStage, CancellationToken, Task>? observer = null)
        {
            var company = new CompanyContext();
            company.Set(companyId);
            var audit = new AuditEventCollector();
            var options = new DbContextOptionsBuilder<ManufacturingDbContext>()
                .UseSqlServer(run.Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", ManufacturingDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(audit, company))
                .Options;
            var context = new ManufacturingDbContext(options, company);
            var store = new ManufacturingStore(context, company, audit, TimeProvider.System);
            var host = new ManufacturingInventoryInputIssueHost(context, store, company, authority,
                ManufacturingSettings(run), observer);
            return new ManufacturingHostScope(context, store, host);
        }

        public ValueTask DisposeAsync() => context.DisposeAsync();
    }

    private sealed class InventoryHostStoreScope(InventoryDbContext context, InventoryStore store)
        : IAsyncDisposable
    {
        public IInventoryLotStore Lots => store;
        public IInventoryLabelStore Labels => store;
        public IInventoryMovementStore Movements => store;

        public ValueTask DisposeAsync() => context.DisposeAsync();
    }

    private sealed record ManufacturingParentSeed(MaterialLabel Label, Guid LocationId);

    private sealed class ManufacturingHostCurrentUser(Guid actorId, Guid companyId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actorId.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { companyId };
        public bool CanAccessCompany(Guid candidate) => AllowedCompanyIds.Contains(candidate);
    }

    private sealed class SyntheticManufacturingHostFailure : Exception;
}
