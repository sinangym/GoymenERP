using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Transfers;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// STK-SC34-a actual SQL. KLN-D97: a rejected loss request stays as it was and a new request takes the line's next
/// number; the maker never decides their own renewal and a line is written off at most once (filtered unique index).
/// KLN-D98: an undecided loss request refuses the dispatch cancel with its own code. The request-number migration's
/// Down passes while every request is a line's first and refuses (THROW 51811) once a renewed request exists.
/// SENTETIK data only.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    private const string LossRenewalExplanation = "SENTETIK yolda kayip";
    private const string LossRenewalSecondExplanation = "SENTETIK yolda kayip ikinci bildirim";
    private const string LossRenewalReason = "SENTETIK kontrol karari";

    [SqlServerFact]
    public async Task Transfer_loss_request_number_migrations_Down_passes_with_first_requests_and_refuses_while_a_renewed_request_exists()
    {
        await using var run = await Run.StartAsync();
        var (plan, lineId) = await DispatchedLossTransferAsync(run, "SYN-TRANSFER-LOSS-DOWN");
        var maker = Metadata(run);
        var checker = LossChecker(run);
        await LossRequestAsync(run, plan.TransferId, lineId,
            new RequestInventoryTransferLossV1("2", At.AddHours(2), LossRenewalExplanation), maker);
        await LossDecisionAsync(run, plan.TransferId, lineId, new DecideInventoryTransferLossV1("2",
            InventoryTransferDecisionKindV1.Reject, At.AddHours(3), LossRenewalReason), checker);

        var guard = Assert.IsType<SqlOperation>(new AddInventoryTransferLossRequestNumbers().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);

        await LossRequestAsync(run, plan.TransferId, lineId,
            new RequestInventoryTransferLossV1("2", At.AddHours(4), LossRenewalSecondExplanation),
            Metadata(run));
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));

        Assert.Equal(51811, refused.Number);
        Assert.Equal((2, 1), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferLossRequests WHERE TransferId = @id",
                ("@id", plan.TransferId)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferLossDecisions WHERE TransferId = @id",
                ("@id", plan.TransferId))));
    }

    [SqlServerFact]
    public async Task Transfer_loss_renewal_follows_only_a_rejection_keeps_the_maker_off_its_decision_and_writes_the_line_off_once()
    {
        await using var run = await Run.StartAsync();
        var (plan, lineId) = await DispatchedLossTransferAsync(run, "SYN-TRANSFER-LOSS-RENEW");
        var transferId = plan.TransferId;
        var maker = Metadata(run);
        var checker = LossChecker(run);

        var first = await LossRequestAsync(run, transferId, lineId,
            new RequestInventoryTransferLossV1("2", At.AddHours(2), LossRenewalExplanation), maker);
        Assert.Equal((1, "Pending"), (Assert.Single(first.Facts.Losses).RequestNumber,
            Assert.Single(first.Facts.Losses).Status));

        // ASSUMPTION (KLN-D97): one undecided request per line; a different second request waits for the decision.
        await AssertLossRefusedAsync(InventoryTransferReceiptCodesV1.LossAlreadyRequested, () => LossRequestAsync(run,
            transferId, lineId, new RequestInventoryTransferLossV1("2", At.AddHours(2), LossRenewalSecondExplanation),
            Metadata(run)));
        // KLN-D98: the undecided request refuses the dispatch cancel with its own code and is left as it was.
        await using (var scope = run.Store(CompanyA))
        {
            var cancel = await Assert.ThrowsAsync<InventoryTransferException>(() => scope.Store.CancelDispatchAsync(
                transferId, new CancelInventoryTransferDispatchV1("2", At.AddHours(2), TransferDispatchCancelReason),
                null, null, Metadata(run), Authorized));
            Assert.Equal((409, InventoryTransferCodesV1.CancelLossRequestPending), (cancel.Status, cancel.Code));
        }

        var rejected = await LossDecisionAsync(run, transferId, lineId, new DecideInventoryTransferLossV1("2",
            InventoryTransferDecisionKindV1.Reject, At.AddHours(3), LossRenewalReason), checker);
        Assert.Equal((1, "Rejected", "2"), (Assert.Single(rejected.Facts.Losses).RequestNumber,
            Assert.Single(rejected.Facts.Losses).Status, rejected.Transfer.Version));

        // A renewal never predates the rejection it follows.
        await AssertLossRefusedAsync(InventoryTransferCodesV1.TransferStateConflict, () => LossRequestAsync(run,
            transferId, lineId, new RequestInventoryTransferLossV1("2", At.AddHours(2).AddMinutes(30),
                LossRenewalSecondExplanation), Metadata(run)));

        var renewed = await LossRequestAsync(run, transferId, lineId,
            new RequestInventoryTransferLossV1("2", At.AddHours(4), LossRenewalSecondExplanation), maker);
        Assert.Equal([(1, "Rejected"), (2, "Pending")],
            renewed.Facts.Losses.Select(loss => (loss.RequestNumber, loss.Status)));

        // A decision without a number is the first request's: it never lands on the renewal.
        await AssertLossRefusedAsync(InventoryTransferReceiptCodesV1.AlreadyDecided, () => LossDecisionAsync(run,
            transferId, lineId, new DecideInventoryTransferLossV1("2", InventoryTransferDecisionKindV1.Approve,
                At.AddHours(5), LossRenewalReason), checker with { IdempotencyKey = Guid.NewGuid().ToString("N") }));
        // KLN-D92/KLN-D97 maker-checker: the requester never decides their own renewal.
        await AssertLossRefusedAsync(InventoryTransferReceiptCodesV1.SelfApproval, () => LossDecisionAsync(run,
            transferId, lineId, new DecideInventoryTransferLossV1("2", InventoryTransferDecisionKindV1.Approve,
                At.AddHours(5), LossRenewalReason, RequestNumber: 2),
            maker with { IdempotencyKey = Guid.NewGuid().ToString("N") }));

        var approved = await LossDecisionAsync(run, transferId, lineId, new DecideInventoryTransferLossV1("2",
            InventoryTransferDecisionKindV1.Approve, At.AddHours(5), LossRenewalReason, RequestNumber: 2),
            LossChecker(run));
        Assert.Equal([(1, "Rejected"), (2, "Approved")],
            approved.Facts.Losses.Select(loss => (loss.RequestNumber, loss.Status)));
        Assert.Equal("3", approved.Transfer.Version);
        Assert.NotNull(approved.Facts.Losses[1].MovementId);

        // An approval is terminal for the line.
        await AssertLossRefusedAsync(InventoryTransferCodesV1.TransferStateConflict, () => LossRequestAsync(run,
            transferId, lineId, new RequestInventoryTransferLossV1("3", At.AddHours(6), LossRenewalExplanation),
            Metadata(run)));

        // KLN-D23 (c) in the schema: a second approval on the same line is refused by the filtered unique index, even
        // with an otherwise valid, unused movement.
        var duplicate = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            """
            UPDATE inventory.TransferLossDecisions
            SET Decision = 1,
                MovementId = (SELECT TOP (1) m.Id FROM inventory.Movements AS m
                              WHERE m.CompanyId = inventory.TransferLossDecisions.CompanyId
                                AND NOT EXISTS (SELECT 1 FROM inventory.TransferLossDecisions AS d
                                                WHERE d.MovementId = m.Id))
            WHERE TransferId = @id AND RequestNumber = 1
            """,
            ("@id", transferId)));
        Assert.Equal(2601, duplicate.Number);
        Assert.Contains("IX_TransferLossDecisions_CompanyId_TransferId_LineId", duplicate.Message, StringComparison.Ordinal);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.TransferLossDecisions WHERE TransferId = @id AND Decision = 1",
            ("@id", transferId)));
    }

    private static async Task<(PlanInventoryTransferV1 Plan, Guid LineId)> DispatchedLossTransferAsync(
        Run run,
        string code)
    {
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, code, 2m);
        var sourceLocation = await run.LocationOfAsync(CompanyA);
        var destinationLocation = await TransferLocationAsync(run, CompanyA, offset: 1);
        var plan = TransferPlan(source, sourceLocation, destinationLocation, "2");
        var request = TransferDispatch();
        await using (var scope = run.Store(CompanyA))
        {
            await scope.Store.PlanAsync(plan, Metadata(run), Authorized);
            var dispatched = await scope.Store.DispatchAsync(plan.TransferId, request, EffectiveOutgoing(plan, request),
                InventoryTransferSourceScaleSetting.Off, Metadata(run), Authorized);
            Assert.Equal(("2", nameof(InventoryTransferStatus.InTransit)),
                (dispatched.Transfer.Version, dispatched.Transfer.Status));
        }

        return (plan, Assert.Single(plan.Lines).LineId);
    }

    /// <summary>A second, different actor for the checker side of the loss decision.</summary>
    private static InventoryCommandMetadata LossChecker(Run run) =>
        Metadata(run) with { ActorId = Guid.NewGuid().ToString("D") };

    private static async Task<InventoryTransferReceiptResultV1> LossRequestAsync(
        Run run,
        Guid transferId,
        Guid lineId,
        RequestInventoryTransferLossV1 request,
        InventoryCommandMetadata metadata)
    {
        await using var scope = run.Store(CompanyA);
        return await scope.Store.RequestLossAsync(transferId, lineId, request, metadata, Authorized);
    }

    private static async Task<InventoryTransferReceiptResultV1> LossDecisionAsync(
        Run run,
        Guid transferId,
        Guid lineId,
        DecideInventoryTransferLossV1 request,
        InventoryCommandMetadata metadata)
    {
        await using var scope = run.Store(CompanyA);
        return await scope.Store.DecideLossAsync(transferId, lineId, request, metadata, Authorized);
    }

    private static async Task AssertLossRefusedAsync(string code, Func<Task> call)
    {
        var refused = await Assert.ThrowsAsync<InventoryTransferException>(call);
        Assert.Equal((409, code), (refused.Status, refused.Code));
    }
}
