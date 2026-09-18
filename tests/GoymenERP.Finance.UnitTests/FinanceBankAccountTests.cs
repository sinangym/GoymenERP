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

        // The shared command record names exactly one target.
        var commands = design.FindEntityType(typeof(FinanceCommandRow))!;
        Assert.Contains(commands.GetCheckConstraints(), check => check.Name == "CK_Commands_Target");
        Assert.All(commands.GetForeignKeys().Where(key => key.PrincipalEntityType.ClrType != typeof(OrganizationCompanyReference)),
            key => Assert.False(key.IsRequired));
    }

    [Fact]
    public void The_bank_account_migration_is_discoverable()
    {
        using var context = Context();
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddBankAccounts", StringComparison.Ordinal));
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
        var propose = new BankAccountProposeRequest("SENTETIK hesap", Currency);
        var hash = FinanceBankAccountService.PayloadHash(FinanceBankAccountService.ProposeCommand, propose);
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, FinanceBankAccountService.PayloadHash(FinanceBankAccountService.ProposeCommand,
            new BankAccountProposeRequest("SENTETIK hesap", Currency)));
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
        var request = new BankAccountProposeRequest(fault == "null-name" ? null! : "SENTETIK hesap", Currency);

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
    }

    private static FinanceBankAccountService Service(FinanceDbContext context, PermissionDecision permission)
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        return new FinanceBankAccountService(context, company, new FixedUser(Actor, CompanyA), new FixedPermission(permission),
            new NoCandidates(), new AuditEventCollector(),
            new FinanceAuditEnvelope(FinanceBankAccountService.ProposeCommand, ["SENTETIK-FINANCE"], "Web"), TimeProvider.System);
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

    private sealed class FixedPermission(PermissionDecision decision) : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(decision);
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
