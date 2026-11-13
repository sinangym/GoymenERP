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
/// currency below is SENTETIK / NOT_ENV test data. A cash account holds no balance and moves no money.
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

        /// <summary>The same scope over Settings' own fail-closed adapter, which has no currency source.</summary>
        public Scope OpenWithUnavailableSettings(Guid company, string actor)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return Open(company, actor, new SyntheticPermissions(),
                new UnavailableSettingsPrerequisiteProvider(companyContext, new FixedCurrentUser(actor, company)), []);
        }

        private Scope Open(Guid company, string actor, IFinancePermissionPort permissions,
            ISettingsMoneyCandidateReader currencies, Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] extra)
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
                    new FinanceAuditEnvelope(FinanceCashAccountService.ProposeCommand, ["SENTETIK-FINANCE"], "Web"),
                    new FinanceAuditEnvelope(FinanceCashAccountService.ApproveCommand, ["SENTETIK-FINANCE"], "Web")),
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
