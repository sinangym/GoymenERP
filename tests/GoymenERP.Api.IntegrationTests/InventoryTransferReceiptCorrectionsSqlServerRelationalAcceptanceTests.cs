using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Domain.Transfers;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// STK-SC36-b actual SQL. KLN-D107: a rejected receipt excess takes a corrected re-request (number 2 onwards) that never
/// exceeds the declared excess and is decided by neither the receiver nor the re-requester. KLN-D108: an undecided loss
/// request is withdrawn, is never decided afterwards and no longer holds the dispatch cancel. The migration's Down passes on
/// untouched data and refuses (THROW 51836) once a re-request or a withdrawal exists. FNS-D77/D78: the first-receipt rate port
/// stays dry (no recorded rate) and names the first entry. SENTETIK data only.
/// </summary>
public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    private const string OverageRenewalReason = "SENTETIK duzeltilmis fazla";
    private const string OverageDecisionReason = "SENTETIK fazla karari";
    private const string LossWithdrawalReason = "SENTETIK yanlis bildirim";

    [SqlServerFact]
    public async Task Transfer_overage_renewal_follows_only_a_rejection_stays_within_the_declared_excess_and_keeps_receiver_and_renewer_off_its_decision()
    {
        await using var run = await Run.StartAsync();
        var (transferId, lineId, received) = await ReceivedWithOverageAsync(run, "SYN-TRANSFER-OVERAGE-RENEW");
        var receiver = Metadata(run);
        var renewer = LossChecker(run);
        var checker = LossChecker(run);

        // KLN-D107: the receipt's own excess (request 1) is still undecided.
        await AssertLossRefusedAsync(InventoryTransferReceiptCodesV1.OverageAlreadyRequested, () => RenewOverageAsync(run,
            transferId, lineId, new RenewInventoryTransferOverageV1(received.Transfer.Version, At.AddHours(3), "1",
                OverageRenewalReason), renewer with { IdempotencyKey = Guid.NewGuid().ToString("N") }));

        var rejected = await DecideOverageAsync(run, transferId, lineId, new DecideInventoryTransferOverageV1(
            InventoryTransferDecisionKindV1.Reject, At.AddHours(3), OverageDecisionReason), checker);
        Assert.Equal("Rejected", Assert.Single(rejected.Facts.Receipts).OverageStatus);
        var version = rejected.Transfer.Version;

        // A re-request never predates the rejection it corrects.
        await AssertLossRefusedAsync(InventoryTransferCodesV1.TransferStateConflict, () => RenewOverageAsync(run,
            transferId, lineId, new RenewInventoryTransferOverageV1(version, At.AddHours(2).AddMinutes(30), "1",
                OverageRenewalReason), renewer with { IdempotencyKey = Guid.NewGuid().ToString("N") }));
        // ... and never asks for more than the excess declared on the receipt (12 - 10 = 2 kg).
        var exceeded = await Assert.ThrowsAsync<InventoryTransferException>(() => RenewOverageAsync(run, transferId, lineId,
            new RenewInventoryTransferOverageV1(version, At.AddHours(4), "2.5", OverageRenewalReason),
            renewer with { IdempotencyKey = Guid.NewGuid().ToString("N") }));
        Assert.Equal((422, InventoryTransferReceiptCodesV1.OverageRenewalExceedsDeclared), (exceeded.Status, exceeded.Code));

        var renewed = await RenewOverageAsync(run, transferId, lineId,
            new RenewInventoryTransferOverageV1(version, At.AddHours(4), "1.5", OverageRenewalReason), renewer);
        var renewal = Assert.Single(renewed.Facts.OverageRenewals);
        Assert.Equal((2, "1.5", "Pending", version), (renewal.RequestNumber, renewal.OverageKg, renewal.Status,
            renewed.Transfer.Version));
        Assert.Equal("Rejected", Assert.Single(renewed.Facts.Receipts).OverageStatus);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'inventory.transfer.overage-renewed.v1'"));

        // One undecided request per line: a second re-request waits for the decision.
        await AssertLossRefusedAsync(InventoryTransferReceiptCodesV1.OverageAlreadyRequested, () => RenewOverageAsync(run,
            transferId, lineId, new RenewInventoryTransferOverageV1(version, At.AddHours(4), "1", OverageRenewalReason),
            renewer with { IdempotencyKey = Guid.NewGuid().ToString("N") }));
        // A decision without a number is the receipt's own request, which is already decided.
        await AssertLossRefusedAsync(InventoryTransferReceiptCodesV1.AlreadyDecided, () => DecideOverageAsync(run,
            transferId, lineId, new DecideInventoryTransferOverageV1(InventoryTransferDecisionKindV1.Approve,
                At.AddHours(5), OverageDecisionReason), LossChecker(run)));
        // Maker-checker: neither the receiver nor the re-requester decides the re-request.
        await AssertLossRefusedAsync(InventoryTransferReceiptCodesV1.SelfApproval, () => DecideOverageAsync(run,
            transferId, lineId, new DecideInventoryTransferOverageV1(InventoryTransferDecisionKindV1.Approve,
                At.AddHours(5), OverageDecisionReason, RequestNumber: 2),
            receiver with { IdempotencyKey = Guid.NewGuid().ToString("N") }));
        await AssertLossRefusedAsync(InventoryTransferReceiptCodesV1.SelfApproval, () => DecideOverageAsync(run,
            transferId, lineId, new DecideInventoryTransferOverageV1(InventoryTransferDecisionKindV1.Approve,
                At.AddHours(5), OverageDecisionReason, RequestNumber: 2),
            renewer with { IdempotencyKey = Guid.NewGuid().ToString("N") }));

        var approvalRequest = new DecideInventoryTransferOverageV1(
            InventoryTransferDecisionKindV1.Approve, At.AddHours(5), OverageDecisionReason, RequestNumber: 2);
        var approvalMetadata = LossChecker(run);
        var beforeApproval = (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));
        var stockBeforeApproval = await run.ScalarAsync<decimal>(
            "SELECT SUM(QuantityKg) FROM inventory.Balances WHERE CompanyId = @company", ("@company", CompanyA));
        // A borrowed transaction stays with its caller: the actual approval can be saved, then rolled back in full.
        await using (var scope = run.Store(CompanyA))
        {
            await using var transaction = await scope.DbContext.Database.BeginTransactionAsync();
            var staged = await scope.Store.DecideOverageAsync(transferId, lineId, approvalRequest, approvalMetadata, Authorized);
            var movementId = Assert.Single(staged.Facts.OverageRenewals).MovementId!.Value;
            Assert.Same(transaction, scope.DbContext.Database.CurrentTransaction);
            Assert.True(await scope.DbContext.Movements.AsNoTracking().AnyAsync(row => row.Id == movementId));
            await transaction.RollbackAsync();
        }
        Assert.Equal(beforeApproval, (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Commands"),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents")));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.TransferOverageDecisions WHERE TransferId = @id AND RequestNumber = 2",
            ("@id", transferId)));
        Assert.Equal(stockBeforeApproval, await run.ScalarAsync<decimal>(
            "SELECT SUM(QuantityKg) FROM inventory.Balances WHERE CompanyId = @company", ("@company", CompanyA)));

        // The same key can succeed after caller rollback; without a caller transaction the command owns its commit.
        var approved = await DecideOverageAsync(run, transferId, lineId, approvalRequest, approvalMetadata);
        Assert.False(approved.IsReplay);
        var approvedRenewal = Assert.Single(approved.Facts.OverageRenewals);
        Assert.Equal((2, "Approved"), (approvedRenewal.RequestNumber, approvedRenewal.Status));
        Assert.NotNull(approvedRenewal.MovementId);
        var replay = await DecideOverageAsync(run, transferId, lineId, approvalRequest, approvalMetadata);
        Assert.True(replay.IsReplay);
        Assert.Equal(approved.OriginalCorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(approvedRenewal, Assert.Single(replay.Facts.OverageRenewals));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE Id = @id",
            ("@id", approvedRenewal.MovementId!.Value)));

        // An approved excess is terminal for the line.
        await AssertLossRefusedAsync(InventoryTransferCodesV1.TransferStateConflict, () => RenewOverageAsync(run,
            transferId, lineId, new RenewInventoryTransferOverageV1(approved.Transfer.Version, At.AddHours(6), "1",
                OverageRenewalReason), Metadata(run)));

        // The schema keeps a line's excess approved at most once (filtered unique index on the approvals).
        var duplicate = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            """
            UPDATE inventory.TransferOverageDecisions
            SET Decision = 1
            WHERE TransferId = @id AND RequestNumber = 1
            """,
            ("@id", transferId)));
        Assert.Equal(2601, duplicate.Number);
        Assert.Equal((2, 1), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferOverageDecisions WHERE TransferId = @id",
                ("@id", transferId)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferOverageRenewals WHERE TransferId = @id",
                ("@id", transferId))));
    }

    [SqlServerFact]
    public async Task Transfer_loss_withdrawal_ends_an_undecided_request_refuses_its_decision_and_frees_the_dispatch_cancel()
    {
        await using var run = await Run.StartAsync();
        var (plan, lineId) = await DispatchedLossTransferAsync(run, "SYN-TRANSFER-LOSS-WITHDRAW");
        var transferId = plan.TransferId;
        await LossRequestAsync(run, transferId, lineId,
            new RequestInventoryTransferLossV1("2", At.AddHours(2), LossRenewalExplanation), Metadata(run));

        // A request that does not exist is not withdrawn; a withdrawal never predates its request.
        await AssertLossRefusedAsync(InventoryTransferCodesV1.TransferStateConflict, () => WithdrawLossAsync(run,
            transferId, lineId, new WithdrawInventoryTransferLossV1("2", 2, At.AddHours(3), LossWithdrawalReason),
            Metadata(run)));
        await AssertLossRefusedAsync(InventoryTransferCodesV1.TransferStateConflict, () => WithdrawLossAsync(run,
            transferId, lineId, new WithdrawInventoryTransferLossV1("2", 1, At.AddHours(1), LossWithdrawalReason),
            Metadata(run)));

        var withdrawal = new WithdrawInventoryTransferLossV1("2", 1, At.AddHours(3), LossWithdrawalReason);
        var withdrawer = Metadata(run);
        var withdrawn = await WithdrawLossAsync(run, transferId, lineId, withdrawal, withdrawer);
        var loss = Assert.Single(withdrawn.Facts.Losses);
        Assert.Equal((1, "Withdrawn", LossWithdrawalReason, "2"), (loss.RequestNumber, loss.Status, loss.DecisionReason,
            withdrawn.Transfer.Version));
        Assert.Null(loss.MovementId);

        // The same withdrawal under a new key answers the stored result; a different one is refused.
        var replay = await WithdrawLossAsync(run, transferId, lineId, withdrawal,
            withdrawer with { IdempotencyKey = Guid.NewGuid().ToString("N") });
        Assert.Equal("Withdrawn", Assert.Single(replay.Facts.Losses).Status);
        await AssertLossRefusedAsync(InventoryTransferReceiptCodesV1.LossWithdrawn, () => WithdrawLossAsync(run,
            transferId, lineId, withdrawal with { Reason = LossRenewalReason }, Metadata(run)));
        // KLN-D108: a withdrawn request is never decided.
        await AssertLossRefusedAsync(InventoryTransferReceiptCodesV1.LossWithdrawn, () => LossDecisionAsync(run,
            transferId, lineId, new DecideInventoryTransferLossV1("2", InventoryTransferDecisionKindV1.Approve,
                At.AddHours(4), LossRenewalReason), LossChecker(run)));
        Assert.Equal((0, 1, 1), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferLossDecisions WHERE TransferId = @id",
                ("@id", transferId)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.TransferLossWithdrawals WHERE TransferId = @id",
                ("@id", transferId)),
            await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'inventory.transfer.loss-withdrawn.v1'")));

        // KLN-D98 now passes: no undecided loss request holds the dispatch cancel.
        await using var scope = run.Store(CompanyA);
        await scope.Store.CancelDispatchAsync(transferId,
            new CancelInventoryTransferDispatchV1("2", At.AddHours(5), TransferDispatchCancelReason), null, null,
            Metadata(run), Authorized);
    }

    [SqlServerFact]
    public async Task Transfer_decided_loss_request_is_not_withdrawn()
    {
        await using var run = await Run.StartAsync();
        var (plan, lineId) = await DispatchedLossTransferAsync(run, "SYN-TRANSFER-LOSS-WITHDRAW-DECIDED");
        await LossRequestAsync(run, plan.TransferId, lineId,
            new RequestInventoryTransferLossV1("2", At.AddHours(2), LossRenewalExplanation), Metadata(run));
        await LossDecisionAsync(run, plan.TransferId, lineId, new DecideInventoryTransferLossV1("2",
            InventoryTransferDecisionKindV1.Reject, At.AddHours(3), LossRenewalReason), LossChecker(run));

        await AssertLossRefusedAsync(InventoryTransferReceiptCodesV1.AlreadyDecided, () => WithdrawLossAsync(run,
            plan.TransferId, lineId, new WithdrawInventoryTransferLossV1("2", 1, At.AddHours(4), LossWithdrawalReason),
            Metadata(run)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.TransferLossWithdrawals WHERE TransferId = @id", ("@id", plan.TransferId)));
    }

    [SqlServerFact]
    public async Task Transfer_overage_renewal_and_loss_withdrawal_migrations_Down_passes_untouched_and_refuses_with_51836_after_a_withdrawal()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(
            new AddInventoryTransferOverageRenewalsAndLossWithdrawals().DownOperations[0]).Sql;
        var (plan, lineId) = await DispatchedLossTransferAsync(run, "SYN-TRANSFER-CORRECTIONS-DOWN");
        await LossRequestAsync(run, plan.TransferId, lineId,
            new RequestInventoryTransferLossV1("2", At.AddHours(2), LossRenewalExplanation), Metadata(run));

        await run.ExecuteAsync(guard);

        await WithdrawLossAsync(run, plan.TransferId, lineId,
            new WithdrawInventoryTransferLossV1("2", 1, At.AddHours(3), LossWithdrawalReason), Metadata(run));
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));

        Assert.Equal(51836, refused.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM inventory.TransferLossWithdrawals WHERE TransferId = @id", ("@id", plan.TransferId)));
    }

    [SqlServerFact]
    public async Task First_receipt_exchange_rate_port_stays_dry_names_the_first_entry_and_reads_only_its_own_company()
    {
        await using var run = await Run.StartAsync();
        // A non-purchasing receipt (the fixture's synthetic source) is the first entry of this label; it also seeds the lot.
        var synthetic = await SeedFreePhysicalSourceAsync(run, CompanyA, "SYN-FIRST-RECEIPT-OTHER", 2m);
        var location = await run.LocationOfAsync(CompanyA);
        var accepted = NewLabel(CompanyA, "SYN-FIRST-RECEIPT-ACCEPTED");
        var neverEntered = NewLabel(CompanyA, "SYN-FIRST-RECEIPT-NONE");
        var acceptanceId = Guid.NewGuid();
        await using (var scope = run.Store(CompanyA))
        {
            var seedMetadata = Metadata(run) with { CorrelationId = $"seed-{Guid.NewGuid():N}" };
            await scope.Labels.SaveLabelAsync(accepted, seedMetadata);
            await scope.Labels.SaveLabelAsync(neverEntered, seedMetadata);
            await scope.Movements.RecordMovementAsync(Movement(CompanyA, StockMovementKind.Receipt,
                    new StockSourceReference(InventoryGoodsReceiptContractV1.SourceKind, acceptanceId),
                    In(Position(CompanyA, location, accepted, BlockState.Free), 3m)),
                seedMetadata);
        }

        var goodsReceipt = await FirstReceiptRateAsync(run, CompanyA, CompanyA, accepted.Id);
        Assert.Equal((InventoryFirstReceiptExchangeRateStatusV1.Unavailable,
                InventoryFirstReceiptExchangeRateReasonsV1.RateNotRecorded),
            (goodsReceipt.Status, goodsReceipt.UnavailableReason));
        Assert.Null(goodsReceipt.Rate);
        Assert.Equal((accepted.Id, acceptanceId), (goodsReceipt.Entry!.EntryLabelId, goodsReceipt.Entry.GoodsAcceptanceId));

        var other = await FirstReceiptRateAsync(run, CompanyA, CompanyA, synthetic.LabelId);
        Assert.Equal((InventoryFirstReceiptExchangeRateStatusV1.Unavailable,
                InventoryFirstReceiptExchangeRateReasonsV1.FirstEntryNotGoodsReceipt),
            (other.Status, other.UnavailableReason));
        Assert.Null(other.Entry!.GoodsAcceptanceId);

        var none = await FirstReceiptRateAsync(run, CompanyA, CompanyA, neverEntered.Id);
        Assert.Equal((InventoryFirstReceiptExchangeRateStatusV1.Unavailable,
                InventoryFirstReceiptExchangeRateReasonsV1.NoStockEntry),
            (none.Status, none.UnavailableReason));
        Assert.Null(none.Entry);

        // Company isolation: the argument never widens the resolved company, and another company's label is not found.
        Assert.Equal(InventoryFirstReceiptExchangeRateStatusV1.NotFound,
            (await FirstReceiptRateAsync(run, CompanyA, CompanyB, accepted.Id)).Status);
        Assert.Equal(InventoryFirstReceiptExchangeRateStatusV1.NotFound,
            (await FirstReceiptRateAsync(run, CompanyB, CompanyB, accepted.Id)).Status);
    }

    /// <summary>Plans and dispatches a 10 kg whole-label transfer and receives it as 12 kg with a declared excess.</summary>
    private static async Task<(Guid TransferId, Guid LineId, InventoryTransferReceiptResultV1 Received)>
        ReceivedWithOverageAsync(Run run, string code)
    {
        var source = await SeedFreePhysicalSourceAsync(run, CompanyA, code, 10m);
        var sourceLocation = await run.LocationOfAsync(CompanyA);
        var destinationLocation = await TransferLocationAsync(run, CompanyA, offset: 1);
        var plan = TransferPlan(source, sourceLocation, destinationLocation, "10");
        var lineId = plan.Lines[0].LineId;
        var dispatch = TransferDispatch();
        await using var scope = run.Store(CompanyA);
        await scope.Store.PlanAsync(plan, Metadata(run), Authorized);
        await scope.Store.DispatchAsync(plan.TransferId, dispatch, EffectiveOutgoing(plan, dispatch),
            InventoryTransferSourceScaleSetting.Off, Metadata(run), Authorized);
        var received = await scope.Store.ReceiveLineAsync(plan.TransferId, lineId,
            await RecordIncomingReceiptDocumentAsync(run, plan.TransferId, lineId, "12", "SENTETIK tarti fazlasi"),
            Metadata(run), Authorized);
        Assert.Equal(("2", "Pending"), (Assert.Single(received.Facts.Receipts).OverageKg,
            Assert.Single(received.Facts.Receipts).OverageStatus));
        return (plan.TransferId, lineId, received);
    }

    private static async Task<InventoryTransferReceiptResultV1> RenewOverageAsync(
        Run run,
        Guid transferId,
        Guid lineId,
        RenewInventoryTransferOverageV1 request,
        InventoryCommandMetadata metadata)
    {
        await using var scope = run.Store(CompanyA);
        return await scope.Store.RenewOverageAsync(transferId, lineId, request, metadata, Authorized);
    }

    private static async Task<InventoryTransferReceiptResultV1> DecideOverageAsync(
        Run run,
        Guid transferId,
        Guid lineId,
        DecideInventoryTransferOverageV1 request,
        InventoryCommandMetadata metadata)
    {
        await using var scope = run.Store(CompanyA);
        return await scope.Store.DecideOverageAsync(transferId, lineId, request, metadata, Authorized);
    }

    private static async Task<InventoryTransferReceiptResultV1> WithdrawLossAsync(
        Run run,
        Guid transferId,
        Guid lineId,
        WithdrawInventoryTransferLossV1 request,
        InventoryCommandMetadata metadata)
    {
        await using var scope = run.Store(CompanyA);
        return await scope.Store.WithdrawLossAsync(transferId, lineId, request, metadata, Authorized);
    }

    private static async Task<InventoryFirstReceiptExchangeRateV1> FirstReceiptRateAsync(
        Run run,
        Guid resolvedCompany,
        Guid askedCompany,
        Guid labelId)
    {
        var companyContext = new CompanyContext();
        companyContext.Set(resolvedCompany);
        await using var context = run.Context(resolvedCompany);
        return await new InventoryFirstReceiptExchangeRateReadPort(context, companyContext).GetAsync(askedCompany, labelId);
    }
}
