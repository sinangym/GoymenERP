using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    private static readonly Guid AcceptanceSupplier = Guid.Parse("72000000-0000-0000-0000-000000000001");
    private static readonly Guid AcceptanceAccount = Guid.Parse("73000000-0000-0000-0000-000000000001");
    private static readonly Guid AcceptanceProduct = Guid.Parse("74000000-0000-0000-0000-000000000001");
    private static readonly Guid AcceptanceUnit = Guid.Parse("75000000-0000-0000-0000-000000000001");
    private static readonly Guid AcceptancePieceUnit = Guid.Parse("75000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset AcceptanceTime = new(2026, 9, 24, 9, 0, 0, TimeSpan.Zero);
    private const string InitialPurchasingMigration = "20260919170000_InitialPurchasing";
    private const string AcceptanceMigration = "20260924102017_AddPurchaseAcceptanceParticipant";

    [SqlServerFact]
    public async Task Acceptance_participant_leaves_commit_and_rollback_to_host_and_restores_durable_share()
    {
        await using var run = await Run.StartAsync();
        var seeded = await SeedApprovedAcceptanceOrderAsync(run);
        var acceptanceId = Guid.Parse("76000000-0000-0000-0000-000000000001");
        var request = AcceptanceRequest(seeded, acceptanceId, "4");

        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using (var participant = await PurchaseAcceptanceParticipant.EnlistAsync(connection, transaction, CompanyA))
            {
                var staged = await participant.StageAsync(request, Envelope("acceptance-rollback", "correlation-rollback"));
                Assert.False(staged.IsReplay);
                Assert.Equal("4", staged.PurchaseOrderVersion);
                Assert.Equal("6", staged.RemainingQuantity);
            }

            Assert.NotNull(transaction.Connection);
            Assert.Equal(System.Data.ConnectionState.Open, connection.State);
            await transaction.RollbackAsync();
        }

        await using (StoreScope scope = run.Store(CompanyA))
        {
            var rolledBack = await scope.Store.FindOrderAsync(seeded.OrderId);
            Assert.NotNull(rolledBack);
            Assert.Equal(3, rolledBack!.Order.Version);
            Assert.Empty(rolledBack.Order.Acceptances);
            Assert.Equal(0m, Assert.Single(rolledBack.Order.Lines).AcceptedQuantity.Value);
        }
        Assert.Equal(0, await ExactAcceptanceAuditCountAsync(run, seeded.OrderId));

        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using (var participant = await PurchaseAcceptanceParticipant.EnlistAsync(connection, transaction, CompanyA))
            {
                var staged = await participant.StageAsync(request, Envelope("acceptance-commit", "correlation-commit"));
                Assert.False(staged.IsReplay);
                Assert.Equal("4", staged.PurchaseOrderVersion);
            }

            Assert.NotNull(transaction.Connection);
            await transaction.CommitAsync();
        }

        await using (StoreScope scope = run.Store(CompanyA))
        {
            var committed = await scope.Store.FindOrderAsync(seeded.OrderId);
            Assert.NotNull(committed);
            Assert.Equal(4, committed!.Order.Version);
            Assert.Equal(acceptanceId, Assert.Single(committed.Order.Acceptances).AcceptanceId);
            var line = Assert.Single(committed.Order.Lines);
            Assert.Equal(4m, line.AcceptedQuantity.Value);
            Assert.Equal(6m, line.RemainingQuantity.Value);
        }
        Assert.Equal(1, await ExactAcceptanceAuditCountAsync(run, seeded.OrderId));

        var secondAcceptanceId = Guid.Parse("76000000-0000-0000-0000-000000000011");
        var second = request with
        {
            AcceptanceId = secondAcceptanceId,
            ExpectedPurchaseOrderVersion = "4",
            AcceptedQuantity = "2",
            OccurredAtUtc = AcceptanceTime.AddMinutes(1),
        };
        var secondStage = await StageAndCommitAsync(
            run, CompanyA, second, Envelope("acceptance-commit-v5", "correlation-commit-v5"));
        Assert.Equal("5", secondStage.PurchaseOrderVersion);

        await using (StoreScope scope = run.Store(CompanyA))
        {
            var reopened = await scope.Store.FindOrderAsync(seeded.OrderId);
            Assert.NotNull(reopened);
            Assert.Equal(5, reopened!.Order.Version);
            Assert.Equal([acceptanceId, secondAcceptanceId],
                reopened.Order.Acceptances.Select(acceptance => acceptance.AcceptanceId).ToArray());
            var line = Assert.Single(reopened.Order.Lines);
            Assert.Equal(6m, line.AcceptedQuantity.Value);
            Assert.Equal(4m, line.RemainingQuantity.Value);
        }
        Assert.Equal(2, await ExactAcceptanceAuditCountAsync(run, seeded.OrderId));

        var beforeRefusedDown = await AcceptanceDatabaseStateAsync(run, seeded.OrderId);
        await using (var migrationContext = MigrationContext(run, CompanyA))
        {
            var migrator = migrationContext.GetService<IMigrator>();
            var refusal = await Assert.ThrowsAsync<SqlException>(() =>
                migrator.MigrateAsync(InitialPurchasingMigration));
            Assert.Equal(51042, refusal.Number);
        }

        Assert.Equal(beforeRefusedDown, await AcceptanceDatabaseStateAsync(run, seeded.OrderId));

        await using var emptyRun = await Run.StartAsync();
        Assert.Equal((1, 1), await AcceptanceSchemaStateAsync(emptyRun));
        await using (var migrationContext = MigrationContext(emptyRun, CompanyA))
        {
            var migrator = migrationContext.GetService<IMigrator>();
            await migrator.MigrateAsync(InitialPurchasingMigration);
            Assert.Equal((0, 0), await AcceptanceSchemaStateAsync(emptyRun));
            await migrator.MigrateAsync();
        }

        Assert.Equal((1, 1), await AcceptanceSchemaStateAsync(emptyRun));
    }

    [SqlServerFact]
    public async Task Acceptance_participant_replays_exact_content_and_refuses_stale_over_remaining_or_cross_company_without_partial_write()
    {
        await using var run = await Run.StartAsync();
        var seeded = await SeedApprovedAcceptanceOrderAsync(run);
        var acceptanceId = Guid.Parse("76000000-0000-0000-0000-000000000002");
        var request = AcceptanceRequest(seeded, acceptanceId, "4");
        var envelope = Envelope("acceptance-replay", "correlation-original");

        await StageAndCommitAsync(run, CompanyA, request, envelope);

        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using var participant = await PurchaseAcceptanceParticipant.EnlistAsync(connection, transaction, CompanyA);
            var replay = await participant.StageAsync(request, envelope with { CorrelationId = "correlation-retry" });
            Assert.True(replay.IsReplay);
            Assert.Equal("correlation-original", replay.OriginalCorrelationId);
            Assert.Equal("4", replay.PurchaseOrderVersion);
            await transaction.CommitAsync();
        }
        Assert.Equal(1, await ExactAcceptanceAuditCountAsync(run, seeded.OrderId));

        await AssertRefusedAsync(
            run,
            CompanyA,
            request with { ExpectedPurchaseOrderVersion = "3", AcceptanceId = Guid.NewGuid() },
            Envelope("acceptance-stale", "correlation-stale"),
            PurchaseAcceptanceParticipantCodes.OrderVersionConflict);
        await AssertRefusedAsync(
            run,
            CompanyA,
            request with { ExpectedPurchaseOrderVersion = "4", AcceptedQuantity = "7", AcceptanceId = Guid.NewGuid() },
            Envelope("acceptance-over", "correlation-over"),
            PurchaseAcceptanceParticipantCodes.QuantityExceedsRemaining);
        await AssertRefusedAsync(
            run,
            CompanyB,
            request with { ExpectedPurchaseOrderVersion = "4", AcceptedQuantity = "1", AcceptanceId = Guid.NewGuid() },
            Envelope("acceptance-other-company", "correlation-other"),
            PurchaseAcceptanceParticipantCodes.InvalidReference);

        var piece = await SeedApprovedAcceptanceOrderAsync(run, QuantityUnit.Piece, AcceptancePieceUnit);
        var fractionalPiece = AcceptanceRequest(
            piece, Guid.Parse("76000000-0000-0000-0000-000000000091"), "1.5");
        await AssertRefusedAsync(
            run,
            CompanyA,
            fractionalPiece,
            Envelope("acceptance-fractional-piece", "correlation-fractional-piece"),
            PurchaseAcceptanceParticipantCodes.InvalidQuantity);
        await using (StoreScope pieceScope = run.Store(CompanyA))
        {
            var unchangedPiece = await pieceScope.Store.FindOrderAsync(piece.OrderId);
            Assert.NotNull(unchangedPiece);
            Assert.Equal(3, unchangedPiece!.Order.Version);
            Assert.Empty(unchangedPiece.Order.Acceptances);
            Assert.Equal(0m, Assert.Single(unchangedPiece.Order.Lines).AcceptedQuantity.Value);
        }
        Assert.Equal(0, await ExactAcceptanceAuditCountAsync(run, piece.OrderId));

        await using (StoreScope scope = run.Store(CompanyA))
        {
            var afterRefusals = await scope.Store.FindOrderAsync(seeded.OrderId);
            Assert.NotNull(afterRefusals);
            Assert.Equal(4, afterRefusals!.Order.Version);
            Assert.Single(afterRefusals.Order.Acceptances);
            Assert.Equal(4m, Assert.Single(afterRefusals.Order.Lines).AcceptedQuantity.Value);
        }
    }

    private static async Task<AcceptanceSeed> SeedApprovedAcceptanceOrderAsync(
        Run run,
        QuantityUnit unit = QuantityUnit.Kilogram,
        Guid? selectedUnitId = null)
    {
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var unitId = selectedUnitId ?? AcceptanceUnit;
        await using StoreScope scope = run.Store(CompanyA);
        var store = scope.Store;
        var order = PurchaseOrder.Create(orderId, CompanyA, AcceptanceSupplier, AcceptanceTime);
        order.AddLine(
            order.Version,
            lineId,
            AcceptanceProduct,
            unitId,
            Quantity.Of(10m, unit),
            sourceRequestLineId: null,
            AcceptanceTime);
        await store.StoreOrderAsync(
            Envelope("acceptance-seed-create", "correlation-seed-create"),
            AcceptanceHash("acceptance-seed-create"),
            AcceptanceAccount,
            DateOnly.FromDateTime(AcceptanceTime.UtcDateTime),
            () => order,
            new Dictionary<Guid, string> { [lineId] = "product-v1" });
        var approved = await store.MutateOrderAsync(
            Envelope("acceptance-seed-approve", "correlation-seed-approve"),
            AcceptanceHash("acceptance-seed-approve"),
            PurchasingCommands.OrderApproveV1,
            orderId,
            (candidate, now) => candidate.Approve(candidate.Version, now),
            new PurchasingMutationAudit("purchasing.purchase-order.approved.v1", "Approve", "Test approval"));
        Assert.NotNull(approved);
        Assert.Equal(3, approved.Order.Order.Version);
        return new AcceptanceSeed(orderId, lineId, unitId, approved.Order.Order.Version);
    }

    private static PurchaseAcceptanceStageRequestV1 AcceptanceRequest(AcceptanceSeed seed, Guid acceptanceId, string quantity) => new(
        CompanyA,
        acceptanceId,
        seed.OrderId,
        seed.LineId,
        seed.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
        AcceptanceProduct,
        "product-v1",
        seed.UnitId,
        quantity,
        AcceptanceTime.AddMinutes(5));

    private static async Task<PurchaseAcceptanceStageResultV1> StageAndCommitAsync(
        Run run,
        Guid companyId,
        PurchaseAcceptanceStageRequestV1 request,
        PurchasingWriteEnvelope envelope)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using var participant = await PurchaseAcceptanceParticipant.EnlistAsync(connection, transaction, companyId);
        var result = await participant.StageAsync(request, envelope);
        await transaction.CommitAsync();
        return result;
    }

    private static async Task AssertRefusedAsync(
        Run run,
        Guid companyId,
        PurchaseAcceptanceStageRequestV1 request,
        PurchasingWriteEnvelope envelope,
        string expectedCode)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using var participant = await PurchaseAcceptanceParticipant.EnlistAsync(connection, transaction, companyId);
        var refusal = await Assert.ThrowsAsync<PurchaseAcceptanceParticipantException>(() => participant.StageAsync(request, envelope));
        Assert.Equal(expectedCode, refusal.Code);
        await transaction.RollbackAsync();
    }

    private static PurchasingDbContext MigrationContext(Run run, Guid companyId)
    {
        var company = new CompanyContext();
        company.Set(companyId);
        var options = new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PurchasingDbContext.Schema))
            .Options;
        return new PurchasingDbContext(options, company);
    }

    private static async Task<AcceptanceDatabaseState> AcceptanceDatabaseStateAsync(Run run, Guid orderId) => new(
        await run.ScalarAsync<int>(
            "SELECT CASE WHEN OBJECT_ID(N'purchasing.PurchaseAcceptanceShares', N'U') IS NULL THEN 0 ELSE 1 END"),
        await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'purchasing.PurchaseAcceptanceShares', N'U')"),
        await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'purchasing.PurchaseAcceptanceShares', N'U')"),
        await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.__EFMigrationsHistory WHERE MigrationId = @id", ("@id", AcceptanceMigration)),
        await run.ScalarAsync<long>(
            "SELECT Version FROM purchasing.PurchaseOrders WHERE CompanyId = @company AND Id = @id", ("@company", CompanyA), ("@id", orderId)),
        await run.ScalarAsync<int>(
            "SELECT Status FROM purchasing.PurchaseOrders WHERE CompanyId = @company AND Id = @id", ("@company", CompanyA), ("@id", orderId)),
        await run.ScalarAsync<decimal>(
            "SELECT Accepted FROM purchasing.PurchaseOrderLines WHERE CompanyId = @company AND OrderId = @id", ("@company", CompanyA), ("@id", orderId)),
        await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.PurchaseAcceptanceShares WHERE CompanyId = @company AND OrderId = @id", ("@company", CompanyA), ("@id", orderId)),
        await run.ScalarAsync<decimal>(
            "SELECT COALESCE(SUM(Quantity), 0) FROM purchasing.PurchaseAcceptanceShares WHERE CompanyId = @company AND OrderId = @id",
            ("@company", CompanyA), ("@id", orderId)),
        await ExactAcceptanceAuditCountAsync(run, orderId));

    private static async Task<(int TableExists, int MigrationHistory)> AcceptanceSchemaStateAsync(Run run) =>
        (await run.ScalarAsync<int>(
            "SELECT CASE WHEN OBJECT_ID(N'purchasing.PurchaseAcceptanceShares', N'U') IS NULL THEN 0 ELSE 1 END"),
         await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.__EFMigrationsHistory WHERE MigrationId = @id", ("@id", AcceptanceMigration)));

    private static Task<int> ExactAcceptanceAuditCountAsync(Run run, Guid orderId) =>
        run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents "
            + "WHERE CompanyId = @company AND ActorId = N'purchasing-acceptance-test' "
            + "AND SourceChannel = N'Integration' AND Operation = N'RecordAcceptance' "
            + "AND EntityType = N'Purchasing.PurchaseOrder' AND EntityId = @entity "
            + "AND EventType = N'purchasing.purchase-order.acceptance-recorded.v1'",
            ("@company", CompanyA), ("@entity", orderId.ToString("D")));

    private static PurchasingWriteEnvelope Envelope(string key, string correlationId) => new(
        ActorId: "purchasing-acceptance-test",
        PermissionCode: "purchasing.orders.acceptance.write",
        RoleCodes: ["purchasing-test"],
        CorrelationId: correlationId,
        CausationId: null,
        IdempotencyKey: key,
        SourceChannel: "Integration",
        ClientIpAddress: null,
        UserAgent: null);

    private static string AcceptanceHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record AcceptanceSeed(Guid OrderId, Guid LineId, Guid UnitId, long Version);

    private sealed record AcceptanceDatabaseState(
        int TableExists,
        int ColumnCount,
        int ForeignKeyCount,
        int MigrationHistory,
        long OrderVersion,
        int OrderStatus,
        decimal LineAccepted,
        int AcceptanceShares,
        decimal AcceptanceQuantity,
        int AcceptanceAuditEvents);
}
