using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

/// <summary>FNS-D24 service behavior in memory. SENTETIK / NOT_ENV; not SQL or browser evidence.</summary>
public sealed class CashTransferServiceTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Lira = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Euro = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly Guid Bank = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid CashLira = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid CashEuro = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid CashPending = Guid.Parse("88888888-8888-4888-8888-888888888888");
    private static readonly Guid Profile = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly DateOnly OccurredOn = new(2026, 11, 10);

    [Fact]
    public async Task Same_currency_commit_writes_one_record_one_command_and_one_audit_and_replays_without_a_second()
    {
        await using var run = await Run.StartAsync();
        var request = new CommitCashTransferRequest(SameCurrency(), TransferConfirmed: true);

        CashTransferCommandResult committed;
        await using (var scope = run.Scope("treasurer", new AllowAll(), Sufficient()))
        {
            committed = await scope.Service.CommitAsync(scope.Context, Key("transfer-1"), request,
                CancellationToken.None);
        }

        Assert.Equal((CommandOutcome.Succeeded, false, "1", true),
            (committed.Outcome, committed.IsReplay, committed.Resource!.Version, committed.Resource.TransferConfirmed));
        Assert.Equal(request.Preparation, committed.Resource.Preparation);

        await using (var again = run.Scope("treasurer", new AllowAll(), Sufficient()))
        {
            var replay = await again.Service.CommitAsync(again.Context, Key("transfer-1"), request,
                CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, true, committed.OriginalCorrelationId, committed.Resource.Id),
                (replay.Outcome, replay.IsReplay, replay.OriginalCorrelationId, replay.Resource!.Id));
        }

        await using var verify = run.Context();
        var row = await verify.CashTransfers.SingleAsync();
        Assert.Equal((CashTransferKinds.SameCurrency, CashTransferAccountKinds.Bank, CashTransferAccountKinds.Cash),
            (row.Kind, row.SourceKind, row.TargetKind));
        Assert.Equal(("1000.00", "1000.00", (string?)null), (row.SourceAmountText, row.TargetAmountText, row.RateText));
        Assert.Equal(1, await verify.CashTransferCommands.CountAsync());
        var sourceEvent = await verify.OutboxMessages.SingleAsync();
        Assert.Equal((row.Id, row.Version, FinanceFinancialMovementEventsV1.CashTransferRecorded),
            (sourceEvent.AggregateId, sourceEvent.AggregateVersion, sourceEvent.EventType));
        var audit = await verify.Set<AuditEvent>().SingleAsync();
        Assert.Equal("finance.cash-transfer.recorded.v1", audit.EventType);
    }

    [Fact]
    public async Task Same_key_with_another_payload_or_another_actor_is_an_idempotency_conflict()
    {
        await using var run = await Run.StartAsync();
        await using (var scope = run.Scope("treasurer", new AllowAll(), Sufficient()))
        {
            var first = await scope.Service.CommitAsync(scope.Context, Key("transfer-1"),
                new CommitCashTransferRequest(SameCurrency(), true), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, first.Outcome);
            var changed = await scope.Service.CommitAsync(scope.Context, Key("transfer-1"),
                new CommitCashTransferRequest(SameCurrency(amount: "999.00"), true), CancellationToken.None);
            Assert.Equal((CommandOutcome.IdempotencyConflict, (CashTransferView?)null),
                (changed.Outcome, changed.Resource));
        }

        await using (var other = run.Scope("other-treasurer", new AllowAll(), Sufficient()))
        {
            var otherActor = await other.Service.CommitAsync(other.Context, Key("transfer-1"),
                new CommitCashTransferRequest(SameCurrency(), true), CancellationToken.None);
            Assert.Equal(CommandOutcome.IdempotencyConflict, otherActor.Outcome);
        }

        await using var verify = run.Context();
        Assert.Equal(1, await verify.CashTransfers.CountAsync());
    }

    [Fact]
    public async Task Unconfirmed_commit_is_refused_writes_nothing_and_does_not_consume_the_key()
    {
        await using var run = await Run.StartAsync();
        await using (var scope = run.Scope("treasurer", new AllowAll(), Sufficient()))
        {
            var refused = await scope.Service.CommitAsync(scope.Context, Key("transfer-1"),
                new CommitCashTransferRequest(SameCurrency(), TransferConfirmed: false), CancellationToken.None);
            Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
            Assert.Equal(("transferConfirmed", "cash_transfer_not_confirmed"),
                (refused.Issues.Single().Field, refused.Issues.Single().Code));
        }

        await using var verify = run.Context();
        Assert.Equal((0, 0, 0), (await verify.CashTransfers.CountAsync(), await verify.CashTransferCommands.CountAsync(),
            await verify.Set<AuditEvent>().CountAsync()));
    }

    [Fact]
    public async Task Currency_exchange_is_refused_as_unavailable_by_the_registered_default_rate_rule()
    {
        await using var run = await Run.StartAsync();
        await using (var scope = run.Scope("treasurer", new AllowAll(), Sufficient()))
        {
            var refused = await scope.Service.CommitAsync(scope.Context, Key("exchange-1"),
                new CommitCashTransferRequest(Exchange(), true), CancellationToken.None);
            Assert.Equal(CommandOutcome.Unavailable, refused.Outcome);
            Assert.Equal(("rate", CurrencyExchangeRateAnswer.UnavailableCode),
                (refused.Issues.Single().Field, refused.Issues.Single().Code));
            var preview = await scope.Service.PreviewAsync(scope.Context, Exchange(), CancellationToken.None);
            Assert.Equal((CommandOutcome.Unavailable, (CashTransferPreparation?)null),
                (preview.Outcome, preview.Preparation));
        }

        await using var verify = run.Context();
        Assert.Equal(0, await verify.CashTransfers.CountAsync());
    }

    [Fact]
    public async Task Currency_exchange_with_a_consistent_rate_rule_is_one_record_carrying_both_amounts_and_the_rate()
    {
        await using var run = await Run.StartAsync();
        CashTransferCommandResult committed;
        await using (var scope = run.Scope("treasurer", new AllowAll(), Sufficient(),
                         new FixedRate(CurrencyExchangeRateOutcome.Consistent)))
        {
            var preview = await scope.Service.PreviewAsync(scope.Context, Exchange() with { Rate = null }, CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, preview.Outcome);
            Assert.Null(preview.Preparation!.Rate);
            Assert.Equal("36.50", preview.Exchange!.TransactionRate);
            committed = await scope.Service.CommitAsync(scope.Context, Key("exchange-1"),
                new CommitCashTransferRequest(Exchange() with { Rate = null }, true), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, committed.Outcome);
        }

        // Owner availability may change after the money event: the exact original result is replayed without re-quoting.
        await using (var scope = run.Scope("treasurer", new AllowAll(), Sufficient()))
        {
            var replay = await scope.Service.CommitAsync(scope.Context, Key("exchange-1"),
                new CommitCashTransferRequest(Exchange() with { Rate = null }, true), CancellationToken.None);
            Assert.True(replay.IsReplay);
            Assert.Equal(committed.Resource!.Exchange, replay.Resource!.Exchange);
        }

        await using var verify = run.Context();
        var row = await verify.CashTransfers.SingleAsync();
        Assert.Equal((CashTransferKinds.CurrencyExchange, Euro, "100.00", Lira, "3650.00", "36.50"),
            (row.Kind, row.SourceCurrencyId, row.SourceAmountText, row.TargetCurrencyId, row.TargetAmountText,
                row.RateText));
        Assert.NotNull(row.ExchangeSnapshotJson);
        Assert.Single(await verify.CashTransferCommands.ToListAsync());
    }

    [Fact]
    public async Task Inconsistent_rate_is_a_validation_failure_and_writes_nothing()
    {
        await using var run = await Run.StartAsync();
        await using (var scope = run.Scope("treasurer", new AllowAll(), Sufficient(),
                         new FixedRate(CurrencyExchangeRateOutcome.Inconsistent)))
        {
            var refused = await scope.Service.CommitAsync(scope.Context, Key("exchange-1"),
                new CommitCashTransferRequest(Exchange(), true), CancellationToken.None);
            Assert.Equal((CommandOutcome.ValidationFailed, CurrencyExchangeRateAnswer.InconsistentCode),
                (refused.Outcome, refused.Issues.Single().Code));
        }

        await using var verify = run.Context();
        Assert.Equal(0, await verify.CashTransfers.CountAsync());
    }

    [Theory]
    [InlineData(TreasuryBalanceOutcome.Unavailable, CommandOutcome.Unavailable, "finance_treasury_balance_unavailable")]
    [InlineData(TreasuryBalanceOutcome.Insufficient, CommandOutcome.ValidationFailed, "finance_treasury_balance_insufficient")]
    public async Task Balance_guard_that_does_not_answer_sufficient_refuses_and_never_clips(
        TreasuryBalanceOutcome balance,
        CommandOutcome expected,
        string code)
    {
        await using var run = await Run.StartAsync();
        await using (var scope = run.Scope("treasurer", new AllowAll(), new FixedBalance(balance)))
        {
            var refused = await scope.Service.CommitAsync(scope.Context, Key("transfer-1"),
                new CommitCashTransferRequest(SameCurrency(), true), CancellationToken.None);
            Assert.Equal((expected, code), (refused.Outcome, refused.Issues.Single().Code));
        }

        await using var verify = run.Context();
        Assert.Equal((0, 0), (await verify.CashTransfers.CountAsync(), await verify.CashTransferCommands.CountAsync()));
    }

    [Fact]
    public async Task Registered_default_balance_guard_refuses_every_commit_as_unavailable()
    {
        await using var run = await Run.StartAsync();
        await using var scope = run.Scope("treasurer", new AllowAll(), new UnavailableTreasuryBalanceGuard());
        var refused = await scope.Service.CommitAsync(scope.Context, Key("transfer-1"),
            new CommitCashTransferRequest(SameCurrency(), true), CancellationToken.None);
        Assert.Equal((CommandOutcome.Unavailable, TreasuryBalanceAnswer.UnavailableCode),
            (refused.Outcome, refused.Issues.Single().Code));
    }

    [Theory]
    [InlineData(PermissionDecision.Denied, CommandOutcome.Denied)]
    [InlineData(PermissionDecision.Unavailable, CommandOutcome.Unavailable)]
    public async Task Permission_that_is_not_allowed_refuses_preview_and_commit(
        PermissionDecision decision,
        CommandOutcome expected)
    {
        await using var run = await Run.StartAsync();
        await using (var scope = run.Scope("treasurer", new FixedPermission(decision), Sufficient()))
        {
            var commit = await scope.Service.CommitAsync(scope.Context, Key("transfer-1"),
                new CommitCashTransferRequest(SameCurrency(), true), CancellationToken.None);
            var preview = await scope.Service.PreviewAsync(scope.Context, SameCurrency(), CancellationToken.None);
            Assert.Equal((expected, expected), (commit.Outcome, preview.Outcome));
        }

        await using var verify = run.Context();
        Assert.Equal(0, await verify.CashTransfers.CountAsync());
    }

    [Fact]
    public async Task Context_for_another_actor_is_denied_before_any_read()
    {
        await using var run = await Run.StartAsync();
        await using var scope = run.Scope("treasurer", new AllowAll(), Sufficient());
        var foreign = scope.Context with { ActorId = "someone-else" };
        var commit = await scope.Service.CommitAsync(foreign, Key("transfer-1"),
            new CommitCashTransferRequest(SameCurrency(), true), CancellationToken.None);
        var list = await scope.Service.ListAuthorizedAsync(foreign, null, CancellationToken.None);
        Assert.Equal((CommandOutcome.Denied, ReadOutcome.Denied), (commit.Outcome, list.Outcome));
    }

    [Fact]
    public async Task Ineligible_or_currency_mismatched_accounts_are_refused()
    {
        await using var run = await Run.StartAsync();
        await using var scope = run.Scope("treasurer", new AllowAll(), Sufficient());

        var pending = await scope.Service.CommitAsync(scope.Context, Key("transfer-1"),
            new CommitCashTransferRequest(SameCurrency(target: CashPending), true), CancellationToken.None);
        Assert.Equal((CommandOutcome.ValidationFailed, "targetAccountId", "cash_transfer_target_ineligible"),
            (pending.Outcome, pending.Issues.Single().Field, pending.Issues.Single().Code));

        var unknown = await scope.Service.CommitAsync(scope.Context, Key("transfer-2"),
            new CommitCashTransferRequest(SameCurrency(source: Guid.NewGuid(), sourceKind: CashTransferAccountKind.Cash),
                true), CancellationToken.None);
        Assert.Equal("cash_transfer_source_ineligible", unknown.Issues.Single().Code);

        var wrongCurrency = await scope.Service.CommitAsync(scope.Context, Key("transfer-3"),
            new CommitCashTransferRequest(SameCurrency(target: CashEuro), true), CancellationToken.None);
        Assert.Equal("cash_transfer_target_ineligible", wrongCurrency.Issues.Single().Code);
    }

    [Fact]
    public async Task Shape_failures_are_refused_before_any_owner_read()
    {
        await using var run = await Run.StartAsync();
        await using var scope = run.Scope("treasurer", new AllowAll(), Sufficient());
        var same = await scope.Service.CommitAsync(scope.Context, Key("transfer-1"),
            new CommitCashTransferRequest(SameCurrency(target: Bank, targetKind: CashTransferAccountKind.Bank), true),
            CancellationToken.None);
        Assert.Equal((CommandOutcome.ValidationFailed, "cash_transfer_same_account"),
            (same.Outcome, same.Issues.Single().Code));

        var blankKey = await scope.Service.CommitAsync(scope.Context, Key(" padded "),
            new CommitCashTransferRequest(SameCurrency(), true), CancellationToken.None);
        Assert.Equal("idempotency_key_invalid", blankKey.Issues.Single().Code);
    }

    [Fact]
    public async Task Profile_that_the_client_did_not_name_is_a_profile_mismatch()
    {
        await using var run = await Run.StartAsync();
        await using var scope = run.Scope("treasurer", new AllowAll(), Sufficient());
        var preparation = SameCurrency() with { TargetProfile = new ProfileReference(Profile.ToString("D"), "9") };
        var refused = await scope.Service.CommitAsync(scope.Context, Key("transfer-1"),
            new CommitCashTransferRequest(preparation, true), CancellationToken.None);
        Assert.Equal((CommandOutcome.ValidationFailed, "targetProfile", "profile_mismatch"),
            (refused.Outcome, refused.Issues.Single().Field, refused.Issues.Single().Code));
    }

    [Fact]
    public async Task Missing_money_operation_binding_refuses_as_unavailable()
    {
        await using var run = await Run.StartAsync();
        await using var scope = run.Scope("treasurer", new AllowAll(), Sufficient(), operation: null);
        var refused = await scope.Service.CommitAsync(scope.Context, Key("transfer-1"),
            new CommitCashTransferRequest(SameCurrency(), true), CancellationToken.None);
        Assert.Equal((CommandOutcome.Unavailable, "finance_operation_mapping_unavailable"),
            (refused.Outcome, refused.Issues.Single().Code));
    }

    [Fact]
    public async Task Closed_period_refuses_the_commit()
    {
        await using var run = await Run.StartAsync();
        await using var scope = run.Scope("treasurer", new AllowAll(), Sufficient(),
            period: new FixedPeriod(RecordPeriodOutcome.NotOpen));
        var refused = await scope.Service.CommitAsync(scope.Context, Key("transfer-1"),
            new CommitCashTransferRequest(SameCurrency(), true), CancellationToken.None);
        Assert.Equal((CommandOutcome.ValidationFailed, "occurredOn"), (refused.Outcome, refused.Issues.Single().Field));
    }

    [Fact]
    public async Task Preview_runs_the_commit_checks_and_writes_nothing()
    {
        await using var run = await Run.StartAsync();
        await using (var scope = run.Scope("treasurer", new AllowAll(), Sufficient()))
        {
            var preview = await scope.Service.PreviewAsync(scope.Context, SameCurrency(), CancellationToken.None);
            Assert.Equal((CommandOutcome.Succeeded, 0), (preview.Outcome, preview.Issues.Count));
            Assert.Equal(SameCurrency(), preview.Preparation);
        }

        await using var verify = run.Context();
        Assert.Equal((0, 0, 0), (await verify.CashTransfers.CountAsync(), await verify.CashTransferCommands.CountAsync(),
            await verify.Set<AuditEvent>().CountAsync()));
    }

    [Fact]
    public async Task List_and_detail_answer_committed_transfers_and_not_found_for_unknown_ids()
    {
        await using var run = await Run.StartAsync();
        await using var scope = run.Scope("treasurer", new AllowAll(), Sufficient());
        var committed = await scope.Service.CommitAsync(scope.Context, Key("transfer-1"),
            new CommitCashTransferRequest(SameCurrency(), true), CancellationToken.None);

        var list = await scope.Service.ListAuthorizedAsync(scope.Context, null, CancellationToken.None);
        Assert.Equal((ReadOutcome.Found, Company, 1, (string?)null),
            (list.Outcome, list.Resource!.CompanyId, list.Resource.Items.Count, list.Resource.NextCursor));
        var detail = await scope.Service.DetailAuthorizedAsync(scope.Context, committed.Resource!.Id,
            CancellationToken.None);
        Assert.Equal((ReadOutcome.Found, committed.Resource.Id), (detail.Outcome, detail.Resource!.Id));
        var missing = await scope.Service.DetailAuthorizedAsync(scope.Context, Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(ReadOutcome.NotFound, missing.Outcome);
        var badCursor = await scope.Service.ListAuthorizedAsync(scope.Context, "not-a-cursor", CancellationToken.None);
        Assert.Equal(ReadOutcome.NotFound, badCursor.Outcome);
    }

    [Fact]
    public async Task Account_context_answers_the_currency_and_profile_of_an_eligible_account_only()
    {
        await using var run = await Run.StartAsync();
        await using var scope = run.Scope("treasurer", new AllowAll(), Sufficient());
        var bank = await scope.Service.AccountContextAuthorizedAsync(scope.Context, CashTransferAccountKind.Bank, Bank,
            OccurredOn, CancellationToken.None);
        Assert.Equal((ReadOutcome.Found, Lira, Profile.ToString("D"), "1"),
            (bank.Outcome, bank.Resource!.CurrencyId, bank.Resource.Profile.ProfileId, bank.Resource.Profile.Version));
        var euro = await scope.Service.AccountContextAuthorizedAsync(scope.Context, CashTransferAccountKind.Cash,
            CashEuro, OccurredOn, CancellationToken.None);
        Assert.Equal((ReadOutcome.Found, Euro), (euro.Outcome, euro.Resource!.CurrencyId));
        var pending = await scope.Service.AccountContextAuthorizedAsync(scope.Context, CashTransferAccountKind.Cash,
            CashPending, OccurredOn, CancellationToken.None);
        Assert.Equal(ReadOutcome.NotFound, pending.Outcome);
        var wrongKind = await scope.Service.AccountContextAuthorizedAsync(scope.Context, CashTransferAccountKind.Cash,
            Bank, OccurredOn, CancellationToken.None);
        Assert.Equal(ReadOutcome.NotFound, wrongKind.Outcome);
    }

    [Fact]
    public async Task Passivation_reader_never_claims_zero_from_an_unprotected_InMemory_read()
    {
        await using var run = await Run.StartAsync();
        await using (var before = run.Context())
        {
            Assert.Equal(CashAccountBalanceState.Unavailable, await new TreasuryMovementAwareCashAccountBalanceReader(before, ResolvedCompany(Company))
                .ReadAsync(Company, CashLira, CancellationToken.None));
        }

        await using (var scope = run.Scope("treasurer", new AllowAll(), Sufficient()))
        {
            var committed = await scope.Service.CommitAsync(scope.Context, Key("transfer-1"),
                new CommitCashTransferRequest(SameCurrency(), true), CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, committed.Outcome);
        }

        await using var after = run.Context();
        var reader = new TreasuryMovementAwareCashAccountBalanceReader(after, ResolvedCompany(Company));
        Assert.Equal(CashAccountBalanceState.Unavailable, await reader.ReadAsync(Company, CashLira,
            CancellationToken.None));
        Assert.Equal(CashAccountBalanceState.Unavailable, await reader.ReadAsync(Company, CashEuro, CancellationToken.None));
        Assert.Equal(CashAccountBalanceState.Unavailable, await reader.ReadAsync(Guid.Empty, CashEuro,
            CancellationToken.None));
    }

    private static CompanyContext ResolvedCompany(Guid id)
    {
        var company = new CompanyContext();
        company.Set(id);
        return company;
    }

    private static CommandMetadata Key(string value) => new(value);

    private static FixedBalance Sufficient() => new(TreasuryBalanceOutcome.Sufficient);

    private static ProfileReference ProfileRef() => new(Profile.ToString("D"), "1");

    private static CashTransferPreparation SameCurrency(
        string amount = "1000.00",
        Guid? source = null,
        CashTransferAccountKind sourceKind = CashTransferAccountKind.Bank,
        Guid? target = null,
        CashTransferAccountKind targetKind = CashTransferAccountKind.Cash) => new(
        CashTransferKind.SameCurrency,
        sourceKind, source ?? Bank, new MoneyText(amount, Lira), ProfileRef(),
        targetKind, target ?? CashLira, new MoneyText(amount, Lira), ProfileRef(),
        null, OccurredOn, null, null, "SENTETIK kasaya cekis");

    private static CashTransferPreparation Exchange() => new(
        CashTransferKind.CurrencyExchange,
        CashTransferAccountKind.Cash, CashEuro, new MoneyText("100.00", Euro), ProfileRef(),
        CashTransferAccountKind.Bank, Bank, new MoneyText("3650.00", Lira), ProfileRef(),
        "36.5", OccurredOn, null, null, "SENTETIK doviz bozdurma");

    private sealed class Run(string databaseName) : IAsyncDisposable
    {
        public static async Task<Run> StartAsync()
        {
            var run = new Run($"finance-fns-d24-{Guid.NewGuid():N}");
            await using var context = run.Context();
            var at = DateTimeOffset.UtcNow;
            context.BankAccounts.Add(new BankAccountRow
            {
                Id = Bank,
                CompanyId = Company,
                CurrencyId = Lira,
                DisplayName = "SENTETIK banka",
                BankName = "SENTETIK",
                AccountType = "Vadesiz",
                Iban = "TR330006100519786457841326",
                MoneyMovementEligible = true,
                Status = 2,
                Version = 2,
                ProposedBy = "bank-maker",
                ProposedAtUtc = at,
                ApprovedBy = "bank-checker",
                ApprovedAtUtc = at,
                UpdatedAtUtc = at,
            });
            context.CashAccounts.AddRange(
                Cash(CashLira, Lira, status: 2, at),
                Cash(CashEuro, Euro, status: 2, at),
                Cash(CashPending, Lira, status: 1, at));
            await context.SaveChangesAsync();
            return run;
        }

        public FinanceDbContext Context()
        {
            var company = new CompanyContext();
            company.Set(Company);
            return new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase(databaseName).Options, company);
        }

        public Scope Scope(
            string actor,
            IFinancePermissionPort permission,
            IFinanceTreasuryBalanceGuard balance,
            IFinanceCurrencyExchangeRateRule? rate = null,
            IFinanceRecordPeriodRule? period = null,
            string? operation = FinanceMoneyOperations.ManualBankPaymentV1)
        {
            var company = new CompanyContext();
            company.Set(Company);
            var collector = new AuditEventCollector();
            var context = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase(databaseName)
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).Options, company);
            var service = new CashTransferService(context, company, new User(actor), permission, balance,
                rate ?? new UnavailableCurrencyExchangeRateRule(), new SettingsPort(), period ?? new FixedPeriod(
                    RecordPeriodOutcome.Open), collector, new CashTransferBinding(operation),
                new CashTransferAuditEnvelopes(Envelope(FinancePermissionNames.CashTransferCommit)),
                TimeProvider.System, documentNumbers: new SyntheticFinanceRecordNumbers());
            return new Scope(context, service,
                new FinanceContext(Company, actor, $"fns-d24-{Guid.NewGuid():N}", null));
        }

        public async ValueTask DisposeAsync()
        {
            await using var context = Context();
            await context.Database.EnsureDeletedAsync();
        }

        private static CashAccountRow Cash(Guid id, Guid currency, int status, DateTimeOffset at) => new()
        {
            Id = id,
            CompanyId = Company,
            CurrencyId = currency,
            DisplayName = "SENTETIK kasa",
            Status = status,
            Version = status,
            ProposedBy = "cash-maker",
            ProposedAtUtc = at,
            DecidedBy = status == 2 ? "cash-checker" : null,
            DecidedAtUtc = status == 2 ? at : null,
            UpdatedAtUtc = at,
        };

        private static FinanceAuditEnvelope Envelope(string permission) =>
            new(permission, ["SENTETIK-FINANCE"], "Web");
    }

    private sealed class Scope(FinanceDbContext db, CashTransferService service, FinanceContext context)
        : IAsyncDisposable
    {
        public ICashTransferService Service => service;
        public FinanceContext Context => context;
        public ValueTask DisposeAsync() => db.DisposeAsync();
    }

    private sealed class AllowAll : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }

    private sealed class FixedPermission(PermissionDecision decision) : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(decision);
    }

    private sealed class FixedBalance(TreasuryBalanceOutcome outcome) : IFinanceTreasuryBalanceGuard
    {
        public Task<TreasuryBalanceAnswer> CheckOutgoingAsync(FinanceContext context,
            ExpensePaymentSourceKind sourceKind, Guid treasuryAccountId, MoneyText money,
            CancellationToken cancellationToken) => Task.FromResult(new TreasuryBalanceAnswer(outcome,
            outcome switch
            {
                TreasuryBalanceOutcome.Insufficient => TreasuryBalanceAnswer.InsufficientCode,
                TreasuryBalanceOutcome.Unavailable => TreasuryBalanceAnswer.UnavailableCode,
                _ => null,
            }));
    }

    private sealed class FixedRate(CurrencyExchangeRateOutcome outcome) : IFinanceCurrencyExchangeRateRule
    {
        public Task<CurrencyExchangeRateAnswer> CheckAsync(FinanceContext context, MoneyText sourceMoney,
            MoneyText targetMoney, string? rate, DateOnly occurredOn, CancellationToken cancellationToken) =>
            Task.FromResult(new CurrencyExchangeRateAnswer(outcome, outcome switch
            {
                CurrencyExchangeRateOutcome.Inconsistent => CurrencyExchangeRateAnswer.InconsistentCode,
                CurrencyExchangeRateOutcome.Unavailable => CurrencyExchangeRateAnswer.UnavailableCode,
                _ => null,
            }, outcome == CurrencyExchangeRateOutcome.Consistent ? CashTransferExchangeCalculatorV1.Calculate(
                context.CompanyId, occurredOn, sourceMoney, targetMoney, new(Profile.ToString("D"), "1"),
                new(Profile.ToString("D"), "1"), new(sourceMoney.CurrencyId, "36.5", occurredOn, "TCMB", Guid.NewGuid(),
                    "1", occurredOn.AddDays(-1), "SENTETIK / NOT_ENV"),
                new(targetMoney.CurrencyId, "1", occurredOn, "FunctionalCurrency", null, null, null, null)) : null));
    }

    private sealed class SettingsPort : ISettingsPrerequisiteProvider
    {
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => Task.FromResult(new MoneyProfileV1(
            Company, currencyId, currencyId == Euro ? "EUR" : "TRY", FinanceMoneyOperations.ManualBankPaymentV1,
            new ProfileVersionV1(Profile, 1), 2, true, new DateOnly(2026, 1, 1), null));

        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn,
            DateOnly? valueOn, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedPeriod(RecordPeriodOutcome outcome) : IFinanceRecordPeriodRule
    {
        public Task<RecordPeriodAnswer> CheckAsync(FinanceContext context, DateOnly occurredOn, DateOnly? valueOn,
            DateTimeOffset recordedAtUtc, CancellationToken cancellationToken) =>
            Task.FromResult(new RecordPeriodAnswer(outcome, null));
    }

    private sealed class User(string actor) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }
}
