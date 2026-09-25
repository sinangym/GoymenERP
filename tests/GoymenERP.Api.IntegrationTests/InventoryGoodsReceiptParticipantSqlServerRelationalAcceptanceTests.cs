using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    private static readonly Guid GoodsReceiptSupplier = Guid.Parse("72000000-0000-0000-0000-000000000001");
    private static readonly Guid GoodsReceiptAccount = Guid.Parse("73000000-0000-0000-0000-000000000001");
    private static readonly Guid GoodsReceiptProduct = Guid.Parse("74000000-0000-0000-0000-000000000001");
    private static readonly Guid GoodsReceiptUnit = Guid.Parse("75000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset GoodsReceiptTime = new(2026, 9, 24, 9, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task Goods_receipt_commits_purchasing_and_inventory_once_and_both_participants_replay()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var seed = await SeedGoodsReceiptPurchaseOrderAsync(run);
        var acceptanceId = Guid.NewGuid();
        var receipt = Receipt(seed, acceptanceId, "4", location);
        var purchasing = GoodsReceiptPurchaseRequest(seed, acceptanceId, "4");
        var key = $"goods-receipt-{Guid.NewGuid():N}";
        var correlation = $"goods-receipt-{Guid.NewGuid():N}";

        InventoryGoodsReceiptResultV1 inventoryResult;
        PurchaseAcceptanceStageResultV1 purchasingResult;
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using var purchasingParticipant = await PurchaseAcceptanceParticipant.EnlistAsync(
                connection, transaction, CompanyA);
            await using var inventoryParticipant = await InventoryGoodsReceiptParticipant.EnlistAsync(
                connection, transaction, CompanyA, GoodsReceiptSettings(run));
            Assert.Null(await inventoryParticipant.ReadReplayAsync(
                acceptanceId, ReceiptMetadata(run, key, correlation)));
            purchasingResult = await purchasingParticipant.StageAsync(purchasing, GoodsReceiptPurchaseEnvelope(run, key, correlation));
            receipt = receipt with { PurchaseOrderVersionAfter = purchasingResult.PurchaseOrderVersion };
            inventoryResult = await inventoryParticipant.StageAsync(receipt, ReceiptMetadata(run, key, correlation));
            await transaction.CommitAsync();
        }

        Assert.False(purchasingResult.IsReplay);
        Assert.False(inventoryResult.IsReplay);
        Assert.Equal("4", purchasingResult.PurchaseOrderVersion);
        Assert.Equal(purchasingResult.PurchaseOrderVersion, inventoryResult.PurchaseOrderVersion);
        Assert.Equal("2", inventoryResult.LabelVersion);
        Assert.Equal(correlation, inventoryResult.OriginalCorrelationId);
        var committed = await ReceiptEffectsAsync(run, seed, receipt, correlation);
        Assert.Equal(new ReceiptEffects(1, 4L, 1, 1, 1, 1, 1, 1, 3, 1), committed);
        await AssertCommittedReceiptFactsAsync(
            run, seed, receipt, key, correlation, purchasingResult.PurchaseOrderVersion);

        var reopened = await ReadGoodsReceiptSourceAsync(run, CompanyA, acceptanceId);
        AssertGoodsReceiptSource(receipt, inventoryResult, reopened);
        Assert.Null(await ReadGoodsReceiptSourceAsync(run, CompanyB, acceptanceId));

        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using var purchasingParticipant = await PurchaseAcceptanceParticipant.EnlistAsync(
                connection, transaction, CompanyA);
            await using var inventoryParticipant = await InventoryGoodsReceiptParticipant.EnlistAsync(
                connection, transaction, CompanyA, GoodsReceiptSettings(run));
            var inventorySource = await inventoryParticipant.ReadReplayAsync(
                acceptanceId, ReceiptMetadata(run, key, "goods-receipt-retry"));
            AssertGoodsReceiptSource(receipt, inventoryResult, inventorySource);
            var purchasingReplay = await purchasingParticipant.StageAsync(
                purchasing, GoodsReceiptPurchaseEnvelope(run, key, "goods-receipt-retry"));
            var inventoryReplay = await inventoryParticipant.StageAsync(
                receipt, ReceiptMetadata(run, key, "goods-receipt-retry"));
            Assert.True(purchasingReplay.IsReplay);
            Assert.True(inventoryReplay.IsReplay);
            Assert.Equal(correlation, purchasingReplay.OriginalCorrelationId);
            Assert.Equal(correlation, inventoryReplay.OriginalCorrelationId);
            Assert.Equal(purchasingReplay.PurchaseOrderVersion, inventoryReplay.PurchaseOrderVersion);
            Assert.Equal(inventoryResult.LabelVersion, inventoryReplay.LabelVersion);
            await transaction.CommitAsync();
        }

        Assert.Equal(committed, await ReceiptEffectsAsync(run, seed, receipt, correlation));

        var requiredDocumentsJson = await run.ScalarAsync<string>(
            "SELECT RequiredDocumentsJson FROM inventory.Labels WHERE CompanyId = @company AND Id = @label",
            ("@company", CompanyA), ("@label", receipt.LabelId));
        try
        {
            await SetRequiredDocumentsJsonAsync(run, receipt.LabelId, "{}");
            var unavailable = await Assert.ThrowsAsync<InventoryGoodsReceiptException>(() =>
                ReadGoodsReceiptSourceAsync(run, CompanyA, acceptanceId));
            Assert.Equal((503, InventoryGoodsReceiptCodesV1.Unavailable),
                (unavailable.Status, unavailable.Code));
        }
        finally
        {
            await SetRequiredDocumentsJsonAsync(run, receipt.LabelId, requiredDocumentsJson);
        }
    }

    [SqlServerFact]
    public async Task Goods_receipt_host_rollback_removes_purchasing_and_inventory_effects()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var seed = await SeedGoodsReceiptPurchaseOrderAsync(run);
        var acceptanceId = Guid.NewGuid();
        var receipt = Receipt(seed, acceptanceId, "3", location);
        var correlation = $"goods-receipt-rollback-{Guid.NewGuid():N}";
        var key = $"goods-receipt-rollback-{Guid.NewGuid():N}";

        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using var purchasingParticipant = await PurchaseAcceptanceParticipant.EnlistAsync(
                connection, transaction, CompanyA);
            await using var inventoryParticipant = await InventoryGoodsReceiptParticipant.EnlistAsync(
                connection, transaction, CompanyA, GoodsReceiptSettings(run));
            var purchasingResult = await purchasingParticipant.StageAsync(GoodsReceiptPurchaseRequest(seed, acceptanceId, "3"),
                GoodsReceiptPurchaseEnvelope(run, key, correlation));
            receipt = receipt with { PurchaseOrderVersionAfter = purchasingResult.PurchaseOrderVersion };
            await inventoryParticipant.StageAsync(receipt, ReceiptMetadata(run, key, correlation));
            Assert.NotNull(transaction.Connection);
            await transaction.RollbackAsync();
        }

        Assert.Equal(new ReceiptEffects(0, 3L, 0, 0, 0, 0, 0, 0, 0, 0),
            await ReceiptEffectsAsync(run, seed, receipt, correlation));
    }

    [SqlServerFact]
    public async Task Goods_receipt_payload_or_company_mismatch_writes_no_partial_effect()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        var seed = await SeedGoodsReceiptPurchaseOrderAsync(run);
        var acceptanceId = Guid.NewGuid();
        var receipt = Receipt(seed, acceptanceId, "2", location);
        var correlation = $"goods-receipt-conflict-{Guid.NewGuid():N}";
        var key = $"goods-receipt-conflict-{Guid.NewGuid():N}";

        receipt = await StageGoodsReceiptAndCommitAsync(run, seed, receipt, "2", key, correlation);
        var committed = await ReceiptEffectsAsync(run, seed, receipt, correlation);

        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using var purchasingParticipant = await PurchaseAcceptanceParticipant.EnlistAsync(
                connection, transaction, CompanyA);
            await using var inventoryParticipant = await InventoryGoodsReceiptParticipant.EnlistAsync(
                connection, transaction, CompanyA, GoodsReceiptSettings(run));
            var purchasingReplay = await purchasingParticipant.StageAsync(
                GoodsReceiptPurchaseRequest(seed, acceptanceId, "2"),
                GoodsReceiptPurchaseEnvelope(run, key, "goods-receipt-conflict-retry"));
            Assert.True(purchasingReplay.IsReplay);
            var actorConflict = await Assert.ThrowsAsync<InventoryGoodsReceiptException>(() =>
                inventoryParticipant.ReadReplayAsync(
                    acceptanceId,
                    ReceiptMetadata(run, key, "goods-receipt-conflict-retry") with
                    {
                        ActorId = "another-inventory-receipt-user",
                    }));
            Assert.Equal((409, InventoryGoodsReceiptCodesV1.IdempotencyConflict),
                (actorConflict.Status, actorConflict.Code));
            var targetConflict = await Assert.ThrowsAsync<InventoryGoodsReceiptException>(() =>
                inventoryParticipant.ReadReplayAsync(
                    Guid.NewGuid(), ReceiptMetadata(run, key, "goods-receipt-conflict-retry")));
            Assert.Equal((409, InventoryGoodsReceiptCodesV1.IdempotencyConflict),
                (targetConflict.Status, targetConflict.Code));
            var conflict = await Assert.ThrowsAsync<InventoryGoodsReceiptException>(() =>
                inventoryParticipant.StageAsync(
                    receipt with { QuantityKg = "1" },
                    ReceiptMetadata(run, key, "goods-receipt-conflict-retry")));
            Assert.Equal((409, InventoryGoodsReceiptCodesV1.IdempotencyConflict),
                (conflict.Status, conflict.Code));
            await transaction.RollbackAsync();
        }

        Assert.Equal(committed, await ReceiptEffectsAsync(run, seed, receipt, correlation));

        var duplicateReceipt = Receipt(seed, acceptanceId, "2", location) with
        {
            PurchaseOrderVersionAfter = receipt.PurchaseOrderVersionAfter,
        };
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using var inventoryParticipant = await InventoryGoodsReceiptParticipant.EnlistAsync(
                connection, transaction, CompanyA, GoodsReceiptSettings(run));
            var duplicate = await Assert.ThrowsAsync<InventoryGoodsReceiptException>(() =>
                inventoryParticipant.StageAsync(
                    duplicateReceipt,
                    ReceiptMetadata(run, $"goods-receipt-fresh-{Guid.NewGuid():N}", "goods-receipt-fresh-duplicate")));
            Assert.Equal((409, InventoryGoodsReceiptCodesV1.IdempotencyConflict),
                (duplicate.Status, duplicate.Code));
            await transaction.RollbackAsync();
        }

        Assert.Equal(committed, await ReceiptEffectsAsync(run, seed, receipt, correlation));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Lots WHERE Id = @id", ("@id", duplicateReceipt.LotId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Labels WHERE Id = @id", ("@id", duplicateReceipt.LabelId)));

        var foreignSeed = await SeedGoodsReceiptPurchaseOrderAsync(run);
        var foreignReceipt = Receipt(foreignSeed, Guid.NewGuid(), "1", location);
        var foreignCorrelation = $"goods-receipt-foreign-{Guid.NewGuid():N}";
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using var purchasingParticipant = await PurchaseAcceptanceParticipant.EnlistAsync(
                connection, transaction, CompanyA);
            await using var inventoryParticipant = await InventoryGoodsReceiptParticipant.EnlistAsync(
                connection, transaction, CompanyB, GoodsReceiptSettings(run, CompanyB));
            var purchasingResult = await purchasingParticipant.StageAsync(
                GoodsReceiptPurchaseRequest(foreignSeed, foreignReceipt.AcceptanceId, "1"),
                GoodsReceiptPurchaseEnvelope(run, "goods-receipt-foreign-purchase", foreignCorrelation));
            foreignReceipt = foreignReceipt with
            {
                PurchaseOrderVersionAfter = purchasingResult.PurchaseOrderVersion,
            };
            var hidden = await Assert.ThrowsAsync<InventoryGoodsReceiptException>(() =>
                inventoryParticipant.StageAsync(
                    foreignReceipt,
                    ReceiptMetadata(run, "goods-receipt-foreign-inventory", foreignCorrelation)));
            Assert.Equal((404, InventoryGoodsReceiptCodesV1.InvalidReference), (hidden.Status, hidden.Code));
            await transaction.RollbackAsync();
        }

        Assert.Equal(new ReceiptEffects(0, 3L, 0, 0, 0, 0, 0, 0, 0, 0),
            await ReceiptEffectsAsync(run, foreignSeed, foreignReceipt, foreignCorrelation));
    }

    private static async Task<GoodsReceiptAcceptanceSeed> SeedGoodsReceiptPurchaseOrderAsync(Run run)
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        var audit = new AuditEventCollector();
        var options = new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema))
            .AddInterceptors(new AuditSaveChangesInterceptor(audit, company))
            .Options;
        await using var context = new PurchasingDbContext(options, company);
        await context.Database.MigrateAsync();
        var store = new PurchasingStore(context, company, audit, TimeProvider.System);
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var order = PurchaseOrder.Create(orderId, CompanyA, GoodsReceiptSupplier, GoodsReceiptTime);
        order.AddLine(order.Version, lineId, GoodsReceiptProduct, GoodsReceiptUnit,
            Quantity.Of(10m, QuantityUnit.Kilogram), sourceRequestLineId: null, GoodsReceiptTime);
        await store.StoreOrderAsync(
            GoodsReceiptPurchaseEnvelope(run, $"goods-receipt-seed-create-{Guid.NewGuid():N}",
                $"goods-receipt-seed-create-{Guid.NewGuid():N}"),
            GoodsReceiptHash($"goods-receipt-seed-create-{orderId:N}"),
            GoodsReceiptAccount,
            DateOnly.FromDateTime(GoodsReceiptTime.UtcDateTime),
            () => order,
            new Dictionary<Guid, string> { [lineId] = "product-v1" });
        var approved = await store.MutateOrderAsync(
            GoodsReceiptPurchaseEnvelope(run, $"goods-receipt-seed-approve-{Guid.NewGuid():N}",
                $"goods-receipt-seed-approve-{Guid.NewGuid():N}"),
            GoodsReceiptHash($"goods-receipt-seed-approve-{orderId:N}"),
            PurchasingCommands.OrderApproveV1,
            orderId,
            (candidate, now) => candidate.Approve(candidate.Version, now),
            new PurchasingMutationAudit("purchasing.purchase-order.approved.v1", "Approve", "SENTETIK approval"));
        Assert.NotNull(approved);
        Assert.Equal(3L, approved.Order.Order.Version);
        return new GoodsReceiptAcceptanceSeed(orderId, lineId, approved.Order.Order.Version);
    }

    private static async Task<InventoryGoodsReceiptSourceV1?> ReadGoodsReceiptSourceAsync(
        Run run, Guid companyId, Guid acceptanceId)
    {
        var company = new CompanyContext();
        company.Set(companyId);
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.Schema))
            .Options;
        await using var context = new InventoryDbContext(options, company);
        var source = new InventoryGoodsReceiptSourceService(context, company);
        return await source.ReadAsync(acceptanceId);
    }

    private static async Task SetRequiredDocumentsJsonAsync(Run run, Guid labelId, string json)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE inventory.Labels
            SET RequiredDocumentsJson = @json
            WHERE CompanyId = @company AND Id = @label;
            """;
        command.Parameters.AddWithValue("@json", json);
        command.Parameters.AddWithValue("@company", CompanyA);
        command.Parameters.AddWithValue("@label", labelId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static void AssertGoodsReceiptSource(
        InventoryGoodsReceiptRequestV1 expectedRequest,
        InventoryGoodsReceiptResultV1 expectedResult,
        InventoryGoodsReceiptSourceV1? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expectedRequest.CompanyId, actual.OriginalRequest.CompanyId);
        Assert.Equal(expectedRequest.AcceptanceId, actual.OriginalRequest.AcceptanceId);
        Assert.Equal(expectedRequest.PurchaseOrderId, actual.OriginalRequest.PurchaseOrderId);
        Assert.Equal(expectedRequest.PurchaseOrderLineId, actual.OriginalRequest.PurchaseOrderLineId);
        Assert.Equal(expectedRequest.PurchaseOrderVersionAfter, actual.OriginalRequest.PurchaseOrderVersionAfter);
        Assert.Equal(expectedRequest.ProductId, actual.OriginalRequest.ProductId);
        Assert.Equal(expectedRequest.ExpectedProductVersion, actual.OriginalRequest.ExpectedProductVersion);
        Assert.Equal(expectedRequest.PurchaseUnitId, actual.OriginalRequest.PurchaseUnitId);
        Assert.Equal(expectedRequest.LotId, actual.OriginalRequest.LotId);
        Assert.Equal(expectedRequest.LabelId, actual.OriginalRequest.LabelId);
        Assert.Equal(expectedRequest.LabelCode, actual.OriginalRequest.LabelCode);
        Assert.Equal(expectedRequest.HeatNumber, actual.OriginalRequest.HeatNumber);
        Assert.Equal(expectedRequest.CoilNumber, actual.OriginalRequest.CoilNumber);
        Assert.Equal(expectedRequest.RequiredDocuments, actual.OriginalRequest.RequiredDocuments);
        Assert.Equal(expectedRequest.OwnerKind, actual.OriginalRequest.OwnerKind);
        Assert.Equal(expectedRequest.OwnerId, actual.OriginalRequest.OwnerId);
        Assert.Equal(expectedRequest.CustodianKind, actual.OriginalRequest.CustodianKind);
        Assert.Equal(expectedRequest.CustodianId, actual.OriginalRequest.CustodianId);
        Assert.Equal(expectedRequest.LocationId, actual.OriginalRequest.LocationId);
        Assert.Equal(expectedRequest.InitialBlock, actual.OriginalRequest.InitialBlock);
        Assert.Equal(expectedRequest.MovementId, actual.OriginalRequest.MovementId);
        Assert.Equal(expectedRequest.QuantityKg, actual.OriginalRequest.QuantityKg);
        Assert.Equal(expectedRequest.OccurredAtUtc, actual.OriginalRequest.OccurredAtUtc);
        Assert.Equal(expectedResult, actual.OriginalResult);
    }

    private static PurchaseAcceptanceStageRequestV1 GoodsReceiptPurchaseRequest(
        GoodsReceiptAcceptanceSeed seed, Guid acceptanceId, string quantity) => new(
        CompanyA,
        acceptanceId,
        seed.OrderId,
        seed.LineId,
        seed.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
        GoodsReceiptProduct,
        "product-v1",
        GoodsReceiptUnit,
        quantity,
        GoodsReceiptTime.AddMinutes(5));

    private static PurchasingWriteEnvelope GoodsReceiptPurchaseEnvelope(
        Run run, string key, string correlation) => new(
        run.Actor.ToString("D"),
        "purchasing.orders.acceptance.write",
        ["SENTETIK-PURCHASING"],
        correlation,
        null,
        key,
        "Integration",
        null,
        null);

    private static string GoodsReceiptHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static async Task<InventoryGoodsReceiptRequestV1> StageGoodsReceiptAndCommitAsync(
        Run run, GoodsReceiptAcceptanceSeed seed,
        InventoryGoodsReceiptRequestV1 receipt, string quantity, string key, string correlation)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using var purchasingParticipant = await PurchaseAcceptanceParticipant.EnlistAsync(
            connection, transaction, CompanyA);
        await using var inventoryParticipant = await InventoryGoodsReceiptParticipant.EnlistAsync(
            connection, transaction, CompanyA, GoodsReceiptSettings(run));
        var purchasingResult = await purchasingParticipant.StageAsync(
            GoodsReceiptPurchaseRequest(seed, receipt.AcceptanceId, quantity),
            GoodsReceiptPurchaseEnvelope(run, key, correlation));
        receipt = receipt with { PurchaseOrderVersionAfter = purchasingResult.PurchaseOrderVersion };
        await inventoryParticipant.StageAsync(receipt, ReceiptMetadata(run, key, correlation));
        await transaction.CommitAsync();
        return receipt;
    }

    private static InventoryGoodsReceiptRequestV1 Receipt(
        GoodsReceiptAcceptanceSeed seed, Guid acceptanceId, string quantity, Guid locationId) => new(
        CompanyA,
        acceptanceId,
        seed.OrderId,
        seed.LineId,
        seed.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
        GoodsReceiptProduct,
        "product-v1",
        GoodsReceiptUnit,
        Guid.NewGuid(),
        Guid.NewGuid(),
        $"SYN-RECEIPT-{Guid.NewGuid():N}",
        "SYN-HEAT",
        "SYN-COIL",
        ["MTC"],
        StockPartyKind.Company,
        CompanyA,
        StockPartyKind.Company,
        CompanyA,
        locationId,
        BlockState.Blocked,
        Guid.NewGuid(),
        quantity,
        GoodsReceiptTime.AddMinutes(5));

    private static InventoryCommandMetadata ReceiptMetadata(
        Run run, string key, string correlation) => new(
        run.Actor.ToString("D"),
        "purchasing.goods-receipt.record",
        ["SENTETIK-PURCHASING"],
        correlation,
        null,
        key,
        "Integration");

    private static ISettingsPrerequisiteProvider GoodsReceiptSettings(Run run, Guid? company = null)
    {
        var resolvedCompany = company ?? CompanyA;
        var context = new CompanyContext();
        context.Set(resolvedCompany);
        return new ApprovedSettingsPrerequisiteProvider(context, new FixedCurrentUser(run.Actor, resolvedCompany));
    }

    private static async Task AssertCommittedReceiptFactsAsync(
        Run run,
        GoodsReceiptAcceptanceSeed seed,
        InventoryGoodsReceiptRequestV1 receipt,
        string idempotencyKey,
        string correlation,
        string purchaseOrderVersion)
    {
        var actor = run.Actor.ToString("D");
        var idempotencyKeyHash = GoodsReceiptHash(idempotencyKey);
        var orderVersionAfter = long.Parse(
            purchaseOrderVersion,
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.PurchaseAcceptanceShares "
            + "WHERE CompanyId = @company AND AcceptanceId = @acceptance AND OrderId = @order "
            + "AND LineId = @line AND Quantity = @quantity AND OrderVersionAfter = @version "
            + "AND CorrelationId = @correlation",
            ("@company", CompanyA), ("@acceptance", receipt.AcceptanceId), ("@order", seed.OrderId),
            ("@line", seed.LineId), ("@quantity", decimal.Parse(receipt.QuantityKg,
                System.Globalization.CultureInfo.InvariantCulture)), ("@version", orderVersionAfter),
            ("@correlation", correlation)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Movements "
            + "WHERE CompanyId = @company AND Id = @movement AND Kind = @kind "
            + "AND OccurredAtUtc = @occurred AND SourceKind = @sourceKind AND SourceId = @acceptance "
            + "AND SourceLineId IS NULL AND SourceGrain = @grain AND CorrelationId = @correlation",
            ("@company", CompanyA), ("@movement", receipt.MovementId),
            ("@kind", (int)GoymenERP.Modules.Inventory.Domain.Movements.StockMovementKind.Receipt),
            ("@occurred", receipt.OccurredAtUtc), ("@sourceKind", InventoryGoodsReceiptContractV1.SourceKind),
            ("@acceptance", receipt.AcceptanceId),
            ("@grain", (int)GoymenERP.Modules.Inventory.Domain.Movements.StockSourceGrain.Whole),
            ("@correlation", correlation)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.SourceGrains "
            + "WHERE CompanyId = @company AND Kind = @kind AND SourceKind = @sourceKind "
            + "AND SourceId = @acceptance AND Grain = @grain",
            ("@company", CompanyA),
            ("@kind", (int)GoymenERP.Modules.Inventory.Domain.Movements.StockMovementKind.Receipt),
            ("@sourceKind", InventoryGoodsReceiptContractV1.SourceKind),
            ("@acceptance", receipt.AcceptanceId),
            ("@grain", (int)GoymenERP.Modules.Inventory.Domain.Movements.StockSourceGrain.Whole)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.MovementLines "
            + "WHERE CompanyId = @company AND MovementId = @movement AND LineNumber = 1 "
            + "AND Direction = @direction AND OwnerKind = @ownerKind AND OwnerId = @owner "
            + "AND CustodianKind = @custodianKind AND CustodianId = @custodian "
            + "AND LocationId = @location AND LabelId = @label AND ProductId = @product "
            + "AND Block = @block AND QuantityKg = @quantity",
            ("@company", CompanyA), ("@movement", receipt.MovementId),
            ("@direction", (int)GoymenERP.Modules.Inventory.Domain.Movements.StockDirection.In),
            ("@ownerKind", (int)receipt.OwnerKind), ("@owner", receipt.OwnerId),
            ("@custodianKind", (int)receipt.CustodianKind), ("@custodian", receipt.CustodianId),
            ("@location", receipt.LocationId), ("@label", receipt.LabelId),
            ("@product", receipt.ProductId), ("@block", (int)receipt.InitialBlock),
            ("@quantity", decimal.Parse(receipt.QuantityKg,
                System.Globalization.CultureInfo.InvariantCulture))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Balances "
            + "WHERE CompanyId = @company AND OwnerKind = @ownerKind AND OwnerId = @owner "
            + "AND CustodianKind = @custodianKind AND CustodianId = @custodian "
            + "AND LocationId = @location AND LabelId = @label AND ProductId = @product "
            + "AND Block = @block AND QuantityKg = @quantity",
            ("@company", CompanyA), ("@ownerKind", (int)receipt.OwnerKind), ("@owner", receipt.OwnerId),
            ("@custodianKind", (int)receipt.CustodianKind), ("@custodian", receipt.CustodianId),
            ("@location", receipt.LocationId), ("@label", receipt.LabelId),
            ("@product", receipt.ProductId), ("@block", (int)receipt.InitialBlock),
            ("@quantity", decimal.Parse(receipt.QuantityKg,
                System.Globalization.CultureInfo.InvariantCulture))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands "
            + "WHERE CompanyId = @company AND CommandName = @command AND TargetId = @acceptance "
            + "AND ActorId = @actor AND CorrelationId = @correlation "
            + "AND JSON_VALUE(ResultJson, '$.purchaseOrderVersion') = @purchaseVersion "
            + "AND JSON_VALUE(ResultJson, '$.labelVersion') = N'2' "
            + "AND JSON_VALUE(ResultJson, '$.movementId') = @movement "
            + "AND JSON_VALUE(ResultJson, '$.quantityKg') = @quantity",
            ("@company", CompanyA), ("@command", InventoryGoodsReceiptContractV1.CommandName),
            ("@acceptance", receipt.AcceptanceId), ("@actor", actor), ("@correlation", correlation),
            ("@purchaseVersion", purchaseOrderVersion), ("@movement", receipt.MovementId.ToString("D")),
            ("@quantity", receipt.QuantityKg)));

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents "
            + "WHERE CompanyId = @company AND CorrelationId = @correlation "
            + "AND EventType = N'inventory.lot.registered.v1' AND EntityType = N'Inventory.MaterialLot' "
            + "AND EntityId = @entity AND Operation = N'Register' "
            + "AND EntityVersionBefore IS NULL AND EntityVersionAfter = 1 "
            + "AND ActorType = N'User' AND ActorId = @actor AND PermissionCode = @permission "
            + "AND JSON_VALUE(RoleCodesJson, '$[0]') = N'SENTETIK-PURCHASING' "
            + "AND JSON_VALUE(RoleCodesJson, '$[1]') IS NULL "
            + "AND SourceChannel = N'Integration' AND CausationId IS NULL "
            + "AND IdempotencyKeyHash = @keyHash AND Outcome = N'Succeeded' AND SchemaVersion = 1 "
            + "AND BeforeJson = N'{}' "
            + "AND JSON_VALUE(AfterJson, '$.id') = @entity "
            + "AND JSON_VALUE(AfterJson, '$.productId') = @product",
            ("@company", CompanyA), ("@correlation", correlation),
            ("@entity", receipt.LotId.ToString("D")), ("@actor", actor),
            ("@permission", "purchasing.goods-receipt.record"),
            ("@keyHash", idempotencyKeyHash),
            ("@product", receipt.ProductId.ToString("D"))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents "
            + "WHERE CompanyId = @company AND CorrelationId = @correlation "
            + "AND EventType = N'inventory.label.registered.v1' AND EntityType = N'Inventory.MaterialLabel' "
            + "AND EntityId = @entity AND Operation = N'Register' "
            + "AND EntityVersionBefore IS NULL AND EntityVersionAfter = 1 "
            + "AND ActorType = N'User' AND ActorId = @actor AND PermissionCode = @permission "
            + "AND JSON_VALUE(RoleCodesJson, '$[0]') = N'SENTETIK-PURCHASING' "
            + "AND JSON_VALUE(RoleCodesJson, '$[1]') IS NULL "
            + "AND SourceChannel = N'Integration' AND CausationId IS NULL "
            + "AND IdempotencyKeyHash = @keyHash AND Outcome = N'Succeeded' AND SchemaVersion = 1 "
            + "AND BeforeJson = N'{}' "
            + "AND JSON_VALUE(AfterJson, '$.id') = @entity AND JSON_VALUE(AfterJson, '$.code') = @code "
            + "AND JSON_VALUE(AfterJson, '$.productId') = @product "
            + "AND JSON_VALUE(AfterJson, '$.lotId') = @lot",
            ("@company", CompanyA), ("@correlation", correlation),
            ("@entity", receipt.LabelId.ToString("D")), ("@actor", actor),
            ("@permission", "purchasing.goods-receipt.record"), ("@code", receipt.LabelCode),
            ("@keyHash", idempotencyKeyHash),
            ("@product", receipt.ProductId.ToString("D")), ("@lot", receipt.LotId.ToString("D"))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents "
            + "WHERE CompanyId = @company AND CorrelationId = @correlation "
            + "AND EventType = N'inventory.movement.recorded.v1' AND EntityType = N'Inventory.StockMovement' "
            + "AND EntityId = @entity AND Operation = N'Record' "
            + "AND EntityVersionBefore IS NULL AND EntityVersionAfter = 1 "
            + "AND ActorType = N'User' AND ActorId = @actor AND PermissionCode = @permission "
            + "AND JSON_VALUE(RoleCodesJson, '$[0]') = N'SENTETIK-PURCHASING' "
            + "AND JSON_VALUE(RoleCodesJson, '$[1]') IS NULL "
            + "AND SourceChannel = N'Integration' AND CausationId IS NULL "
            + "AND IdempotencyKeyHash = @keyHash AND Outcome = N'Succeeded' AND SchemaVersion = 1 "
            + "AND BeforeJson = N'{}' "
            + "AND JSON_VALUE(AfterJson, '$.id') = @entity "
            + "AND JSON_VALUE(AfterJson, '$.kind') = N'Receipt' "
            + "AND JSON_VALUE(AfterJson, '$.source.kind') = @sourceKind "
            + "AND JSON_VALUE(AfterJson, '$.source.id') = @acceptance "
            + "AND JSON_VALUE(AfterJson, '$.source.lineId') IS NULL "
            + "AND JSON_VALUE(AfterJson, '$.lines') = N'1'",
            ("@company", CompanyA), ("@correlation", correlation),
            ("@entity", receipt.MovementId.ToString("D")), ("@actor", actor),
            ("@permission", "purchasing.goods-receipt.record"),
            ("@keyHash", idempotencyKeyHash),
            ("@sourceKind", InventoryGoodsReceiptContractV1.SourceKind),
            ("@acceptance", receipt.AcceptanceId.ToString("D"))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents "
            + "WHERE CompanyId = @company AND CorrelationId = @correlation "
            + "AND EventType = N'purchasing.purchase-order.acceptance-recorded.v1' "
            + "AND EntityType = N'Purchasing.PurchaseOrder' AND EntityId = @entity "
            + "AND Operation = N'RecordAcceptance' AND EntityVersionBefore = @before "
            + "AND EntityVersionAfter = @after AND ActorType = N'User' AND ActorId = @actor "
            + "AND PermissionCode = @permission "
            + "AND JSON_VALUE(RoleCodesJson, '$[0]') = N'SENTETIK-PURCHASING' "
            + "AND JSON_VALUE(RoleCodesJson, '$[1]') IS NULL "
            + "AND SourceChannel = N'Integration' AND CausationId IS NULL "
            + "AND IdempotencyKeyHash = @keyHash AND Outcome = N'Succeeded' AND SchemaVersion = 1 "
            + "AND JSON_VALUE(BeforeJson, '$.version') = @beforeText "
            + "AND JSON_VALUE(BeforeJson, '$.accepted') = N'0' "
            + "AND JSON_VALUE(AfterJson, '$.version') = @afterText "
            + "AND JSON_VALUE(AfterJson, '$.accepted') = @quantity "
            + "AND JSON_VALUE(AfterJson, '$.remaining') = @remaining",
            ("@company", CompanyA), ("@correlation", correlation),
            ("@entity", seed.OrderId.ToString("D")), ("@before", seed.Version),
            ("@after", orderVersionAfter), ("@actor", actor),
            ("@permission", "purchasing.orders.acceptance.write"),
            ("@keyHash", idempotencyKeyHash),
            ("@beforeText", seed.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("@afterText", purchaseOrderVersion), ("@quantity", receipt.QuantityKg),
            ("@remaining", (10m - decimal.Parse(receipt.QuantityKg,
                System.Globalization.CultureInfo.InvariantCulture)).ToString(System.Globalization.CultureInfo.InvariantCulture))));
    }

    private static async Task<ReceiptEffects> ReceiptEffectsAsync(
        Run run, GoodsReceiptAcceptanceSeed seed, InventoryGoodsReceiptRequestV1 receipt, string correlation) => new(
        await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.PurchaseAcceptanceShares WHERE AcceptanceId = @id", ("@id", receipt.AcceptanceId)),
        await run.ScalarAsync<long>(
            "SELECT Version FROM purchasing.PurchaseOrders WHERE Id = @id", ("@id", seed.OrderId)),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Lots WHERE Id = @id", ("@id", receipt.LotId)),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Labels WHERE Id = @id", ("@id", receipt.LabelId)),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE Id = @id", ("@id", receipt.MovementId)),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.MovementLines WHERE MovementId = @id", ("@id", receipt.MovementId)),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Balances WHERE LabelId = @id", ("@id", receipt.LabelId)),
        await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.Commands WHERE TargetId = @id AND CommandName = @name",
            ("@id", receipt.AcceptanceId), ("@name", InventoryGoodsReceiptContractV1.CommandName)),
        await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = @correlation AND EntityType LIKE 'Inventory.%'",
            ("@correlation", correlation)),
        await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE CorrelationId = @correlation "
            + "AND EventType = N'purchasing.purchase-order.acceptance-recorded.v1'",
            ("@correlation", correlation)));

    private sealed record ReceiptEffects(
        int PurchaseAcceptanceShares,
        long PurchaseOrderVersion,
        int Lots,
        int Labels,
        int Movements,
        int MovementLines,
        int Balances,
        int Commands,
        int InventoryAuditEvents,
        int PurchasingAuditEvents);

    private sealed record GoodsReceiptAcceptanceSeed(Guid OrderId, Guid LineId, long Version);
}
