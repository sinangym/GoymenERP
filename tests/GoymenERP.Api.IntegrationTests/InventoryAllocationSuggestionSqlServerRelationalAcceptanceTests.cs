using System.Net;
using System.Net.Http.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// KLN-D34/D39/D40/D70 (STK-S30) on real SQL Server: the label suggestion read over Inventory's own company-filtered
/// rows, the server-side refusal of an unverified substitute ("muadil") create, the sales-only substitute check and the
/// forward-fix Down of the substitute flag migration. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    private const string SubstituteFlagMigration = "20261115183012_AddInventoryAllocationSubstituteFlag";
    private const string BeforeSubstituteFlagMigration = "20261115151824_AddInventoryTransferDispatchCancellation";

    [SqlServerFact]
    public async Task Physical_allocation_suggestions_list_only_unreserved_company_labels_of_the_line_product()
    {
        await using var run = await Run.StartAsync();
        var partial = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-SUG-A", 100m);
        var full = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-SUG-B", 50m);
        var foreign = await SeedFreePhysicalSourceAsync(run, CompanyB, "SYN-SUG-C", 100m);
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.CreateAsync(AllocationRequest(partial, "60"),
                Metadata(run) with { IdempotencyKey = $"sug-{Guid.NewGuid():N}" });
        }

        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.CreateAsync(AllocationRequest(full, "50"),
                Metadata(run) with { IdempotencyKey = $"sug-{Guid.NewGuid():N}" });
        }

        var partialVersionAfterAllocation = await run.ScalarAsync<long>(
            "SELECT Version FROM inventory.Labels WHERE Id = @label", ("@label", partial.LabelId));
        Assert.Equal(checked(partial.LabelVersion + 1), partialVersionAfterAllocation);
        const string labelsSql = "SELECT * FROM inventory.Labels ORDER BY Id FOR JSON PATH";
        var labelsBeforeReads = await run.ScalarAsync<string>(labelsSql);
        var allocations = await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations");
        var movements = await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements");
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var actor = await GrantAllocationAsync(run, TestUsers.CompanyA, CompanyA,
            ErpPermissions.InventoryPhysicalAllocationWrite);
        var ownerAnswer = new SalesOrderLineReferenceAnswerV1(
            SalesOrderLineReferenceOutcomeV1.Approved,
            new SalesOrderLineReferenceV1(CompanyA, orderId, lineId, 1, Guid.NewGuid(), Guid.NewGuid(),
                partial.ProductId, "7", Guid.NewGuid(), "10", 0, 4),
            null);
        await using var host = run.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISalesOrderLineReferencePortV1>();
            services.AddSingleton<ISalesOrderLineReferencePortV1>(new FixedApprovedSalesLine(ownerAnswer));
        }));
        var path = $"/api/v1/inventory/physical-allocations/suggestions?salesOrderId={orderId:D}&salesOrderLineId={lineId:D}";

        using (var denied = ClientFor(host, TestUsers.CompanyAViewer, CompanyA))
        using (var deniedResponse = await denied.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
        }

        using var client = ClientFor(host, actor, CompanyA);
        using (var malformed = await client.GetAsync($"/api/v1/inventory/physical-allocations/suggestions?salesOrderId={orderId:D}"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        }

        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var answer = (await response.Content.ReadFromJsonAsync<InventoryAllocationSuggestionsV1>())!;
        Assert.Equal((CompanyA, orderId, lineId, partial.ProductId, "7"),
            (answer.CompanyId, answer.SalesOrderId, answer.SalesOrderLineId, answer.ProductId, answer.ProductVersion));
        Assert.Equal((InventorySimilarProductModesV1.SameProductOnly, InventoryAllocationAiSuggestionStatesV1.Disabled, false),
            (answer.SimilarProductMode, answer.AiSuggestions, answer.Truncated));
        var item = Assert.Single(answer.Items);
        Assert.Equal((partial.LabelId, partialVersionAfterAllocation.ToString(), "SYN-SUG-A", partial.ProductId, "40"),
            (item.LabelId, item.LabelVersion, item.LabelCode, item.ProductId, item.UnreservedKg));
        Assert.Equal((InventoryAllocationSuggestionKindsV1.SameProduct, false, (string?)null),
            (item.Kind, item.IsSubstitute, item.MaximumDeviationPercent));
        Assert.DoesNotContain(answer.Items, entry => entry.LabelId == full.LabelId || entry.LabelId == foreign.LabelId);

        Assert.Equal(allocations, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations"));
        Assert.Equal(movements, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"));
        Assert.Equal(labelsBeforeReads, await run.ScalarAsync<string>(labelsSql));
    }

    [SqlServerFact]
    public async Task Physical_allocation_substitute_is_refused_without_a_Settings_tolerance_and_nothing_is_stored()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-SUB-NOTOL", 100m);
        var before = await AllocationEffectCountsAsync(run);

        await using (var scope = run.Store(CompanyA))
        {
            var substitute = await Assert.ThrowsAsync<InventoryAllocationException>(() => scope.Store.CreateAsync(
                AllocationRequest(source, "10") with { ProductId = Guid.NewGuid(), IsSubstitute = true },
                Metadata(run) with { IdempotencyKey = $"sub-{Guid.NewGuid():N}" }));
            Assert.Equal((422, InventoryAllocationCodesV1.SubstituteNotSimilar), (substitute.Status, substitute.Code));
        }

        await using (var scope = run.Store(CompanyA))
        {
            var ownProduct = await Assert.ThrowsAsync<InventoryAllocationException>(() => scope.Store.CreateAsync(
                AllocationRequest(source, "10") with { IsSubstitute = true },
                Metadata(run) with { IdempotencyKey = $"sub-{Guid.NewGuid():N}" }));
            Assert.Equal((422, InventoryAllocationCodesV1.SubstituteNotSimilar), (ownProduct.Status, ownProduct.Code));
        }

        await using (var scope = run.Store(CompanyA))
        {
            var unflagged = await Assert.ThrowsAsync<InventoryAllocationException>(() => scope.Store.CreateAsync(
                AllocationRequest(source, "10") with { ProductId = Guid.NewGuid() },
                Metadata(run) with { IdempotencyKey = $"sub-{Guid.NewGuid():N}" }));
            Assert.Equal((422, InventoryAllocationCodesV1.InvalidReference), (unflagged.Status, unflagged.Code));
        }

        Assert.Equal(before.Allocations, (await AllocationEffectCountsAsync(run)).Allocations);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations WHERE IsSubstitute = 1"));
        Assert.Equal(100m, await FreeAfterAllocationsAsync(run, source.LabelId));
    }

    [SqlServerFact]
    public async Task Physical_allocation_substitute_check_allows_the_flag_only_on_a_sales_line_demand()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-SUB-CHECK", 50m);
        var legacyId = Guid.NewGuid();
        await ExecuteAsync(run,
            PhysicalAllocationInsertSql("NULL", "NULL", "NULL", "NULL", "NULL", "NULL", "NULL", "10", "1", "NULL", "NULL"),
            ("@id", legacyId), ("@label", source.LabelId));

        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Allocations WHERE Id = @id AND IsSubstitute = 1", ("@id", legacyId)));
        var rejected = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(run,
            "UPDATE inventory.Allocations SET IsSubstitute = 1 WHERE Id = @id", ("@id", legacyId)));
        Assert.Equal(547, rejected.Number);
        Assert.Contains("CK_Allocations_Substitute", rejected.Message, StringComparison.Ordinal);

        var salesId = Guid.NewGuid();
        await ExecuteAsync(run,
            PhysicalAllocationInsertSql(
                "N'sales.order-line'", "NEWID()", "NEWID()", "b.ProductId", "'1'", "N'approved line'", "NULL",
                "10", "1", "NULL", "NULL"),
            ("@id", salesId), ("@label", source.LabelId));
        Assert.Equal(1, await ExecuteAsync(run,
            "UPDATE inventory.Allocations SET IsSubstitute = 1 WHERE Id = @id", ("@id", salesId)));
    }

    [SqlServerFact]
    public async Task Physical_allocation_substitute_flag_migration_Down_refuses_while_a_substitute_exists()
    {
        await using var run = await Run.StartAsync();
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-SUB-DOWN", 50m);
        var salesId = Guid.NewGuid();
        await ExecuteAsync(run,
            PhysicalAllocationInsertSql(
                "N'sales.order-line'", "NEWID()", "NEWID()", "b.ProductId", "'1'", "N'approved line'", "NULL",
                "10", "1", "NULL", "NULL") + " UPDATE inventory.Allocations SET IsSubstitute = 1 WHERE Id = @id;",
            ("@id", salesId), ("@label", source.LabelId));

        await using var context = run.Context(CompanyA);
        var appliedBefore = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var migration = new GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations.AddInventoryAllocationSubstituteFlag();
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(migration.DownOperations[0]).Sql;
        const string guardProofSql = "SELECT CompanyId, Version FROM inventory.NegativeStockValidationGuards ORDER BY CompanyId FOR JSON PATH";
        const string labelProofSql = "SELECT * FROM inventory.Labels ORDER BY Id FOR JSON PATH";
        const string fullAllocationSql = "SELECT * FROM inventory.Allocations WHERE Id = @id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER";
        // The successful flag update/DDL may refresh SQL rowversion; the persisted business Version remains in this proof.
        const string legacyAllocationSql = "SELECT JSON_MODIFY(JSON_MODIFY((SELECT * FROM inventory.Allocations WHERE Id = @id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER), '$.IsSubstitute', NULL), '$.RowVersion', NULL)";
        const string rowVersionSql = "SELECT RowVersion FROM inventory.Allocations WHERE Id = @id";
        var guardProof = await run.ScalarAsync<string>(guardProofSql);
        var labelProof = await run.ScalarAsync<string>(labelProofSql);
        var allocationProof = await run.ScalarAsync<string>(legacyAllocationSql, ("@id", salesId));
        var fullAllocationBeforeRefusal = await run.ScalarAsync<string>(fullAllocationSql, ("@id", salesId));
        var rowVersionBeforeRefusal = await run.ScalarAsync<byte[]>(rowVersionSql, ("@id", salesId));
        Assert.Equal(8, rowVersionBeforeRefusal.Length);
        Assert.True(await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.NegativeStockValidationGuards") > 0);
        var refused = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(run, guard));
        Assert.Equal(51653, refused.Number);
        Assert.Equal(fullAllocationBeforeRefusal, await run.ScalarAsync<string>(fullAllocationSql, ("@id", salesId)));
        Assert.Equal(rowVersionBeforeRefusal, await run.ScalarAsync<byte[]>(rowVersionSql, ("@id", salesId)));
        Assert.Equal(appliedBefore, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'inventory.Allocations') AND [name] = N'IsSubstitute'"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations WHERE Id = @id AND IsSubstitute = 1", ("@id", salesId)));

        await ExecuteAsync(run, "UPDATE inventory.Allocations SET IsSubstitute = 0 WHERE Id = @id", ("@id", salesId));
        var rowVersionBeforeDdl = await run.ScalarAsync<byte[]>(rowVersionSql, ("@id", salesId));
        Assert.Equal(8, rowVersionBeforeDdl.Length);
        Assert.True(StringComparer.Ordinal.Compare(Convert.ToHexString(rowVersionBeforeDdl), Convert.ToHexString(rowVersionBeforeRefusal)) > 0);
        Assert.Equal(allocationProof, await run.ScalarAsync<string>(legacyAllocationSql, ("@id", salesId)));
        // Exercise the target's full DDL against a harmless legacy allocation without walking across newer proofs.
        // This deliberately does not alter EF's history; the separate empty-store test below covers IMigrator.
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var model = context.GetService<IDesignTimeModel>().Model;
        foreach (var command in generator.Generate(migration.DownOperations, model))
            await context.Database.ExecuteSqlRawAsync(command.CommandText);
        var rowVersionAfterDown = await run.ScalarAsync<byte[]>(rowVersionSql, ("@id", salesId));
        Assert.Equal(8, rowVersionAfterDown.Length);
        Assert.True(StringComparer.Ordinal.Compare(Convert.ToHexString(rowVersionAfterDown), Convert.ToHexString(rowVersionBeforeDdl)) >= 0);
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'inventory.Allocations') AND [name] = N'IsSubstitute'"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations WHERE Id = @id", ("@id", salesId)));
        Assert.Equal(allocationProof, await run.ScalarAsync<string>(legacyAllocationSql, ("@id", salesId)));
        Assert.Equal(appliedBefore, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.Equal(guardProof, await run.ScalarAsync<string>(guardProofSql));
        Assert.Equal(labelProof, await run.ScalarAsync<string>(labelProofSql));
        foreach (var command in generator.Generate(migration.UpOperations, model))
            await context.Database.ExecuteSqlRawAsync(command.CommandText);
        var rowVersionAfterUp = await run.ScalarAsync<byte[]>(rowVersionSql, ("@id", salesId));
        Assert.Equal(8, rowVersionAfterUp.Length);
        Assert.True(StringComparer.Ordinal.Compare(Convert.ToHexString(rowVersionAfterUp), Convert.ToHexString(rowVersionAfterDown)) >= 0);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'inventory.Allocations') AND [name] = N'IsSubstitute'"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Allocations WHERE Id = @id AND IsSubstitute = 0", ("@id", salesId)));
        Assert.Equal(allocationProof, await run.ScalarAsync<string>(legacyAllocationSql, ("@id", salesId)));
        Assert.Equal(appliedBefore, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.Equal(guardProof, await run.ScalarAsync<string>(guardProofSql));
        Assert.Equal(labelProof, await run.ScalarAsync<string>(labelProofSql));

        await using var empty = await Run.StartAsync();
        await using var emptyContext = empty.Context(CompanyA);
        var emptyAppliedBefore = (await emptyContext.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Contains(SubstituteFlagMigration, emptyAppliedBefore);
        await emptyContext.GetService<IMigrator>().MigrateAsync(BeforeSubstituteFlagMigration);
        Assert.Equal(BeforeSubstituteFlagMigration, (await emptyContext.Database.GetAppliedMigrationsAsync()).Last());
        Assert.Equal(0, await empty.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'inventory.Allocations') AND [name] = N'IsSubstitute'"));
        await emptyContext.Database.MigrateAsync();
        Assert.Equal(emptyAppliedBefore, (await emptyContext.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.Equal(1, await empty.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'inventory.Allocations') AND [name] = N'IsSubstitute'"));
    }
}
