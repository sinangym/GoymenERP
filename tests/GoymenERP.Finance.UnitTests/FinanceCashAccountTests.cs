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
/// API-30 without a database: the cash account model (schema, filter, the maker-checker decision and
/// state-change checks, the live-name index, the command target), both forward-fix Downs, the write guard,
/// the payload hash and every refusal that must happen before anything is read. Passivation and
/// reactivation (Issue #66, 13 November 2026) also run through the real service in memory: the two-person
/// flow, the zero-balance seam, state refusals, replay, stale version, company isolation and authority lost
/// before the save. What SQL Server enforces is proven by the relational acceptance tests. SENTETIK / NOT_ENV.
/// </summary>
public sealed class FinanceCashAccountTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("12121212-1212-1212-1212-121212121212");
    private static readonly Guid Currency = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset At = new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
    private const string Actor = "SENTETIK-proposer";
    private const string Approver = "SENTETIK-approver";
    private const string Reader = "SENTETIK-reader";
    private const string Reason = "SENTETIK neden";
    private const string DeleteGuard = "Finans kayıtları silinmez.";
    private const string CompanyGuard = "Finans kaydı yalnız aktif şirket bağlamında ve o şirket için yazılır.";
    private const string StateGuard =
        "Kasa hesabı yalnız açılış kararıyla veya pasife alma/yeniden etkinleştirme öneri ve kararıyla değişir.";
    private const string FieldGuard = "Finans kaydının yalnız izinli alanı değişir; komut kayıtları değişmez.";

    [Fact]
    public void A_cash_account_is_a_company_row_without_money_whose_decision_is_held_by_the_database()
    {
        using var context = Context();
        var accounts = context.Model.FindEntityType(typeof(CashAccountRow))!;
        Assert.Equal((FinanceDbContext.Schema, "CashAccounts"), (accounts.GetSchema(), accounts.GetTableName()));
        Assert.Single(accounts.GetDeclaredQueryFilters());
        Assert.True(accounts.FindProperty(nameof(CashAccountRow.Version))!.IsConcurrencyToken);
        // A definition only: no balance, no opening balance, no money column.
        Assert.DoesNotContain(accounts.GetProperties(), property => property.ClrType == typeof(decimal)
            || property.ClrType == typeof(decimal?));

        var design = context.GetService<IDesignTimeModel>().Model;
        var designAccounts = design.FindEntityType(typeof(CashAccountRow))!;
        var decision = Assert.Single(designAccounts.GetCheckConstraints(), check => check.Name == "CK_CashAccounts_Decision");
        Assert.Equal(FinanceDbContext.CashAccountDecisionCheck, decision.Sql);
        Assert.Contains("[DecidedBy] COLLATE Latin1_General_100_BIN2 <> [ProposedBy] COLLATE Latin1_General_100_BIN2",
            decision.Sql, StringComparison.Ordinal);
        Assert.Equal(FinanceDbContext.IdentityCollation, designAccounts.FindProperty(nameof(CashAccountRow.ProposedBy))!.GetCollation());
        Assert.Equal(FinanceDbContext.IdentityCollation, designAccounts.FindProperty(nameof(CashAccountRow.DecidedBy))!.GetCollation());
        // An approved account may be passive (4); the opening decision stays as it was.
        Assert.Contains("[Status] IN (2, 4) AND [DecidedBy] IS NOT NULL", decision.Sql, StringComparison.Ordinal);

        // Passivation/reactivation: proposer and decider of the change differ (binary collation), the change
        // names its kind, and an approved change leaves the account in its target state.
        var stateChange = Assert.Single(designAccounts.GetCheckConstraints(), check => check.Name == "CK_CashAccounts_StateChange");
        Assert.Equal(FinanceDbContext.CashAccountStateChangeCheck, stateChange.Sql);
        Assert.Contains("[ChangeDecidedBy] COLLATE Latin1_General_100_BIN2 <> [ChangeProposedBy] COLLATE Latin1_General_100_BIN2",
            stateChange.Sql, StringComparison.Ordinal);
        // No NULL hole: a change row names its kind and status before anything else is compared.
        Assert.Contains("[ChangeKind] IS NOT NULL AND [ChangeStatus] IS NOT NULL", stateChange.Sql, StringComparison.Ordinal);
        Assert.Equal(FinanceDbContext.IdentityCollation,
            designAccounts.FindProperty(nameof(CashAccountRow.ChangeProposedBy))!.GetCollation());
        Assert.Equal(FinanceDbContext.IdentityCollation,
            designAccounts.FindProperty(nameof(CashAccountRow.ChangeDecidedBy))!.GetCollation());
        Assert.All(new[]
            {
                nameof(CashAccountRow.ChangeKind), nameof(CashAccountRow.ChangeStatus), nameof(CashAccountRow.ChangeProposedBy),
                nameof(CashAccountRow.ChangeProposedAtUtc), nameof(CashAccountRow.ChangeDecidedBy),
                nameof(CashAccountRow.ChangeDecidedAtUtc), nameof(CashAccountRow.ChangeRejectionReason),
            },
            name => Assert.True(designAccounts.FindProperty(name)!.IsNullable));

        // A proposed, approved or passive name is unique per company; a rejected proposal frees it.
        var liveName = Assert.Single(designAccounts.GetIndexes(), index => index.GetDatabaseName() == FinanceCashAccountService.LiveNameIndex);
        Assert.True(liveName.IsUnique);
        Assert.Equal([nameof(CashAccountRow.CompanyId), nameof(CashAccountRow.DisplayName)],
            liveName.Properties.Select(property => property.Name));
        Assert.Equal(FinanceDbContext.CashAccountLiveNameFilter, liveName.GetFilter());
        Assert.Equal("[Status] IN (1, 2, 4)", liveName.GetFilter());

        // The cash account's own command record names its target, or none only for a recorded refusal (ADR-011 §7).
        var commands = design.FindEntityType(typeof(CashAccountCommandRow))!;
        Assert.Equal((FinanceDbContext.Schema, "CashAccountCommands"), (commands.GetSchema(), commands.GetTableName()));
        var target = Assert.Single(commands.GetCheckConstraints(), check => check.Name == "CK_CashAccountCommands_Target");
        Assert.EndsWith("([CashAccountId] IS NULL AND JSON_VALUE([ResultJson], '$.outcome') = 'ValidationFailed')", target.Sql,
            StringComparison.Ordinal);
        Assert.Equal([nameof(CashAccountCommandRow.CompanyId), nameof(CashAccountCommandRow.CommandName),
            nameof(CashAccountCommandRow.IdempotencyKeyHash)], commands.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Single(context.Model.FindEntityType(typeof(CashAccountCommandRow))!.GetDeclaredQueryFilters());
    }

    [Fact]
    public void The_cash_account_migration_is_pure_DDL_up_and_refuses_to_drop_stored_accounts()
    {
        var migration = new AddCashAccounts();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        Assert.Equal(2, migration.UpOperations.OfType<CreateTableOperation>().Count());

        // Forward fix: the first Down operation is the one guard; only an empty store rolls back.
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
        Assert.Contains("THROW 51051", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[finance].[CashAccounts]", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[finance].[CashAccountCommands]", guard.Sql, StringComparison.Ordinal);
        Assert.Equal(["CashAccountCommands", "CashAccounts"],
            migration.DownOperations.OfType<DropTableOperation>().Select(operation => operation.Name));
    }

    [Fact]
    public void The_state_change_migration_is_pure_DDL_up_and_refuses_to_lose_a_passivation_or_reactivation()
    {
        var migration = new AddCashAccountStateChanges();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var added = migration.UpOperations.OfType<AddColumnOperation>().ToList();
        Assert.Equal(7, added.Count);
        Assert.All(added, column => Assert.True(column.IsNullable));
        var liveName = Assert.Single(migration.UpOperations.OfType<CreateIndexOperation>());
        Assert.Equal((FinanceCashAccountService.LiveNameIndex, FinanceDbContext.CashAccountLiveNameFilter), (liveName.Name, liveName.Filter));
        Assert.Equal(new[]
            {
                ("CK_CashAccounts_Decision", FinanceDbContext.CashAccountDecisionCheck),
                ("CK_CashAccounts_StateChange", FinanceDbContext.CashAccountStateChangeCheck),
            },
            migration.UpOperations.OfType<AddCheckConstraintOperation>().Select(check => (check.Name, check.Sql)));

        // Forward fix: the first Down operation is the one guard; a store whose accounts never left their opening
        // decision rolls back to the old index and decision check.
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
        Assert.Contains("THROW 51051", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[Status] = 4 OR [ChangeKind] IS NOT NULL", guard.Sql, StringComparison.Ordinal);
        Assert.Equal(7, migration.DownOperations.OfType<DropColumnOperation>().Count());
        Assert.Equal(["CK_CashAccounts_Decision", "CK_CashAccounts_StateChange"],
            migration.DownOperations.OfType<DropCheckConstraintOperation>().Select(check => check.Name));
        Assert.Equal("[Status] IN (1, 2)", Assert.Single(migration.DownOperations.OfType<CreateIndexOperation>()).Filter);
        Assert.Contains("[Status] = 2 AND [DecidedBy] IS NOT NULL",
            Assert.Single(migration.DownOperations.OfType<AddCheckConstraintOperation>()).Sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("delete", DeleteGuard)]
    [InlineData("edit-name", FieldGuard)]
    [InlineData("edit-currency", FieldGuard)]
    [InlineData("edit-proposer", FieldGuard)]
    [InlineData("self-approval", StateGuard)]
    [InlineData("reject-without-reason", StateGuard)]
    [InlineData("approve-with-reason", StateGuard)]
    [InlineData("redecide", StateGuard)]
    [InlineData("no-version-step", StateGuard)]
    [InlineData("skips-version", StateGuard)]
    [InlineData("other-company", CompanyGuard)]
    public void An_account_changes_only_by_one_decision_of_another_actor(string fault, string message)
    {
        using var context = Context();
        switch (fault)
        {
            case "delete":
            {
                context.CashAccounts.Remove(Attach(context, Account()));
                break;
            }
            case "edit-name":
            {
                var row = Attach(context, Account());
                Decide(row, 2, Approver, null);
                row.DisplayName = "SENTETIK başka kasa";
                break;
            }
            case "edit-currency":
            {
                var row = Attach(context, Account());
                Decide(row, 2, Approver, null);
                row.CurrencyId = Guid.NewGuid();
                break;
            }
            case "edit-proposer":
            {
                var row = Attach(context, Account());
                Decide(row, 2, Approver, null);
                row.ProposedBy = "SENTETIK-other";
                break;
            }
            case "self-approval":
            {
                Decide(Attach(context, Account()), 2, Actor, null);
                break;
            }
            case "reject-without-reason":
            {
                Decide(Attach(context, Account()), 3, Approver, null);
                break;
            }
            case "approve-with-reason":
            {
                Decide(Attach(context, Account()), 2, Approver, Reason);
                break;
            }
            case "redecide":
            {
                Decide(Attach(context, Approved()), 3, Approver, Reason);
                break;
            }
            case "no-version-step":
            {
                var row = Attach(context, Account());
                Decide(row, 2, Approver, null);
                row.Version -= 1;
                break;
            }
            case "skips-version":
            {
                var row = Attach(context, Account());
                Decide(row, 2, Approver, null);
                row.Version += 1;
                break;
            }
            case "other-company":
            {
                var row = Account();
                row.CompanyId = CompanyB;
                context.CashAccounts.Add(row);
                break;
            }
            default:
            {
                throw new ArgumentOutOfRangeException(nameof(fault));
            }
        }

        Assert.Equal(message, Assert.Throws<InvalidOperationException>(() => context.SaveChanges()).Message);
    }

    [Theory]
    [InlineData("self-decided-change", StateGuard)]
    [InlineData("wrong-target", StateGuard)]
    [InlineData("approve-change-with-reason", StateGuard)]
    [InlineData("reject-change-without-reason", StateGuard)]
    [InlineData("reject-change-moves-state", StateGuard)]
    [InlineData("decide-change-of-other-kind", StateGuard)]
    [InlineData("replace-pending-proposal", StateGuard)]
    [InlineData("edit-decided-change-proposer", StateGuard)]
    [InlineData("redecide-change", StateGuard)]
    [InlineData("change-on-proposal", StateGuard)]
    [InlineData("change-on-rejected", StateGuard)]
    [InlineData("reactivation-of-active", StateGuard)]
    [InlineData("passivation-of-passive", StateGuard)]
    [InlineData("redecide-opening-with-proposal", StateGuard)]
    [InlineData("passivate-without-proposal", StateGuard)]
    [InlineData("propose-moves-state", StateGuard)]
    [InlineData("propose-with-decision", StateGuard)]
    [InlineData("change-without-version-step", StateGuard)]
    [InlineData("change-skips-version", StateGuard)]
    [InlineData("rename-with-proposal", FieldGuard)]
    public void A_passivation_or_reactivation_moves_only_by_one_proposal_and_one_decision_of_another_actor(string fault,
        string message)
    {
        using var context = Context();
        var row = Attach(context, fault switch
        {
            "self-decided-change" or "wrong-target" or "approve-change-with-reason" or "reject-change-without-reason"
                or "reject-change-moves-state" or "decide-change-of-other-kind" or "replace-pending-proposal" => PendingPassivation(),
            "edit-decided-change-proposer" or "redecide-change" or "passivation-of-passive" => Passive(),
            "change-on-proposal" => Account(),
            "change-on-rejected" => Rejected(),
            _ => Approved(),
        });
        switch (fault)
        {
            case "self-decided-change":
            {
                DecideChange(row, 2, 4, Actor, null);
                break;
            }
            case "wrong-target":
            {
                DecideChange(row, 2, 2, Approver, null);
                break;
            }
            case "approve-change-with-reason":
            {
                DecideChange(row, 2, 4, Approver, Reason);
                break;
            }
            case "reject-change-without-reason":
            {
                DecideChange(row, 3, 2, Approver, null);
                break;
            }
            case "reject-change-moves-state":
            {
                DecideChange(row, 3, 4, Approver, Reason);
                break;
            }
            case "decide-change-of-other-kind":
            {
                DecideChange(row, 2, 4, Approver, null);
                row.ChangeKind = 2;
                break;
            }
            case "edit-decided-change-proposer":
            {
                row.ChangeProposedBy = "SENTETIK-other";
                row.Version += 1;
                break;
            }
            case "redecide-change":
            {
                DecideChange(row, 3, 4, Approver, Reason);
                break;
            }
            case "reactivation-of-active":
            {
                ProposeChange(row, 2);
                break;
            }
            case "replace-pending-proposal" or "change-on-proposal" or "change-on-rejected" or "passivation-of-passive":
            {
                ProposeChange(row, 1);
                break;
            }
            case "redecide-opening-with-proposal":
            {
                ProposeChange(row, 1);
                row.DecidedBy = "SENTETIK-other";
                break;
            }
            case "passivate-without-proposal":
            {
                row.Status = 4;
                row.Version += 1;
                break;
            }
            case "propose-moves-state":
            {
                ProposeChange(row, 1);
                row.Status = 4;
                break;
            }
            case "propose-with-decision":
            {
                ProposeChange(row, 1);
                row.ChangeDecidedBy = Approver;
                break;
            }
            case "change-without-version-step":
            {
                ProposeChange(row, 1);
                row.Version -= 1;
                break;
            }
            case "change-skips-version":
            {
                ProposeChange(row, 1);
                row.Version += 1;
                break;
            }
            case "rename-with-proposal":
            {
                ProposeChange(row, 1);
                row.DisplayName = "SENTETIK başka kasa";
                break;
            }
            default:
            {
                throw new ArgumentOutOfRangeException(nameof(fault));
            }
        }

        Assert.Equal(message, Assert.Throws<InvalidOperationException>(() => context.SaveChanges()).Message);
    }

    [Fact]
    public async Task Every_legal_opening_and_state_change_step_passes_the_write_guard()
    {
        await using var run = new Run();
        var account = await run.SeedAsync(Account());
        // Opening approval, passivation, a rejected and then an approved reactivation, a rejected passivation.
        Action<CashAccountRow>[] steps =
        [
            row => Decide(row, 2, Approver, null),
            row => ProposeChange(row, 1),
            row => DecideChange(row, 2, 4, Approver, null),
            row => ProposeChange(row, 2),
            row => DecideChange(row, 3, 4, Approver, Reason),
            row => ProposeChange(row, 2),
            row => DecideChange(row, 2, 2, Approver, null),
            row => ProposeChange(row, 1),
            row => DecideChange(row, 3, 2, Approver, Reason),
        ];
        foreach (var step in steps)
        {
            await using var context = run.Memory(CompanyA);
            step(await context.CashAccounts.SingleAsync(candidate => candidate.Id == account.Id));
            await context.SaveChangesAsync();
        }

        var stored = await run.RowAsync(account.Id);
        Assert.Equal((2, 10L, (int?)1, (int?)3), (stored.Status, stored.Version, stored.ChangeKind, stored.ChangeStatus));
    }

    [Fact]
    public void The_payload_hash_names_the_command_and_every_field()
    {
        var propose = new CashAccountProposeRequest("SENTETIK kasa", Currency);
        var hash = FinanceCashAccountService.PayloadHash(FinanceCashAccountService.ProposeCommand, propose);
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, FinanceCashAccountService.PayloadHash(FinanceCashAccountService.ProposeCommand,
            new CashAccountProposeRequest("SENTETIK kasa", Currency)));
        Assert.NotEqual(hash, FinanceCashAccountService.PayloadHash(FinanceCashAccountService.ProposeCommand,
            propose with { DisplayName = "SENTETIK kasa 2" }));
        Assert.NotEqual(hash, FinanceCashAccountService.PayloadHash(FinanceCashAccountService.ProposeCommand,
            propose with { CurrencyId = Guid.NewGuid() }));
        Assert.NotEqual(hash, FinanceCashAccountService.PayloadHash(FinanceCashAccountService.ApproveCommand, propose));

        var account = Guid.NewGuid();
        var reject = new CashAccountRejectRequest(account, "1", "SENTETIK neden");
        Assert.NotEqual(FinanceCashAccountService.PayloadHash(FinanceCashAccountService.RejectCommand, reject),
            FinanceCashAccountService.PayloadHash(FinanceCashAccountService.RejectCommand, reject with { Reason = "SENTETIK başka" }));
        Assert.NotEqual(FinanceCashAccountService.PayloadHash(FinanceCashAccountService.ApproveCommand,
                new CashAccountApproveRequest(account, "1")),
            FinanceCashAccountService.PayloadHash(FinanceCashAccountService.RejectCommand, new CashAccountApproveRequest(account, "1")));
    }

    [Theory]
    [InlineData("other-actor", CommandOutcome.Denied, null)]
    [InlineData("other-company", CommandOutcome.Denied, null)]
    [InlineData("permission-denied", CommandOutcome.Denied, null)]
    [InlineData("permission-unavailable", CommandOutcome.Unavailable, "finance_permission_mapping_unavailable")]
    [InlineData("key-with-space", CommandOutcome.ValidationFailed, "idempotency_key_invalid")]
    [InlineData("null-name", CommandOutcome.ValidationFailed, "display_name_invalid")]
    public async Task A_proposal_is_refused_before_anything_is_read(string fault, CommandOutcome outcome, string? code)
    {
        var permission = fault switch
        {
            "permission-denied" => PermissionDecision.Denied,
            "permission-unavailable" => PermissionDecision.Unavailable,
            _ => PermissionDecision.Allowed,
        };
        await using var context = Context();
        var permissions = new RecordingPermission(permission);
        var finance = new FinanceContext(fault == "other-company" ? CompanyB : CompanyA,
            fault == "other-actor" ? "SENTETIK-someone-else" : Actor, "SENTETIK-correlation", null);
        var key = new CommandMetadata(fault == "key-with-space" ? " k-1" : "k-1");
        var request = new CashAccountProposeRequest(fault == "null-name" ? null! : "SENTETIK kasa", Currency);

        // The context's database is unreachable: any read would throw, so a result proves nothing was read.
        var result = await Service(context, permissions).ProposeAsync(finance, key, request, CancellationToken.None);

        Assert.Equal(outcome, result.Outcome);
        Assert.Null(result.Resource);
        if (code is not null)
        {
            Assert.Equal(code, Assert.Single(result.Issues).Code);
        }

        Assert.All(permissions.Actions, action => Assert.Equal(FinanceAction.ProposeCashAccount, action));
    }

    [Theory]
    [InlineData("approve", "empty-account", "accountId", "identity_required")]
    [InlineData("approve", "null-version", "expectedVersion", "identity_required")]
    [InlineData("approve", "padded-version", "expectedVersion", "identity_required")]
    [InlineData("approve", "blank-version", "expectedVersion", "identity_required")]
    [InlineData("reject", "empty-account", "accountId", "identity_required")]
    [InlineData("reject", "padded-version", "expectedVersion", "identity_required")]
    [InlineData("reject", "null-reason", "reason", "reason_invalid")]
    [InlineData("reject", "blank-reason", "reason", "reason_invalid")]
    [InlineData("reject", "padded-reason", "reason", "reason_invalid")]
    [InlineData("reject", "long-reason", "reason", "finance_value_does_not_fit")]
    public async Task A_malformed_decision_names_its_own_field_before_anything_is_read(string command, string fault, string field,
        string code)
    {
        await using var context = Context();
        var permissions = new RecordingPermission(PermissionDecision.Allowed);
        var service = Service(context, permissions, actor: Approver);
        var account = fault == "empty-account" ? Guid.Empty : Guid.NewGuid();
        var version = fault switch
        {
            "null-version" => null!,
            "padded-version" => " 1",
            "blank-version" => " ",
            _ => "1",
        };
        var reason = fault switch
        {
            "null-reason" => null!,
            "blank-reason" => "  ",
            "padded-reason" => "SENTETIK neden ",
            "long-reason" => new string('n', 501),
            _ => "SENTETIK neden",
        };
        var finance = new FinanceContext(CompanyA, Approver, "SENTETIK-correlation", null);

        // The context's database is unreachable: a result proves nothing was read.
        var result = command == "approve"
            ? await service.ApproveAsync(finance, new CommandMetadata("k-1"), new CashAccountApproveRequest(account, version),
                CancellationToken.None)
            : await service.RejectAsync(finance, new CommandMetadata("k-1"), new CashAccountRejectRequest(account, version, reason),
                CancellationToken.None);

        Assert.Equal(CommandOutcome.ValidationFailed, result.Outcome);
        var issue = Assert.Single(result.Issues);
        Assert.Equal((field, code), (issue.Field, issue.Code));
        // A rejection is the other answer of the same decision: the approve authority, never a separate one.
        Assert.All(permissions.Actions, action => Assert.Equal(FinanceAction.ApproveCashAccount, action));
    }

    [Theory]
    [InlineData("permission-denied", ReadOutcome.Denied)]
    [InlineData("permission-unavailable", ReadOutcome.Unavailable)]
    [InlineData("other-company", ReadOutcome.Denied)]
    public async Task A_read_is_refused_before_anything_is_read(string fault, ReadOutcome outcome)
    {
        var permission = fault switch
        {
            "permission-denied" => PermissionDecision.Denied,
            "permission-unavailable" => PermissionDecision.Unavailable,
            _ => PermissionDecision.Allowed,
        };
        await using var context = Context();
        var service = Service(context, new RecordingPermission(permission));
        var finance = new FinanceContext(fault == "other-company" ? CompanyB : CompanyA, Actor, "SENTETIK-correlation", null);

        Assert.Equal(outcome, (await service.ListAsync(finance, CancellationToken.None)).Outcome);
        Assert.Equal(outcome, (await service.GetAsync(finance, Guid.NewGuid(), CancellationToken.None)).Outcome);
        Assert.Equal(outcome, (await service.CurrencyCandidatesAsync(finance, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task The_currency_candidates_are_Settings_answer_as_given_and_never_a_default()
    {
        await using var context = Context();
        var finance = new FinanceContext(CompanyA, Actor, "SENTETIK-correlation", null);
        MoneyCurrencyCandidateV1[] given =
        [
            new MoneyCurrencyCandidateV1(Guid.Parse("33333333-3333-3333-3333-333333333333"), "USD", "SENTETIK Dolar"),
            new MoneyCurrencyCandidateV1(Currency, "TRY", "SENTETIK Lira"),
        ];
        var permissions = new RecordingPermission(PermissionDecision.Allowed);

        var found = await Service(context, permissions, new Candidates(given)).CurrencyCandidatesAsync(finance, CancellationToken.None);

        Assert.Equal(ReadOutcome.Found, found.Outcome);
        Assert.Equal(CompanyA, found.Resource!.CompanyId);
        // Order kept, nothing added, nothing picked.
        Assert.Equal(given.Select(item => (item.CurrencyId, item.Code, item.DisplayLabel)),
            found.Resource.Items.Select(item => (item.CurrencyId, item.Code, item.DisplayLabel)));
        // The propose authority is measured before Settings and again after it.
        Assert.Equal([FinanceAction.ProposeCashAccount, FinanceAction.ProposeCashAccount], permissions.Actions);

        // No candidates is an honest empty list, not a made-up one.
        var empty = await Service(context, new RecordingPermission(PermissionDecision.Allowed), new Candidates([]))
            .CurrencyCandidatesAsync(finance, CancellationToken.None);
        Assert.Equal(ReadOutcome.Found, empty.Outcome);
        Assert.Empty(empty.Resource!.Items);
    }

    [Theory]
    [InlineData("settings_provider_unavailable", ReadOutcome.Unavailable)]
    [InlineData("settings_access_denied", ReadOutcome.Denied)]
    [InlineData("malformed", ReadOutcome.Unavailable)]
    public async Task An_unusable_candidate_source_is_refused_never_emptied(string fault, ReadOutcome outcome)
    {
        await using var context = Context();
        ISettingsMoneyCandidateReader reader = fault == "malformed"
            ? new Candidates([new MoneyCurrencyCandidateV1(Currency, "TRY", "A"), new MoneyCurrencyCandidateV1(Currency, "TRY", "B")])
            : new FailingCandidates(fault);

        var result = await Service(context, new RecordingPermission(PermissionDecision.Allowed), reader).CurrencyCandidatesAsync(
            new FinanceContext(CompanyA, Actor, "SENTETIK-correlation", null), CancellationToken.None);

        Assert.Equal(outcome, result.Outcome);
        Assert.Null(result.Resource);
    }

    [Fact]
    public void The_state_change_payload_hash_names_the_command_the_account_and_the_version()
    {
        var account = Guid.NewGuid();
        var request = new CashAccountStateChangeRequest(account, "2");
        var hashes = new[]
            {
                FinanceCashAccountService.ProposePassivationCommand, FinanceCashAccountService.ApprovePassivationCommand,
                FinanceCashAccountService.ProposeReactivationCommand, FinanceCashAccountService.ApproveReactivationCommand,
            }
            .Select(command => FinanceCashAccountService.PayloadHash(command, request))
            .Append(FinanceCashAccountService.PayloadHash(FinanceCashAccountService.ApproveCommand,
                new CashAccountApproveRequest(account, "2")))
            .ToList();
        Assert.All(hashes, hash => Assert.Equal(64, hash.Length));
        Assert.Equal(5, hashes.Distinct(StringComparer.Ordinal).Count());
        Assert.NotEqual(hashes[0], FinanceCashAccountService.PayloadHash(FinanceCashAccountService.ProposePassivationCommand,
            request with { ExpectedVersion = "3" }));
        Assert.NotEqual(hashes[0], FinanceCashAccountService.PayloadHash(FinanceCashAccountService.ProposePassivationCommand,
            request with { AccountId = Guid.NewGuid() }));

        var reject = new CashAccountStateChangeRejectRequest(account, "3", Reason);
        var rejectHash = FinanceCashAccountService.PayloadHash(FinanceCashAccountService.RejectPassivationCommand, reject);
        Assert.NotEqual(rejectHash, FinanceCashAccountService.PayloadHash(FinanceCashAccountService.RejectPassivationCommand,
            reject with { Reason = "SENTETIK başka" }));
        Assert.NotEqual(rejectHash, FinanceCashAccountService.PayloadHash(FinanceCashAccountService.RejectReactivationCommand, reject));
        Assert.NotEqual(rejectHash, FinanceCashAccountService.PayloadHash(FinanceCashAccountService.RejectCommand,
            new CashAccountRejectRequest(account, "3", Reason)));
    }

    [Fact]
    public async Task Without_cash_movements_every_account_reads_a_zero_balance()
    {
        // The only reading there is until cash movements exist (rule 2's seam); it reads nothing.
        var reader = new NoCashMovementBalanceReader();
        Assert.Equal(CashAccountBalanceState.Zero, await reader.ReadAsync(CompanyA, Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(CashAccountBalanceState.Zero, await reader.ReadAsync(CompanyB, Guid.NewGuid(), CancellationToken.None));
    }

    public static TheoryData<CashAccountStateChange, string, string, CommandOutcome, string?, string?> EarlyRefusals
    {
        get
        {
            var data = new TheoryData<CashAccountStateChange, string, string, CommandOutcome, string?, string?>();
            foreach (var change in new[] { CashAccountStateChange.Passivation, CashAccountStateChange.Reactivation })
            {
                foreach (var step in new[] { "propose", "approve", "reject" })
                {
                    data.Add(change, step, "permission-denied", CommandOutcome.Denied, null, null);
                    data.Add(change, step, "permission-unavailable", CommandOutcome.Unavailable, "permission",
                        "finance_permission_mapping_unavailable");
                    data.Add(change, step, "other-company", CommandOutcome.Denied, null, null);
                    data.Add(change, step, "other-actor", CommandOutcome.Denied, null, null);
                    data.Add(change, step, "key-with-space", CommandOutcome.ValidationFailed, "idempotencyKey",
                        "idempotency_key_invalid");
                    data.Add(change, step, "empty-account", CommandOutcome.ValidationFailed, "accountId", "identity_required");
                    data.Add(change, step, "padded-version", CommandOutcome.ValidationFailed, "expectedVersion",
                        "identity_required");
                }

                data.Add(change, "reject", "blank-reason", CommandOutcome.ValidationFailed, "reason", "reason_invalid");
                data.Add(change, "reject", "long-reason", CommandOutcome.ValidationFailed, "reason", "finance_value_does_not_fit");
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(EarlyRefusals))]
    public async Task A_state_change_is_refused_before_anything_is_read(CashAccountStateChange change, string step, string fault,
        CommandOutcome outcome, string? field, string? code)
    {
        var permission = fault switch
        {
            "permission-denied" => PermissionDecision.Denied,
            "permission-unavailable" => PermissionDecision.Unavailable,
            _ => PermissionDecision.Allowed,
        };
        await using var context = Context();
        var permissions = new RecordingPermission(permission);
        var actor = step == "propose" ? Actor : Approver;
        var service = Service(context, permissions, actor: actor);
        var finance = new FinanceContext(fault == "other-company" ? CompanyB : CompanyA,
            fault == "other-actor" ? "SENTETIK-someone-else" : actor, "SENTETIK-correlation", null);
        var metadata = new CommandMetadata(fault == "key-with-space" ? " k-1" : "k-1");
        var account = fault == "empty-account" ? Guid.Empty : Guid.NewGuid();
        var version = fault == "padded-version" ? " 2" : "2";
        var reason = fault switch
        {
            "blank-reason" => "  ",
            "long-reason" => new string('n', 501),
            _ => Reason,
        };

        // The context's database is unreachable and the balance reader throws: a result proves nothing was read.
        var result = step switch
        {
            "propose" => await service.ProposeStateChangeAsync(finance, metadata, change,
                new CashAccountStateChangeRequest(account, version), CancellationToken.None),
            "approve" => await service.ApproveStateChangeAsync(finance, metadata, change,
                new CashAccountStateChangeRequest(account, version), CancellationToken.None),
            _ => await service.RejectStateChangeAsync(finance, metadata, change,
                new CashAccountStateChangeRejectRequest(account, version, reason), CancellationToken.None),
        };

        Assert.Equal(outcome, result.Outcome);
        Assert.Null(result.Resource);
        Assert.False(result.IsReplay);
        if (code is null)
        {
            Assert.Empty(result.Issues);
        }
        else
        {
            var issue = Assert.Single(result.Issues);
            Assert.Equal((field, code), ((string?)issue.Field, (string?)issue.Code));
        }

        // Another actor or company is refused before any authority is asked; otherwise only the step's own
        // authority is (a rejection is the other answer of the kind's decision: its approve authority).
        if (fault is "other-company" or "other-actor")
        {
            Assert.Empty(permissions.Actions);
        }
        else
        {
            Assert.Equal(ActionOf(change, step), Assert.Single(permissions.Actions));
        }
    }

    [Fact]
    public async Task An_active_account_is_passivated_and_reactivated_by_two_people_and_stays_readable_while_passive()
    {
        await using var run = new Run();
        var account = (await run.SeedAsync(Approved())).Id;
        var balances = new FixedBalance(CashAccountBalanceState.Zero);

        var proposed = await run.StepAsync(Actor, CashAccountStateChange.Passivation, "propose", account, "2", balances: balances);
        Assert.Equal(CommandOutcome.Succeeded, proposed.Outcome);
        // The account does not move until the change is approved.
        Assert.Equal(("3", CashAccountStatus.Approved, true, (CashAccountStateChange?)CashAccountStateChange.Passivation),
            (proposed.Resource!.Version, proposed.Resource.Status, proposed.Resource.IsActive, proposed.Resource.PendingChange));

        AssertIssue(await run.StepAsync(Actor, CashAccountStateChange.Passivation, "approve", account, "3", balances: balances),
            CommandOutcome.Denied, "approval", "finance_approver_is_proposer");

        var passive = await run.StepAsync(Approver, CashAccountStateChange.Passivation, "approve", account, "3", balances: balances);
        Assert.Equal(CommandOutcome.Succeeded, passive.Outcome);
        Assert.Equal(("4", CashAccountStatus.Passive, false, (CashAccountStateChange?)null),
            (passive.Resource!.Version, passive.Resource.Status, passive.Resource.IsActive, passive.Resource.PendingChange));
        // The balance was read for this company's account at the proposal and again at the approval.
        Assert.Equal(new[] { (CompanyA, account), (CompanyA, account) }, balances.Seen);

        // Passive is not active, but a read-only caller still sees it with its state.
        var listed = await run.ListAsync(Reader, new ReadOnlyPermission());
        Assert.Equal(ReadOutcome.Found, listed.Outcome);
        var item = Assert.Single(listed.Resource!.Items);
        Assert.Equal((account, CashAccountStatus.Passive, false), (item.Account.Id, item.Account.Status, item.Account.IsActive));
        var got = await run.GetAsync(Reader, account, new ReadOnlyPermission());
        Assert.Equal((ReadOutcome.Found, (CashAccountStatus?)CashAccountStatus.Passive), (got.Outcome, got.Resource?.Status));

        // A reactivation never reads the balance.
        var reactivation = await run.StepAsync(Actor, CashAccountStateChange.Reactivation, "propose", account, "4",
            balances: new NoBalanceRead());
        Assert.Equal(("5", CashAccountStatus.Passive, (CashAccountStateChange?)CashAccountStateChange.Reactivation),
            (reactivation.Resource!.Version, reactivation.Resource.Status, reactivation.Resource.PendingChange));
        Assert.True(Assert.Single((await run.ListAsync(Actor)).Resource!.Items).IsOwnPendingChange);
        Assert.False(Assert.Single((await run.ListAsync(Approver)).Resource!.Items).IsOwnPendingChange);

        var active = await run.StepAsync(Approver, CashAccountStateChange.Reactivation, "approve", account, "5",
            balances: new NoBalanceRead());
        Assert.Equal(("6", CashAccountStatus.Approved, true, (CashAccountStateChange?)null),
            (active.Resource!.Version, active.Resource.Status, active.Resource.IsActive, active.Resource.PendingChange));

        var audits = await run.AuditsAsync(account);
        Assert.Equal(new[]
            {
                ("finance.cash-account.passivation-proposed.v1", "Propose", ErpPermissions.FinanceCashAccountPassivationPropose,
                    Actor, (long?)2, (long?)3),
                ("finance.cash-account.passivation-approved.v1", "Approve", ErpPermissions.FinanceCashAccountPassivationApprove,
                    Approver, (long?)3, (long?)4),
                ("finance.cash-account.reactivation-proposed.v1", "Propose", ErpPermissions.FinanceCashAccountReactivationPropose,
                    Actor, (long?)4, (long?)5),
                ("finance.cash-account.reactivation-approved.v1", "Approve", ErpPermissions.FinanceCashAccountReactivationApprove,
                    Approver, (long?)5, (long?)6),
            },
            audits.Select(audit => (audit.EventType, audit.Operation, audit.PermissionCode, audit.ActorId, audit.EntityVersionBefore,
                audit.EntityVersionAfter)));
        Assert.All(audits, audit => Assert.Equal(SourceChannels.Web, audit.SourceChannel));
        Assert.Null(audits[0].ApprovalContextJson);
        Assert.Null(audits[2].ApprovalContextJson);
        foreach (var decision in new[] { audits[1], audits[3] })
        {
            using var approval = JsonDocument.Parse(decision.ApprovalContextJson!);
            Assert.Equal(("O4", Actor, Approver), (approval.RootElement.GetProperty("rule").GetString(),
                approval.RootElement.GetProperty("proposedBy").GetString(), approval.RootElement.GetProperty("approvedBy").GetString()));
        }
    }

    [Theory]
    [InlineData("propose", CashAccountBalanceState.NotZero, CommandOutcome.ValidationFailed, "cash_account_balance_not_zero")]
    [InlineData("propose", CashAccountBalanceState.Unavailable, CommandOutcome.Unavailable, "cash_account_balance_unavailable")]
    [InlineData("approve", CashAccountBalanceState.NotZero, CommandOutcome.ValidationFailed, "cash_account_balance_not_zero")]
    [InlineData("approve", CashAccountBalanceState.Unavailable, CommandOutcome.Unavailable, "cash_account_balance_unavailable")]
    public async Task A_passivation_is_refused_unless_the_balance_reads_zero(string step, CashAccountBalanceState balance,
        CommandOutcome outcome, string code)
    {
        await using var run = new Run();
        var seeded = await run.SeedAsync(step == "propose" ? Approved() : PendingPassivation());
        var before = await run.SnapshotAsync(seeded.Id);
        var reader = new FixedBalance(balance);

        var result = await run.StepAsync(step == "propose" ? Actor : Approver, CashAccountStateChange.Passivation, step, seeded.Id,
            FinanceCashAccountService.VersionText(seeded.Version), balances: reader);

        AssertIssue(result, outcome, "accountId", code);
        Assert.Equal((CompanyA, seeded.Id), Assert.Single(reader.Seen));
        // Not stored, not audited, the account unchanged: the same request may come again once the balance is zero.
        Assert.Equal(before, await run.SnapshotAsync(seeded.Id));
    }

    [Fact]
    public async Task A_state_change_needs_its_starting_state_one_pending_change_at_a_time_and_a_rejection_keeps_the_account()
    {
        await using var run = new Run();
        var account = (await run.SeedAsync(Approved())).Id;
        var noBalance = new NoBalanceRead();

        AssertIssue(await run.StepAsync(Actor, CashAccountStateChange.Reactivation, "propose", account, "2", balances: noBalance),
            CommandOutcome.ValidationFailed, "accountId", "cash_account_not_passive");
        AssertIssue(await run.StepAsync(Approver, CashAccountStateChange.Passivation, "approve", account, "2", balances: noBalance),
            CommandOutcome.ValidationFailed, "accountId", "cash_account_change_not_pending");
        AssertIssue(await run.StepAsync(Approver, CashAccountStateChange.Passivation, "reject", account, "2", balances: noBalance),
            CommandOutcome.ValidationFailed, "accountId", "cash_account_change_not_pending");

        Assert.Equal("3", (await run.StepAsync(Actor, CashAccountStateChange.Passivation, "propose", account, "2")).Resource!.Version);
        AssertIssue(await run.StepAsync(Actor, CashAccountStateChange.Passivation, "propose", account, "3", balances: noBalance),
            CommandOutcome.ValidationFailed, "accountId", "cash_account_change_pending");
        AssertIssue(await run.StepAsync(Approver, CashAccountStateChange.Reactivation, "approve", account, "3", balances: noBalance),
            CommandOutcome.ValidationFailed, "accountId", "cash_account_change_not_pending");

        var rejected = await run.StepAsync(Approver, CashAccountStateChange.Passivation, "reject", account, "3", balances: noBalance);
        Assert.Equal(("4", CashAccountStatus.Approved, true, (CashAccountStateChange?)null),
            (rejected.Resource!.Version, rejected.Resource.Status, rejected.Resource.IsActive, rejected.Resource.PendingChange));
        var row = await run.RowAsync(account);
        Assert.Equal((2, (int?)1, (int?)3, (string?)Approver, (string?)Reason),
            (row.Status, row.ChangeKind, row.ChangeStatus, row.ChangeDecidedBy, row.ChangeRejectionReason));
        var rejection = (await run.AuditsAsync(account))[^1];
        Assert.Equal(("finance.cash-account.passivation-rejected.v1", "Reject", ErpPermissions.FinanceCashAccountPassivationApprove,
            Approver), (rejection.EventType, rejection.Operation, rejection.PermissionCode, rejection.ActorId));
        Assert.EndsWith(": " + Reason, rejection.Reason, StringComparison.Ordinal);
        using (var decision = JsonDocument.Parse(rejection.ApprovalContextJson!))
        {
            Assert.Equal(Approver, decision.RootElement.GetProperty("rejectedBy").GetString());
            Assert.False(decision.RootElement.TryGetProperty("approvedBy", out _));
        }

        // After a rejection the account may be proposed again; once passive it is no longer passivated.
        Assert.Equal("5", (await run.StepAsync(Actor, CashAccountStateChange.Passivation, "propose", account, "4")).Resource!.Version);
        Assert.Equal(CashAccountStatus.Passive,
            (await run.StepAsync(Approver, CashAccountStateChange.Passivation, "approve", account, "5")).Resource!.Status);
        AssertIssue(await run.StepAsync(Actor, CashAccountStateChange.Passivation, "propose", account, "6", balances: noBalance),
            CommandOutcome.ValidationFailed, "accountId", "cash_account_not_active");

        // Only the four accepted steps are stored; a state refusal is answered, not recorded.
        Assert.Equal(4, (await run.CommandsAsync()).Count);
    }

    [Fact]
    public async Task A_state_change_replays_by_key_records_a_stale_version_and_never_reaches_another_company()
    {
        await using var run = new Run();
        var account = (await run.SeedAsync(Approved())).Id;
        var key = Key();

        var first = await run.StepAsync(Actor, CashAccountStateChange.Passivation, "propose", account, "2", key: key,
            correlation: "SENTETIK-first");
        Assert.Equal((CommandOutcome.Succeeded, false), (first.Outcome, first.IsReplay));
        var replay = await run.StepAsync(Actor, CashAccountStateChange.Passivation, "propose", account, "2", key: key,
            correlation: "SENTETIK-second");
        Assert.Equal((CommandOutcome.Succeeded, true, "SENTETIK-first", "3"),
            (replay.Outcome, replay.IsReplay, replay.OriginalCorrelationId, replay.Resource!.Version));

        // Another body or another actor under the key discloses nothing.
        var otherBody = await run.StepAsync(Actor, CashAccountStateChange.Passivation, "propose", account, "3", key: key);
        Assert.Equal(CommandOutcome.IdempotencyConflict, otherBody.Outcome);
        Assert.Empty(otherBody.Issues);
        var otherActor = await run.StepAsync(Approver, CashAccountStateChange.Passivation, "propose", account, "2", key: key);
        Assert.Equal(CommandOutcome.IdempotencyConflict, otherActor.Outcome);
        Assert.Empty(otherActor.Issues);

        // A stale version is a recorded answer: the same key replays it.
        var staleKey = Key();
        AssertIssue(await run.StepAsync(Approver, CashAccountStateChange.Passivation, "approve", account, "2", key: staleKey),
            CommandOutcome.VersionConflict, "expectedVersion", "version_conflict");
        var staleReplay = await run.StepAsync(Approver, CashAccountStateChange.Passivation, "approve", account, "2", key: staleKey);
        Assert.Equal((CommandOutcome.VersionConflict, true), (staleReplay.Outcome, staleReplay.IsReplay));

        // Another company's account is the same answer as no account; that company's row does not move.
        var other = Approved();
        other.CompanyId = CompanyB;
        await run.SeedAsync(other);
        AssertIssue(await run.StepAsync(Actor, CashAccountStateChange.Passivation, "propose", other.Id, "2"),
            CommandOutcome.ValidationFailed, "accountId", "cash_account_not_found");
        var untouched = await run.RowAsync(other.Id, CompanyB);
        Assert.Equal((2, 2L, (int?)null), (untouched.Status, untouched.Version, untouched.ChangeKind));
        var commands = await run.CommandsAsync();
        Assert.Equal(3, commands.Count);
        Assert.Single(commands, command => command.CashAccountId is null);
        Assert.Empty(await run.CommandsAsync(CompanyB));
        Assert.DoesNotContain((await run.ListAsync(Actor)).Resource!.Items, item => item.Account.Id == other.Id);
    }

    [Theory]
    [InlineData(CashAccountStateChange.Passivation, "propose", 2)]
    [InlineData(CashAccountStateChange.Passivation, "propose", 1)]
    [InlineData(CashAccountStateChange.Passivation, "approve", 2)]
    [InlineData(CashAccountStateChange.Passivation, "approve", 1)]
    [InlineData(CashAccountStateChange.Passivation, "reject", 1)]
    [InlineData(CashAccountStateChange.Reactivation, "propose", 1)]
    [InlineData(CashAccountStateChange.Reactivation, "approve", 1)]
    [InlineData(CashAccountStateChange.Reactivation, "reject", 1)]
    public async Task Authority_revoked_before_the_save_refuses_every_state_change_step_and_stores_nothing(
        CashAccountStateChange change, string step, int allowed)
    {
        await using var run = new Run();
        var seeded = await run.SeedAsync(change == CashAccountStateChange.Passivation
            ? (step == "propose" ? Approved() : PendingPassivation())
            : (step == "propose" ? Passive() : PendingReactivation()));
        var version = FinanceCashAccountService.VersionText(seeded.Version);
        var actor = step == "propose" ? Actor : Approver;
        var before = await run.SnapshotAsync(seeded.Id);
        var key = Key();

        // The step's authority holds for its first `allowed` checks (the start; for a passivation with 2, also after the
        // balance read) and is gone at the next one, which is the last before the save.
        var refused = await run.StepAsync(actor, change, step, seeded.Id, version, key: key,
            permissions: new RevokingPermission(ActionOf(change, step), allowed));

        Assert.Equal(CommandOutcome.Denied, refused.Outcome);
        Assert.Null(refused.Resource);
        Assert.Empty(refused.Issues);
        Assert.Equal(before, await run.SnapshotAsync(seeded.Id));

        // Nothing was stored under the key: with the authority back the same command runs for the first time.
        var retried = await run.StepAsync(actor, change, step, seeded.Id, version, key: key);
        Assert.Equal((CommandOutcome.Succeeded, false), (retried.Outcome, retried.IsReplay));
    }

    private static FinanceCashAccountService Service(FinanceDbContext context, IFinancePermissionPort permissions,
        ISettingsMoneyCandidateReader? currencies = null, string actor = Actor)
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        return new FinanceCashAccountService(context, company, new FixedUser(actor), permissions,
            currencies ?? new NoCandidates(), new AuditEventCollector(), Envelopes(), new NoBalanceRead(), TimeProvider.System);
    }

    private static FinanceCashAccountEnvelopes Envelopes() => new FinanceCashAccountEnvelopes(
        Envelope(ErpPermissions.FinanceCashAccountPropose), Envelope(ErpPermissions.FinanceCashAccountApprove),
        Envelope(ErpPermissions.FinanceCashAccountPassivationPropose), Envelope(ErpPermissions.FinanceCashAccountPassivationApprove),
        Envelope(ErpPermissions.FinanceCashAccountReactivationPropose), Envelope(ErpPermissions.FinanceCashAccountReactivationApprove));

    private static FinanceAuditEnvelope Envelope(string permission) => new FinanceAuditEnvelope(permission, ["SENTETIK-FINANCE"],
        SourceChannels.Web);

    private static FinanceAction ActionOf(CashAccountStateChange change, string step) => (change, step) switch
    {
        (CashAccountStateChange.Passivation, "propose") => FinanceAction.ProposeCashAccountPassivation,
        (CashAccountStateChange.Passivation, _) => FinanceAction.ApproveCashAccountPassivation,
        (_, "propose") => FinanceAction.ProposeCashAccountReactivation,
        _ => FinanceAction.ApproveCashAccountReactivation,
    };

    private static void AssertIssue(CashAccountCommandResult result, CommandOutcome outcome, string field, string code)
    {
        Assert.Equal(outcome, result.Outcome);
        Assert.Null(result.Resource);
        var issue = Assert.Single(result.Issues);
        Assert.Equal((field, code), (issue.Field, issue.Code));
    }

    private static string Key() => $"SENTETIK-{Guid.NewGuid():N}";

    private static CashAccountRow Account() => new CashAccountRow
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, CurrencyId = Currency, DisplayName = "SENTETIK kasa", Status = 1, Version = 1,
        ProposedBy = Actor, ProposedAtUtc = At, UpdatedAtUtc = At,
    };

    private static CashAccountRow Approved()
    {
        var row = Account();
        row.Status = 2;
        row.DecidedBy = Approver;
        row.DecidedAtUtc = At.AddMinutes(5);
        row.UpdatedAtUtc = At.AddMinutes(5);
        row.Version = 2;
        return row;
    }

    private static CashAccountRow Rejected()
    {
        var row = Approved();
        row.Status = 3;
        row.RejectionReason = Reason;
        return row;
    }

    private static CashAccountRow PendingPassivation()
    {
        var row = Approved();
        row.ChangeKind = 1;
        row.ChangeStatus = 1;
        row.ChangeProposedBy = Actor;
        row.ChangeProposedAtUtc = At.AddMinutes(10);
        row.UpdatedAtUtc = At.AddMinutes(10);
        row.Version = 3;
        return row;
    }

    private static CashAccountRow Passive()
    {
        var row = PendingPassivation();
        row.Status = 4;
        row.ChangeStatus = 2;
        row.ChangeDecidedBy = Approver;
        row.ChangeDecidedAtUtc = At.AddMinutes(15);
        row.UpdatedAtUtc = At.AddMinutes(15);
        row.Version = 4;
        return row;
    }

    private static CashAccountRow PendingReactivation()
    {
        var row = Passive();
        ProposeChange(row, 2);
        return row;
    }

    /// <summary>The opening decision, exactly one version step.</summary>
    private static void Decide(CashAccountRow row, int status, string decidedBy, string? reason)
    {
        row.Status = status;
        row.DecidedBy = decidedBy;
        row.DecidedAtUtc = At.AddMinutes(5);
        row.RejectionReason = reason;
        row.UpdatedAtUtc = At.AddMinutes(5);
        row.Version += 1;
    }

    /// <summary>A passivation (1) or reactivation (2) proposal by <see cref="Actor"/>, exactly one version step.</summary>
    private static void ProposeChange(CashAccountRow row, int kind)
    {
        row.ChangeKind = kind;
        row.ChangeStatus = 1;
        row.ChangeProposedBy = Actor;
        row.ChangeProposedAtUtc = At.AddMinutes(20);
        row.ChangeDecidedBy = null;
        row.ChangeDecidedAtUtc = null;
        row.ChangeRejectionReason = null;
        row.UpdatedAtUtc = At.AddMinutes(20);
        row.Version += 1;
    }

    /// <summary>The pending change's decision (2 approved, 3 rejected) and the account's state after it.</summary>
    private static void DecideChange(CashAccountRow row, int changeStatus, int status, string decidedBy, string? reason)
    {
        row.ChangeStatus = changeStatus;
        row.Status = status;
        row.ChangeDecidedBy = decidedBy;
        row.ChangeDecidedAtUtc = At.AddMinutes(25);
        row.ChangeRejectionReason = reason;
        row.UpdatedAtUtc = At.AddMinutes(25);
        row.Version += 1;
    }

    private static T Attach<T>(FinanceDbContext context, T row) where T : class
    {
        context.Attach(row);
        return row;
    }

    private static FinanceDbContext Context()
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;")
            .Options;
        return new FinanceDbContext(options, company);
    }

    /// <summary>
    /// The real service over one in-memory store: every call is a fresh scope (context, company, user, audit
    /// collector and interceptor) like a request; seeds and checks read the store through the write guard.
    /// </summary>
    private sealed class Run : IAsyncDisposable
    {
        private readonly string databaseName = $"finance-cash-accounts-{Guid.NewGuid():N}";

        public FinanceDbContext Memory(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase(databaseName).Options, companyContext);
        }

        public async Task<CashAccountRow> SeedAsync(CashAccountRow row)
        {
            await using var context = Memory(row.CompanyId);
            context.CashAccounts.Add(row);
            await context.SaveChangesAsync();
            return row;
        }

        public async Task<CashAccountCommandResult> StepAsync(string actor, CashAccountStateChange change, string step,
            Guid account, string version, string? key = null, IFinancePermissionPort? permissions = null,
            IFinanceCashAccountBalanceReader? balances = null, string? correlation = null)
        {
            var (context, service, finance) = Open(actor, permissions, balances, correlation);
            await using (context)
            {
                var metadata = new CommandMetadata(key ?? Key());
                return step switch
                {
                    "propose" => await service.ProposeStateChangeAsync(finance, metadata, change,
                        new CashAccountStateChangeRequest(account, version), CancellationToken.None),
                    "approve" => await service.ApproveStateChangeAsync(finance, metadata, change,
                        new CashAccountStateChangeRequest(account, version), CancellationToken.None),
                    _ => await service.RejectStateChangeAsync(finance, metadata, change,
                        new CashAccountStateChangeRejectRequest(account, version, Reason), CancellationToken.None),
                };
            }
        }

        public async Task<ReadResult<CashAccountMasterPage>> ListAsync(string actor, IFinancePermissionPort? permissions = null)
        {
            var (context, service, finance) = Open(actor, permissions, balances: null, correlation: null);
            await using (context)
            {
                return await service.ListAsync(finance, CancellationToken.None);
            }
        }

        public async Task<ReadResult<CashAccountView>> GetAsync(string actor, Guid account, IFinancePermissionPort? permissions = null)
        {
            var (context, service, finance) = Open(actor, permissions, balances: null, correlation: null);
            await using (context)
            {
                return await service.GetAsync(finance, account, CancellationToken.None);
            }
        }

        public async Task<CashAccountRow> RowAsync(Guid account, Guid? company = null)
        {
            await using var context = Memory(company ?? CompanyA);
            return await context.CashAccounts.AsNoTracking().SingleAsync(row => row.Id == account);
        }

        public async Task<List<CashAccountCommandRow>> CommandsAsync(Guid? company = null)
        {
            await using var context = Memory(company ?? CompanyA);
            return await context.CashAccountCommands.AsNoTracking().ToListAsync();
        }

        public async Task<List<AuditEvent>> AuditsAsync(Guid account)
        {
            var entityId = account.ToString("D");
            await using var context = Memory(CompanyA);
            return await context.Set<AuditEvent>().AsNoTracking().Where(audit => audit.EntityId == entityId)
                .OrderBy(audit => audit.EntityVersionAfter).ToListAsync();
        }

        /// <summary>The account's version, the company's stored commands and the account's audit events.</summary>
        public async Task<(long Version, int Commands, int Audits)> SnapshotAsync(Guid account) =>
            ((await RowAsync(account)).Version, (await CommandsAsync()).Count, (await AuditsAsync(account)).Count);

        public async ValueTask DisposeAsync()
        {
            await using var context = Memory(CompanyA);
            await context.Database.EnsureDeletedAsync();
        }

        private (FinanceDbContext Context, FinanceCashAccountService Service, FinanceContext Finance) Open(string actor,
            IFinancePermissionPort? permissions, IFinanceCashAccountBalanceReader? balances, string? correlation)
        {
            var company = new CompanyContext();
            company.Set(CompanyA);
            var collector = new AuditEventCollector();
            var context = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase(databaseName)
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).Options, company);
            var service = new FinanceCashAccountService(context, company, new FixedUser(actor),
                permissions ?? new RecordingPermission(PermissionDecision.Allowed),
                new Candidates([new MoneyCurrencyCandidateV1(Currency, "TRY", "SENTETIK Lira")]), collector, Envelopes(),
                balances ?? new FixedBalance(CashAccountBalanceState.Zero), TimeProvider.System);
            return (context, service, new FinanceContext(CompanyA, actor, correlation ?? "SENTETIK-correlation", null));
        }
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

    /// <summary>A caller allowed to read cash accounts and nothing else.</summary>
    private sealed class ReadOnlyPermission : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) =>
            Task.FromResult(action == FinanceAction.ReadCashAccounts ? PermissionDecision.Allowed : PermissionDecision.Denied);
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

    private sealed class FixedBalance(CashAccountBalanceState state) : IFinanceCashAccountBalanceReader
    {
        public List<(Guid CompanyId, Guid AccountId)> Seen { get; } = [];

        public Task<CashAccountBalanceState> ReadAsync(Guid companyId, Guid cashAccountId, CancellationToken cancellationToken)
        {
            Seen.Add((companyId, cashAccountId));
            return Task.FromResult(state);
        }
    }

    /// <summary>Never reached: the refusal comes before the balance is read, or the step never reads it.</summary>
    private sealed class NoBalanceRead : IFinanceCashAccountBalanceReader
    {
        public Task<CashAccountBalanceState> ReadAsync(Guid companyId, Guid cashAccountId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The balance must not be read here.");
    }

    private sealed class Candidates(IReadOnlyList<MoneyCurrencyCandidateV1> candidates) : ISettingsMoneyCandidateReader
    {
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(candidates);
    }

    private sealed class FailingCandidates(string code) : ISettingsMoneyCandidateReader
    {
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken cancellationToken = default) =>
            throw new SettingsPrerequisiteException(code);
    }

    /// <summary>Never reached by these tests: every refusal comes before Settings is asked.</summary>
    private sealed class NoCandidates : ISettingsMoneyCandidateReader
    {
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Settings must not be asked before the refusal.");
    }

    private sealed class FixedUser(string userId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyA };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
