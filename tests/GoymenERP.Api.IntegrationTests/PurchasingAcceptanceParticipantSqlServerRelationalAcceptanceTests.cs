using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    private static readonly Guid AcceptanceSupplier = Guid.Parse("72000000-0000-0000-0000-000000000001");
    private static readonly Guid AcceptanceAccount = Guid.Parse("73000000-0000-0000-0000-000000000001");
    private static readonly Guid AcceptanceProduct = Guid.Parse("74000000-0000-0000-0000-000000000001");
    private static readonly Guid AcceptanceUnit = Guid.Parse("75000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset AcceptanceTime = new(2026, 9, 24, 9, 0, 0, TimeSpan.Zero);

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
            await transaction.CommitAsync();
        }

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

        await using (StoreScope scope = run.Store(CompanyA))
        {
            var afterRefusals = await scope.Store.FindOrderAsync(seeded.OrderId);
            Assert.NotNull(afterRefusals);
            Assert.Equal(4, afterRefusals!.Order.Version);
            Assert.Single(afterRefusals.Order.Acceptances);
            Assert.Equal(4m, Assert.Single(afterRefusals.Order.Lines).AcceptedQuantity.Value);
        }
    }

    private static async Task<AcceptanceSeed> SeedApprovedAcceptanceOrderAsync(Run run)
    {
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        await using StoreScope scope = run.Store(CompanyA);
        var store = scope.Store;
        var order = PurchaseOrder.Create(orderId, CompanyA, AcceptanceSupplier, AcceptanceTime);
        order.AddLine(
            order.Version,
            lineId,
            AcceptanceProduct,
            AcceptanceUnit,
            Quantity.Of(10m, QuantityUnit.Kilogram),
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
        return new AcceptanceSeed(orderId, lineId, approved.Order.Order.Version);
    }

    private static PurchaseAcceptanceStageRequestV1 AcceptanceRequest(AcceptanceSeed seed, Guid acceptanceId, string quantity) => new(
        CompanyA,
        acceptanceId,
        seed.OrderId,
        seed.LineId,
        seed.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
        AcceptanceProduct,
        "product-v1",
        AcceptanceUnit,
        quantity,
        AcceptanceTime.AddMinutes(5));

    private static async Task StageAndCommitAsync(
        Run run,
        Guid companyId,
        PurchaseAcceptanceStageRequestV1 request,
        PurchasingWriteEnvelope envelope)
    {
        await using var connection = new SqlConnection(run.Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
        await using var participant = await PurchaseAcceptanceParticipant.EnlistAsync(connection, transaction, companyId);
        await participant.StageAsync(request, envelope);
        await transaction.CommitAsync();
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

    private static PurchasingWriteEnvelope Envelope(string key, string correlationId) => new(
        ActorId: "purchasing-acceptance-test",
        PermissionCode: "purchasing.orders.acceptance.write",
        RoleCodes: ["purchasing-test"],
        CorrelationId: correlationId,
        CausationId: null,
        IdempotencyKey: key,
        SourceChannel: "integration-test",
        ClientIpAddress: null,
        UserAgent: null);

    private static string AcceptanceHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record AcceptanceSeed(Guid OrderId, Guid LineId, long Version);
}
