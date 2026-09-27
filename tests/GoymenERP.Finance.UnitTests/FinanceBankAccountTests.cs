using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// FS0 without a database: the bank account model (schema, filter, the Ö4 check, the command
/// target), the write guard, the payload hash and every refusal that must happen before anything
/// is read. What SQL Server enforces is proven by the relational acceptance tests. SENTETIK / NOT_ENV.
/// </summary>
public sealed class FinanceBankAccountTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("12121212-1212-1212-1212-121212121212");
    private static readonly Guid Currency = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset At = new DateTimeOffset(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);
    private const string Actor = "SENTETIK-proposer";

    [Fact]
    public void A_bank_account_is_a_company_row_whose_approval_is_held_by_the_database()
    {
        using var context = Context();
        var accounts = context.Model.FindEntityType(typeof(BankAccountRow))!;
        Assert.Equal((FinanceDbContext.Schema, "BankAccounts"), (accounts.GetSchema(), accounts.GetTableName()));
        Assert.Single(accounts.GetDeclaredQueryFilters());
        Assert.True(accounts.FindProperty(nameof(BankAccountRow.Version))!.IsConcurrencyToken);
        // Exactly the port's fields plus the lifecycle trace: no balance, no money column.
        Assert.DoesNotContain(accounts.GetProperties(), property => property.ClrType == typeof(decimal));

        var design = context.GetService<IDesignTimeModel>().Model;
        var designAccounts = design.FindEntityType(typeof(BankAccountRow))!;
        var approval = Assert.Single(designAccounts.GetCheckConstraints(), check => check.Name == "CK_BankAccounts_Approval");
        Assert.Contains("[ApprovedBy] <> [ProposedBy]", approval.Sql, StringComparison.Ordinal);
        Assert.Equal(FinanceDbContext.IdentityCollation, designAccounts.FindProperty(nameof(BankAccountRow.ProposedBy))!.GetCollation());
        Assert.Equal(FinanceDbContext.IdentityCollation, designAccounts.FindProperty(nameof(BankAccountRow.ApprovedBy))!.GetCollation());

        // The shared command record names at most one target; none only for a recorded refusal (ADR-011 §7).
        var commands = design.FindEntityType(typeof(FinanceCommandRow))!;
        var target = Assert.Single(commands.GetCheckConstraints(), check => check.Name == "CK_Commands_Target");
        Assert.Equal(FinanceDbContext.TargetCheck, target.Sql);
        Assert.EndsWith("OR ([DraftId] IS NULL AND [BankAccountId] IS NULL AND [ReversalProposalId] IS NULL AND [ManualBankPaymentId] IS NULL AND [FinanceAllocationId] IS NULL AND JSON_VALUE([ResultJson], '$.outcome') = 'ValidationFailed')",
            target.Sql, StringComparison.Ordinal);
        Assert.All(commands.GetForeignKeys().Where(key => key.PrincipalEntityType.ClrType != typeof(OrganizationCompanyReference)),
            key => Assert.False(key.IsRequired));
    }

    [Fact]
    public void The_bank_account_migration_is_discoverable()
    {
        using var context = Context();
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddBankAccounts", StringComparison.Ordinal));
        // The NEWEST migration is pinned by name on purpose: DEC-FIN-014's POS facts are a deliberate
        // append after F-03, never an unreviewed migration that silently becomes authoritative.
        Assert.Equal("20260927120000_AddPosCollectionFacts", context.Database.GetMigrations().Last());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("edit-name")]
    [InlineData("edit-currency")]
    [InlineData("edit-proposer")]
    [InlineData("other-company")]
    public void An_account_is_never_deleted_renamed_recurrencied_or_written_for_another_company(string fault)
    {
        using var context = Context();
        switch (fault)
        {
            case "delete":
                context.BankAccounts.Remove(Attach(context, Account()));
                break;
            case "edit-name":
                Attach(context, Account()).DisplayName = "SENTETIK başka ad";
                break;
            case "edit-currency":
                Attach(context, Account()).CurrencyId = Guid.NewGuid();
                break;
            case "edit-proposer":
                Attach(context, Account()).ProposedBy = "SENTETIK-other";
                break;
            default:
                var row = Account();
                row.CompanyId = CompanyB;
                context.BankAccounts.Add(row);
                break;
        }

        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
    }

    [Fact]
    public void The_payload_hash_names_the_command_and_every_field()
    {
        var propose = new BankAccountProposeRequest("SENTETIK hesap", Currency, "SENTETIK Banka", "SENTETIK tür", false);
        var hash = FinanceBankAccountService.PayloadHash(FinanceBankAccountService.ProposeCommand, propose);
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, FinanceBankAccountService.PayloadHash(FinanceBankAccountService.ProposeCommand,
            new BankAccountProposeRequest("SENTETIK hesap", Currency, "SENTETIK Banka", "SENTETIK tür", false)));
        Assert.NotEqual(hash, FinanceBankAccountService.PayloadHash(FinanceBankAccountService.ProposeCommand,
            propose with { DisplayName = "SENTETIK hesap 2" }));
        Assert.NotEqual(hash, FinanceBankAccountService.PayloadHash(FinanceBankAccountService.ProposeCommand,
            propose with { CurrencyId = Guid.NewGuid() }));
        Assert.NotEqual(hash, FinanceBankAccountService.PayloadHash(FinanceBankAccountService.ApproveCommand, propose));
    }

    [Theory]
    [InlineData("other-actor", CommandOutcome.Denied, null)]
    [InlineData("other-company", CommandOutcome.Denied, null)]
    [InlineData("permission-denied", CommandOutcome.Denied, null)]
    [InlineData("permission-unavailable", CommandOutcome.Unavailable, "finance_permission_mapping_unavailable")]
    [InlineData("key-with-space", CommandOutcome.ValidationFailed, "idempotency_key_invalid")]
    [InlineData("null-name", CommandOutcome.ValidationFailed, "display_name_invalid")]
    // K2 (Sinan #66 5748258299): bank name and account type are MANDATORY on a card written from now
    // on, and an IBAN that is given must be structurally valid. These rows go through the real
    // ProposeAsync against an unreachable database, so they also pin that the card is judged BEFORE
    // anything is read — a refusal here cannot have touched a row.
    [InlineData("no-bank-name", CommandOutcome.ValidationFailed, "bank_name_required")]
    [InlineData("blank-bank-name", CommandOutcome.ValidationFailed, "bank_name_required")]
    [InlineData("no-account-type", CommandOutcome.ValidationFailed, "account_type_required")]
    [InlineData("bad-iban", CommandOutcome.ValidationFailed, "iban_invalid")]
    // B3 + K2: the IBAN requirement attaches to the DECLARATION, not to the other fields. A card that
    // says it is for payment or collection and carries no IBAN is refused; the very same card
    // declaring false is accepted, which is K2's IBAN-less special credit or term account.
    [InlineData("eligible-without-iban", CommandOutcome.ValidationFailed, "iban_required_for_money_movement")]
    public async Task A_proposal_is_refused_before_anything_is_read(string fault, CommandOutcome outcome, string? code)
    {
        var permission = fault switch
        {
            "permission-denied" => PermissionDecision.Denied,
            "permission-unavailable" => PermissionDecision.Unavailable,
            _ => PermissionDecision.Allowed,
        };
        await using var context = Context();
        var service = Service(context, permission);
        var finance = new FinanceContext(fault == "other-company" ? CompanyB : CompanyA,
            fault == "other-actor" ? "SENTETIK-someone-else" : Actor, "SENTETIK-correlation", null);
        var key = new CommandMetadata(fault == "key-with-space" ? " k-1" : "k-1");
        var request = new BankAccountProposeRequest(
            fault == "null-name" ? null! : "SENTETIK hesap",
            Currency,
            fault switch { "no-bank-name" => null!, "blank-bank-name" => "   ", _ => "SENTETIK Banka" },
            fault == "no-account-type" ? null! : "SENTETIK tür",
            // B3: only the money-eligible fault declares itself for money movement. Every other case
            // declares false, so the IBAN rule cannot be what refuses them.
            MoneyMovementEligible: fault == "eligible-without-iban",
            // A structurally invalid IBAN: the published TR example with one character altered, so the
            // refusal is the checksum and not the length or the shape.
            Iban: fault == "bad-iban" ? "TR330006100519786457841327" : null);

        // The context's database is unreachable: any read would throw, so a result proves nothing was read.
        var result = await service.ProposeAsync(finance, key, request, CancellationToken.None);

        Assert.Equal(outcome, result.Outcome);
        Assert.Null(result.Resource);
        if (code is not null)
        {
            Assert.Equal(code, Assert.Single(result.Issues).Code);
        }
    }

    [Theory]
    [InlineData("empty-account", "accountId")]
    [InlineData("null-version", "expectedVersion")]
    [InlineData("padded-version", "expectedVersion")]
    [InlineData("blank-version", "expectedVersion")]
    public async Task A_malformed_approval_names_its_own_field_before_anything_is_read(string fault, string field)
    {
        await using var context = Context();
        var request = new BankAccountApproveRequest(fault == "empty-account" ? Guid.Empty : Guid.NewGuid(), fault switch
        {
            "null-version" => null!,
            "padded-version" => " 1",
            "blank-version" => " ",
            _ => "1",
        });

        // The context's database is unreachable: a result proves nothing was read.
        var result = await Service(context, PermissionDecision.Allowed).ApproveAsync(
            new FinanceContext(CompanyA, Actor, "SENTETIK-correlation", null), new CommandMetadata("k-1"), request, CancellationToken.None);

        Assert.Equal(CommandOutcome.ValidationFailed, result.Outcome);
        Assert.Equal(field, Assert.Single(result.Issues).Field);
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
        var service = Service(context, permission);
        var finance = new FinanceContext(fault == "other-company" ? CompanyB : CompanyA, Actor, "SENTETIK-correlation", null);

        Assert.Equal(outcome, (await service.ListAuthorizedAsync(finance, CancellationToken.None)).Outcome);
        Assert.Equal(outcome, (await service.GetAuthorizedAsync(finance, Guid.NewGuid(), CancellationToken.None)).Outcome);
        Assert.Equal(outcome, (await service.ListMasterAsync(finance, CancellationToken.None)).Outcome);
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
        Assert.Equal([FinanceAction.ProposeBankAccount, FinanceAction.ProposeBankAccount], permissions.Actions);

        // No candidates is an honest empty list, not a made-up one.
        var empty = await Service(context, PermissionDecision.Allowed, new Candidates([])).CurrencyCandidatesAsync(finance,
            CancellationToken.None);
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

        var result = await Service(context, PermissionDecision.Allowed, reader).CurrencyCandidatesAsync(
            new FinanceContext(CompanyA, Actor, "SENTETIK-correlation", null), CancellationToken.None);

        Assert.Equal(outcome, result.Outcome);
        Assert.Null(result.Resource);
    }

    private static FinanceBankAccountService Service(FinanceDbContext context, PermissionDecision permission,
        ISettingsMoneyCandidateReader? currencies = null) =>
        Service(context, new RecordingPermission(permission), currencies);

    private static FinanceBankAccountService Service(FinanceDbContext context, IFinancePermissionPort permissions,
        ISettingsMoneyCandidateReader? currencies = null)
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        return new FinanceBankAccountService(context, company, new FixedUser(Actor, CompanyA), permissions,
            currencies ?? new NoCandidates(), new AuditEventCollector(),
            new FinanceBankAccountEnvelopes(
                new FinanceAuditEnvelope("finance.bank-account.propose", ["SENTETIK-FINANCE"], "Web"),
                new FinanceAuditEnvelope("finance.bank-account.approve", ["SENTETIK-FINANCE"], "Web")),
            TimeProvider.System);
    }

    private static BankAccountRow Account() => new BankAccountRow
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, CurrencyId = Currency, DisplayName = "SENTETIK hesap", Status = 1, Version = 1,
        ProposedBy = Actor, ProposedAtUtc = At, UpdatedAtUtc = At,
    };

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

    private sealed class FixedUser(string userId, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
