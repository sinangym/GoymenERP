using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// API-23: maker-checker reversal of a RECORDED manual bank payment through the real
/// <see cref="PaymentReversalProposalService"/> against a REAL SQL Server. Approval by another user writes, in one
/// save, the immutable counter-event, the zero allocation barrier, the proposal decision, the command record and the
/// audit; the payment and its outgoing event stay untouched; the party ledger shows the counter-event as its own
/// credit row; a reversed payment can never be allocated; an active allocation blocks the reversal; and the
/// migration's Down refuses while a proposal is stored. Party, account, payment and authority are SENTETIK inputs.
/// </summary>
public sealed class FinancePaymentReversalSqlServerRelationalAcceptanceTests
{
    private const string Maker = "SENTETIK-reversal-maker";
    private const string Checker = "SENTETIK-reversal-checker";
    private const string BeforePaymentReversals = "20261107154419_AddPaymentAllocations";
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid Lira = Guid.Parse("7c3f1f72-8d4c-4e9f-a021-3c4d5e6f7101");
    private static readonly Guid Party = Guid.Parse("7c3f1f72-8d4c-4e9f-a021-3c4d5e6f7103");
    private static readonly Guid Account = Guid.Parse("7c3f1f72-8d4c-4e9f-a021-3c4d5e6f7104");

    [SqlServerFact]
    public async Task An_approval_by_another_user_appends_one_counter_event_and_a_credit_ledger_row_in_one_save()
    {
        await using var run = await Run.StartAsync();
        var payment = await run.RecordPaymentAsync("250.00");

        var proposed = await run.ProposeAsync(Maker, "propose-1", payment);
        Assert.Equal(CommandOutcome.Succeeded, proposed.Outcome);
        Assert.Equal(ReversalProposalStatus.Pending, proposed.Resource!.Status);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutgoingMoneyEventReversals"));

        var self = await run.ApproveAsync(Maker, "self-approve", proposed.Resource.Id);
        Assert.Contains(self.Issues, issue => issue.Code == "finance_approver_is_proposer");

        var approved = await run.ApproveAsync(Checker, "approve-1", proposed.Resource.Id);
        Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
        Assert.Equal(ReversalProposalStatus.Approved, approved.Resource!.Status);
        var reversalEvent = approved.Resource.ReversalEventId!.Value;
        var replay = await run.ApproveAsync(Checker, "approve-1", proposed.Resource.Id);
        Assert.True(replay.IsReplay);
        Assert.Equal(approved.Resource, replay.Resource);

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OutgoingMoneyEventReversals r JOIN finance.OutgoingMoneyEvents e "
            + "ON e.Id = r.OriginalEventId WHERE r.Id = @id AND r.PaymentId = @payment AND r.ProposalId = @proposal "
            + "AND r.AmountText = '250.00' AND r.RecordedBy = @checker",
            ("@id", reversalEvent), ("@payment", payment), ("@proposal", proposed.Resource.Id),
            ("@checker", Checker)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.PaymentReversalProposals WHERE Id = @id AND Status = 1 "
            + "AND ProposedBy = @maker AND DecidedBy = @checker AND ReversalEventId = @event",
            ("@id", proposed.Resource.Id), ("@maker", Maker), ("@checker", Checker), ("@event", reversalEvent)));
        Assert.Equal("0.00", await run.ScalarAsync<string>(
            "SELECT UnallocatedAmountText FROM finance.PaymentAllocationStates WHERE PaymentId = @id AND Version = 1",
            ("@id", payment)));
        // The recorded payment and its outgoing event never change (no update, no second outgoing row).
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.ManualBankPayments WHERE Id = @id AND Version = 3 AND Status = @status",
            ("@id", payment), ("@status", ManualBankPaymentStatuses.Recorded)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutgoingMoneyEvents"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Commands WHERE CommandName = @name AND ManualBankPaymentId = @id",
            ("@name", PaymentReversalProposalService.ApproveCommand), ("@id", payment)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = 'finance.payment-reversal.approved.v1' "
            + "AND EntityId = @id", ("@id", proposed.Resource.Id.ToString("D"))));

        var ledger = Assert.Single((await run.LedgerAsync()).Ledger!.Currencies);
        Assert.Equal(
            [(PartyLedgerEntryKind.ManualBankPayment, PartyLedgerSide.Debit),
                (PartyLedgerEntryKind.ManualBankPaymentReversal, PartyLedgerSide.Credit)],
            ledger.Entries.Select(entry => (entry.Kind, entry.Side)).ToArray());
        var row = ledger.Entries[1];
        Assert.Equal((proposed.Resource.Id, (Guid?)payment, "250.00"),
            (row.SourceId, row.ReversesSourceId, row.Amount.Money!.Amount));
        Assert.Equal(PartyLedgerBalanceSide.Zero, ledger.ClosingBalance.Side);

        var again = await run.ProposeAsync(Maker, "propose-2", payment);
        Assert.Contains(again.Issues, issue => issue.Code == "finance_source_already_reversed");
    }

    [SqlServerFact]
    public async Task A_reversed_payment_is_refused_by_the_allocation_service_and_an_active_allocation_blocks_the_reversal()
    {
        await using var run = await Run.StartAsync();
        var invoice = await run.AdmitPayableAsync("400.00");
        var item = invoice.Installments[0].OpenItemId;

        // Reversed first: the allocation service names the reason and writes nothing.
        var reversed = await run.RecordPaymentAsync("100.00");
        var proposal = await run.ProposeAsync(Maker, "propose-r", reversed);
        Assert.Equal(CommandOutcome.Succeeded,
            (await run.ApproveAsync(Checker, "approve-r", proposal.Resource!.Id)).Outcome);
        var refused = await run.AllocateAsync("allocate-r", reversed, item, "10");
        Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
        Assert.Contains(refused.Issues, issue => issue.Code == "finance_source_already_reversed"
            && issue.Field == "paymentId");
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PaymentAllocations"));

        // Allocated first: approval refuses while the allocation is active and appends nothing.
        var allocated = await run.RecordPaymentAsync("100.00");
        Assert.Equal(CommandOutcome.Succeeded,
            (await run.AllocateAsync("allocate-a", allocated, item, "40")).Outcome);
        var blockedProposal = await run.ProposeAsync(Maker, "propose-a", allocated);
        Assert.Contains(blockedProposal.Issues, issue => issue.Code == "finance_reversal_has_allocations");
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutgoingMoneyEventReversals"));
        Assert.Equal("60.00", await run.ScalarAsync<string>(
            "SELECT UnallocatedAmountText FROM finance.PaymentAllocationStates WHERE PaymentId = @id",
            ("@id", allocated)));
    }

    [SqlServerFact]
    public async Task The_payment_reversals_migrations_Down_passes_on_an_empty_store_and_refuses_while_a_proposal_exists()
    {
        await using var run = await Run.StartAsync();
        const string Tables = "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'finance') "
            + "AND name IN (N'PaymentReversalProposals', N'OutgoingMoneyEventReversals')";
        var payment = await run.RecordPaymentAsync("10.00");

        // A recorded payment alone is harmless: the Down runs and the reapply restores the tables.
        await run.MigrateFinanceAsync(BeforePaymentReversals);
        Assert.Equal(0, await run.ScalarAsync<int>(Tables));
        await run.MigrateFinanceAsync(null);
        Assert.Equal(2, await run.ScalarAsync<int>(Tables));

        var proposal = await run.ProposeAsync(Maker, "rollback-1", payment);
        Assert.Equal(CommandOutcome.Succeeded, proposal.Outcome);
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.MigrateFinanceAsync(BeforePaymentReversals));
        Assert.Equal(51051, refused.Number);
        Assert.Equal(2, await run.ScalarAsync<int>(Tables));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.PaymentReversalProposals WHERE Id = @id", ("@id", proposal.Resource!.Id)));
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            await using var context = run.Context(null);
            await context.Database.MigrateAsync();
            return run;
        }

        public async Task<FinanceInvoiceOpenItemAdmissionV1> AdmitPayableAsync(string amount)
        {
            var invoice = new FinanceInvoiceOpenItemAdmissionV1(Guid.NewGuid(), FinanceInvoiceProducerV1.Purchasing,
                Guid.NewGuid(), "invoice-v1", Party, Account, Lira, null,
                [new FinanceInvoiceInstallmentV1(Guid.NewGuid(), Guid.NewGuid(), 1, new DateOnly(2026, 12, 1), amount,
                    null)]);
            var (companyContext, collector) = Scope();
            await using var dbContext = new FinanceDbContext(Options(companyContext, collector), companyContext);
            var service = new FinanceInvoiceOpenItemAdmissionService(dbContext, companyContext,
                new FixedCurrentUser(Maker), new SyntheticParty(), new SyntheticAdmissionAuthority(), collector,
                new FinanceAuditEnvelope("synthetic.finance.invoice-open-items.admit", ["SENTETIK-FINANCE"], "Web"),
                TimeProvider.System);
            var result = await service.AdmitAsync(new FinanceContext(CompanyA, Maker, $"api23-{Guid.NewGuid():N}",
                null), new CommandMetadata($"api23-admit-{invoice.InvoiceId:N}"), invoice, CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            return invoice;
        }

        /// <summary>SENTETIK: a recorded manual bank payment at version 3 and its one outgoing money event.</summary>
        public async Task<Guid> RecordPaymentAsync(string amount)
        {
            await using var db = Context(null);
            var now = DateTimeOffset.UtcNow;
            var today = DateOnly.FromDateTime(now.UtcDateTime);
            var id = Guid.NewGuid();
            var treasury = Guid.NewGuid();
            var profile = Guid.NewGuid().ToString("D");
            db.ManualBankPayments.Add(new ManualBankPaymentRow
            {
                Id = id,
                CompanyId = CompanyA,
                Version = 3,
                PartyId = Party,
                CompanyAccountId = Account,
                TreasuryAccountId = treasury,
                CurrencyId = Lira,
                AmountText = amount,
                ProfileId = profile,
                ProfileVersion = "1",
                OccurredOn = today,
                ValueOn = today,
                Status = ManualBankPaymentStatuses.Recorded,
                PreparedBy = "SENTETIK-preparer",
                PreparedAtUtc = now,
                ApprovedBy = "SENTETIK-approver",
                ApprovedAtUtc = now,
                RecordedBy = "SENTETIK-recorder",
                RecordedAtUtc = now,
                BankResultConfirmed = true,
            });
            db.OutgoingMoneyEvents.Add(new OutgoingMoneyEventRow
            {
                Id = Guid.NewGuid(),
                CompanyId = CompanyA,
                PaymentId = id,
                PartyId = Party,
                CompanyAccountId = Account,
                TreasuryAccountId = treasury,
                CurrencyId = Lira,
                AmountText = amount,
                ProfileId = profile,
                ProfileVersion = "1",
                OccurredOn = today,
                ValueOn = today,
                RecordedAtUtc = now,
                RecordedBy = "SENTETIK-recorder",
                CorrelationId = "SENTETIK-api23-payment",
            });
            await db.SaveChangesAsync();
            return id;
        }

        public Task<PaymentReversalProposalCommandResult> ProposeAsync(string actor, string key, Guid payment) =>
            WithReversalAsync(actor, (service, context) => service.ProposeAsync(context,
                new CommandMetadata($"api23-{key}"),
                new ProposePaymentReversalRequest(payment, "3", DateOnly.FromDateTime(DateTime.UtcNow),
                    "SENTETIK banka iadesi"), CancellationToken.None));

        public Task<PaymentReversalProposalCommandResult> ApproveAsync(string actor, string key, Guid proposal) =>
            WithReversalAsync(actor, (service, context) => service.ApproveAsync(context,
                new CommandMetadata($"api23-{key}"), new ApprovePaymentReversalRequest(proposal, "1"),
                CancellationToken.None));

        public async Task<FinancePaymentAllocationCommandResult> AllocateAsync(string key, Guid payment, Guid openItem,
            string amount)
        {
            var (companyContext, collector) = Scope();
            await using var dbContext = new FinanceDbContext(Options(companyContext, collector), companyContext);
            var service = new FinancePaymentAllocationService(dbContext, companyContext, new FixedCurrentUser(Maker),
                new SyntheticPermissions(), collector,
                new FinanceAuditEnvelope("synthetic.finance.allocations.write", ["SENTETIK-FINANCE"], "Web"),
                TimeProvider.System);
            var current = await dbContext.OpenItems.AsNoTracking().SingleAsync(row => row.Id == openItem);
            return await service.CreateAsync(new FinanceContext(CompanyA, Maker, $"api23-{Guid.NewGuid():N}", null),
                new CommandMetadata($"api23-{key}"),
                new CreateFinancePaymentAllocationRequest(Guid.NewGuid(), payment, "3", openItem,
                    current.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), amount,
                    "SENTETIK tedarikci odemesi dagitimi"), CancellationToken.None);
        }

        public async Task<PartyLedgerResult> LedgerAsync()
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var (companyContext, _) = Scope();
            await using var dbContext = Context(null);
            return await new PartyLedgerService(dbContext, companyContext, new FixedCurrentUser(Maker),
                    new SyntheticPermissions())
                .ReadAuthorizedAsync(new FinanceContext(CompanyA, Maker, "api23-ledger", null),
                    new PartyLedgerRequest(Party, Account, today.AddDays(-1), today.AddDays(1)), CancellationToken.None);
        }

        /// <summary>Moves Finance's schema to <paramref name="target"/> (null: the latest migration).</summary>
        public async Task MigrateFinanceAsync(string? target)
        {
            await using var context = Context(null);
            await context.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync(target);
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

        private async Task<PaymentReversalProposalCommandResult> WithReversalAsync(string actor,
            Func<PaymentReversalProposalService, FinanceContext, Task<PaymentReversalProposalCommandResult>> call)
        {
            var (companyContext, collector) = Scope();
            await using var dbContext = new FinanceDbContext(Options(companyContext, collector), companyContext);
            var envelope = new FinanceAuditEnvelope("synthetic.finance.payment-reversals.write", ["SENTETIK-FINANCE"],
                "Web");
            var service = new PaymentReversalProposalService(dbContext, companyContext, new FixedCurrentUser(actor),
                new SyntheticPermissions(), new OpenPeriod(), collector,
                new FinancePaymentReversalEnvelopes(envelope, envelope), TimeProvider.System);
            return await call(service, new FinanceContext(CompanyA, actor, $"api23-{Guid.NewGuid():N}", null));
        }

        private static (CompanyContext, AuditEventCollector) Scope()
        {
            var companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            return (companyContext, new AuditEventCollector());
        }

        private FinanceDbContext Context(AuditEventCollector? collector)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            return new FinanceDbContext(Options(companyContext, collector), companyContext);
        }

        private DbContextOptions<FinanceDbContext> Options(CompanyContext companyContext, AuditEventCollector? collector)
        {
            var builder = new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema));
            if (collector is not null)
            {
                builder.AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext));
            }

            return builder.Options;
        }
    }

    /// <summary>SENTETIK: one active party account (Party, Account) in company A.</summary>
    private sealed class SyntheticParty : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId,
            Guid companyAccountId, CancellationToken cancellationToken) =>
            Task.FromResult(new ReadResult<PartyAccountEligibility>(ReadOutcome.Found,
                new PartyAccountEligibility(Party, Account, CompanyA, true, IsPartyActive: true)));
    }

    /// <summary>SENTETIK: the host-owned admission authority answer. Not a grant.</summary>
    private sealed class SyntheticAdmissionAuthority : IFinanceInvoiceOpenItemAdmissionAuthorityV1
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceInvoiceProducerV1 producer,
            Guid invoiceId, CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }

    /// <summary>SENTETIK: every Finance permission fence allows. Not a grant.</summary>
    private sealed class SyntheticPermissions : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }

    /// <summary>SENTETIK: the record period is open.</summary>
    private sealed class OpenPeriod : IFinanceRecordPeriodRule
    {
        public Task<RecordPeriodAnswer> CheckAsync(FinanceContext context, DateOnly occurredOn, DateOnly? valueOn,
            DateTimeOffset recordedAtUtc, CancellationToken cancellationToken) =>
            Task.FromResult(new RecordPeriodAnswer(RecordPeriodOutcome.Open, null));
    }

    private sealed class FixedCurrentUser(string userId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyA };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
