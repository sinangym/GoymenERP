using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// API-30 against a REAL SQL Server: the Finance cash account (kasa) master on Finance's own migrations
/// over the host's migrated and seeded database, with Audit's real save interceptor.
/// <para>
/// Permission and the Settings currency candidates are ISOLATED SYNTHETIC ports: no Finance grant
/// exists, so these tests prove the store and its rules, not a user outcome. Every identity and
/// currency below is SENTETIK / NOT_ENV test data. A cash account moves no money; no cash movement exists, so
/// the registered balance reading answers zero and a SENTETIK reading stands in for any other answer.
/// </para>
/// </summary>
public sealed class FinanceCashAccountSqlServerRelationalAcceptanceTests
{
    private const string Proposer = "SENTETIK-proposer";
    private const string Approver = "SENTETIK-approver";
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Currency = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5b01");

    [SqlServerFact]
    public async Task A_cash_account_is_active_only_after_another_user_approves_it_once_and_moves_no_money()
    {
        await using var run = await Run.StartAsync();

        CashAccountView proposed;
        await using (var a = run.Open(CompanyA, Proposer))
        {
            var result = await a.Accounts.ProposeAsync(a.Context, Key("p-1"),
                new CashAccountProposeRequest("SENTETIK kasa", Currency), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            proposed = result.Resource!;
            Assert.Equal((CashAccountStatus.Proposed, false, "1"), (proposed.Status, proposed.IsActive, proposed.Version));

            var item = Assert.Single((await a.Accounts.ListAsync(a.Context, CancellationToken.None)).Resource!.Items);
            Assert.Equal((proposed.Id, true), (item.Account.Id, item.IsOwnProposal));

            // The proposer cannot approve their own proposal.
            var self = await a.Accounts.ApproveAsync(a.Context, Key("a-self"),
                new CashAccountApproveRequest(proposed.Id, "1"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Denied, self.Outcome);
            Assert.Equal("finance_approver_is_proposer", Assert.Single(self.Issues).Code);
        }

        await using (var b = run.Open(CompanyA, Approver))
        {
            var approved = await b.Accounts.ApproveAsync(b.Context, Key("a-1"),
                new CashAccountApproveRequest(proposed.Id, "1"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
            Assert.Equal((CashAccountStatus.Approved, true, "2"),
                (approved.Resource!.Status, approved.Resource.IsActive, approved.Resource.Version));

            // One decision only: a second is refused, whatever version it names.
            Assert.Equal(CommandOutcome.ValidationFailed, (await b.Accounts.ApproveAsync(b.Context, Key("a-2"),
                new CashAccountApproveRequest(proposed.Id, "2"), CancellationToken.None)).Outcome);
            Assert.Equal(CommandOutcome.VersionConflict, (await b.Accounts.ApproveAsync(b.Context, Key("a-3"),
                new CashAccountApproveRequest(proposed.Id, "1"), CancellationToken.None)).Outcome);

            var replay = await b.Accounts.ApproveAsync(b.Context, Key("a-1"),
                new CashAccountApproveRequest(proposed.Id, "1"), CancellationToken.None);
            Assert.True(replay.IsReplay);
            Assert.Equal("2", replay.Resource!.Version);
        }

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT Status FROM finance.CashAccounts WHERE Id = @id", ("@id", proposed.Id)));
        Assert.Equal(Approver, await run.ScalarAsync<string>("SELECT DecidedBy FROM finance.CashAccounts WHERE Id = @id", ("@id", proposed.Id)));
        // Proposal, approval and the stale approval (a terminal refusal): three command records; the
        // self-approval and the not-proposed refusal store none.
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccountCommands"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.CashAccount' AND CorrelationId = @c", ("@c", run.Correlation)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.cash-account.approved.v1' AND ApprovalContextJson IS NOT NULL"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.cash-account.approved.v1' "
            + "AND JSON_VALUE(ApprovalContextJson, '$.proposedBy') = @proposer AND JSON_VALUE(ApprovalContextJson, '$.approvedBy') = @approver "
            + "AND JSON_VALUE(ApprovalContextJson, '$.rejectedBy') IS NULL",
            ("@proposer", Proposer), ("@approver", Approver)));
        // A cash account is a definition: no money event, no balance.
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
    }

    [SqlServerFact]
    public async Task The_database_refuses_a_self_decision_and_a_second_live_account_of_the_same_name()
    {
        await using var run = await Run.StartAsync();

        // Without the service: the database refuses an approval by the proposer (547), binary-compared.
        var error = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO finance.CashAccounts (Id, CompanyId, CurrencyId, DisplayName, Status, Version, ProposedBy, ProposedAtUtc, "
            + "DecidedBy, DecidedAtUtc, UpdatedAtUtc) VALUES (NEWID(), @c, @cur, N'SENTETIK', 2, 2, N'x', SYSDATETIMEOFFSET(), "
            + "N'x', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())", ("@c", CompanyA), ("@cur", Currency)));
        Assert.Equal(547, error.Number);
        Assert.Contains("CK_CashAccounts_Decision", error.Message, StringComparison.Ordinal);

        CashAccountView first;
        await using (var a = run.Open(CompanyA, Proposer))
        {
            first = (await a.Accounts.ProposeAsync(a.Context, Key("p-1"),
                new CashAccountProposeRequest("SENTETIK kasa", Currency), CancellationToken.None)).Resource!;
            var taken = await a.Accounts.ProposeAsync(a.Context, Key("p-2"),
                new CashAccountProposeRequest("SENTETIK kasa", Currency), CancellationToken.None);
            Assert.Equal("cash_account_name_taken", Assert.Single(taken.Issues).Code);
        }

        // Without the service: the filtered unique index refuses a second live row of the same name (2601).
        var duplicate = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO finance.CashAccounts (Id, CompanyId, CurrencyId, DisplayName, Status, Version, ProposedBy, ProposedAtUtc, "
            + "UpdatedAtUtc) VALUES (NEWID(), @c, @cur, N'SENTETIK kasa', 1, 1, N'x', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())",
            ("@c", CompanyA), ("@cur", Currency)));
        Assert.Equal(2601, duplicate.Number);
        Assert.Contains("UX_CashAccounts_LiveName", duplicate.Message, StringComparison.Ordinal);

        // A rejection frees the name, and a read-only caller sees approved accounts only.
        await using (var b = run.Open(CompanyA, Approver))
        {
            var rejected = await b.Accounts.RejectAsync(b.Context, Key("r-1"),
                new CashAccountRejectRequest(first.Id, "1", "SENTETIK gerekçe"), CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, CashAccountStatus.Rejected), (rejected.Outcome, rejected.Resource!.Status));
        }

        CashAccountView second;
        await using (var a = run.Open(CompanyA, Proposer))
        {
            var again = await a.Accounts.ProposeAsync(a.Context, Key("p-3"),
                new CashAccountProposeRequest("SENTETIK kasa", Currency), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, again.Outcome);
            second = again.Resource!;
        }

        await using (var reader = run.Open(CompanyA, "SENTETIK-reader", new ReadOnlyPermissions()))
        {
            Assert.Empty((await reader.Accounts.ListAsync(reader.Context, CancellationToken.None)).Resource!.Items);
            Assert.Equal(ReadOutcome.NotFound, (await reader.Accounts.GetAsync(reader.Context, second.Id, CancellationToken.None)).Outcome);
        }

        await using (var b = run.Open(CompanyA, Approver))
        {
            Assert.Equal(CommandOutcome.Succeeded, (await b.Accounts.ApproveAsync(b.Context, Key("a-1"),
                new CashAccountApproveRequest(second.Id, "1"), CancellationToken.None)).Outcome);
        }

        await using (var reader = run.Open(CompanyA, "SENTETIK-reader", new ReadOnlyPermissions()))
        {
            var item = Assert.Single((await reader.Accounts.ListAsync(reader.Context, CancellationToken.None)).Resource!.Items);
            Assert.Equal((second.Id, CashAccountStatus.Approved), (item.Account.Id, item.Account.Status));
        }

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccounts"));
        Assert.Equal("SENTETIK gerekçe", await run.ScalarAsync<string>(
            "SELECT RejectionReason FROM finance.CashAccounts WHERE Id = @id", ("@id", first.Id)));
    }

    [SqlServerFact]
    public async Task Company_B_can_neither_see_nor_approve_company_A_cash_account()
    {
        await using var run = await Run.StartAsync();
        CashAccountView accountA;
        await using (var a = run.Open(CompanyA, Proposer))
        {
            accountA = (await a.Accounts.ProposeAsync(a.Context, Key("p-1"),
                new CashAccountProposeRequest("SENTETIK A", Currency), CancellationToken.None)).Resource!;
        }

        await using (var b = run.Open(CompanyB, Approver))
        {
            Assert.Equal(ReadOutcome.NotFound, (await b.Accounts.GetAsync(b.Context, accountA.Id, CancellationToken.None)).Outcome);
            Assert.Empty((await b.Accounts.ListAsync(b.Context, CancellationToken.None)).Resource!.Items);
            var approve = await b.Accounts.ApproveAsync(b.Context, Key("a-1"),
                new CashAccountApproveRequest(accountA.Id, "1"), CancellationToken.None);
            Assert.Equal("cash_account_not_found", Assert.Single(approve.Issues).Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT Status FROM finance.CashAccounts WHERE Id = @id", ("@id", accountA.Id)));
        // A's proposal, and B's not-found as B's key's terminal answer: company B's, with no target.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.CashAccountCommands WHERE CompanyId = @b AND CashAccountId IS NULL", ("@b", CompanyB)));
    }

    [SqlServerFact]
    public async Task The_cash_account_migrations_Down_passes_on_an_empty_store_and_refuses_while_a_cash_account_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddCashAccounts().DownOperations[0]).Sql;

        await run.ExecuteAsync(guard);

        CashAccountView account;
        await using (var a = run.Open(CompanyA, Proposer))
        {
            account = (await a.Accounts.ProposeAsync(a.Context, Key("p-1"),
                new CashAccountProposeRequest("SENTETIK kasa", Currency), CancellationToken.None)).Resource!;
        }

        var error = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51051, error.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccounts WHERE Id = @id", ("@id", account.Id)));
    }

    [SqlServerFact]
    public async Task The_same_key_replays_and_another_payload_or_actor_conflicts()
    {
        await using var run = await Run.StartAsync();
        var request = new CashAccountProposeRequest("SENTETIK kasa", Currency);
        CashAccountView first;
        await using (var a = run.Open(CompanyA, Proposer))
        {
            var stored = await a.Accounts.ProposeAsync(a.Context, Key("k-1"), request, CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, false), (stored.Outcome, stored.IsReplay));
            first = stored.Resource!;

            // ADR-011: the same key and payload again is the stored answer with its FIRST correlation.
            var replay = await a.Accounts.ProposeAsync(a.Context with { CorrelationId = run.Correlation + "-again" },
                Key("k-1"), request, CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, true), (replay.Outcome, replay.IsReplay));
            Assert.Equal((first.Id, "1", run.Correlation),
                (replay.Resource!.Id, replay.Resource.Version, replay.OriginalCorrelationId));

            var otherPayload = await a.Accounts.ProposeAsync(a.Context, Key("k-1"),
                request with { DisplayName = "SENTETIK başka" }, CancellationToken.None);
            Assert.Equal(CommandOutcome.IdempotencyConflict, otherPayload.Outcome);
            Assert.Null(otherPayload.Resource);
            Assert.Empty(otherPayload.Issues);
        }

        await using (var b = run.Open(CompanyA, Approver))
        {
            // ADR-011 §3: the same key and payload by ANOTHER actor is a conflict that discloses nothing.
            var otherActor = await b.Accounts.ProposeAsync(b.Context, Key("k-1"), request, CancellationToken.None);
            Assert.Equal(CommandOutcome.IdempotencyConflict, otherActor.Outcome);
            Assert.Null(otherActor.Resource);
            Assert.Empty(otherActor.Issues);

            // ADR-011 §2: the key belongs to its command. The same key on the approval is that command's
            // own first attempt, neither a replay nor a conflict of the proposal.
            var approved = await b.Accounts.ApproveAsync(b.Context, Key("k-1"),
                new CashAccountApproveRequest(first.Id, "1"), CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, false, "2"), (approved.Outcome, approved.IsReplay, approved.Resource!.Version));

            var replay = await b.Accounts.ApproveAsync(b.Context, Key("k-1"),
                new CashAccountApproveRequest(first.Id, "1"), CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, true, "2"), (replay.Outcome, replay.IsReplay, replay.Resource!.Version));

            var otherPayload = await b.Accounts.ApproveAsync(b.Context, Key("k-1"),
                new CashAccountApproveRequest(first.Id, "2"), CancellationToken.None);
            Assert.Equal(CommandOutcome.IdempotencyConflict, otherPayload.Outcome);
            Assert.Null(otherPayload.Resource);
        }

        await using (var c = run.Open(CompanyA, "SENTETIK-approver-2"))
        {
            var otherActor = await c.Accounts.ApproveAsync(c.Context, Key("k-1"),
                new CashAccountApproveRequest(first.Id, "1"), CancellationToken.None);
            Assert.Equal(CommandOutcome.IdempotencyConflict, otherActor.Outcome);
            Assert.Null(otherActor.Resource);
        }

        await using (var other = run.Open(CompanyB, Proposer))
        {
            // The key belongs to its company too: company B's same key and payload is B's own proposal.
            var own = await other.Accounts.ProposeAsync(other.Context, Key("k-1"), request, CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, false), (own.Outcome, own.IsReplay));
            Assert.NotEqual(first.Id, own.Resource!.Id);
            Assert.Equal(CompanyB, own.Resource.CompanyId);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccounts WHERE CompanyId = @a", ("@a", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccounts WHERE CompanyId = @b", ("@b", CompanyB)));
        // A's proposal and approval, B's proposal: a replay and a conflict store nothing.
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccountCommands"));
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM finance.CashAccounts WHERE Id = @id", ("@id", first.Id)));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.CashAccount'"));
    }

    [SqlServerFact]
    public async Task A_recorded_not_found_or_stale_version_refusal_replays_as_stored_whatever_happened_since()
    {
        await using var run = await Run.StartAsync();
        var missing = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5b77");
        CashAccountView proposed;
        await using (var a = run.Open(CompanyA, Proposer))
        {
            proposed = (await a.Accounts.ProposeAsync(a.Context, Key("p-1"),
                new CashAccountProposeRequest("SENTETIK kasa", Currency), CancellationToken.None)).Resource!;
        }

        await using (var b = run.Open(CompanyA, Approver))
        {
            // ADR-011 §7: not found is a terminal answer of its key, recorded with no target.
            var notFound = await b.Accounts.ApproveAsync(b.Context, Key("a-missing"),
                new CashAccountApproveRequest(missing, "1"), CancellationToken.None);
            Assert.Equal((CommandOutcome.ValidationFailed, false), (notFound.Outcome, notFound.IsReplay));
            Assert.Equal("cash_account_not_found", Assert.Single(notFound.Issues).Code);

            var notFoundAgain = await b.Accounts.ApproveAsync(b.Context with { CorrelationId = run.Correlation + "-again" },
                Key("a-missing"), new CashAccountApproveRequest(missing, "1"), CancellationToken.None);
            Assert.Equal((CommandOutcome.ValidationFailed, true, run.Correlation),
                (notFoundAgain.Outcome, notFoundAgain.IsReplay, notFoundAgain.OriginalCorrelationId));
            Assert.Equal("cash_account_not_found", Assert.Single(notFoundAgain.Issues).Code);

            // A stale version is terminal too, for the approval and for the rejection alike.
            var stale = await b.Accounts.ApproveAsync(b.Context, Key("a-stale"),
                new CashAccountApproveRequest(proposed.Id, "7"), CancellationToken.None);
            Assert.Equal((CommandOutcome.VersionConflict, false), (stale.Outcome, stale.IsReplay));
            Assert.Equal("version_conflict", Assert.Single(stale.Issues).Code);

            var staleReject = await b.Accounts.RejectAsync(b.Context, Key("r-stale"),
                new CashAccountRejectRequest(proposed.Id, "7", "SENTETIK gerekçe"), CancellationToken.None);
            Assert.Equal((CommandOutcome.VersionConflict, false), (staleReject.Outcome, staleReject.IsReplay));
            Assert.Equal("version_conflict", Assert.Single(staleReject.Issues).Code);

            // The refusals changed nothing: the proposal is still decidable at its version.
            Assert.Equal(CommandOutcome.Succeeded, (await b.Accounts.ApproveAsync(b.Context, Key("a-1"),
                new CashAccountApproveRequest(proposed.Id, "1"), CancellationToken.None)).Outcome);

            // The stored answers stay the keys' answers after the account moved on.
            var staleAgain = await b.Accounts.ApproveAsync(b.Context, Key("a-stale"),
                new CashAccountApproveRequest(proposed.Id, "7"), CancellationToken.None);
            Assert.Equal((CommandOutcome.VersionConflict, true), (staleAgain.Outcome, staleAgain.IsReplay));
            Assert.Equal("version_conflict", Assert.Single(staleAgain.Issues).Code);
            Assert.Null(staleAgain.Resource);

            var staleRejectAgain = await b.Accounts.RejectAsync(b.Context, Key("r-stale"),
                new CashAccountRejectRequest(proposed.Id, "7", "SENTETIK gerekçe"), CancellationToken.None);
            Assert.Equal((CommandOutcome.VersionConflict, true), (staleRejectAgain.Outcome, staleRejectAgain.IsReplay));

            // A recorded refusal owns its key like any other record: another payload is a conflict.
            Assert.Equal(CommandOutcome.IdempotencyConflict, (await b.Accounts.ApproveAsync(b.Context, Key("a-stale"),
                new CashAccountApproveRequest(proposed.Id, "2"), CancellationToken.None)).Outcome);
        }

        // The proposal, the not-found, the two stale answers and the approval: five records, each once.
        Assert.Equal(5, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccountCommands"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccountCommands WHERE CashAccountId IS NULL"));
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM finance.CashAccounts WHERE Id = @id", ("@id", proposed.Id)));
        // A terminal refusal is recorded alone: only the proposal and the approval are audited.
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.CashAccount'"));
    }

    [SqlServerFact]
    public async Task A_rejection_is_stored_and_audited_once_and_its_key_replays_or_conflicts()
    {
        await using var run = await Run.StartAsync();
        CashAccountView proposed;
        await using (var a = run.Open(CompanyA, Proposer))
        {
            proposed = (await a.Accounts.ProposeAsync(a.Context, Key("p-1"),
                new CashAccountProposeRequest("SENTETIK kasa", Currency), CancellationToken.None)).Resource!;

            // The proposer cannot reject their own proposal either; nothing is recorded for it.
            var self = await a.Accounts.RejectAsync(a.Context, Key("r-self"),
                new CashAccountRejectRequest(proposed.Id, "1", "SENTETIK gerekçe"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Denied, self.Outcome);
            Assert.Equal("finance_approver_is_proposer", Assert.Single(self.Issues).Code);
        }

        await using (var b = run.Open(CompanyA, Approver))
        {
            var request = new CashAccountRejectRequest(proposed.Id, "1", "SENTETIK gerekçe");
            var rejected = await b.Accounts.RejectAsync(b.Context, Key("r-1"), request, CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, false), (rejected.Outcome, rejected.IsReplay));
            Assert.Equal((CashAccountStatus.Rejected, false, "SENTETIK gerekçe", "2"),
                (rejected.Resource!.Status, rejected.Resource.IsActive, rejected.Resource.RejectionReason, rejected.Resource.Version));

            var replay = await b.Accounts.RejectAsync(b.Context, Key("r-1"), request, CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, true), (replay.Outcome, replay.IsReplay));
            Assert.Equal((CashAccountStatus.Rejected, "2"), (replay.Resource!.Status, replay.Resource.Version));

            Assert.Equal(CommandOutcome.IdempotencyConflict, (await b.Accounts.RejectAsync(b.Context, Key("r-1"),
                request with { Reason = "SENTETIK başka gerekçe" }, CancellationToken.None)).Outcome);

            // One decision only: a rejected proposal is not approvable afterwards.
            var late = await b.Accounts.ApproveAsync(b.Context, Key("a-late"),
                new CashAccountApproveRequest(proposed.Id, "2"), CancellationToken.None);
            Assert.Equal("cash_account_not_proposed", Assert.Single(late.Issues).Code);
        }

        Assert.Equal(3, await run.ScalarAsync<int>("SELECT Status FROM finance.CashAccounts WHERE Id = @id", ("@id", proposed.Id)));
        Assert.Equal(Approver, await run.ScalarAsync<string>("SELECT DecidedBy FROM finance.CashAccounts WHERE Id = @id", ("@id", proposed.Id)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccountCommands"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.cash-account.rejected.v1' AND ApprovalContextJson IS NOT NULL"));
        // The rejection's context names the decider as the one who rejected, never as an approver.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.cash-account.rejected.v1' "
            + "AND JSON_VALUE(ApprovalContextJson, '$.proposedBy') = @proposer AND JSON_VALUE(ApprovalContextJson, '$.rejectedBy') = @approver "
            + "AND JSON_VALUE(ApprovalContextJson, '$.approvedBy') IS NULL",
            ("@proposer", Proposer), ("@approver", Approver)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.cash-account.approved.v1'"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.CashAccount'"));
    }

    [SqlServerFact]
    public async Task Two_racing_approvals_store_one_decision()
    {
        await using var run = await Run.StartAsync();
        CashAccountView proposed;
        await using (var a = run.Open(CompanyA, Proposer))
        {
            proposed = (await a.Accounts.ProposeAsync(a.Context, Key("p-1"),
                new CashAccountProposeRequest("SENTETIK kasa", Currency), CancellationToken.None)).Resource!;
        }

        // Two approvers read the same proposal, then wait for each other at the save.
        var barrier = new Barrier(2);
        async Task<CashAccountCommandResult> ApproveAsync(string approver)
        {
            await using var scope = run.OpenWith(CompanyA, approver, new SyntheticCandidates(), new BarrierInterceptor(barrier));
            return await scope.Accounts.ApproveAsync(scope.Context, Key("a-" + approver),
                new CashAccountApproveRequest(proposed.Id, "1"), CancellationToken.None);
        }

        var results = await Task.WhenAll(Task.Run(() => ApproveAsync(Approver)), Task.Run(() => ApproveAsync("SENTETIK-approver-2")));
        var won = Assert.Single(results, result => result.Outcome == CommandOutcome.Succeeded);
        var lost = Assert.Single(results, result => result.Outcome == CommandOutcome.VersionConflict);
        Assert.Equal((CashAccountStatus.Approved, "2"), (won.Resource!.Status, won.Resource.Version));
        Assert.Equal("concurrent_change", Assert.Single(lost.Issues).Code);
        Assert.Null(lost.Resource);

        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM finance.CashAccounts WHERE Id = @id", ("@id", proposed.Id)));
        // The proposal and the winning approval; the loser's attempt and audit event rolled back.
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccountCommands"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.cash-account.approved.v1'"));
    }

    [SqlServerFact]
    public async Task Two_racing_proposals_under_one_key_store_one_and_the_loser_answers_the_winners_record()
    {
        await using var run = await Run.StartAsync();

        // The same actor, key and payload twice at once: both pass the replay lookup, then meet at the save.
        var barrier = new Barrier(2);
        async Task<CashAccountCommandResult> ProposeAsync()
        {
            await using var scope = run.OpenWith(CompanyA, Proposer, new SyntheticCandidates(), new BarrierInterceptor(barrier));
            return await scope.Accounts.ProposeAsync(scope.Context, Key("p-1"),
                new CashAccountProposeRequest("SENTETIK kasa", Currency), CancellationToken.None);
        }

        var results = await Task.WhenAll(Task.Run(ProposeAsync), Task.Run(ProposeAsync));
        Assert.All(results, result => Assert.Equal(CommandOutcome.Succeeded, result.Outcome));
        var won = Assert.Single(results, result => !result.IsReplay);
        var lost = Assert.Single(results, result => result.IsReplay);
        Assert.Equal(won.Resource!.Id, lost.Resource!.Id);

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccounts"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccountCommands"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.cash-account.proposed.v1'"));
    }

    [SqlServerFact]
    public async Task Two_racing_proposals_of_one_name_store_one_and_the_loser_is_told_the_name_is_taken()
    {
        await using var run = await Run.StartAsync();

        // Two different keys for the same name: both pass the name pre-check, then meet at the save,
        // where the filtered unique index is the authority.
        var barrier = new Barrier(2);
        async Task<CashAccountCommandResult> ProposeAsync(string key)
        {
            await using var scope = run.OpenWith(CompanyA, Proposer, new SyntheticCandidates(), new BarrierInterceptor(barrier));
            return await scope.Accounts.ProposeAsync(scope.Context, Key(key),
                new CashAccountProposeRequest("SENTETIK kasa", Currency), CancellationToken.None);
        }

        var results = await Task.WhenAll(Task.Run(() => ProposeAsync("p-1")), Task.Run(() => ProposeAsync("p-2")));
        var won = Assert.Single(results, result => result.Outcome == CommandOutcome.Succeeded);
        var lost = Assert.Single(results, result => result.Outcome == CommandOutcome.ValidationFailed);
        Assert.False(won.IsReplay);
        Assert.Equal("cash_account_name_taken", Assert.Single(lost.Issues).Code);
        Assert.Null(lost.Resource);

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccounts"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccountCommands"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.cash-account.proposed.v1'"));
    }

    [SqlServerFact]
    public async Task A_currency_outside_the_settings_candidates_or_an_unavailable_source_stores_nothing()
    {
        await using var run = await Run.StartAsync();
        await using (var a = run.Open(CompanyA, Proposer))
        {
            var outside = await a.Accounts.ProposeAsync(a.Context, Key("k-1"),
                new CashAccountProposeRequest("SENTETIK kasa", Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5b02")),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, outside.Outcome);
            Assert.Equal("currency_not_a_candidate", Assert.Single(outside.Issues).Code);
        }

        // Settings' own fail-closed adapter: no currency source, so the proposal is refused.
        await using (var a = run.OpenWithUnavailableSettings(CompanyA, Proposer))
        {
            var unavailable = await a.Accounts.ProposeAsync(a.Context, Key("k-2"),
                new CashAccountProposeRequest("SENTETIK kasa", Currency), CancellationToken.None);
            Assert.Equal(CommandOutcome.Unavailable, unavailable.Outcome);
            Assert.Equal("settings_provider_unavailable", Assert.Single(unavailable.Issues).Code);

            var candidates = await a.Accounts.CurrencyCandidatesAsync(a.Context, CancellationToken.None);
            Assert.Equal(ReadOutcome.Unavailable, candidates.Outcome);
            Assert.Null(candidates.Resource);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccounts"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccountCommands"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.CashAccount'"));
    }

    [SqlServerFact]
    public async Task An_approval_checks_the_currency_again_and_stores_nothing_while_it_is_not_a_candidate()
    {
        await using var run = await Run.StartAsync();
        CashAccountView proposed;
        await using (var a = run.Open(CompanyA, Proposer))
        {
            proposed = (await a.Accounts.ProposeAsync(a.Context, Key("p-1"),
                new CashAccountProposeRequest("SENTETIK kasa", Currency), CancellationToken.None)).Resource!;
        }

        // Between the proposal and the approval the currency left Settings' candidates.
        await using (var b = run.OpenWith(CompanyA, Approver, new OtherCandidates()))
        {
            var refused = await b.Accounts.ApproveAsync(b.Context, Key("a-1"),
                new CashAccountApproveRequest(proposed.Id, "1"), CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
            Assert.Equal("currency_not_a_candidate", Assert.Single(refused.Issues).Code);
        }

        await using (var b = run.OpenWithUnavailableSettings(CompanyA, Approver))
        {
            var unavailable = await b.Accounts.ApproveAsync(b.Context, Key("a-1"),
                new CashAccountApproveRequest(proposed.Id, "1"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Unavailable, unavailable.Outcome);
            Assert.Equal("settings_provider_unavailable", Assert.Single(unavailable.Issues).Code);
        }

        // Neither refusal is terminal: nothing was stored, the proposal is untouched.
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT Status FROM finance.CashAccounts WHERE Id = @id", ("@id", proposed.Id)));
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM finance.CashAccounts WHERE Id = @id", ("@id", proposed.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccountCommands"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.cash-account.approved.v1'"));

        // With the currency a candidate again, the same key is still a first attempt and approves.
        await using (var b = run.Open(CompanyA, Approver))
        {
            var approved = await b.Accounts.ApproveAsync(b.Context, Key("a-1"),
                new CashAccountApproveRequest(proposed.Id, "1"), CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, false, "2"), (approved.Outcome, approved.IsReplay, approved.Resource!.Version));
        }
    }

    [SqlServerFact]
    public async Task Permission_revoked_during_the_settings_read_or_just_before_the_save_refuses_propose_approve_and_reject_and_stores_nothing()
    {
        await using var run = await Run.StartAsync();
        var auditsAtStart = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");

        // Propose, the authority withdrawn WHILE Settings is read: the fence after that read refuses.
        var duringRead = new RevocablePermissions(int.MaxValue);
        await using (var a = run.OpenWithPermissions(CompanyA, Proposer, duringRead, new RevokingCandidates(duringRead)))
        {
            var refused = await a.Accounts.ProposeAsync(a.Context, Key("p-fence"),
                new CashAccountProposeRequest("SENTETIK kasa", Currency), CancellationToken.None);
            Assert.Equal(CommandOutcome.Denied, refused.Outcome);
            Assert.Null(refused.Resource);
        }

        // Propose, the authority withdrawn after that fence: the one immediately before the save refuses.
        var beforeSave = new RevocablePermissions(allowedDecisions: 2);
        await using (var a = run.OpenWithPermissions(CompanyA, Proposer, beforeSave, new SyntheticCandidates()))
        {
            var refused = await a.Accounts.ProposeAsync(a.Context, Key("p-fence"),
                new CashAccountProposeRequest("SENTETIK kasa", Currency), CancellationToken.None);
            Assert.Equal(CommandOutcome.Denied, refused.Outcome);
            Assert.Null(refused.Resource);
            Assert.Equal(3, beforeSave.Decisions);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccounts"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccountCommands"));
        Assert.Equal(auditsAtStart, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));

        // The refused key was never used: with the authority held it is a first attempt.
        CashAccountView proposed;
        await using (var a = run.Open(CompanyA, Proposer))
        {
            var first = await a.Accounts.ProposeAsync(a.Context, Key("p-fence"),
                new CashAccountProposeRequest("SENTETIK kasa", Currency), CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, false), (first.Outcome, first.IsReplay));
            proposed = first.Resource!;
        }

        var auditsAfterProposal = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");

        // Approve, the authority withdrawn while Settings is read.
        duringRead = new RevocablePermissions(int.MaxValue);
        await using (var b = run.OpenWithPermissions(CompanyA, Approver, duringRead, new RevokingCandidates(duringRead)))
        {
            Assert.Equal(CommandOutcome.Denied, (await b.Accounts.ApproveAsync(b.Context, Key("a-fence"),
                new CashAccountApproveRequest(proposed.Id, "1"), CancellationToken.None)).Outcome);
        }

        // Approve, the authority withdrawn after that fence and before the save.
        beforeSave = new RevocablePermissions(allowedDecisions: 2);
        await using (var b = run.OpenWithPermissions(CompanyA, Approver, beforeSave, new SyntheticCandidates()))
        {
            Assert.Equal(CommandOutcome.Denied, (await b.Accounts.ApproveAsync(b.Context, Key("a-fence"),
                new CashAccountApproveRequest(proposed.Id, "1"), CancellationToken.None)).Outcome);
            Assert.Equal(3, beforeSave.Decisions);
        }

        // Reject reads no Settings: the authority withdrawn after the first decision, before the save.
        beforeSave = new RevocablePermissions(allowedDecisions: 1);
        await using (var b = run.OpenWithPermissions(CompanyA, Approver, beforeSave, new SyntheticCandidates()))
        {
            Assert.Equal(CommandOutcome.Denied, (await b.Accounts.RejectAsync(b.Context, Key("r-fence"),
                new CashAccountRejectRequest(proposed.Id, "1", "SENTETIK revoke"), CancellationToken.None)).Outcome);
            Assert.Equal(2, beforeSave.Decisions);
        }

        // No decision reached the store: the proposal, its one command record and its one audit event.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.CashAccounts WHERE Id = @id AND Status = 1 AND Version = 1 AND DecidedBy IS NULL "
            + "AND DecidedAtUtc IS NULL AND RejectionReason IS NULL", ("@id", proposed.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccounts"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccountCommands"));
        Assert.Equal(auditsAfterProposal, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));

        // The refused approval key is a first attempt too once the authority is held.
        await using (var b = run.Open(CompanyA, Approver))
        {
            var approved = await b.Accounts.ApproveAsync(b.Context, Key("a-fence"),
                new CashAccountApproveRequest(proposed.Id, "1"), CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, false, "2"), (approved.Outcome, approved.IsReplay, approved.Resource!.Version));
        }
    }

    [SqlServerFact]
    public async Task An_active_cash_account_is_passivated_and_reactivated_by_two_people_stays_readable_and_keeps_its_name()
    {
        await using var run = await Run.StartAsync();
        var account = await ApprovedAsync(run, "SENTETIK kasa");

        // A passivation proposal leaves the account active; its proposer cannot decide it; a rejection keeps it active.
        await using (var a = run.Open(CompanyA, Proposer))
        {
            var proposed = await a.Accounts.ProposeStateChangeAsync(a.Context, Key("pp-1"), CashAccountStateChange.Passivation,
                new CashAccountStateChangeRequest(account.Id, "2"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, proposed.Outcome);
            Assert.Equal((CashAccountStatus.Approved, true, (CashAccountStateChange?)CashAccountStateChange.Passivation, "3"),
                (proposed.Resource!.Status, proposed.Resource.IsActive, proposed.Resource.PendingChange, proposed.Resource.Version));

            var self = await a.Accounts.ApproveStateChangeAsync(a.Context, Key("pa-self"), CashAccountStateChange.Passivation,
                new CashAccountStateChangeRequest(account.Id, "3"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Denied, self.Outcome);
            Assert.Equal("finance_approver_is_proposer", Assert.Single(self.Issues).Code);
        }

        await using (var b = run.Open(CompanyA, Approver))
        {
            var rejected = await b.Accounts.RejectStateChangeAsync(b.Context, Key("pr-1"), CashAccountStateChange.Passivation,
                new CashAccountStateChangeRejectRequest(account.Id, "3", "SENTETIK gerekçe"), CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, CashAccountStatus.Approved, true, (CashAccountStateChange?)null, "4"),
                (rejected.Outcome, rejected.Resource!.Status, rejected.Resource.IsActive, rejected.Resource.PendingChange,
                    rejected.Resource.Version));
        }

        await using (var a = run.Open(CompanyA, Proposer))
        {
            Assert.Equal(CommandOutcome.Succeeded, (await a.Accounts.ProposeStateChangeAsync(a.Context, Key("pp-2"),
                CashAccountStateChange.Passivation, new CashAccountStateChangeRequest(account.Id, "4"), CancellationToken.None)).Outcome);
        }

        await using (var b = run.Open(CompanyA, Approver))
        {
            var passive = await b.Accounts.ApproveStateChangeAsync(b.Context, Key("pa-1"), CashAccountStateChange.Passivation,
                new CashAccountStateChangeRequest(account.Id, "5"), CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, CashAccountStatus.Passive, false, (CashAccountStateChange?)null, "6"),
                (passive.Outcome, passive.Resource!.Status, passive.Resource.IsActive, passive.Resource.PendingChange,
                    passive.Resource.Version));
        }

        // Passive: still the company's account, readable with its state by a read-only caller, and its name stays taken.
        await using (var reader = run.Open(CompanyA, "SENTETIK-reader", new ReadOnlyPermissions()))
        {
            var item = Assert.Single((await reader.Accounts.ListAsync(reader.Context, CancellationToken.None)).Resource!.Items);
            Assert.Equal((account.Id, CashAccountStatus.Passive, false), (item.Account.Id, item.Account.Status, item.Account.IsActive));
            var read = await reader.Accounts.GetAsync(reader.Context, account.Id, CancellationToken.None);
            Assert.Equal((ReadOutcome.Found, CashAccountStatus.Passive), (read.Outcome, read.Resource!.Status));
        }

        await using (var a = run.Open(CompanyA, Proposer))
        {
            var taken = await a.Accounts.ProposeAsync(a.Context, Key("p-again"),
                new CashAccountProposeRequest("SENTETIK kasa", Currency), CancellationToken.None);
            Assert.Equal("cash_account_name_taken", Assert.Single(taken.Issues).Code);
        }

        // Reactivation: the same two-person flow the other way round.
        await using (var b = run.Open(CompanyA, Approver))
        {
            var proposed = await b.Accounts.ProposeStateChangeAsync(b.Context, Key("rp-1"), CashAccountStateChange.Reactivation,
                new CashAccountStateChangeRequest(account.Id, "6"), CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, CashAccountStatus.Passive, (CashAccountStateChange?)CashAccountStateChange.Reactivation),
                (proposed.Outcome, proposed.Resource!.Status, proposed.Resource.PendingChange));
            var self = await b.Accounts.ApproveStateChangeAsync(b.Context, Key("ra-self"), CashAccountStateChange.Reactivation,
                new CashAccountStateChangeRequest(account.Id, "7"), CancellationToken.None);
            Assert.Equal("finance_approver_is_proposer", Assert.Single(self.Issues).Code);
        }

        await using (var a = run.Open(CompanyA, Proposer))
        {
            var active = await a.Accounts.ApproveStateChangeAsync(a.Context, Key("ra-1"), CashAccountStateChange.Reactivation,
                new CashAccountStateChangeRequest(account.Id, "7"), CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, CashAccountStatus.Approved, true, "8"),
                (active.Outcome, active.Resource!.Status, active.Resource.IsActive, active.Resource.Version));
        }

        // The row keeps its opening decision and only the latest change, decided by someone other than its proposer.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.CashAccounts WHERE Id = @id AND Status = 2 AND Version = 8 AND DecidedBy = @approver "
            + "AND ChangeKind = 2 AND ChangeStatus = 2 AND ChangeProposedBy = @approver AND ChangeDecidedBy = @proposer "
            + "AND ChangeRejectionReason IS NULL",
            ("@id", account.Id), ("@approver", Approver), ("@proposer", Proposer)));
        // Two opening and six state-change records; the self-decisions and the taken name store none.
        Assert.Equal(8, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccountCommands"));
        Assert.Equal(6, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.CashAccountCommands WHERE CommandName LIKE N'finance.cash-account.passivation.%' "
            + "OR CommandName LIKE N'finance.cash-account.reactivation.%'"));
        Assert.Equal(8, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.CashAccount' AND CorrelationId = @c", ("@c", run.Correlation)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.cash-account.passivation-rejected.v1' "
            + "AND PermissionCode = N'finance.cash-account.passivation.approve' "
            + "AND JSON_VALUE(ApprovalContextJson, '$.proposedBy') = @proposer AND JSON_VALUE(ApprovalContextJson, '$.rejectedBy') = @approver "
            + "AND JSON_VALUE(ApprovalContextJson, '$.approvedBy') IS NULL",
            ("@proposer", Proposer), ("@approver", Approver)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.cash-account.passivation-approved.v1' "
            + "AND PermissionCode = N'finance.cash-account.passivation.approve' "
            + "AND JSON_VALUE(ApprovalContextJson, '$.proposedBy') = @proposer AND JSON_VALUE(ApprovalContextJson, '$.approvedBy') = @approver",
            ("@proposer", Proposer), ("@approver", Approver)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.cash-account.reactivation-approved.v1' "
            + "AND PermissionCode = N'finance.cash-account.reactivation.approve' "
            + "AND JSON_VALUE(ApprovalContextJson, '$.proposedBy') = @approver AND JSON_VALUE(ApprovalContextJson, '$.approvedBy') = @proposer",
            ("@proposer", Proposer), ("@approver", Approver)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.cash-account.passivation-proposed.v1' "
            + "AND PermissionCode = N'finance.cash-account.passivation.propose' AND ApprovalContextJson IS NULL"));
        // A state change moves no money.
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
    }

    [SqlServerFact]
    public async Task A_passivation_is_refused_unless_the_balance_reads_zero_at_the_proposal_and_at_the_approval()
    {
        await using var run = await Run.StartAsync();
        var account = await ApprovedAsync(run, "SENTETIK kasa");

        foreach (var (state, outcome, code) in new[]
        {
            (CashAccountBalanceState.NotZero, CommandOutcome.ValidationFailed, "cash_account_balance_not_zero"),
            (CashAccountBalanceState.Unavailable, CommandOutcome.Unavailable, "cash_account_balance_unavailable"),
        })
        {
            var balance = new FixedBalance(state);
            await using var a = run.OpenWithBalance(CompanyA, Proposer, balance);
            var refused = await a.Accounts.ProposeStateChangeAsync(a.Context, Key("pp-" + code), CashAccountStateChange.Passivation,
                new CashAccountStateChangeRequest(account.Id, "2"), CancellationToken.None);
            Assert.Equal((outcome, code), (refused.Outcome, Assert.Single(refused.Issues).Code));
            Assert.Null(refused.Resource);
            Assert.Equal((CompanyA, account.Id), Assert.Single(balance.Seen));
        }

        await using (var a = run.Open(CompanyA, Proposer))
        {
            Assert.Equal(CommandOutcome.Succeeded, (await a.Accounts.ProposeStateChangeAsync(a.Context, Key("pp-1"),
                CashAccountStateChange.Passivation, new CashAccountStateChangeRequest(account.Id, "2"), CancellationToken.None)).Outcome);
        }

        foreach (var (state, outcome, code) in new[]
        {
            (CashAccountBalanceState.NotZero, CommandOutcome.ValidationFailed, "cash_account_balance_not_zero"),
            (CashAccountBalanceState.Unavailable, CommandOutcome.Unavailable, "cash_account_balance_unavailable"),
        })
        {
            var balance = new FixedBalance(state);
            await using var b = run.OpenWithBalance(CompanyA, Approver, balance);
            var refused = await b.Accounts.ApproveStateChangeAsync(b.Context, Key("pa-" + code), CashAccountStateChange.Passivation,
                new CashAccountStateChangeRequest(account.Id, "3"), CancellationToken.None);
            Assert.Equal((outcome, code), (refused.Outcome, Assert.Single(refused.Issues).Code));
            Assert.Null(refused.Resource);
            Assert.Equal((CompanyA, account.Id), Assert.Single(balance.Seen));
        }

        // Nothing of the four refusals reached the store: the account is active with its pending proposal.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.CashAccounts WHERE Id = @id AND Status = 2 AND Version = 3 AND ChangeKind = 1 "
            + "AND ChangeStatus = 1 AND ChangeDecidedBy IS NULL", ("@id", account.Id)));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccountCommands"));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.CashAccount'"));

        // A zero reading lets the approval through.
        var zero = new FixedBalance(CashAccountBalanceState.Zero);
        await using (var b = run.OpenWithBalance(CompanyA, Approver, zero))
        {
            var passive = await b.Accounts.ApproveStateChangeAsync(b.Context, Key("pa-1"), CashAccountStateChange.Passivation,
                new CashAccountStateChangeRequest(account.Id, "3"), CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, CashAccountStatus.Passive), (passive.Outcome, passive.Resource!.Status));
        }

        Assert.Equal((CompanyA, account.Id), Assert.Single(zero.Seen));
    }

    [SqlServerFact]
    public async Task The_database_refuses_a_passive_account_without_a_change_a_self_decided_change_and_a_half_written_change()
    {
        await using var run = await Run.StartAsync();

        // Without the service: every refusal names the state-change check (547); the opening decision is legal throughout.
        foreach (var (name, status, kind, changeStatus, proposer, decider, reason) in new[]
        {
            ("SENTETIK passive-without-change", "4", "NULL", "NULL", "NULL", "NULL", "NULL"),
            ("SENTETIK self-decided", "4", "1", "2", "N'z'", "N'z'", "NULL"),
            ("SENTETIK self-rejected", "2", "1", "3", "N'z'", "N'z'", "N'SENTETIK'"),
            ("SENTETIK kind-without-state", "2", "1", "NULL", "N'z'", "NULL", "NULL"),
            ("SENTETIK state-without-kind", "2", "NULL", "1", "N'z'", "NULL", "NULL"),
            ("SENTETIK pending-without-proposer", "2", "1", "1", "NULL", "NULL", "NULL"),
            ("SENTETIK pending-in-wrong-state", "4", "1", "1", "N'z'", "NULL", "NULL"),
            ("SENTETIK approved-in-wrong-state", "2", "1", "2", "N'z'", "N'w'", "NULL"),
            ("SENTETIK rejected-without-reason", "2", "1", "3", "N'z'", "N'w'", "NULL"),
            ("SENTETIK unknown-kind", "2", "3", "1", "N'z'", "NULL", "NULL"),
        })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
                RawAccount(name, status, kind, changeStatus, proposer, decider, reason), ("@c", CompanyA), ("@cur", Currency)));
            Assert.Equal(547, error.Number);
            Assert.Contains("CK_CashAccounts_StateChange", error.Message, StringComparison.Ordinal);
        }

        // The legal shapes pass: passive by another's decision, a pending reactivation, a rejected passivation.
        await run.ExecuteAsync(RawAccount("SENTETIK passive", "4", "1", "2", "N'z'", "N'w'", "NULL"), ("@c", CompanyA), ("@cur", Currency));
        await run.ExecuteAsync(RawAccount("SENTETIK reactivating", "4", "2", "1", "N'z'", "NULL", "NULL"), ("@c", CompanyA), ("@cur", Currency));
        await run.ExecuteAsync(RawAccount("SENTETIK kept", "2", "1", "3", "N'z'", "N'w'", "N'SENTETIK'"), ("@c", CompanyA), ("@cur", Currency));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccounts"));

        // A passive account keeps its name: a second live row of it is refused (2601).
        var duplicate = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            RawAccount("SENTETIK passive", "2", "NULL", "NULL", "NULL", "NULL", "NULL"), ("@c", CompanyA), ("@cur", Currency)));
        Assert.Equal(2601, duplicate.Number);
        Assert.Contains("UX_CashAccounts_LiveName", duplicate.Message, StringComparison.Ordinal);
    }

    [SqlServerFact]
    public async Task Company_B_can_neither_passivate_nor_decide_a_change_of_company_A_cash_account()
    {
        await using var run = await Run.StartAsync();
        var account = await ApprovedAsync(run, "SENTETIK A");

        await using (var b = run.Open(CompanyB, Proposer))
        {
            var propose = await b.Accounts.ProposeStateChangeAsync(b.Context, Key("pp-b"), CashAccountStateChange.Passivation,
                new CashAccountStateChangeRequest(account.Id, "2"), CancellationToken.None);
            Assert.Equal((CommandOutcome.ValidationFailed, "cash_account_not_found"), (propose.Outcome, Assert.Single(propose.Issues).Code));
        }

        await using (var a = run.Open(CompanyA, Proposer))
        {
            Assert.Equal(CommandOutcome.Succeeded, (await a.Accounts.ProposeStateChangeAsync(a.Context, Key("pp-1"),
                CashAccountStateChange.Passivation, new CashAccountStateChangeRequest(account.Id, "2"), CancellationToken.None)).Outcome);
        }

        await using (var b = run.Open(CompanyB, Approver))
        {
            var approve = await b.Accounts.ApproveStateChangeAsync(b.Context, Key("pa-b"), CashAccountStateChange.Passivation,
                new CashAccountStateChangeRequest(account.Id, "3"), CancellationToken.None);
            Assert.Equal("cash_account_not_found", Assert.Single(approve.Issues).Code);
            var reject = await b.Accounts.RejectStateChangeAsync(b.Context, Key("pr-b"), CashAccountStateChange.Passivation,
                new CashAccountStateChangeRejectRequest(account.Id, "3", "SENTETIK gerekçe"), CancellationToken.None);
            Assert.Equal("cash_account_not_found", Assert.Single(reject.Issues).Code);
            Assert.Empty((await b.Accounts.ListAsync(b.Context, CancellationToken.None)).Resource!.Items);
        }

        // A's account is untouched by B: active, with A's own pending proposal.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.CashAccounts WHERE Id = @id AND Status = 2 AND Version = 3 AND ChangeStatus = 1 "
            + "AND ChangeProposedBy = @proposer", ("@id", account.Id), ("@proposer", Proposer)));
        // B's three not-found answers are B's keys' terminal records, with no target.
        Assert.Equal(3, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.CashAccountCommands WHERE CompanyId = @b AND CashAccountId IS NULL", ("@b", CompanyB)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.CashAccount' AND CompanyId = @b", ("@b", CompanyB)));
    }

    [SqlServerFact]
    public async Task A_state_change_replays_by_its_key_and_a_stale_version_is_a_recorded_refusal()
    {
        await using var run = await Run.StartAsync();
        var account = await ApprovedAsync(run, "SENTETIK kasa");
        var request = new CashAccountStateChangeRequest(account.Id, "2");

        await using (var a = run.Open(CompanyA, Proposer))
        {
            var first = await a.Accounts.ProposeStateChangeAsync(a.Context, Key("pp-1"), CashAccountStateChange.Passivation,
                request, CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, false, "3"), (first.Outcome, first.IsReplay, first.Resource!.Version));

            var replay = await a.Accounts.ProposeStateChangeAsync(a.Context with { CorrelationId = run.Correlation + "-again" },
                Key("pp-1"), CashAccountStateChange.Passivation, request, CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, true, "3", run.Correlation),
                (replay.Outcome, replay.IsReplay, replay.Resource!.Version, replay.OriginalCorrelationId));

            // The key belongs to its command: the same key on a reactivation is that command's own first attempt.
            var otherCommand = await a.Accounts.ProposeStateChangeAsync(a.Context, Key("pp-1"), CashAccountStateChange.Reactivation,
                request, CancellationToken.None);
            Assert.Equal((CommandOutcome.VersionConflict, false), (otherCommand.Outcome, otherCommand.IsReplay));

            var otherPayload = await a.Accounts.ProposeStateChangeAsync(a.Context, Key("pp-1"), CashAccountStateChange.Passivation,
                request with { ExpectedVersion = "3" }, CancellationToken.None);
            Assert.Equal(CommandOutcome.IdempotencyConflict, otherPayload.Outcome);
            Assert.Null(otherPayload.Resource);
        }

        await using (var b = run.Open(CompanyA, Approver))
        {
            var stale = await b.Accounts.ApproveStateChangeAsync(b.Context, Key("pa-stale"), CashAccountStateChange.Passivation,
                new CashAccountStateChangeRequest(account.Id, "2"), CancellationToken.None);
            Assert.Equal((CommandOutcome.VersionConflict, false, "version_conflict"),
                (stale.Outcome, stale.IsReplay, Assert.Single(stale.Issues).Code));

            Assert.Equal(CommandOutcome.Succeeded, (await b.Accounts.ApproveStateChangeAsync(b.Context, Key("pa-1"),
                CashAccountStateChange.Passivation, new CashAccountStateChangeRequest(account.Id, "3"), CancellationToken.None)).Outcome);

            // The recorded stale answer stays its key's answer after the account moved on.
            var staleAgain = await b.Accounts.ApproveStateChangeAsync(b.Context, Key("pa-stale"), CashAccountStateChange.Passivation,
                new CashAccountStateChangeRequest(account.Id, "2"), CancellationToken.None);
            Assert.Equal((CommandOutcome.VersionConflict, true), (staleAgain.Outcome, staleAgain.IsReplay));
            var approvedAgain = await b.Accounts.ApproveStateChangeAsync(b.Context, Key("pa-1"), CashAccountStateChange.Passivation,
                new CashAccountStateChangeRequest(account.Id, "3"), CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, true, CashAccountStatus.Passive, "4"),
                (approvedAgain.Outcome, approvedAgain.IsReplay, approvedAgain.Resource!.Status, approvedAgain.Resource.Version));
        }

        Assert.Equal(4L, await run.ScalarAsync<long>("SELECT Version FROM finance.CashAccounts WHERE Id = @id", ("@id", account.Id)));
        // Opening two; the passivation proposal, the reactivation's stale answer, the stale approval and the approval.
        Assert.Equal(6, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccountCommands"));
        Assert.Equal(4, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.CashAccount'"));
    }

    [SqlServerFact]
    public async Task Permission_revoked_just_before_the_save_refuses_every_passivation_and_reactivation_step_and_stores_nothing()
    {
        await using var run = await Run.StartAsync();
        var account = await ApprovedAsync(run, "SENTETIK kasa");
        var auditsAtStart = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");

        // Passivation reads the balance: the authority holds at the start and after the reading, then is withdrawn.
        var beforeSave = new RevocablePermissions(allowedDecisions: 2);
        await using (var a = run.OpenWithPermissions(CompanyA, Proposer, beforeSave, new SyntheticCandidates()))
        {
            var refused = await a.Accounts.ProposeStateChangeAsync(a.Context, Key("pp-fence"), CashAccountStateChange.Passivation,
                new CashAccountStateChangeRequest(account.Id, "2"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Denied, refused.Outcome);
            Assert.Null(refused.Resource);
            Assert.Equal(3, beforeSave.Decisions);
        }

        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM finance.CashAccounts WHERE Id = @id", ("@id", account.Id)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccountCommands"));
        Assert.Equal(auditsAtStart, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));

        await using (var a = run.Open(CompanyA, Proposer))
        {
            var first = await a.Accounts.ProposeStateChangeAsync(a.Context, Key("pp-fence"), CashAccountStateChange.Passivation,
                new CashAccountStateChangeRequest(account.Id, "2"), CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, false, "3"), (first.Outcome, first.IsReplay, first.Resource!.Version));
        }

        var auditsAfterProposal = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        beforeSave = new RevocablePermissions(allowedDecisions: 2);
        await using (var b = run.OpenWithPermissions(CompanyA, Approver, beforeSave, new SyntheticCandidates()))
        {
            Assert.Equal(CommandOutcome.Denied, (await b.Accounts.ApproveStateChangeAsync(b.Context, Key("pa-fence"),
                CashAccountStateChange.Passivation, new CashAccountStateChangeRequest(account.Id, "3"), CancellationToken.None)).Outcome);
            Assert.Equal(3, beforeSave.Decisions);
        }

        // A rejection reads no balance: the authority withdrawn after the first decision.
        beforeSave = new RevocablePermissions(allowedDecisions: 1);
        await using (var b = run.OpenWithPermissions(CompanyA, Approver, beforeSave, new SyntheticCandidates()))
        {
            Assert.Equal(CommandOutcome.Denied, (await b.Accounts.RejectStateChangeAsync(b.Context, Key("pr-fence"),
                CashAccountStateChange.Passivation, new CashAccountStateChangeRejectRequest(account.Id, "3", "SENTETIK revoke"),
                CancellationToken.None)).Outcome);
            Assert.Equal(2, beforeSave.Decisions);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.CashAccounts WHERE Id = @id AND Status = 2 AND Version = 3 AND ChangeStatus = 1 "
            + "AND ChangeDecidedBy IS NULL AND ChangeDecidedAtUtc IS NULL AND ChangeRejectionReason IS NULL", ("@id", account.Id)));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccountCommands"));
        Assert.Equal(auditsAfterProposal, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));

        await using (var b = run.Open(CompanyA, Approver))
        {
            var approved = await b.Accounts.ApproveStateChangeAsync(b.Context, Key("pa-fence"), CashAccountStateChange.Passivation,
                new CashAccountStateChangeRequest(account.Id, "3"), CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, false, "4"), (approved.Outcome, approved.IsReplay, approved.Resource!.Version));
        }

        // Reactivation reads no balance: each step fenced at its start and immediately before its save.
        beforeSave = new RevocablePermissions(allowedDecisions: 1);
        await using (var b = run.OpenWithPermissions(CompanyA, Approver, beforeSave, new SyntheticCandidates()))
        {
            Assert.Equal(CommandOutcome.Denied, (await b.Accounts.ProposeStateChangeAsync(b.Context, Key("rp-fence"),
                CashAccountStateChange.Reactivation, new CashAccountStateChangeRequest(account.Id, "4"), CancellationToken.None)).Outcome);
            Assert.Equal(2, beforeSave.Decisions);
        }

        await using (var b = run.Open(CompanyA, Approver))
        {
            Assert.Equal(CommandOutcome.Succeeded, (await b.Accounts.ProposeStateChangeAsync(b.Context, Key("rp-fence"),
                CashAccountStateChange.Reactivation, new CashAccountStateChangeRequest(account.Id, "4"), CancellationToken.None)).Outcome);
        }

        var auditsBeforeDecision = await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents");
        foreach (var reject in new[] { false, true })
        {
            beforeSave = new RevocablePermissions(allowedDecisions: 1);
            await using var a = run.OpenWithPermissions(CompanyA, Proposer, beforeSave, new SyntheticCandidates());
            var refused = reject
                ? await a.Accounts.RejectStateChangeAsync(a.Context, Key("rr-fence"), CashAccountStateChange.Reactivation,
                    new CashAccountStateChangeRejectRequest(account.Id, "5", "SENTETIK revoke"), CancellationToken.None)
                : await a.Accounts.ApproveStateChangeAsync(a.Context, Key("ra-fence"), CashAccountStateChange.Reactivation,
                    new CashAccountStateChangeRequest(account.Id, "5"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Denied, refused.Outcome);
            Assert.Equal(2, beforeSave.Decisions);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.CashAccounts WHERE Id = @id AND Status = 4 AND Version = 5 AND ChangeKind = 2 "
            + "AND ChangeStatus = 1 AND ChangeDecidedBy IS NULL", ("@id", account.Id)));
        Assert.Equal(5, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccountCommands"));
        Assert.Equal(auditsBeforeDecision, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"));

        await using (var a = run.Open(CompanyA, Proposer))
        {
            var active = await a.Accounts.ApproveStateChangeAsync(a.Context, Key("ra-fence"), CashAccountStateChange.Reactivation,
                new CashAccountStateChangeRequest(account.Id, "5"), CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, false, CashAccountStatus.Approved, "6"),
                (active.Outcome, active.IsReplay, active.Resource!.Status, active.Resource.Version));
        }
    }

    [SqlServerFact]
    public async Task Two_racing_decisions_on_one_passivation_store_one()
    {
        await using var run = await Run.StartAsync();
        var account = await ApprovedAsync(run, "SENTETIK kasa");
        await using (var a = run.Open(CompanyA, Proposer))
        {
            Assert.Equal(CommandOutcome.Succeeded, (await a.Accounts.ProposeStateChangeAsync(a.Context, Key("pp-1"),
                CashAccountStateChange.Passivation, new CashAccountStateChangeRequest(account.Id, "2"), CancellationToken.None)).Outcome);
        }

        // An approval and a rejection by two deciders read the same pending change, then wait for each other at the save.
        var barrier = new Barrier(2);
        async Task<CashAccountCommandResult> DecideAsync(string decider, bool approve)
        {
            await using var scope = run.OpenWith(CompanyA, decider, new SyntheticCandidates(), new BarrierInterceptor(barrier));
            return approve
                ? await scope.Accounts.ApproveStateChangeAsync(scope.Context, Key("pa-" + decider), CashAccountStateChange.Passivation,
                    new CashAccountStateChangeRequest(account.Id, "3"), CancellationToken.None)
                : await scope.Accounts.RejectStateChangeAsync(scope.Context, Key("pr-" + decider), CashAccountStateChange.Passivation,
                    new CashAccountStateChangeRejectRequest(account.Id, "3", "SENTETIK gerekçe"), CancellationToken.None);
        }

        var results = await Task.WhenAll(Task.Run(() => DecideAsync(Approver, approve: true)),
            Task.Run(() => DecideAsync("SENTETIK-approver-2", approve: false)));
        var won = Assert.Single(results, result => result.Outcome == CommandOutcome.Succeeded);
        var lost = Assert.Single(results, result => result.Outcome == CommandOutcome.VersionConflict);
        Assert.Equal("4", won.Resource!.Version);
        Assert.Equal("concurrent_change", Assert.Single(lost.Issues).Code);
        Assert.Null(lost.Resource);

        Assert.Equal(4L, await run.ScalarAsync<long>("SELECT Version FROM finance.CashAccounts WHERE Id = @id", ("@id", account.Id)));
        // Opening two, the proposal and the winning decision; the loser's attempt and audit event rolled back.
        Assert.Equal(4, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CashAccountCommands"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType IN (N'finance.cash-account.passivation-approved.v1', "
            + "N'finance.cash-account.passivation-rejected.v1')"));
    }

    [SqlServerFact]
    public async Task The_state_change_migrations_Down_passes_while_accounts_kept_their_opening_decision_and_refuses_after_a_change()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddCashAccountStateChanges().DownOperations[0]).Sql;

        await run.ExecuteAsync(guard);
        var account = await ApprovedAsync(run, "SENTETIK kasa");
        await run.ExecuteAsync(guard);

        await using (var a = run.Open(CompanyA, Proposer))
        {
            Assert.Equal(CommandOutcome.Succeeded, (await a.Accounts.ProposeStateChangeAsync(a.Context, Key("pp-1"),
                CashAccountStateChange.Passivation, new CashAccountStateChangeRequest(account.Id, "2"), CancellationToken.None)).Outcome);
        }

        var pending = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51051, pending.Number);

        // A rejected change is still a change the old schema cannot hold.
        await using (var b = run.Open(CompanyA, Approver))
        {
            Assert.Equal(CommandOutcome.Succeeded, (await b.Accounts.RejectStateChangeAsync(b.Context, Key("pr-1"),
                CashAccountStateChange.Passivation, new CashAccountStateChangeRejectRequest(account.Id, "3", "SENTETIK gerekçe"),
                CancellationToken.None)).Outcome);
        }

        var rejected = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51051, rejected.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.CashAccounts WHERE Id = @id AND Status = 2 AND ChangeStatus = 3", ("@id", account.Id)));
    }

    /// <summary>One SENTETIK account proposed by <c>Proposer</c> and approved by <c>Approver</c>: active at version 2.</summary>
    private static async Task<CashAccountView> ApprovedAsync(Run run, string name)
    {
        CashAccountView proposed;
        await using (var a = run.Open(CompanyA, Proposer))
        {
            proposed = (await a.Accounts.ProposeAsync(a.Context, Key("open-p"),
                new CashAccountProposeRequest(name, Currency), CancellationToken.None)).Resource!;
        }

        await using var b = run.Open(CompanyA, Approver);
        var approved = await b.Accounts.ApproveAsync(b.Context, Key("open-a"),
            new CashAccountApproveRequest(proposed.Id, "1"), CancellationToken.None);
        Assert.Equal((CommandOutcome.Succeeded, "2"), (approved.Outcome, approved.Resource!.Version));
        return approved.Resource;
    }

    /// <summary>A raw SENTETIK row past the service: an approved opening decision with the given change columns (SQL literals).</summary>
    private static string RawAccount(string name, string status, string kind, string changeStatus, string proposer,
        string decider, string reason) =>
        "INSERT INTO finance.CashAccounts (Id, CompanyId, CurrencyId, DisplayName, Status, Version, ProposedBy, ProposedAtUtc, "
        + "DecidedBy, DecidedAtUtc, UpdatedAtUtc, ChangeKind, ChangeStatus, ChangeProposedBy, ChangeProposedAtUtc, "
        + "ChangeDecidedBy, ChangeDecidedAtUtc, ChangeRejectionReason) VALUES (NEWID(), @c, @cur, N'" + name + "', "
        + status + ", 3, N'x', SYSDATETIMEOFFSET(), N'y', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), "
        + kind + ", " + changeStatus + ", " + proposer + ", " + (proposer == "NULL" ? "NULL" : "SYSDATETIMEOFFSET()") + ", "
        + decider + ", " + (decider == "NULL" ? "NULL" : "SYSDATETIMEOFFSET()") + ", " + reason + ")";

    private static CommandMetadata Key(string key) => new CommandMetadata(key);

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }
        public string Correlation { get; } = $"api30-{Guid.NewGuid():N}";

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            // The host migrates and seeds Organization, Audit and the rest; Finance's own migrations
            // run after it, as their foreign keys need Organization's tables.
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            var companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            await using var context = new FinanceDbContext(Options(factory), companyContext);
            await context.Database.MigrateAsync();
            return run;
        }

        public Scope Open(Guid company, string actor) => Open(company, actor, new SyntheticPermissions());

        public Scope Open(Guid company, string actor, IFinancePermissionPort permissions) =>
            Open(company, actor, permissions, new SyntheticCandidates(), []);

        /// <summary>The same scope over another SENTETIK candidate source and extra save interceptors.</summary>
        public Scope OpenWith(Guid company, string actor, ISettingsMoneyCandidateReader currencies,
            params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] extra) =>
            Open(company, actor, new SyntheticPermissions(), currencies, extra);

        /// <summary>The same scope over another SENTETIK authority and candidate source.</summary>
        public Scope OpenWithPermissions(Guid company, string actor, IFinancePermissionPort permissions,
            ISettingsMoneyCandidateReader currencies) => Open(company, actor, permissions, currencies, []);

        /// <summary>The same scope over Settings' own fail-closed adapter, which has no currency source.</summary>
        public Scope OpenWithUnavailableSettings(Guid company, string actor)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return Open(company, actor, new SyntheticPermissions(),
                new UnavailableSettingsPrerequisiteProvider(companyContext, new FixedCurrentUser(actor, company)), []);
        }

        /// <summary>The same scope over a SENTETIK balance reading instead of the registered no-movement one.</summary>
        public Scope OpenWithBalance(Guid company, string actor, IFinanceCashAccountBalanceReader balances) =>
            Open(company, actor, new SyntheticPermissions(), new SyntheticCandidates(), [], balances);

        private static FinanceAuditEnvelope Envelope(string permission) => new(permission, ["SENTETIK-FINANCE"], "Web");

        private Scope Open(Guid company, string actor, IFinancePermissionPort permissions,
            ISettingsMoneyCandidateReader currencies, Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] extra,
            IFinanceCashAccountBalanceReader? balances = null)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var currentUser = new FixedCurrentUser(actor, company);
            var collector = new AuditEventCollector();
            var interceptors = new List<Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor>
            {
                new AuditSaveChangesInterceptor(collector, companyContext),
            };
            interceptors.AddRange(extra);
            var options = new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .AddInterceptors(interceptors)
                .Options;
            var dbContext = new FinanceDbContext(options, companyContext);
            var accounts = new FinanceCashAccountService(dbContext, companyContext, currentUser, permissions,
                currencies, collector,
                new FinanceCashAccountEnvelopes(
                    Envelope(ErpPermissions.FinanceCashAccountPropose),
                    Envelope(ErpPermissions.FinanceCashAccountApprove),
                    Envelope(ErpPermissions.FinanceCashAccountPassivationPropose),
                    Envelope(ErpPermissions.FinanceCashAccountPassivationApprove),
                    Envelope(ErpPermissions.FinanceCashAccountReactivationPropose),
                    Envelope(ErpPermissions.FinanceCashAccountReactivationApprove)),
                balances ?? new NoCashMovementBalanceReader(),
                TimeProvider.System);
            return new Scope(dbContext, accounts, new FinanceContext(company, actor, Correlation, null));
        }

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Factory.DatabaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        private static DbContextOptions<FinanceDbContext> Options(SqlServerRelationalApiFactory factory) =>
            new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .Options;
    }

    private sealed class Scope(FinanceDbContext dbContext, FinanceCashAccountService accounts, FinanceContext context)
        : IAsyncDisposable
    {
        public FinanceCashAccountService Accounts => accounts;
        public FinanceContext Context => context;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    /// <summary>SENTETIK: allows every Finance action. Not a grant.</summary>
    private sealed class SyntheticPermissions : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }

    /// <summary>SENTETIK: holds only the cash-account read action, so non-approved accounts stay hidden.</summary>
    private sealed class ReadOnlyPermissions : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(
                action == FinanceAction.ReadCashAccounts ? PermissionDecision.Allowed : PermissionDecision.Denied);
    }

    /// <summary>
    /// SENTETIK: an authority that is withdrawn mid-command, either on demand or after a number of
    /// allowed decisions. Not a grant.
    /// </summary>
    private sealed class RevocablePermissions(int allowedDecisions) : IFinancePermissionPort
    {
        private int decisions;
        private volatile bool revoked;

        public int Decisions => Volatile.Read(ref decisions);

        public void Revoke() => revoked = true;

        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(
                Interlocked.Increment(ref decisions) <= allowedDecisions && !revoked
                    ? PermissionDecision.Allowed
                    : PermissionDecision.Denied);
    }

    /// <summary>SENTETIK: the one candidate, with the caller's authority withdrawn during the read. Not a Settings source.</summary>
    private sealed class RevokingCandidates(RevocablePermissions permissions) : ISettingsMoneyCandidateReader
    {
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken cancellationToken = default)
        {
            permissions.Revoke();
            return Task.FromResult<IReadOnlyList<MoneyCurrencyCandidateV1>>([new MoneyCurrencyCandidateV1(Currency, "SENTETIK", "SENTETIK para")]);
        }
    }

    /// <summary>SENTETIK: one currency candidate. Not a Settings source.</summary>
    private sealed class SyntheticCandidates : ISettingsMoneyCandidateReader
    {
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MoneyCurrencyCandidateV1>>([new MoneyCurrencyCandidateV1(Currency, "SENTETIK", "SENTETIK para")]);
    }

    /// <summary>SENTETIK: Settings' candidates after the account's currency left them. Not a Settings source.</summary>
    private sealed class OtherCandidates : ISettingsMoneyCandidateReader
    {
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MoneyCurrencyCandidateV1>>(
                [new MoneyCurrencyCandidateV1(Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5b03"), "SENTETIK2", "SENTETIK başka para")]);
    }

    /// <summary>SENTETIK: one fixed balance reading for every account, and who asked. Not a cash ledger.</summary>
    private sealed class FixedBalance(CashAccountBalanceState state) : IFinanceCashAccountBalanceReader
    {
        public List<(Guid CompanyId, Guid CashAccountId)> Seen { get; } = [];

        public Task<CashAccountBalanceState> ReadAsync(Guid companyId, Guid cashAccountId, CancellationToken cancellationToken)
        {
            Seen.Add((companyId, cashAccountId));
            return Task.FromResult(state);
        }
    }

    /// <summary>Holds a save until the other racer reaches its own, so both have read the same state.</summary>
    private sealed class BarrierInterceptor(Barrier barrier) : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FixedCurrentUser(string userId, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
