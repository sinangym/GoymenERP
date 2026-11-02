using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Requests;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// A1-1b (plan API-01b, TEK-09; ALS-D46) on real SQL Server: the submitter's withdrawal of a Submitted request is stored in
/// ONE save (header facts, attempt, audit) and the same key replays; nobody but the submitter withdraws (the domain refuses
/// and CK_PurchaseRequests_Withdrawal refuses a direct write too, with a binary comparison); the withdrawal facts match the
/// status; an APPROVED order bound to the request (a pre-A1-1 shape) blocks the withdrawal while a draft one does not;
/// company B neither sees nor withdraws company A's request; the migration's Down guard refuses while a withdrawn request
/// exists; and (#900 review P3) the maker ≠ checker check compares binary, as the domain does. SENTETIK owners.
/// </summary>
public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    private static readonly PurchasingMutationAudit RequestWithdrawn =
        new PurchasingMutationAudit("purchasing.purchase-request.withdrawn.v1", "Withdraw", "SENTETIK talep geri çekildi");

    [SqlServerFact]
    public async Task A_withdrawal_is_one_save_only_the_submitter_withdraws_and_the_checks_hold_the_facts()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        Guid requestId = await SubmittedRequestAsync(run, owners, 100m);

        await using (StoreScope a = run.Store(CompanyA))
        {
            // ALS-D46: only the submitter withdraws; the manager (or anyone else) is refused and nothing is written.
            var other = await Assert.ThrowsAsync<PurchasingRuleException>(() => a.Store.MutateRequestAsync(run.Envelope("k-other", actor: Manager),
                Hash("other"), PurchasingCommands.RequestWithdrawV1, requestId,
                (r, bindings, now) => r.Withdraw(3, Manager, "SENTETIK", bindings.HasApprovedOrder, now), RequestWithdrawn));
            Assert.Equal(PurchasingRuleCodes.RequestNotSubmitter, other.Code);
        }

        Assert.Equal((1, 1, 2, 2, 3L), await run.RequestCountsAsync(requestId));

        await using (StoreScope a = run.Store(CompanyA))
        {
            var withdrawn = await a.Store.MutateRequestAsync(run.Envelope("k-withdraw"), Hash("k-withdraw"), PurchasingCommands.RequestWithdrawV1, requestId,
                (r, bindings, now) => r.Withdraw(3, run.Actor, "SENTETIK yanlış ürün", bindings.HasApprovedOrder, now), RequestWithdrawn);
            Assert.Equal((PurchaseRequestStatus.Withdrawn, 4L, run.Actor, "SENTETIK yanlış ürün"), (withdrawn!.Request.Request.Status,
                withdrawn.Request.Request.Version, withdrawn.Request.Request.Withdrawal!.DecidedBy, withdrawn.Request.Request.Withdrawal.Reason));
            var replay = await a.Store.MutateRequestAsync(run.Envelope("k-withdraw", correlation: "SENTETIK-second"), Hash("k-withdraw"),
                PurchasingCommands.RequestWithdrawV1, requestId, (_, _, _) => throw new InvalidOperationException("a replay never mutates"), RequestWithdrawn);
            Assert.Equal((PurchasingWriteOutcome.Replayed, run.Correlation, 4L), (replay!.Outcome, replay.OriginalCorrelationId, replay.Request.Request.Version));
        }

        Assert.Equal((1, 1, 3, 3, 4L), await run.RequestCountsAsync(requestId));
        Assert.Equal((run.Actor, "SENTETIK yanlış ürün"),
            (await run.ScalarAsync<string>("SELECT WithdrawnBy FROM purchasing.PurchaseRequests WHERE Id = @id", ("@id", requestId)),
                await run.ScalarAsync<string>("SELECT WithdrawalReason FROM purchasing.PurchaseRequests WHERE Id = @id", ("@id", requestId))));
        Assert.Equal(DBNull.Value, await run.ScalarAsync<object>("SELECT RejectedBy FROM purchasing.PurchaseRequests WHERE Id = @id", ("@id", requestId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'purchasing.purchase-request.withdrawn.v1' AND ActorId = @actor AND EntityId = @id",
            ("@actor", run.Actor), ("@id", requestId.ToString("D"))));

        await using (StoreScope a = run.Store(CompanyA))
        {
            // Rebuilt through the domain (lines, submission, withdrawal): the same version and facts; a terminal state.
            var rebuilt = await a.Store.FindRequestAsync(requestId);
            Assert.Equal((PurchaseRequestStatus.Withdrawn, 4L, run.Actor), (rebuilt!.Request.Status, rebuilt.Request.Version, rebuilt.Request.Withdrawal!.DecidedBy));
            Assert.Null(rebuilt.Request.Rejection);
            Assert.Equal(requestId, Assert.Single((await a.Store.ListRequestsAsync(10, null, PurchaseRequestStatus.Withdrawn)).Items).Request.Id);
            Assert.Empty((await a.Store.ListRequestsAsync(10, null, PurchaseRequestStatus.Rejected)).Items);
            var decided = await Assert.ThrowsAsync<PurchasingRuleException>(() => a.Store.MutateRequestAsync(run.Envelope("k-after", actor: Manager),
                Hash("after"), PurchasingCommands.RequestApproveV1, requestId, (r, _, now) => r.Approve(4, Manager, now), RequestApproved));
            Assert.Equal(PurchasingRuleCodes.RequestNotSubmitted, decided.Code);
        }

        // The checks refuse what the domain refuses, on a direct write too (547: CHECK constraint): another withdrawer, the
        // submitter in another case (binary comparison), and a status that drops the facts.
        var other2 = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE purchasing.PurchaseRequests SET WithdrawnBy = N'SENTETIK-other' WHERE Id = @id", ("@id", requestId)));
        Assert.Equal(547, other2.Number);
        Assert.Contains("CK_PurchaseRequests_Withdrawal", other2.Message, StringComparison.Ordinal);
        var cased = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE purchasing.PurchaseRequests SET WithdrawnBy = UPPER(SubmittedBy) WHERE Id = @id", ("@id", requestId)));
        Assert.Equal(547, cased.Number);
        Assert.Contains("CK_PurchaseRequests_Withdrawal", cased.Message, StringComparison.Ordinal);
        var status = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE purchasing.PurchaseRequests SET Status = 2 WHERE Id = @id", ("@id", requestId)));
        Assert.Equal(547, status.Number);
        Assert.Contains("CK_PurchaseRequests_Withdrawal", status.Message, StringComparison.Ordinal);
        var reason = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE purchasing.PurchaseRequests SET WithdrawalReason = N'   ' WHERE Id = @id", ("@id", requestId)));
        Assert.Equal(547, reason.Number);
        Assert.Equal(4L, await run.ScalarAsync<long>("SELECT Version FROM purchasing.PurchaseRequests WHERE Id = @id", ("@id", requestId)));
    }

    [SqlServerFact]
    public async Task A_bound_approved_order_blocks_the_withdrawal_and_a_draft_order_does_not()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        Guid blockedId = await SubmittedRequestAsync(run, owners, 100m);
        Guid freeId = await SubmittedRequestAsync(run, owners, 50m, "k2");
        await using (StoreScope a = run.Store(CompanyA))
        {
            Guid blockedLine = (await a.Store.FindRequestAsync(blockedId))!.Request.Lines[0].Id;
            Guid freeLine = (await a.Store.FindRequestAsync(freeId))!.Request.Lines[0].Id;
            PurchaseOrder builtA = Order(owners, blockedId, blockedLine, 40m);
            Guid legacyOrder = (await a.Store.StoreOrderAsync(run.Envelope("k-order-a"), Hash("order-a"), owners.Account, Day, () => builtA, VersionsOf(builtA)))
                .Order.Order.Id;
            PurchaseOrder builtB = Order(owners, freeId, freeLine, 50m);
            await a.Store.StoreOrderAsync(run.Envelope("k-order-b"), Hash("order-b"), owners.Account, Day, () => builtB, VersionsOf(builtB));
            // The pre-A1-1 shape: an order approved while its source request was only Submitted (orders were then opened
            // from Submitted requests). A1-1 no longer produces it; the withdrawal must still refuse it (No.161 §1).
            await run.ExecuteAsync("UPDATE purchasing.PurchaseOrders SET Status = 2, Version = 3 WHERE Id = @id", ("@id", legacyOrder));
        }

        await using (StoreScope a = run.Store(CompanyA))
        {
            var blocked = await Assert.ThrowsAsync<PurchasingRuleException>(() => a.Store.MutateRequestAsync(run.Envelope("k-withdraw-blocked"),
                Hash("withdraw-blocked"), PurchasingCommands.RequestWithdrawV1, blockedId,
                (r, bindings, now) => r.Withdraw(3, run.Actor, "SENTETIK", bindings.HasApprovedOrder, now), RequestWithdrawn));
            Assert.Equal(PurchasingRuleCodes.RequestHasApprovedOrder, blocked.Code);
        }

        Assert.Equal("Submitted", await StatusOfAsync(run, blockedId));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseAttempts WHERE CommandName = @command AND RequestId = @id",
            ("@command", PurchasingCommands.RequestWithdrawV1), ("@id", blockedId)));

        await using (StoreScope a = run.Store(CompanyA))
        {
            // A draft order binds nothing: its request is withdrawn with the reason.
            var withdrawn = await a.Store.MutateRequestAsync(run.Envelope("k-withdraw-free"), Hash("withdraw-free"), PurchasingCommands.RequestWithdrawV1, freeId,
                (r, bindings, now) => r.Withdraw(3, run.Actor, "SENTETIK vazgeçildi", bindings.HasApprovedOrder, now), RequestWithdrawn);
            Assert.Equal((PurchaseRequestStatus.Withdrawn, 4L), (withdrawn!.Request.Request.Status, withdrawn.Request.Request.Version));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'purchasing.purchase-request.withdrawn.v1'"));
    }

    [SqlServerFact]
    public async Task Company_B_neither_sees_nor_withdraws_company_As_request()
    {
        await using Run run = await Run.StartAsync();
        Guid requestId = await SubmittedRequestAsync(run, Owners.For(CompanyA), 5m);

        await using (StoreScope b = run.Store(CompanyB))
        {
            Assert.Null(await b.Store.FindRequestAsync(requestId));
            Assert.Null(await b.Store.MutateRequestAsync(run.Envelope("k-b-withdraw"), Hash("b-withdraw"), PurchasingCommands.RequestWithdrawV1, requestId,
                (r, bindings, now) => r.Withdraw(3, run.Actor, "SENTETIK", bindings.HasApprovedOrder, now), RequestWithdrawn));
        }

        Assert.Equal("Submitted", await StatusOfAsync(run, requestId));
        Assert.Equal((1, 1, 2, 2, 3L), await run.RequestCountsAsync(requestId));
    }

    [SqlServerFact]
    public async Task The_withdrawal_migrations_Down_refuses_while_a_withdrawn_request_exists_and_the_maker_checker_compares_binary()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        Guid withdrawnId = await SubmittedRequestAsync(run, owners, 5m);
        Guid casedId = await SubmittedRequestAsync(run, owners, 6m, "k2");
        string casedManager = run.Actor.ToUpperInvariant();
        Assert.NotEqual(run.Actor, casedManager);

        await using (StoreScope a = run.Store(CompanyA))
        {
            await a.Store.MutateRequestAsync(run.Envelope("k-withdraw"), Hash("withdraw"), PurchasingCommands.RequestWithdrawV1, withdrawnId,
                (r, bindings, now) => r.Withdraw(3, run.Actor, "SENTETIK", bindings.HasApprovedOrder, now), RequestWithdrawn);
            // #900 review P3: an actor id that differs from the maker only by case is another actor for the domain (ordinal),
            // and the database check now agrees (Latin1_General_100_BIN2) instead of refusing it with a 547.
            var approved = await a.Store.MutateRequestAsync(run.Envelope("k-cased", actor: casedManager), Hash("cased"), PurchasingCommands.RequestApproveV1,
                casedId, (r, _, now) => r.Approve(3, casedManager, now), RequestApproved);
            Assert.Equal(PurchaseRequestStatus.Approved, approved!.Request.Request.Status);
        }

        var migration = new AddPurchaseRequestWithdrawal();
        var guard = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql));
        Assert.Equal(51165, guard.Number);
        Assert.Equal("Withdrawn", await StatusOfAsync(run, withdrawnId));
        Assert.Equal("Approved", await StatusOfAsync(run, casedId));
    }
}
