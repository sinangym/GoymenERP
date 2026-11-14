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
    private const string SecondChecker = "SENTETIK-reversal-second-checker";
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

        // Allocated first: the proposal is refused while the allocation is active and nothing is appended.
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

    /// <summary>
    /// Review A P2-1: a first allocation of the payment and the approval of its pending reversal both read "no
    /// allocation" and race on the save. Exactly one commits completely; the other is the key's "concurrent_change"
    /// conflict and leaves nothing behind (no half allocation, no counter-event next to an allocation).
    /// </summary>
    [SqlServerFact]
    public async Task Concurrent_allocation_create_and_payment_reversal_approval_commit_exactly_one_complete_outcome()
    {
        await using var run = await Run.StartAsync();
        var invoice = await run.AdmitPayableAsync("400.00");
        var item = invoice.Installments[0].OpenItemId;
        var payment = await run.RecordPaymentAsync("100.00");
        var proposal = await run.ProposeAsync(Maker, "race-propose", payment);
        Assert.Equal(CommandOutcome.Succeeded, proposal.Outcome);
        var proposalId = proposal.Resource!.Id;

        var allocationId = Guid.NewGuid();
        var itemVersion = (await run.ScalarAsync<long>(
            "SELECT Version FROM finance.OpenItems WHERE Id = @id", ("@id", item)))
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var finalFence = new Barrier(2);
        var allocation = Task.Run(() => run.AllocateAsync("race-allocate", payment, item, "40",
            new FinalWriteBarrierPermission(FinanceAction.WriteAllocation, 2, finalFence), allocationId, itemVersion));
        var approval = Task.Run(() => run.ApproveAsync(Checker, "race-approve", proposalId,
            new FinalWriteBarrierPermission(FinanceAction.ApprovePaymentReversal, 3, finalFence)));
        await Task.WhenAll(allocation, approval);
        CommandOutcome[] outcomes = [allocation.Result.Outcome, approval.Result.Outcome];

        Assert.Single(outcomes, outcome => outcome == CommandOutcome.Succeeded);
        Assert.Single(outcomes, outcome => outcome == CommandOutcome.VersionConflict);
        var allocationWon = allocation.Result.Outcome == CommandOutcome.Succeeded;
        var loserIssues = allocationWon ? approval.Result.Issues : allocation.Result.Issues;
        Assert.Equal("concurrent_change", Assert.Single(loserIssues).Code);

        Assert.Equal(allocationWon ? "60.00" : "0.00", await run.ScalarAsync<string>(
            "SELECT UnallocatedAmountText FROM finance.PaymentAllocationStates WHERE PaymentId = @id",
            ("@id", payment)));
        Assert.Equal(allocationWon ? 1 : 0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.PaymentAllocations WHERE PaymentId = @id", ("@id", payment)));
        Assert.Equal(allocationWon ? 0 : 1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OutgoingMoneyEventReversals WHERE PaymentId = @id", ("@id", payment)));
        Assert.Equal(allocationWon ? 0 : 1, await run.ScalarAsync<int>(
            "SELECT Status FROM finance.PaymentReversalProposals WHERE Id = @id", ("@id", proposalId)));
        Assert.Equal(allocationWon ? "360.00" : "400.00", await run.ScalarAsync<string>(
            "SELECT OutstandingAmountText FROM finance.OpenItems WHERE Id = @id", ("@id", item)));
        Assert.Equal(allocationWon ? 1 : 0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Commands WHERE CommandName = N'finance.payment-allocation.create.v1'"));
        Assert.Equal(allocationWon ? 0 : 1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Commands WHERE CommandName = @name",
            ("@name", PaymentReversalProposalService.ApproveCommand)));

        // P3 (#66): the winner's key, sent again with the same request after the race, is a replay of the stored
        // outcome and the same resource; it writes nothing new.
        if (allocationWon)
        {
            var replay = await run.AllocateAsync("race-allocate", payment, item, "40", null, allocationId, itemVersion);
            Assert.Equal(CommandOutcome.Succeeded, replay.Outcome);
            Assert.True(replay.IsReplay);
            Assert.Equal(allocation.Result.Resource, replay.Resource);
        }
        else
        {
            var replay = await run.ApproveAsync(Checker, "race-approve", proposalId);
            Assert.Equal(CommandOutcome.Succeeded, replay.Outcome);
            Assert.True(replay.IsReplay);
            Assert.Equal(approval.Result.Resource, replay.Resource);
        }
        Assert.Equal(allocationWon ? 1 : 0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.PaymentAllocations WHERE PaymentId = @id", ("@id", payment)));
        Assert.Equal(allocationWon ? 0 : 1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OutgoingMoneyEventReversals WHERE PaymentId = @id", ("@id", payment)));
        Assert.Equal(allocationWon ? 1 : 0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Commands WHERE CommandName = N'finance.payment-allocation.create.v1'"));
        Assert.Equal(allocationWon ? 0 : 1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Commands WHERE CommandName = @name",
            ("@name", PaymentReversalProposalService.ApproveCommand)));
    }

    /// <summary>
    /// Review A P2-1: two different checkers approve the same pending proposal at the same time. Exactly one
    /// counter-event is appended; the other approval is the "concurrent_change" conflict with no second row.
    /// </summary>
    [SqlServerFact]
    public async Task Two_concurrent_payment_reversal_approvals_append_exactly_one_counter_event()
    {
        await using var run = await Run.StartAsync();
        var payment = await run.RecordPaymentAsync("100.00");
        var proposal = await run.ProposeAsync(Maker, "double-propose", payment);
        Assert.Equal(CommandOutcome.Succeeded, proposal.Outcome);
        var proposalId = proposal.Resource!.Id;

        using var finalFence = new Barrier(2);
        var first = Task.Run(() => run.ApproveAsync(Checker, "double-approve-1", proposalId,
            new FinalWriteBarrierPermission(FinanceAction.ApprovePaymentReversal, 3, finalFence)));
        var second = Task.Run(() => run.ApproveAsync(SecondChecker, "double-approve-2", proposalId,
            new FinalWriteBarrierPermission(FinanceAction.ApprovePaymentReversal, 3, finalFence)));
        await Task.WhenAll(first, second);
        CommandOutcome[] outcomes = [first.Result.Outcome, second.Result.Outcome];

        Assert.Single(outcomes, outcome => outcome == CommandOutcome.Succeeded);
        Assert.Single(outcomes, outcome => outcome == CommandOutcome.VersionConflict);
        var loser = first.Result.Outcome == CommandOutcome.Succeeded ? second.Result : first.Result;
        Assert.Equal("concurrent_change", Assert.Single(loser.Issues).Code);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OutgoingMoneyEventReversals WHERE PaymentId = @id", ("@id", payment)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.PaymentReversalProposals WHERE Id = @id AND Status = 1", ("@id", proposalId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Commands WHERE CommandName = @name",
            ("@name", PaymentReversalProposalService.ApproveCommand)));
    }

    /// <summary>
    /// Review A P3: a key reused for another payment is the idempotency conflict and stores no second proposal; an
    /// approve grant lost at the fence right before the save (the third approval check) is "denied" and writes no
    /// counter-event, no decision and no command, so the proposal stays pending for a later checker.
    /// </summary>
    [SqlServerFact]
    public async Task A_reused_key_is_an_idempotency_conflict_and_an_approve_grant_lost_before_the_save_writes_nothing()
    {
        await using var run = await Run.StartAsync();
        var payment = await run.RecordPaymentAsync("100.00");
        var other = await run.RecordPaymentAsync("50.00");
        var proposal = await run.ProposeAsync(Maker, "reuse", payment);
        Assert.Equal(CommandOutcome.Succeeded, proposal.Outcome);

        var reused = await run.ProposeAsync(Maker, "reuse", other);
        Assert.Equal(CommandOutcome.IdempotencyConflict, reused.Outcome);
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.PaymentReversalProposals WHERE PaymentId = @id", ("@id", other)));

        var lost = await run.ApproveAsync(Checker, "lost-grant", proposal.Resource!.Id,
            new DenyAtCheckPermission(FinanceAction.ApprovePaymentReversal, 3));
        Assert.Equal(CommandOutcome.Denied, lost.Outcome);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutgoingMoneyEventReversals"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.PaymentReversalProposals WHERE Id = @id AND Status = 0 AND DecidedBy IS NULL",
            ("@id", proposal.Resource.Id)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Commands WHERE CommandName = @name",
            ("@name", PaymentReversalProposalService.ApproveCommand)));

        var approved = await run.ApproveAsync(Checker, "after-lost-grant", proposal.Resource.Id);
        Assert.Equal(CommandOutcome.Succeeded, approved.Outcome);
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

        public Task<PaymentReversalProposalCommandResult> ApproveAsync(string actor, string key, Guid proposal,
            IFinancePermissionPort? permissions = null) =>
            WithReversalAsync(actor, (service, context) => service.ApproveAsync(context,
                new CommandMetadata($"api23-{key}"), new ApprovePaymentReversalRequest(proposal, "1"),
                CancellationToken.None), permissions);

        public async Task<FinancePaymentAllocationCommandResult> AllocateAsync(string key, Guid payment, Guid openItem,
            string amount, IFinancePermissionPort? permissions = null, Guid? allocationId = null,
            string? openItemVersion = null)
        {
            var (companyContext, collector) = Scope();
            await using var dbContext = new FinanceDbContext(Options(companyContext, collector), companyContext);
            var service = new FinancePaymentAllocationService(dbContext, companyContext, new FixedCurrentUser(Maker),
                permissions ?? new SyntheticPermissions(), collector,
                new FinanceAuditEnvelope("synthetic.finance.allocations.write", ["SENTETIK-FINANCE"], "Web"),
                TimeProvider.System);
            var current = await dbContext.OpenItems.AsNoTracking().SingleAsync(row => row.Id == openItem);
            return await service.CreateAsync(new FinanceContext(CompanyA, Maker, $"api23-{Guid.NewGuid():N}", null),
                new CommandMetadata($"api23-{key}"),
                new CreateFinancePaymentAllocationRequest(allocationId ?? Guid.NewGuid(), payment, "3", openItem,
                    openItemVersion ?? current.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    amount,
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
            Func<PaymentReversalProposalService, FinanceContext, Task<PaymentReversalProposalCommandResult>> call,
            IFinancePermissionPort? permissions = null)
        {
            var (companyContext, collector) = Scope();
            await using var dbContext = new FinanceDbContext(Options(companyContext, collector), companyContext);
            var envelope = new FinanceAuditEnvelope("synthetic.finance.payment-reversals.write", ["SENTETIK-FINANCE"],
                "Web");
            var service = new PaymentReversalProposalService(dbContext, companyContext, new FixedCurrentUser(actor),
                permissions ?? new SyntheticPermissions(), new OpenPeriod(), collector,
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

    /// <summary>
    /// SENTETIK: allows every fence and holds the <paramref name="waitAtCheck"/>-th check of <paramref name="action"/>
    /// (the fence right before the single save) until the other writer reaches its own final fence, so both commands
    /// read the same state and race on the save. Not a grant.
    /// </summary>
    private sealed class FinalWriteBarrierPermission(FinanceAction action, int waitAtCheck, Barrier barrier)
        : IFinancePermissionPort
    {
        private int checkCount;

        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction requestedAction,
            Guid? sourceId, CancellationToken cancellationToken)
        {
            if (requestedAction == action && Interlocked.Increment(ref checkCount) == waitAtCheck
                && !barrier.SignalAndWait(TimeSpan.FromSeconds(30), cancellationToken))
            {
                throw new TimeoutException("The concurrent Finance writers did not reach the final save fence.");
            }

            return Task.FromResult(PermissionDecision.Allowed);
        }
    }

    /// <summary>SENTETIK: allows every fence except the <paramref name="denyAtCheck"/>-th check of
    /// <paramref name="action"/>, which is denied (a grant revoked mid-command). Not a grant.</summary>
    private sealed class DenyAtCheckPermission(FinanceAction action, int denyAtCheck) : IFinancePermissionPort
    {
        private int checkCount;

        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction requestedAction,
            Guid? sourceId, CancellationToken cancellationToken) =>
            Task.FromResult(requestedAction == action && Interlocked.Increment(ref checkCount) == denyAtCheck
                ? PermissionDecision.Denied
                : PermissionDecision.Allowed);
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
