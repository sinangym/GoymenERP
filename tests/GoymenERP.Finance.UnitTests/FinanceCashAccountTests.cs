using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
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
/// API-30 without a database: the cash account model (schema, filter, the maker-checker decision check,
/// the live-name index, the command target), the forward-fix Down, the write guard, the payload hash and
/// every refusal that must happen before anything is read. What SQL Server enforces is proven by the
/// relational acceptance tests. SENTETIK / NOT_ENV.
/// </summary>
public sealed class FinanceCashAccountTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("12121212-1212-1212-1212-121212121212");
    private static readonly Guid Currency = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset At = new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
    private const string Actor = "SENTETIK-proposer";
    private const string Approver = "SENTETIK-approver";

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

        // A proposed or approved name is unique per company; a rejected proposal frees it.
        var liveName = Assert.Single(designAccounts.GetIndexes(), index => index.GetDatabaseName() == FinanceCashAccountService.LiveNameIndex);
        Assert.True(liveName.IsUnique);
        Assert.Equal([nameof(CashAccountRow.CompanyId), nameof(CashAccountRow.DisplayName)],
            liveName.Properties.Select(property => property.Name));
        Assert.Equal("[Status] IN (1, 2)", liveName.GetFilter());

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

    [Theory]
    [InlineData("delete")]
    [InlineData("edit-name")]
    [InlineData("edit-currency")]
    [InlineData("edit-proposer")]
    [InlineData("self-approval")]
    [InlineData("reject-without-reason")]
    [InlineData("approve-with-reason")]
    [InlineData("redecide")]
    [InlineData("other-company")]
    public void An_account_changes_only_by_one_decision_of_another_actor(string fault)
    {
        using var context = Context();
        switch (fault)
        {
            case "delete":
                context.CashAccounts.Remove(Attach(context, Account()));
                break;
            case "edit-name":
                Attach(context, Account()).DisplayName = "SENTETIK başka kasa";
                break;
            case "edit-currency":
                Attach(context, Account()).CurrencyId = Guid.NewGuid();
                break;
            case "edit-proposer":
                Attach(context, Account()).ProposedBy = "SENTETIK-other";
                break;
            case "self-approval":
                Decide(Attach(context, Account()), 2, Actor, null);
                break;
            case "reject-without-reason":
                Decide(Attach(context, Account()), 3, Approver, null);
                break;
            case "approve-with-reason":
                Decide(Attach(context, Account()), 2, Approver, "SENTETIK neden");
                break;
            case "redecide":
                var approved = Account();
                approved.Status = 2;
                approved.DecidedBy = Approver;
                approved.DecidedAtUtc = At;
                Decide(Attach(context, approved), 3, Approver, "SENTETIK neden");
                break;
            default:
                var row = Account();
                row.CompanyId = CompanyB;
                context.CashAccounts.Add(row);
                break;
        }

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
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

    private static FinanceCashAccountService Service(FinanceDbContext context, IFinancePermissionPort permissions,
        ISettingsMoneyCandidateReader? currencies = null, string actor = Actor)
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        return new FinanceCashAccountService(context, company, new FixedUser(actor), permissions,
            currencies ?? new NoCandidates(), new AuditEventCollector(),
            new FinanceCashAccountEnvelopes(
                new FinanceAuditEnvelope("finance.cash-account.propose", ["SENTETIK-FINANCE"], "Web"),
                new FinanceAuditEnvelope("finance.cash-account.approve", ["SENTETIK-FINANCE"], "Web")),
            TimeProvider.System);
    }

    private static CashAccountRow Account() => new CashAccountRow
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, CurrencyId = Currency, DisplayName = "SENTETIK kasa", Status = 1, Version = 1,
        ProposedBy = Actor, ProposedAtUtc = At, UpdatedAtUtc = At,
    };

    private static void Decide(CashAccountRow row, int status, string decidedBy, string? reason)
    {
        row.Status = status;
        row.DecidedBy = decidedBy;
        row.DecidedAtUtc = At.AddMinutes(5);
        row.RejectionReason = reason;
        row.UpdatedAtUtc = At.AddMinutes(5);
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
