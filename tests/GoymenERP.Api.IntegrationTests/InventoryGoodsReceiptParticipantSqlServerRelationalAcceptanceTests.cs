using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Goods_receipt_commits_purchasing_and_inventory_once_and_both_participants_replay()
    {
        await using var run = await Run.StartAsync();
        var seed = await SeedApprovedAcceptanceOrderAsync(run);
        var acceptanceId = Guid.NewGuid();
        var receipt = Receipt(seed, acceptanceId, "4");
        var purchasing = AcceptanceRequest(seed, acceptanceId, "4");
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
            purchasingResult = await purchasingParticipant.StageAsync(purchasing, Envelope(key, correlation));
            inventoryResult = await inventoryParticipant.StageAsync(receipt, ReceiptMetadata(run, key, correlation));
            await transaction.CommitAsync();
        }

        Assert.False(purchasingResult.IsReplay);
        Assert.False(inventoryResult.IsReplay);
        Assert.Equal("2", inventoryResult.LabelVersion);
        Assert.Equal(correlation, inventoryResult.OriginalCorrelationId);
        var committed = await ReceiptEffectsAsync(run, seed, receipt, correlation);
        Assert.Equal(new ReceiptEffects(1, 4L, 1, 1, 1, 1, 1, 1, 3), committed);

        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using var purchasingParticipant = await PurchaseAcceptanceParticipant.EnlistAsync(
                connection, transaction, CompanyA);
            await using var inventoryParticipant = await InventoryGoodsReceiptParticipant.EnlistAsync(
                connection, transaction, CompanyA, GoodsReceiptSettings(run));
            var purchasingReplay = await purchasingParticipant.StageAsync(
                purchasing, Envelope(key, "goods-receipt-retry"));
            var inventoryReplay = await inventoryParticipant.StageAsync(
                receipt, ReceiptMetadata(run, key, "goods-receipt-retry"));
            Assert.True(purchasingReplay.IsReplay);
            Assert.True(inventoryReplay.IsReplay);
            Assert.Equal(correlation, purchasingReplay.OriginalCorrelationId);
            Assert.Equal(correlation, inventoryReplay.OriginalCorrelationId);
            Assert.Equal(inventoryResult.LabelVersion, inventoryReplay.LabelVersion);
            await transaction.CommitAsync();
        }

        Assert.Equal(committed, await ReceiptEffectsAsync(run, seed, receipt, correlation));
    }

    [SqlServerFact]
    public async Task Goods_receipt_host_rollback_removes_purchasing_and_inventory_effects()
    {
        await using var run = await Run.StartAsync();
        var seed = await SeedApprovedAcceptanceOrderAsync(run);
        var acceptanceId = Guid.NewGuid();
        var receipt = Receipt(seed, acceptanceId, "3");
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
            await purchasingParticipant.StageAsync(AcceptanceRequest(seed, acceptanceId, "3"), Envelope(key, correlation));
            await inventoryParticipant.StageAsync(receipt, ReceiptMetadata(run, key, correlation));
            Assert.NotNull(transaction.Connection);
            await transaction.RollbackAsync();
        }

        Assert.Equal(new ReceiptEffects(0, 3L, 0, 0, 0, 0, 0, 0, 0),
            await ReceiptEffectsAsync(run, seed, receipt, correlation));
    }

    [SqlServerFact]
    public async Task Goods_receipt_payload_or_company_mismatch_writes_no_partial_effect()
    {
        await using var run = await Run.StartAsync();
        var seed = await SeedApprovedAcceptanceOrderAsync(run);
        var acceptanceId = Guid.NewGuid();
        var receipt = Receipt(seed, acceptanceId, "2");
        var correlation = $"goods-receipt-conflict-{Guid.NewGuid():N}";
        var key = $"goods-receipt-conflict-{Guid.NewGuid():N}";

        await StageGoodsReceiptAndCommitAsync(run, seed, receipt, "2", key, correlation);
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
                AcceptanceRequest(seed, acceptanceId, "2"), Envelope(key, "goods-receipt-conflict-retry"));
            Assert.True(purchasingReplay.IsReplay);
            var conflict = await Assert.ThrowsAsync<InventoryGoodsReceiptException>(() =>
                inventoryParticipant.StageAsync(
                    receipt with { QuantityKg = "1" },
                    ReceiptMetadata(run, key, "goods-receipt-conflict-retry")));
            Assert.Equal((409, InventoryGoodsReceiptCodesV1.IdempotencyConflict),
                (conflict.Status, conflict.Code));
            await transaction.RollbackAsync();
        }

        Assert.Equal(committed, await ReceiptEffectsAsync(run, seed, receipt, correlation));

        var foreignSeed = await SeedApprovedAcceptanceOrderAsync(run);
        var foreignReceipt = Receipt(foreignSeed, Guid.NewGuid(), "1");
        var foreignCorrelation = $"goods-receipt-foreign-{Guid.NewGuid():N}";
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using var purchasingParticipant = await PurchaseAcceptanceParticipant.EnlistAsync(
                connection, transaction, CompanyA);
            await using var inventoryParticipant = await InventoryGoodsReceiptParticipant.EnlistAsync(
                connection, transaction, CompanyB, GoodsReceiptSettings(run, CompanyB));
            await purchasingParticipant.StageAsync(
                AcceptanceRequest(foreignSeed, foreignReceipt.AcceptanceId, "1"),
                Envelope("goods-receipt-foreign-purchase", foreignCorrelation));
            var hidden = await Assert.ThrowsAsync<InventoryGoodsReceiptException>(() =>
                inventoryParticipant.StageAsync(
                    foreignReceipt,
                    ReceiptMetadata(run, "goods-receipt-foreign-inventory", foreignCorrelation)));
            Assert.Equal((404, InventoryGoodsReceiptCodesV1.InvalidReference), (hidden.Status, hidden.Code));
            await transaction.RollbackAsync();
        }

        Assert.Equal(new ReceiptEffects(0, 3L, 0, 0, 0, 0, 0, 0, 0),
            await ReceiptEffectsAsync(run, foreignSeed, foreignReceipt, foreignCorrelation));
    }

    private static async Task StageGoodsReceiptAndCommitAsync(Run run, AcceptanceSeed seed,
        InventoryGoodsReceiptRequestV1 receipt, string quantity, string key, string correlation)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using var purchasingParticipant = await PurchaseAcceptanceParticipant.EnlistAsync(
            connection, transaction, CompanyA);
        await using var inventoryParticipant = await InventoryGoodsReceiptParticipant.EnlistAsync(
            connection, transaction, CompanyA, GoodsReceiptSettings(run));
        await purchasingParticipant.StageAsync(
            AcceptanceRequest(seed, receipt.AcceptanceId, quantity), Envelope(key, correlation));
        await inventoryParticipant.StageAsync(receipt, ReceiptMetadata(run, key, correlation));
        await transaction.CommitAsync();
    }

    private static InventoryGoodsReceiptRequestV1 Receipt(
        AcceptanceSeed seed, Guid acceptanceId, string quantity) => new(
        CompanyA,
        acceptanceId,
        seed.OrderId,
        seed.LineId,
        seed.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
        AcceptanceProduct,
        "product-v1",
        AcceptanceUnit,
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
        Guid.NewGuid(),
        BlockState.Blocked,
        Guid.NewGuid(),
        quantity,
        AcceptanceTime.AddMinutes(5));

    private static InventoryCommandMetadata ReceiptMetadata(
        Run run, string key, string correlation) => new(
        run.Actor,
        "purchasing.goods-receipt.record",
        ["SENTETIK-PURCHASING"],
        correlation,
        null,
        key,
        "integration-test");

    private static ISettingsPrerequisiteProvider GoodsReceiptSettings(Run run, Guid? company = null)
    {
        var resolvedCompany = company ?? CompanyA;
        var context = new CompanyContext();
        context.Set(resolvedCompany);
        return new ApprovedSettingsPrerequisiteProvider(context, new GoodsReceiptCurrentUser(run.Actor, resolvedCompany));
    }

    private static async Task<ReceiptEffects> ReceiptEffectsAsync(
        Run run, AcceptanceSeed seed, InventoryGoodsReceiptRequestV1 receipt, string correlation) => new(
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
        int InventoryAuditEvents);

    private sealed class GoodsReceiptCurrentUser(string actorId, Guid companyId) : ICurrentUser
    {
        private readonly IReadOnlySet<Guid> companies = new HashSet<Guid> { companyId };

        public bool IsAuthenticated => true;
        public string? UserId => actorId;
        public IReadOnlySet<Guid> AllowedCompanyIds => companies;
        public bool CanAccessCompany(Guid candidate) => companies.Contains(candidate);
    }
}
