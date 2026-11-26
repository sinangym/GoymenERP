using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// FIN-SC47 (FNS-D87, FNS-D88, FNS-D93, FNS-D94) without a database: the credit account model (four groups, exact amount
/// text, the live slot, the two-person decision check, the append-only plan), the forward-fix Down, the write guard and
/// the real service in memory: a proposal only with a plan whose totals hold (taken as given, never calculated), the
/// approval or rejection by a person other than the proposer, the stored plan checked again at approval, stale versions,
/// replay, authority fences and company isolation. What SQL Server enforces is proven by the relational acceptance tests.
/// SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class FinanceCreditAccountTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("12121212-1212-1212-1212-121212121212");
    private static readonly Guid Currency = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherCurrency = Guid.Parse("23232323-2323-2323-2323-232323232323");
    private static readonly DateTimeOffset At = new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Start = new(2026, 11, 15);
    private const string Maker = "SENTETIK-credit-maker";
    private const string Checker = "SENTETIK-credit-checker";
    private const string Lender = "SENTETIK Banka A.Ş.";
    private const string Contract = "SENTETIK-KRD-0001";
    private const string Reason = "SENTETIK ret nedeni";
    private const string Guard =
        "Kredi hesabı yalnız bekleyen öneriden bir kez, öneren dışındaki kişinin onay veya ret kararıyla değişir.";

    [Fact]
    public void A_credit_account_is_a_company_row_whose_group_live_slot_and_two_person_decision_are_held_by_the_database()
    {
        using var context = Context();
        var accounts = context.Model.FindEntityType(typeof(CreditAccountRow))!;
        Assert.Equal((FinanceDbContext.Schema, "CreditAccounts"), (accounts.GetSchema(), accounts.GetTableName()));
        Assert.Single(accounts.GetDeclaredQueryFilters());
        Assert.True(accounts.FindProperty(nameof(CreditAccountRow.Version))!.IsConcurrencyToken);
        // Every amount is exact text: no column could round it.
        foreach (var type in new[] { typeof(CreditAccountRow), typeof(CreditAccountInstallmentRow) })
        {
            Assert.DoesNotContain(context.Model.FindEntityType(type)!.GetProperties(), property =>
                property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?)
                || property.ClrType == typeof(double) || property.ClrType == typeof(float));
        }

        var design = context.GetService<IDesignTimeModel>().Model;
        var designAccounts = design.FindEntityType(typeof(CreditAccountRow))!;
        Assert.Equal(FinanceDbContext.CreditAccountGroupCheck,
            Assert.Single(designAccounts.GetCheckConstraints(), check => check.Name == "CK_CreditAccounts_Group").Sql);
        Assert.Equal("[CreditGroup] IN (1, 2, 3, 4)", FinanceDbContext.CreditAccountGroupCheck);
        Assert.Equal(FinanceDbContext.CreditAccountAmountCheck,
            Assert.Single(designAccounts.GetCheckConstraints(), check => check.Name == "CK_CreditAccounts_Amount").Sql);
        Assert.Contains("[PrincipalAmountText] LIKE '%[1-9]%'", FinanceDbContext.CreditAccountAmountCheck,
            StringComparison.Ordinal);
        var decision = Assert.Single(designAccounts.GetCheckConstraints(),
            check => check.Name == "CK_CreditAccounts_Decision");
        Assert.Equal(FinanceDbContext.CreditAccountDecisionCheck, decision.Sql);
        Assert.Contains("[DecidedBy] COLLATE Latin1_General_100_BIN2 <> [ProposedBy] COLLATE Latin1_General_100_BIN2",
            decision.Sql, StringComparison.Ordinal);
        Assert.Single(designAccounts.GetCheckConstraints(), check => check.Name == "CK_CreditAccounts_Identity");
        Assert.Single(designAccounts.GetCheckConstraints(), check => check.Name == "CK_CreditAccounts_Version");
        Assert.Equal(FinanceDbContext.IdentityCollation,
            designAccounts.FindProperty(nameof(CreditAccountRow.ProposedBy))!.GetCollation());
        Assert.Equal(FinanceDbContext.IdentityCollation,
            designAccounts.FindProperty(nameof(CreditAccountRow.DecidedBy))!.GetCollation());
        Assert.Equal("date", designAccounts.FindProperty(nameof(CreditAccountRow.StartDate))!.GetColumnType());
        Assert.Equal(FinanceCreditAccountService.LenderNameMaximumLength,
            designAccounts.FindProperty(nameof(CreditAccountRow.LenderName))!.GetMaxLength());
        Assert.Equal(FinanceCreditAccountService.ContractReferenceMaximumLength,
            designAccounts.FindProperty(nameof(CreditAccountRow.ContractReference))!.GetMaxLength());
        Assert.Equal(FinanceCreditPlanCalculator.AmountMaximumLength,
            designAccounts.FindProperty(nameof(CreditAccountRow.PrincipalAmountText))!.GetMaxLength());

        // One live (proposed or approved) account per lender and contract reference; a rejected one frees the slot.
        var live = Assert.Single(designAccounts.GetIndexes(),
            index => index.GetDatabaseName() == FinanceDbContext.CreditAccountLiveIndex);
        Assert.True(live.IsUnique);
        Assert.Equal([nameof(CreditAccountRow.CompanyId), nameof(CreditAccountRow.LenderName),
                nameof(CreditAccountRow.ContractReference)],
            live.Properties.Select(property => property.Name));
        Assert.Equal(FinanceDbContext.CreditAccountLiveFilter + " AND [LenderPartyId] IS NULL", live.GetFilter());
        var identityLive = Assert.Single(designAccounts.GetIndexes(), x => x.GetDatabaseName() == "UX_CreditAccounts_LenderIdentity_Live");
        Assert.True(identityLive.IsUnique);
        Assert.Contains(identityLive.Properties, x => x.Name == nameof(CreditAccountRow.LenderCompanyPartyAccountId));
        Assert.Equal("[Status] IN (1, 2)", FinanceDbContext.CreditAccountLiveFilter);

        var installments = design.FindEntityType(typeof(CreditAccountInstallmentRow))!;
        Assert.Equal((FinanceDbContext.Schema, "CreditAccountInstallments"),
            (installments.GetSchema(), installments.GetTableName()));
        Assert.Equal([nameof(CreditAccountInstallmentRow.CompanyId), nameof(CreditAccountInstallmentRow.CreditAccountId),
                nameof(CreditAccountInstallmentRow.Sequence)],
            installments.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(FinanceDbContext.CreditAccountInstallmentAmountsCheck,
            Assert.Single(installments.GetCheckConstraints(),
                check => check.Name == "CK_CreditAccountInstallments_Amounts").Sql);
        Assert.Single(installments.GetCheckConstraints(), check => check.Name == "CK_CreditAccountInstallments_Sequence");
        // The plan belongs to the account of the same company.
        var owner = Assert.Single(installments.GetForeignKeys(),
            key => key.PrincipalEntityType.ClrType == typeof(CreditAccountRow));
        Assert.Equal([nameof(CreditAccountInstallmentRow.CompanyId), nameof(CreditAccountInstallmentRow.CreditAccountId)],
            owner.Properties.Select(property => property.Name));
        Assert.Single(context.Model.FindEntityType(typeof(CreditAccountInstallmentRow))!.GetDeclaredQueryFilters());

        var commands = design.FindEntityType(typeof(CreditAccountCommandRow))!;
        Assert.Equal((FinanceDbContext.Schema, "CreditAccountCommands"), (commands.GetSchema(), commands.GetTableName()));
        var commandTarget = Assert.Single(commands.GetCheckConstraints(),
            check => check.Name == "CK_CreditAccountCommands_Target");
        Assert.EndsWith("JSON_VALUE([ResultJson], '$.outcome') = 'ValidationFailed')", commandTarget.Sql,
            StringComparison.Ordinal);
        Assert.Equal([nameof(CreditAccountCommandRow.CompanyId), nameof(CreditAccountCommandRow.CommandName),
                nameof(CreditAccountCommandRow.IdempotencyKeyHash)],
            commands.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Single(context.Model.FindEntityType(typeof(CreditAccountCommandRow))!.GetDeclaredQueryFilters());
    }

    [Fact]
    public void The_credit_account_migration_is_pure_DDL_up_and_refuses_to_drop_stored_accounts()
    {
        var migration = new AddCreditAccounts();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Equal(new[] { "CreditAccounts", "CreditAccountCommands", "CreditAccountInstallments" }.Order(StringComparer.Ordinal),
            migration.UpOperations.OfType<CreateTableOperation>().Select(operation => operation.Name).Order(StringComparer.Ordinal));

        // Forward fix: the first Down operation is the one guard; only an empty store rolls back.
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
        Assert.Contains("THROW 51861", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[finance].[CreditAccounts]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[finance].[CreditAccountCommands]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("AddCreditAccounts is not rolled back (forward fix).", guard.Sql, StringComparison.Ordinal);
        Assert.Equal(["CreditAccountCommands", "CreditAccountInstallments", "CreditAccounts"],
            migration.DownOperations.OfType<DropTableOperation>().Select(operation => operation.Name));
    }

    [Theory]
    [InlineData("decided-by-proposer")]
    [InlineData("two-version-steps")]
    [InlineData("approved-with-reason")]
    [InlineData("rejected-without-reason")]
    [InlineData("decided-twice")]
    [InlineData("decided-without-time")]
    public async Task A_credit_account_only_leaves_its_proposal_once_by_another_persons_decision(string fault)
    {
        await using var run = new Run();
        var seeded = await run.SeedAsync(Account(fault == "decided-twice"
            ? FinanceCreditAccountService.Approved
            : FinanceCreditAccountService.Proposed));
        await using var context = run.Memory(CompanyA);
        var row = await context.CreditAccounts.SingleAsync(candidate => candidate.Id == seeded.Id);
        switch (fault)
        {
            case "decided-by-proposer":
                Decide(row, FinanceCreditAccountService.Approved, Maker, null);
                break;
            case "two-version-steps":
                Decide(row, FinanceCreditAccountService.Approved, Checker, null);
                row.Version += 1;
                break;
            case "approved-with-reason":
                Decide(row, FinanceCreditAccountService.Approved, Checker, Reason);
                break;
            case "rejected-without-reason":
                Decide(row, FinanceCreditAccountService.Rejected, Checker, null);
                break;
            case "decided-twice":
                Decide(row, FinanceCreditAccountService.Rejected, Checker, Reason);
                break;
            default:
                Decide(row, FinanceCreditAccountService.Approved, Checker, null);
                row.DecidedAtUtc = null;
                break;
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Equal(Guard, exception.Message);
    }

    [Theory]
    [InlineData("principal-amount")]
    [InlineData("lender")]
    [InlineData("group")]
    public async Task A_decision_never_changes_the_accounts_values(string field)
    {
        await using var run = new Run();
        var seeded = await run.SeedAsync(Account(FinanceCreditAccountService.Proposed));
        await using var context = run.Memory(CompanyA);
        var row = await context.CreditAccounts.SingleAsync(candidate => candidate.Id == seeded.Id);
        Decide(row, FinanceCreditAccountService.Approved, Checker, null);
        switch (field)
        {
            case "principal-amount":
                row.PrincipalAmountText = "999.00";
                break;
            case "lender":
                row.LenderName = "SENTETIK Başka Banka";
                break;
            default:
                row.CreditGroup = FinanceCreditAccountService.LeasingGroup;
                break;
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Theory]
    [InlineData("modified")]
    [InlineData("deleted")]
    public async Task An_imported_plan_line_is_append_only(string change)
    {
        await using var run = new Run();
        var seeded = await run.SeedAsync(Account(FinanceCreditAccountService.Proposed));
        await using var context = run.Memory(CompanyA);
        var line = await context.CreditAccountInstallments.FirstAsync(candidate => candidate.CreditAccountId == seeded.Id);
        if (change == "modified")
        {
            line.InterestText = "0";
        }
        else
        {
            context.CreditAccountInstallments.Remove(line);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task A_decision_by_another_person_passes_the_write_guard()
    {
        await using var run = new Run();
        var seeded = await run.SeedAsync(Account(FinanceCreditAccountService.Proposed));
        await using (var context = run.Memory(CompanyA))
        {
            Decide(await context.CreditAccounts.SingleAsync(candidate => candidate.Id == seeded.Id),
                FinanceCreditAccountService.Rejected, Checker, Reason);
            await context.SaveChangesAsync();
        }

        var stored = await run.RowAsync(seeded.Id);
        Assert.Equal((FinanceCreditAccountService.Rejected, 2L, Checker, Reason),
            (stored.Status, stored.Version, stored.DecidedBy, stored.RejectionReason));
    }

    [Fact]
    public async Task A_maker_proposes_the_bank_plan_as_given_and_another_person_approves_each_audited()
    {
        await using var run = new Run();
        var permissions = new RecordingPermission(PermissionDecision.Allowed);

        var proposed = await run.ProposeAsync(Maker, Request(), permissions: permissions);

        Assert.Equal((CommandOutcome.Succeeded, false), (proposed.Outcome, proposed.IsReplay));
        var view = proposed.Resource!;
        Assert.Equal((CreditAccountGroup.Cash, Lender, Contract, Currency, "1000.00", Start, CreditAccountStatus.Proposed,
                true, "1"),
            (view.Group, view.LenderName, view.ContractReference, view.CurrencyId, view.PrincipalAmount, view.StartDate,
                view.Status, view.IsOwnProposal, view.Version));
        // The lines are stored exactly as the bank gave them; the totals are exact sums, never rounded.
        Assert.Equal(Plan().Select(line => (line.Sequence, line.DueDate, line.Installment, line.Principal, line.Interest,
                line.Bsmv, line.Kkdf)),
            view.Installments.Select(line => (line.Sequence, line.DueDate, line.Installment, line.Principal,
                line.Interest, line.Bsmv, line.Kkdf)));
        Assert.Equal(new CreditPlanTotalsView("1072.1515", "1000.00", "58.0000", "2.9000", "11.2515"), view.PlanTotals);
        Assert.All(permissions.Actions, action => Assert.Equal(FinanceAction.ProposeCredit, action));
        Assert.NotEmpty(permissions.Actions);

        var approvals = new RecordingPermission(PermissionDecision.Allowed);
        var approved = await run.ApproveAsync(Checker, new CreditAccountApproveRequest(view.Id, "1"),
            permissions: approvals);
        Assert.Equal((CommandOutcome.Succeeded, CreditAccountStatus.Approved, "2", false, (string?)null),
            (approved.Outcome, approved.Resource!.Status, approved.Resource.Version, approved.Resource.IsOwnProposal,
                approved.Resource.RejectionReason));
        Assert.All(approvals.Actions, action => Assert.Equal(FinanceAction.ApproveCredit, action));
        var stored = await run.RowAsync(view.Id);
        Assert.Equal((Maker, Checker), (stored.ProposedBy, stored.DecidedBy));

        var audits = await run.AuditsAsync(view.Id);
        Assert.Equal(["finance.credit-account.proposed.v1", "finance.credit-account.approved.v1"],
            audits.Select(audit => audit.EventType));
        Assert.Equal((ErpPermissions.FinanceCreditsPropose, Maker, (long?)null, (long?)1),
            (audits[0].PermissionCode, audits[0].ActorId, audits[0].EntityVersionBefore, audits[0].EntityVersionAfter));
        Assert.Equal((ErpPermissions.FinanceCreditsApprove, Checker, (long?)1, (long?)2),
            (audits[1].PermissionCode, audits[1].ActorId, audits[1].EntityVersionBefore, audits[1].EntityVersionAfter));
        using var approval = JsonDocument.Parse(audits[1].ApprovalContextJson!);
        Assert.Equal(("FNS-D88", Maker, Checker),
            (approval.RootElement.GetProperty("rule").GetString(),
                approval.RootElement.GetProperty("proposedBy").GetString(),
                approval.RootElement.GetProperty("approvedBy").GetString()));
        Assert.Equal(2, (await run.CommandsAsync()).Count);
        Assert.Equal(2, (await run.InstallmentsAsync()).Count);

        // The checker sees the approved account; it is not theirs.
        var read = await run.GetAsync(Checker, view.Id);
        Assert.Equal((ReadOutcome.Found, CreditAccountStatus.Approved, false),
            (read.Outcome, read.Resource!.Status, read.Resource.IsOwnProposal));
    }

    [Theory]
    [InlineData(CreditAccountGroup.Cash, FinanceCreditAccountService.CashGroup)]
    [InlineData(CreditAccountGroup.NonCash, FinanceCreditAccountService.NonCashGroup)]
    [InlineData(CreditAccountGroup.InstrumentAndFactoring, FinanceCreditAccountService.InstrumentAndFactoringGroup)]
    [InlineData(CreditAccountGroup.Leasing, FinanceCreditAccountService.LeasingGroup)]
    public async Task Each_of_the_four_groups_is_one_type_field(CreditAccountGroup group, int code)
    {
        await using var run = new Run();

        var proposed = await run.ProposeAsync(Maker, Request() with { Group = group, Profile = Profile(group),
            Installments = PlanFor(group) });

        Assert.Equal(group, proposed.Resource!.Group);
        Assert.Equal(code, (await run.RowAsync(proposed.Resource.Id)).CreditGroup);
    }

    [Fact]
    public async Task The_proposer_never_decides_their_own_account_and_nothing_is_stored()
    {
        await using var run = new Run();
        var proposed = (await run.ProposeAsync(Maker, Request())).Resource!;

        AssertIssue(await run.ApproveAsync(Maker, new CreditAccountApproveRequest(proposed.Id, "1")),
            CommandOutcome.Denied, "approval", "finance_approver_is_proposer");
        AssertIssue(await run.RejectAsync(Maker, new CreditAccountRejectRequest(proposed.Id, "1", Reason)),
            CommandOutcome.Denied, "approval", "finance_approver_is_proposer");

        var stored = await run.RowAsync(proposed.Id);
        Assert.Equal((FinanceCreditAccountService.Proposed, 1L), (stored.Status, stored.Version));
        Assert.Single(await run.CommandsAsync());
        Assert.Single(await run.AuditsAsync(proposed.Id));
    }

    [Fact]
    public async Task A_rejection_names_its_reason_and_frees_the_contract_for_a_new_proposal()
    {
        await using var run = new Run();
        var proposed = (await run.ProposeAsync(Maker, Request())).Resource!;

        AssertIssue(await run.ProposeAsync(Maker, Request()), CommandOutcome.ValidationFailed, "contractReference",
            "credit_account_exists");
        AssertIssue(await run.RejectAsync(Checker, new CreditAccountRejectRequest(proposed.Id, "1", "  ")),
            CommandOutcome.ValidationFailed, "reason", "reason_invalid");
        AssertIssue(await run.RejectAsync(Checker, new CreditAccountRejectRequest(proposed.Id, "1", new string('r', 501))),
            CommandOutcome.ValidationFailed, "reason", "finance_value_does_not_fit");

        var rejected = await run.RejectAsync(Checker, new CreditAccountRejectRequest(proposed.Id, "1", Reason));
        Assert.Equal((CommandOutcome.Succeeded, CreditAccountStatus.Rejected, Reason),
            (rejected.Outcome, rejected.Resource!.Status, rejected.Resource.RejectionReason));
        var rejectAudit = (await run.AuditsAsync(proposed.Id)).Last();
        using var approval = JsonDocument.Parse(rejectAudit.ApprovalContextJson!);
        Assert.Equal(("finance.credit-account.rejected.v1", Checker),
            (rejectAudit.EventType, approval.RootElement.GetProperty("rejectedBy").GetString()));
        Assert.False(approval.RootElement.TryGetProperty("approvedBy", out _));

        var again = await run.ProposeAsync(Maker, Request());
        Assert.Equal(CommandOutcome.Succeeded, again.Outcome);
        Assert.Equal(2, (await run.AllRowsAsync()).Count);
        // Both stay listed: the rejected one with its reason, the new one pending.
        var listed = (await run.ListAsync(Checker)).Resource!.Items;
        Assert.Equal(new[] { again.Resource!.Id, proposed.Id }.Order(), listed.Select(item => item.Id).Order());
        Assert.Equal(CreditAccountStatus.Proposed, listed.Single(item => item.Id == again.Resource.Id).Status);
    }

    [Fact]
    public async Task A_decision_names_the_current_version_of_a_pending_proposal()
    {
        await using var run = new Run();
        var proposed = (await run.ProposeAsync(Maker, Request())).Resource!;

        const string staleKey = "SENTETIK-stale-key";
        var stale = await run.ApproveAsync(Checker, new CreditAccountApproveRequest(proposed.Id, "7"), key: staleKey);
        AssertIssue(stale, CommandOutcome.VersionConflict, "expectedVersion", "version_conflict");
        Assert.Contains(await run.CommandsAsync(), command => command.CreditAccountId == proposed.Id
            && command.ResultJson.Contains("VersionConflict", StringComparison.Ordinal));
        // The recorded refusal replays under its key.
        var staleAgain = await run.ApproveAsync(Checker, new CreditAccountApproveRequest(proposed.Id, "7"), key: staleKey);
        Assert.Equal((CommandOutcome.VersionConflict, true), (staleAgain.Outcome, staleAgain.IsReplay));

        AssertIssue(await run.ApproveAsync(Checker, new CreditAccountApproveRequest(Guid.NewGuid(), "1")),
            CommandOutcome.ValidationFailed, "creditAccountId", "credit_account_not_found");
        AssertIssue(await run.ApproveAsync(Checker, new CreditAccountApproveRequest(Guid.Empty, "1")),
            CommandOutcome.ValidationFailed, "creditAccountId", "identity_required");
        AssertIssue(await run.ApproveAsync(Checker, new CreditAccountApproveRequest(proposed.Id, " 1")),
            CommandOutcome.ValidationFailed, "expectedVersion", "identity_required");

        await run.ApproveAsync(Checker, new CreditAccountApproveRequest(proposed.Id, "1"));
        AssertIssue(await run.RejectAsync(Checker, new CreditAccountRejectRequest(proposed.Id, "2", Reason)),
            CommandOutcome.ValidationFailed, "creditAccountId", "credit_account_not_proposed");
        Assert.Equal(CreditAccountStatus.Approved, (await run.ListAsync(Checker)).Resource!.Items.Single().Status);
    }

    [Theory]
    [InlineData("installment-sum", "installments[1].installment", FinanceCreditPlanCalculator.InstallmentSumMismatch)]
    [InlineData("principal-total", "installments", FinanceCreditPlanCalculator.PrincipalTotalMismatch)]
    [InlineData("sequence", "installments[1].sequence", FinanceCreditPlanCalculator.SequenceInvalid)]
    [InlineData("amount-text", "installments[0].bsmv", FinanceCreditPlanCalculator.AmountInvalid)]
    [InlineData("empty", "installments", FinanceCreditPlanCalculator.PlanEmpty)]
    [InlineData("credit-amount", "principalAmount", FinanceCreditPlanCalculator.PrincipalAmountInvalid)]
    public async Task A_plan_whose_totals_do_not_hold_is_refused_and_never_stored(string fault, string field, string code)
    {
        await using var run = new Run();
        var plan = Plan();
        var request = fault switch
        {
            "installment-sum" => Request() with { Installments = [plan[0], plan[1] with { Installment = "530.928" }] },
            "principal-total" => Request() with { PrincipalAmount = "1000.01" },
            "sequence" => Request() with { Installments = [plan[0], plan[1] with { Sequence = 3 }] },
            "amount-text" => Request() with { Installments = [plan[0] with { Bsmv = "1,65" }, plan[1]] },
            "empty" => Request() with { Installments = [] },
            _ => Request() with { PrincipalAmount = "0" },
        };

        var result = await run.ProposeAsync(Maker, request);

        AssertIssue(result, CommandOutcome.ValidationFailed, field, code);
        Assert.Empty(await run.AllRowsAsync());
        Assert.Empty(await run.InstallmentsAsync());
        Assert.Empty(await run.CommandsAsync());
    }

    [Fact]
    public async Task A_stored_plan_that_no_longer_holds_never_reaches_an_approval_but_can_be_rejected()
    {
        await using var run = new Run();
        var seeded = await run.SeedAsync(Account(FinanceCreditAccountService.Proposed, principal: "999.00"));

        AssertIssue(await run.ApproveAsync(Checker, new CreditAccountApproveRequest(seeded.Id, "1")),
            CommandOutcome.ValidationFailed, "installments", FinanceCreditPlanCalculator.PrincipalTotalMismatch);
        Assert.Equal(FinanceCreditAccountService.Proposed, (await run.RowAsync(seeded.Id)).Status);
        Assert.Empty(await run.CommandsAsync());

        var rejected = await run.RejectAsync(Checker, new CreditAccountRejectRequest(seeded.Id, "1", Reason));
        Assert.Equal((CommandOutcome.Succeeded, CreditAccountStatus.Rejected),
            (rejected.Outcome, rejected.Resource!.Status));
    }

    [Theory]
    [InlineData("group", "group", "credit_group_invalid")]
    [InlineData("lender-blank", "lenderName", "credit_lender_invalid")]
    [InlineData("lender-padded", "lenderName", "credit_lender_invalid")]
    [InlineData("lender-long", "lenderName", "credit_lender_invalid")]
    [InlineData("contract-blank", "contractReference", "credit_contract_reference_invalid")]
    [InlineData("contract-long", "contractReference", "credit_contract_reference_invalid")]
    [InlineData("currency-empty", "currencyId", "identity_required")]
    [InlineData("currency-not-candidate", "currencyId", "currency_not_a_candidate")]
    [InlineData("null-lender", "lenderName", "credit_lender_invalid")]
    [InlineData("null-contract", "contractReference", "credit_contract_reference_invalid")]
    [InlineData("null-amount", "principalAmount", "credit_amount_invalid")]
    [InlineData("null-plan", "installments", "credit_plan_empty")]
    public async Task A_proposal_names_a_group_a_lender_a_contract_and_a_Settings_currency(string fault, string field,
        string code)
    {
        await using var run = new Run();
        var request = fault switch
        {
            "group" => Request() with { Group = (CreditAccountGroup)9 },
            "lender-blank" => Request() with { LenderName = " " },
            "lender-padded" => Request() with { LenderName = " " + Lender },
            "lender-long" => Request() with
            {
                LenderName = new string('B', FinanceCreditAccountService.LenderNameMaximumLength + 1),
            },
            "contract-blank" => Request() with { ContractReference = "" },
            "contract-long" => Request() with
            {
                ContractReference = new string('K', FinanceCreditAccountService.ContractReferenceMaximumLength + 1),
            },
            "currency-empty" => Request() with { CurrencyId = Guid.Empty },
            "currency-not-candidate" => Request() with { CurrencyId = OtherCurrency },
            "null-lender" => Request() with { LenderName = null! },
            "null-contract" => Request() with { ContractReference = null! },
            "null-amount" => Request() with { PrincipalAmount = null! },
            _ => Request() with { Installments = null! },
        };

        AssertIssue(await run.ProposeAsync(Maker, request), CommandOutcome.ValidationFailed, field, code);
        Assert.Empty(await run.AllRowsAsync());
        Assert.Empty(await run.CommandsAsync());
    }

    [Fact]
    public async Task The_same_key_answers_its_first_result_and_another_payload_or_actor_is_a_conflict()
    {
        await using var run = new Run();
        const string key = "SENTETIK-same-key";
        var first = await run.ProposeAsync(Maker, Request(), key: key, correlation: "SENTETIK-first");
        var again = await run.ProposeAsync(Maker, Request(), key: key, correlation: "SENTETIK-second");

        Assert.Equal((CommandOutcome.Succeeded, true, "SENTETIK-first", first.Resource!.Id),
            (again.Outcome, again.IsReplay, again.OriginalCorrelationId, again.Resource!.Id));
        Assert.Single(await run.AllRowsAsync());

        var otherPayload = await run.ProposeAsync(Maker, Request() with { StartDate = Start.AddDays(1) }, key: key);
        Assert.Equal((CommandOutcome.IdempotencyConflict, (CreditAccountView?)null),
            (otherPayload.Outcome, otherPayload.Resource));
        Assert.Equal(CommandOutcome.IdempotencyConflict,
            (await run.ProposeAsync("SENTETIK-other-maker", Request(), key: key)).Outcome);

        // The authority is measured afresh before a replay.
        var revoked = await run.ProposeAsync(Maker, Request(), key: key,
            permissions: new RecordingPermission(PermissionDecision.Denied));
        Assert.Equal((CommandOutcome.Denied, false), (revoked.Outcome, revoked.IsReplay));
    }

    [Theory]
    [InlineData(PermissionDecision.Denied, CommandOutcome.Denied)]
    [InlineData(PermissionDecision.Unavailable, CommandOutcome.Unavailable)]
    public async Task Without_the_propose_authority_nothing_is_stored(PermissionDecision decision,
        CommandOutcome outcome)
    {
        await using var run = new Run();

        var result = await run.ProposeAsync(Maker, Request(), permissions: new RecordingPermission(decision));

        Assert.Equal(outcome, result.Outcome);
        Assert.Null(result.Resource);
        Assert.Empty(await run.AllRowsAsync());
        Assert.Empty(await run.CommandsAsync());
    }

    [Fact]
    public async Task The_propose_and_approve_authorities_are_separate()
    {
        await using var run = new Run();

        Assert.Equal(CommandOutcome.Denied, (await run.ProposeAsync(Maker, Request(),
            permissions: new OnlyPermission(FinanceAction.ApproveCredit))).Outcome);
        var proposed = (await run.ProposeAsync(Maker, Request(),
            permissions: new OnlyPermission(FinanceAction.ProposeCredit))).Resource!;

        var onlyPropose = new OnlyPermission(FinanceAction.ProposeCredit);
        Assert.Equal(CommandOutcome.Denied, (await run.ApproveAsync(Checker,
            new CreditAccountApproveRequest(proposed.Id, "1"), permissions: onlyPropose)).Outcome);
        Assert.Equal(CommandOutcome.Denied, (await run.RejectAsync(Checker,
            new CreditAccountRejectRequest(proposed.Id, "1", Reason), permissions: onlyPropose)).Outcome);
        Assert.Equal(ReadOutcome.Denied, (await run.ListAsync(Checker, onlyPropose)).Outcome);
        Assert.Equal(ReadOutcome.Denied,
            (await run.ValidatePlanAsync(Maker, new OnlyPermission(FinanceAction.ReadCredits))).Outcome);
        Assert.Equal(FinanceCreditAccountService.Proposed, (await run.RowAsync(proposed.Id)).Status);

        var approved = await run.ApproveAsync(Checker, new CreditAccountApproveRequest(proposed.Id, "1"),
            permissions: new OnlyPermission(FinanceAction.ApproveCredit));
        Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task An_authority_lost_before_the_write_stores_nothing(int allowedChecks)
    {
        await using var run = new Run();
        var proposed = (await run.ProposeAsync(Maker, Request())).Resource!;

        var result = await run.ApproveAsync(Checker, new CreditAccountApproveRequest(proposed.Id, "1"),
            permissions: new RevokingPermission(FinanceAction.ApproveCredit, allowedChecks));

        Assert.Equal((CommandOutcome.Denied, (CreditAccountView?)null), (result.Outcome, result.Resource));
        Assert.Equal((FinanceCreditAccountService.Proposed, 1L),
            ((await run.RowAsync(proposed.Id)).Status, (await run.RowAsync(proposed.Id)).Version));
        Assert.Single(await run.CommandsAsync());
        Assert.Single(await run.AuditsAsync(proposed.Id));
    }

    [Fact]
    public async Task Another_companys_account_is_neither_seen_nor_decided()
    {
        await using var run = new Run();
        var proposed = (await run.ProposeAsync(Maker, Request())).Resource!;

        Assert.Empty((await run.ListAsync(Checker, company: CompanyB)).Resource!.Items);
        Assert.Equal(ReadOutcome.NotFound, (await run.GetAsync(Checker, proposed.Id, company: CompanyB)).Outcome);
        AssertIssue(await run.ApproveAsync(Checker, new CreditAccountApproveRequest(proposed.Id, "1"), company: CompanyB),
            CommandOutcome.ValidationFailed, "creditAccountId", "credit_account_not_found");
        Assert.Equal(FinanceCreditAccountService.Proposed, (await run.RowAsync(proposed.Id)).Status);
        // The same contract in another company is that company's own account.
        Assert.Equal(CommandOutcome.Succeeded, (await run.ProposeAsync(Maker, Request(), company: CompanyB)).Outcome);
    }

    [Fact]
    public async Task An_empty_or_unknown_identity_reads_as_not_found()
    {
        await using var run = new Run();

        Assert.Equal(ReadOutcome.NotFound, (await run.GetAsync(Checker, Guid.Empty)).Outcome);
        Assert.Equal(ReadOutcome.NotFound, (await run.GetAsync(Checker, Guid.NewGuid())).Outcome);
    }

    [Fact]
    public async Task The_plan_check_answers_the_same_calculator_and_stores_nothing()
    {
        await using var run = new Run();

        var valid = await run.ValidatePlanAsync(Maker);
        Assert.Equal((ReadOutcome.Found, true, CompanyA), (valid.Outcome, valid.Resource!.IsValid, valid.Resource.CompanyId));
        Assert.Equal(new CreditPlanTotalsView("1072.1515", "1000.00", "58.0000", "2.9000", "11.2515"),
            valid.Resource.Totals);

        var invalid = await run.ValidatePlanAsync(Maker, request: new CreditPlanValidationRequest("1000.01", Plan(), Currency, Start));
        Assert.False(invalid.Resource!.IsValid);
        var issue = Assert.Single(invalid.Resource.Issues);
        Assert.Equal(("installments", FinanceCreditPlanCalculator.PrincipalTotalMismatch), (issue.Field, issue.Code));

        Assert.Empty(await run.AllRowsAsync());
        Assert.Empty(await run.CommandsAsync());
    }

    [Fact]
    public async Task The_currency_candidates_are_Settings_candidates_under_the_propose_authority()
    {
        await using var run = new Run();

        var candidates = await run.CurrencyCandidatesAsync(Maker, new OnlyPermission(FinanceAction.ProposeCredit));
        var candidate = Assert.Single(candidates.Resource!.Items);
        Assert.Equal((Currency, "TRY"), (candidate.CurrencyId, candidate.Code));
        Assert.Equal(ReadOutcome.Denied,
            (await run.CurrencyCandidatesAsync(Maker, new OnlyPermission(FinanceAction.ReadCredits))).Outcome);
    }

    private static CreditAccountRow Account(int status, string principal = "1000.00") => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, CreditGroup = FinanceCreditAccountService.CashGroup,
        LenderName = Lender, ContractReference = Contract, CurrencyId = Currency, PrincipalAmountText = principal,
        StartDate = Start, Status = status, ProposedBy = Maker, ProposedAtUtc = At,
        DecidedBy = status == FinanceCreditAccountService.Proposed ? null : Checker,
        DecidedAtUtc = status == FinanceCreditAccountService.Proposed ? null : At,
        RejectionReason = status == FinanceCreditAccountService.Rejected ? Reason : null,
        Version = status == FinanceCreditAccountService.Proposed ? 1 : 2, UpdatedAtUtc = At,
    };

    private static void Decide(CreditAccountRow row, int status, string decidedBy, string? reason)
    {
        row.Status = status;
        row.DecidedBy = decidedBy;
        row.DecidedAtUtc = At;
        row.RejectionReason = reason;
        row.UpdatedAtUtc = At;
        row.Version += 1;
    }

    /// <summary>A bank plan as given: exact amounts with the bank's own scales, each line holding, principals = 1000.00.</summary>
    private static List<CreditPlanInstallmentInput> Plan() =>
    [
        new(1, new DateOnly(2026, 12, 15), "541.2345", "500.00", "33.0000", "1.6500", "6.5845"),
        new(2, new DateOnly(2027, 1, 15), "530.917", "500.00", "25.000", "1.250", "4.667"),
    ];

    private static List<CreditPlanInstallmentInput> PlanFor(CreditAccountGroup group) => group switch
    {
        CreditAccountGroup.NonCash => [],
        CreditAccountGroup.Leasing => [
            new(1, new(2026, 12, 15), "541.2345", "500.00", "33.0000", "0", "0", Vat: "8.2345"),
            new(2, new(2027, 1, 15), "530.917", "500.00", "25.000", "0", "0", Vat: "5.917")],
        _ => Plan()
    };

    private static CreditAccountProposeRequest Request() =>
        new(CreditAccountGroup.Cash, Lender, Contract, Currency, "1000.00", Start, Plan(), Profile(CreditAccountGroup.Cash));

    private static CreditAccountProfileV1 Profile(CreditAccountGroup group) => new(
        Guid.Parse("f4710000-0000-4000-8000-000000000001"), Guid.Parse("f4710000-0000-4000-8000-000000000002"),
        new("SENTETIK banka planı", Start, "1"), group == CreditAccountGroup.NonCash ? CreditPlanNatureV1.SourceMovements : CreditPlanNatureV1.Installments,
        Cash: group == CreditAccountGroup.Cash ? new(CreditCashKindV1.Installment, Start.AddYears(1)) : null,
        NonCash: group == CreditAccountGroup.NonCash ? new(CreditNonCashKindV1.Guarantee, "SENTETIK lehtar", true, null) : null,
        InstrumentAndFactoring: group == CreditAccountGroup.InstrumentAndFactoring ? new(CreditInstrumentKindV1.Factoring, true, [], ["SENTETIK-FAT-1"]) : null,
        Leasing: group == CreditAccountGroup.Leasing ? new("SENTETIK varlık", Start, Start.AddYears(2)) : null);

    private static FinanceDbContext Context()
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;")
            .Options;
        return new FinanceDbContext(options, company);
    }

    private static FinanceCreditAccountEnvelopes Envelopes() => new(
        new FinanceAuditEnvelope(ErpPermissions.FinanceCreditsPropose, ["SENTETIK-FINANCE"], SourceChannels.Web),
        new FinanceAuditEnvelope(ErpPermissions.FinanceCreditsApprove, ["SENTETIK-FINANCE"], SourceChannels.Web));

    private static void AssertIssue(CreditAccountCommandResult result, CommandOutcome outcome, string field, string code)
    {
        Assert.Equal(outcome, result.Outcome);
        Assert.Null(result.Resource);
        var issue = Assert.Single(result.Issues);
        Assert.Equal((field, code), (issue.Field, issue.Code));
    }

    private static string Key() => $"SENTETIK-{Guid.NewGuid():N}";

    /// <summary>
    /// The real service over one in-memory store: every call is a fresh scope (context, company, user, audit collector
    /// and interceptor) like a request, on a fixed clock.
    /// </summary>
    private sealed partial class Run : IAsyncDisposable
    {
        private readonly string databaseName = $"finance-credit-accounts-{Guid.NewGuid():N}";
        public Func<Guid, ISettingsPrerequisiteProvider?> Settings { get; set; } = company => new SyntheticCreditSources(company);
        public Func<Guid, IFinanceCreditLenderPortV1?> Lenders { get; set; } = company => new SyntheticCreditSources(company);

        public FinanceDbContext Memory(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase(databaseName).Options, companyContext);
        }

        /// <summary>Seeds an account with the standard plan (its principal total is 1000.00).</summary>
        public async Task<CreditAccountRow> SeedAsync(CreditAccountRow row)
        {
            await using var context = Memory(row.CompanyId);
            context.CreditAccounts.Add(row);
            context.CreditAccountInstallments.AddRange(Plan().Select(line => new CreditAccountInstallmentRow
            {
                CompanyId = row.CompanyId, CreditAccountId = row.Id, Sequence = line.Sequence, DueDate = line.DueDate,
                InstallmentText = line.Installment, PrincipalText = line.Principal, InterestText = line.Interest,
                BsmvText = line.Bsmv, KkdfText = line.Kkdf,
            }));
            await context.SaveChangesAsync();
            return row;
        }

        public Task<CreditAccountCommandResult> ProposeAsync(string actor, CreditAccountProposeRequest request,
            string? key = null, IFinancePermissionPort? permissions = null, string? correlation = null,
            Guid? company = null) =>
            CallAsync(actor, company ?? CompanyA, permissions, correlation,
                (service, finance) => service.ProposeAsync(finance, new CommandMetadata(key ?? Key()), request,
                    CancellationToken.None));

        public Task<CreditAccountCommandResult> ApproveAsync(string actor, CreditAccountApproveRequest request,
            IFinancePermissionPort? permissions = null, Guid? company = null, string? key = null) =>
            CallAsync(actor, company ?? CompanyA, permissions, null,
                (service, finance) => service.ApproveAsync(finance, new CommandMetadata(key ?? Key()), request,
                    CancellationToken.None));

        public Task<CreditAccountCommandResult> RejectAsync(string actor, CreditAccountRejectRequest request,
            IFinancePermissionPort? permissions = null) =>
            CallAsync(actor, CompanyA, permissions, null,
                (service, finance) => service.RejectAsync(finance, new CommandMetadata(Key()), request,
                    CancellationToken.None));

        public Task<ReadResult<CreditAccountListPage>> ListAsync(string actor, IFinancePermissionPort? permissions = null,
            Guid? company = null) =>
            CallAsync(actor, company ?? CompanyA, permissions, null,
                (service, finance) => service.ListAsync(finance, CancellationToken.None));

        public Task<ReadResult<CreditAccountView>> GetAsync(string actor, Guid id, Guid? company = null) =>
            CallAsync(actor, company ?? CompanyA, null, null,
                (service, finance) => service.GetAsync(finance, id, CancellationToken.None));

        public Task<ReadResult<CreditPlanValidationView>> ValidatePlanAsync(string actor,
            IFinancePermissionPort? permissions = null, CreditPlanValidationRequest? request = null) =>
            CallAsync(actor, CompanyA, permissions, null,
                (service, finance) => service.ValidatePlanAsync(finance,
                    request ?? new CreditPlanValidationRequest("1000.00", Plan(), Currency, Start), CancellationToken.None));

        public Task<ReadResult<CreditAccountCurrencyCandidates>> CurrencyCandidatesAsync(string actor,
            IFinancePermissionPort permissions) =>
            CallAsync(actor, CompanyA, permissions, null,
                (service, finance) => service.CurrencyCandidatesAsync(finance, CancellationToken.None));

        public async Task<CreditAccountRow> RowAsync(Guid id)
        {
            await using var context = Memory(CompanyA);
            return await context.CreditAccounts.AsNoTracking().SingleAsync(row => row.Id == id);
        }

        public async Task<List<CreditAccountRow>> AllRowsAsync()
        {
            await using var context = Memory(CompanyA);
            return await context.CreditAccounts.AsNoTracking().ToListAsync();
        }

        public async Task<List<CreditAccountInstallmentRow>> InstallmentsAsync()
        {
            await using var context = Memory(CompanyA);
            return await context.CreditAccountInstallments.AsNoTracking().ToListAsync();
        }

        public async Task<List<CreditAccountCommandRow>> CommandsAsync()
        {
            await using var context = Memory(CompanyA);
            return await context.CreditAccountCommands.AsNoTracking().ToListAsync();
        }

        public async Task<List<AuditEvent>> AuditsAsync(Guid id)
        {
            var entityId = id.ToString("D");
            await using var context = Memory(CompanyA);
            return await context.Set<AuditEvent>().AsNoTracking().Where(audit => audit.EntityId == entityId)
                .OrderBy(audit => audit.EntityVersionAfter).ToListAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await using var context = Memory(CompanyA);
            await context.Database.EnsureDeletedAsync();
        }

        private async Task<T> CallAsync<T>(string actor, Guid companyId, IFinancePermissionPort? permissions,
            string? correlation, Func<FinanceCreditAccountService, FinanceContext, Task<T>> call)
        {
            var company = new CompanyContext();
            company.Set(companyId);
            var collector = new AuditEventCollector();
            await using var context = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase(databaseName)
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).Options, company);
            var service = new FinanceCreditAccountService(context, company, new FixedUser(actor, companyId),
                permissions ?? new RecordingPermission(PermissionDecision.Allowed),
                new Candidates([new MoneyCurrencyCandidateV1(Currency, "TRY", "SENTETIK Lira")]),
                collector, Envelopes(), new FixedClock(), Settings(companyId), Lenders(companyId));
            return await call(service, new FinanceContext(companyId, actor, correlation ?? "SENTETIK-correlation", null));
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => At;
    }

    private sealed class SyntheticCreditSources(Guid company) : ISettingsPrerequisiteProvider, IFinanceCreditLenderPortV1
    {
        public long ProfileVersion { get; set; } = 1;
        public FinanceCreditLenderStatusV1 LenderStatus { get; set; } = FinanceCreditLenderStatusV1.Available;
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MoneyProfileV1(company, currencyId, "TRY", operation, new(Guid.Parse("f4710000-0000-4000-8000-000000000003"), ProfileVersion), 2, true, Start, null));
        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FinanceCreditLenderAnswerV1> ReadAsync(Guid companyId, Guid partyId, Guid companyPartyAccountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FinanceCreditLenderAnswerV1(companyId, partyId, companyPartyAccountId, LenderStatus, Lender, 1, 1));
    }

    private sealed class RecordingPermission(PermissionDecision decision) : IFinancePermissionPort
    {
        public List<FinanceAction> Actions { get; } = [];

        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken)
        {
            Actions.Add(action);
            return Task.FromResult(decision);
        }
    }

    /// <summary>A caller allowed exactly one action.</summary>
    private sealed class OnlyPermission(FinanceAction allowed) : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) =>
            Task.FromResult(action == allowed ? PermissionDecision.Allowed : PermissionDecision.Denied);
    }

    /// <summary>Allows <paramref name="revoked"/> for its first <paramref name="allowed"/> checks, then denies it.</summary>
    private sealed class RevokingPermission(FinanceAction revoked, int allowed) : IFinancePermissionPort
    {
        private int checks;

        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) =>
            Task.FromResult(action != revoked || Interlocked.Increment(ref checks) <= allowed
                ? PermissionDecision.Allowed
                : PermissionDecision.Denied);
    }

    private sealed class Candidates(IReadOnlyList<MoneyCurrencyCandidateV1> candidates) : ISettingsMoneyCandidateReaderV2
    {
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(
            string operation, CancellationToken cancellationToken = default) =>
            Task.FromResult(candidates);
    }

    private sealed class FixedUser(string userId, Guid companyId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { companyId };
        public bool CanAccessCompany(Guid company) => AllowedCompanyIds.Contains(company);
    }
}
