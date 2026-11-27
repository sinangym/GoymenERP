using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// FIN-SC47 (FNS-D87, FNS-D88, FNS-D93, FNS-D94) against a REAL SQL Server: the Finance credit account store on Finance's
/// own migrations over the host's migrated and seeded database, with Audit's real save interceptor.
/// <para>
/// Permission and the Settings currency candidates are ISOLATED SYNTHETIC ports: no finance.credits grant exists, so these
/// tests prove the store, its constraints and its rules, not a user outcome. Every identity, amount and currency below is
/// SENTETIK / NOT_ENV test data. A credit account approval moves no money and writes no voucher.
/// </para>
/// </summary>
public sealed partial class FinanceCreditAccountSqlServerRelationalAcceptanceTests
{
    private const string Maker = "SENTETIK-credit-maker";
    private const string Checker = "SENTETIK-credit-checker";
    private const string Lender = "SENTETIK Banka";
    private const string Contract = "SENTETIK-KRD-0001";
    private const int DownGuardNumber = 51861;
    private const int CheckViolation = 547;
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Currency = Guid.Parse("9b3f4e5d-6c7b-4809-9b2c-3d4e5f6a7d01");

    [SqlServerFact]
    public async Task A_makers_credit_account_with_its_bank_plan_is_approved_by_another_person_in_one_save_each_with_its_attempt_and_audit()
    {
        await using var run = await Run.StartAsync();

        CreditAccountView proposed;
        await using (var maker = run.Open(CompanyA, Maker))
        {
            var result = await maker.Credits.ProposeAsync(maker.Context, Key("p-1"), Request(),
                CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            proposed = result.Resource!;
            Assert.Equal((CreditAccountStatus.Proposed, "1", "1000.00", "1072.1515"),
                (proposed.Status, proposed.Version, proposed.PrincipalAmount, proposed.PlanTotals!.Installment));

            var own = await maker.Credits.ApproveAsync(maker.Context, Key("a-own"),
                new CreditAccountApproveRequest(proposed.Id, "1"), CancellationToken.None);
            Assert.Equal(CommandOutcome.Denied, own.Outcome);
        }

        await using (var checker = run.Open(CompanyA, Checker))
        {
            var approved = await checker.Credits.ApproveAsync(checker.Context, Key("a-1"),
                new CreditAccountApproveRequest(proposed.Id, "1"), CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, CreditAccountStatus.Approved, "2"),
                (approved.Outcome, approved.Resource!.Status, approved.Resource.Version));
        }

        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT [Status] FROM finance.CreditAccounts WHERE Id = @id", ("@id", proposed.Id)));
        Assert.Equal(Checker, await run.ScalarAsync<string>(
            "SELECT [DecidedBy] FROM finance.CreditAccounts WHERE Id = @id", ("@id", proposed.Id)));
        // The plan is stored exactly as the bank gave it.
        Assert.Equal("6.5845", await run.ScalarAsync<string>(
            "SELECT [KkdfText] FROM finance.CreditAccountInstallments WHERE CreditAccountId = @id AND [Sequence] = 1",
            ("@id", proposed.Id)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.CreditAccountInstallments WHERE CreditAccountId = @id", ("@id", proposed.Id)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.CreditAccountCommands WHERE CreditAccountId = @id", ("@id", proposed.Id)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.CreditAccount' AND EntityId = @id",
            ("@id", proposed.Id.ToString("D"))));
    }

    [SqlServerFact]
    public async Task The_database_refuses_a_proposers_own_decision_a_second_live_account_an_unknown_group_and_a_zero_amount()
    {
        await using var run = await Run.StartAsync();
        CreditAccountView proposed;
        await using (var maker = run.Open(CompanyA, Maker))
        {
            proposed = (await maker.Credits.ProposeAsync(maker.Context, Key("p-1"), Request(),
                CancellationToken.None)).Resource!;
        }

        // The service's save guard is bypassed on purpose: the database checks are the authority.
        // CK_CreditAccounts_Decision: maker≠checker at person level.
        var own = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE finance.CreditAccounts SET [Status] = 2, DecidedBy = ProposedBy, DecidedAtUtc = SYSDATETIMEOFFSET(), "
            + "Version = Version + 1 WHERE Id = @id", ("@id", proposed.Id)));
        Assert.Equal(CheckViolation, own.Number);

        // The source-backed lender identity, not an alias of its displayed name, owns the live contract slot.
        var live = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO finance.CreditAccounts (Id, CompanyId, CreditGroup, LenderName, ContractReference, CurrencyId, "
            + "PrincipalAmountText, StartDate, [Status], ProposedBy, ProposedAtUtc, DecidedBy, DecidedAtUtc, "
            + "RejectionReason, Version, UpdatedAtUtc, LenderPartyId, LenderCompanyPartyAccountId, LenderPartyVersion, "
            + "LenderCompanyAccountVersion, SourceProfileJson, MoneyProfileId, MoneyProfileVersion, CurrencyScale) "
            + "SELECT NEWID(), CompanyId, CreditGroup, LenderName, "
            + "ContractReference, CurrencyId, PrincipalAmountText, StartDate, 1, ProposedBy, ProposedAtUtc, NULL, NULL, "
            + "NULL, 1, UpdatedAtUtc, LenderPartyId, LenderCompanyPartyAccountId, LenderPartyVersion, "
            + "LenderCompanyAccountVersion, SourceProfileJson, MoneyProfileId, MoneyProfileVersion, CurrencyScale "
            + "FROM finance.CreditAccounts WHERE Id = @id", ("@id", proposed.Id)));
        Assert.Contains(live.Number, new[] { 2601, 2627 });

        // CK_CreditAccounts_Group: only the four FNS-D87 groups.
        var group = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE finance.CreditAccounts SET CreditGroup = 5 WHERE Id = @id", ("@id", proposed.Id)));
        Assert.Equal(CheckViolation, group.Number);

        var amount = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE finance.CreditAccounts SET PrincipalAmountText = '0.00' WHERE Id = @id", ("@id", proposed.Id)));
        Assert.Equal(CheckViolation, amount.Number);

        // CK_CreditAccountInstallments_Amounts: a plan amount is unsigned exact text.
        var line = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE finance.CreditAccountInstallments SET BsmvText = '-1.65' WHERE CreditAccountId = @id "
            + "AND [Sequence] = 1", ("@id", proposed.Id)));
        Assert.Equal(CheckViolation, line.Number);

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CreditAccounts"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT [Status] FROM finance.CreditAccounts WHERE Id = @id", ("@id", proposed.Id)));
    }

    [SqlServerFact]
    public async Task Another_companys_credit_account_is_never_listed_or_decided()
    {
        await using var run = await Run.StartAsync();
        CreditAccountView proposed;
        await using (var maker = run.Open(CompanyA, Maker))
        {
            proposed = (await maker.Credits.ProposeAsync(maker.Context, Key("p-1"), Request(),
                CancellationToken.None)).Resource!;
        }

        await using (var other = run.Open(CompanyB, Checker))
        {
            var list = await other.Credits.ListAsync(other.Context, CancellationToken.None);
            Assert.Empty(list.Resource!.Items);
            var decided = await other.Credits.ApproveAsync(other.Context, Key("a-b"),
                new CreditAccountApproveRequest(proposed.Id, "1"), CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, decided.Outcome);
            Assert.Equal("credit_account_not_found", Assert.Single(decided.Issues).Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT [Status] FROM finance.CreditAccounts WHERE Id = @id", ("@id", proposed.Id)));
    }

    [SqlServerFact]
    public async Task The_credit_account_migrations_Down_passes_on_an_empty_store_and_refuses_while_an_account_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddCreditAccounts().DownOperations[0]).Sql;

        await run.ExecuteAsync(guard);

        CreditAccountView proposed;
        await using (var maker = run.Open(CompanyA, Maker))
        {
            proposed = (await maker.Credits.ProposeAsync(maker.Context, Key("p-1"), Request(),
                CancellationToken.None)).Resource!;
        }

        // Forward fix: the rollback stops while a credit account would be lost and deletes nothing.
        var error = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(DownGuardNumber, error.Number);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.CreditAccounts WHERE Id = @id", ("@id", proposed.Id)));
    }

    /// <summary>A bank plan as given: each line holds and the principals sum to the credit amount.</summary>
    [SqlServerFact]
    public async Task Credit_source_profile_Down_refuses_protected_rows_and_database_checks_keep_lender_proof_and_charge_json_complete()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddCreditSourceProfiles().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);
        CreditAccountView proposed;
        await using (var maker = run.Open(CompanyA, Maker))
        {
            var request = Request();
            var lines = request.Installments.ToArray();
            lines[0] = lines[0] with { Installment = "546.2345", Charges = [new("SENTETIK ücret", "5",
                CreditChargeTreatmentV1.IncludedInInstallment, "SENTETIK-BANK-1")] };
            proposed = (await maker.Credits.ProposeAsync(maker.Context, Key("source-p-1"), request with { Installments = lines },
                CancellationToken.None)).Resource!;
        }
        Assert.Equal("5", proposed.PlanTotals!.Charges!.IncludedInInstallment);
        Assert.Equal(51961, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard))).Number);
        Assert.Equal(CheckViolation, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE finance.CreditAccounts SET MoneyProfileVersion = NULL WHERE Id = @id", ("@id", proposed.Id)))).Number);
        Assert.Equal(CheckViolation, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE finance.CreditAccounts SET SourceProfileJson = NULL WHERE Id = @id", ("@id", proposed.Id)))).Number);
        Assert.Equal(CheckViolation, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE finance.CreditAccountInstallments SET ChargesJson = N'bozuk' WHERE CreditAccountId = @id AND [Sequence] = 1",
            ("@id", proposed.Id)))).Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CreditAccounts WHERE Id = @id", ("@id", proposed.Id)));
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT MoneyProfileVersion FROM finance.CreditAccounts WHERE Id = @id", ("@id", proposed.Id)));
        Assert.Contains("SENTETIK-BANK-1", await run.ScalarAsync<string>(
            "SELECT ChargesJson FROM finance.CreditAccountInstallments WHERE CreditAccountId = @id AND [Sequence] = 1", ("@id", proposed.Id)), StringComparison.Ordinal);
    }

    /// <summary>A bank plan as given: each line holds and the principals sum to the credit amount.</summary>
    private static CreditAccountProposeRequest Request()
    {
        CreditPlanInstallmentInput[] plan = [
            new CreditPlanInstallmentInput(1, new DateOnly(2026, 12, 15), "541.2345", "500.00", "33.0000", "1.6500",
                "6.5845"),
            new CreditPlanInstallmentInput(2, new DateOnly(2027, 1, 15), "530.917", "500.00", "25.000", "1.250",
                "4.667"),
        ];
        return new(CreditAccountGroup.Cash, Lender, Contract, Currency, "1000.00", new DateOnly(2026, 11, 15),
            plan, new(Guid.Parse("f4720000-0000-4000-8000-000000000001"), Guid.Parse("f4720000-0000-4000-8000-000000000002"),
            new("SENTETIK plan", new DateOnly(2026, 11, 15), "1"), CreditPlanNatureV1.Installments,
            Cash: new(CreditCashKindV1.Installment, plan.Max(line => line.DueDate))));
    }

    private static CommandMetadata Key(string key) => new CommandMetadata(key);

    private sealed partial class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }
        public string Correlation { get; } = $"finsc47-{Guid.NewGuid():N}";

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            // The host migrates and seeds Organization, Audit and the rest; Finance's own migrations run after it.
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            var companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            await using var context = new FinanceDbContext(Options(factory), companyContext);
            await context.Database.MigrateAsync();
            return run;
        }

        public Scope Open(Guid company, string actor)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var options = new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext))
                .Options;
            var dbContext = new FinanceDbContext(options, companyContext);
            var credits = new FinanceCreditAccountService(dbContext, companyContext,
                new FixedCurrentUser(actor, company), new SyntheticPermissions(), new SyntheticCandidates(), collector,
                new FinanceCreditAccountEnvelopes(
                    new FinanceAuditEnvelope(ErpPermissions.FinanceCreditsPropose, ["SENTETIK-FINANCE"], "Web"),
                    new FinanceAuditEnvelope(ErpPermissions.FinanceCreditsApprove, ["SENTETIK-FINANCE"], "Web")),
                TimeProvider.System, new SyntheticCreditSources(company), new SyntheticCreditSources(company));
            return new Scope(dbContext, credits, new FinanceContext(company, actor, Correlation, null));
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

    private sealed class Scope(FinanceDbContext dbContext, FinanceCreditAccountService credits, FinanceContext context)
        : IAsyncDisposable
    {
        public FinanceCreditAccountService Credits => credits;
        public FinanceContext Context => context;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    /// <summary>SENTETIK: allows every Finance action. Not a grant; no finance.credits grant exists.</summary>
    private sealed class SyntheticPermissions : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }

    /// <summary>SENTETIK: one currency candidate. Not a Settings source.</summary>
    private sealed class SyntheticCandidates : ISettingsMoneyCandidateReaderV2
    {
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(
            string operation, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MoneyCurrencyCandidateV1>>(
                [new MoneyCurrencyCandidateV1(Currency, "SENTETIK", "SENTETIK para")]);
    }

    private sealed class FixedCurrentUser(string userId, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class SyntheticCreditSources(Guid company) : ISettingsPrerequisiteProvider, IFinanceCreditLenderPortV1
    {
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MoneyProfileV1(company, currencyId, "TRY", operation, new(Guid.Parse("f4720000-0000-4000-8000-000000000003"), 1), 2, true, new DateOnly(2026, 11, 15), null));
        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FinanceCreditLenderAnswerV1> ReadAsync(Guid companyId, Guid partyId, Guid companyPartyAccountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FinanceCreditLenderAnswerV1(companyId, partyId, companyPartyAccountId, FinanceCreditLenderStatusV1.Available, Lender, 1, 1));
    }
}
