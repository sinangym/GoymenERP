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
/// F2b / API-11 (SAT-D96): an admitted invoice cancelled by its producer, through the real
/// <see cref="FinanceInvoiceOpenItemCancellationService"/> against a REAL SQL Server. The open items are closed with a
/// reversal — nothing is deleted: each keeps its original amount, becomes Cancelled with nothing outstanding, and the
/// party ledger shows its own reversing row, so the account balances back to zero. The same cancellation replays; an
/// invoice never admitted, another version, an active allocation or a missing authority cancels nothing; the
/// migration's Down refuses while a cancellation is stored. Party, authority and ledger grants are SENTETIK inputs.
/// </summary>
public sealed class FinanceInvoiceOpenItemCancellationSqlServerRelationalAcceptanceTests
{
    private const string Actor = "SENTETIK-invoice-canceller";
    private const string BeforeCancellations = "20261102220923_AddOpenItemInvoiceExchange";
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Lira = Guid.Parse("6b3f1f72-8d4c-4e9f-a021-3c4d5e6f7001");
    private static readonly Guid Party = Guid.Parse("6b3f1f72-8d4c-4e9f-a021-3c4d5e6f7003");
    private static readonly Guid Account = Guid.Parse("6b3f1f72-8d4c-4e9f-a021-3c4d5e6f7004");

    [SqlServerFact]
    public async Task A_sales_invoice_cancellation_reverses_its_open_items_and_the_same_cancellation_replays_once()
    {
        await using var run = await Run.StartAsync();
        var invoice = Invoice(FinanceInvoiceProducerV1.Sales, Installment(1, "600.00"), Installment(2, "400.00"));
        Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync("sales-1", invoice)).Outcome);
        var cancellation = Cancellation(invoice);

        var first = await run.CancelAsync("sales-1", cancellation);
        Assert.Equal(CommandOutcome.Succeeded, first.Outcome);
        Assert.False(first.IsReplay);
        Assert.Equal(invoice.Installments.Select(installment => installment.OpenItemId).Order(), first.OpenItemIds.Order());
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE SourceId = @id AND Status = 3 AND OutstandingAmountText = '0' "
            + "AND Version = 2", ("@id", invoice.InvoiceId)));
        Assert.Equal("600.00", await run.ScalarAsync<string>(
            "SELECT OriginalAmountText FROM finance.OpenItems WHERE Id = @id", ("@id", invoice.Installments[0].OpenItemId)));
        Assert.Equal("400.00", await run.ScalarAsync<string>(
            "SELECT ReversedAmountText FROM finance.InvoiceCancellationItems WHERE OpenItemId = @id",
            ("@id", invoice.Installments[1].OpenItemId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.InvoiceCancellations WHERE Id = @id AND InvoiceId = @invoice AND Reason = @reason",
            ("@id", cancellation.CancellationId), ("@invoice", invoice.InvoiceId), ("@reason", cancellation.Reason)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = 'finance.invoice-open-items.cancelled.v1' "
            + "AND EntityId = @id", ("@id", cancellation.CancellationId.ToString("D"))));

        var replay = await run.CancelAsync("sales-1", cancellation);
        Assert.Equal(CommandOutcome.Succeeded, replay.Outcome);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.OpenItemIds, replay.OpenItemIds);
        Assert.Equal(CommandOutcome.IdempotencyConflict,
            (await run.CancelAsync("sales-1", cancellation with { Reason = "SENTETIK baska neden" })).Outcome);
        // A resend under a new key by another authorised user replays (SAT-D208); another id for the same invoice is
        // refused — an invoice is reversed once.
        var resend = await run.CancelAsync("sales-1-resend", cancellation, actor: "SENTETIK-other-canceller");
        Assert.True(resend.IsReplay);
        var again = await run.CancelAsync("sales-1-again", cancellation with { CancellationId = Guid.NewGuid() });
        Assert.Contains(again.Issues,
            issue => issue.Code == FinanceInvoiceOpenItemCancellationService.AlreadyCancelledCode);
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.InvoiceCancellationItems WHERE CancellationId = @id",
            ("@id", cancellation.CancellationId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.InvoiceCancellations"));

        // The ledger keeps the admitted rows and adds one reversing row per item: the balance is back to zero.
        var ledger = await run.LedgerAsync();
        Assert.Equal(PartyLedgerOutcome.Found, ledger.Outcome);
        var entries = Assert.Single(ledger.Ledger!.Currencies).Entries;
        Assert.Equal(2, entries.Count(entry => entry.Kind == PartyLedgerEntryKind.OpenItem
            && entry.Side == PartyLedgerSide.Debit));
        var reversals = entries.Where(entry => entry.Kind == PartyLedgerEntryKind.OpenItemCancellation).ToList();
        Assert.Equal(2, reversals.Count);
        Assert.All(reversals, entry =>
        {
            Assert.Equal(PartyLedgerSide.Credit, entry.Side);
            Assert.Equal(entry.EntryId, entry.ReversesSourceId);
            Assert.Equal(invoice.InvoiceId, entry.DocumentId);
            Assert.Null(entry.DueOn);
        });
        Assert.Equal(PartyLedgerBalanceSide.Zero, ledger.Ledger.Currencies[0].ClosingBalance.Side);

        var summary = await run.BalanceAsync();
        var currency = Assert.Single(summary.Balance!.Currencies);
        Assert.Equal("1000.00", currency.DebitTotal.Money!.Amount);
        Assert.Equal("1000.00", currency.CreditTotal.Money!.Amount);
        Assert.Equal(PartyLedgerBalanceSide.Zero, currency.Balance.Side);
        Assert.Equal("0", currency.OpenReceivable.Money!.Amount);
    }

    [SqlServerFact]
    public async Task A_purchase_invoice_cancellation_is_a_debit_reversal_of_its_payables()
    {
        await using var run = await Run.StartAsync();
        var invoice = Invoice(FinanceInvoiceProducerV1.Purchasing, Installment(1, "250.25"));
        Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync("purchase-1", invoice)).Outcome);
        // The producer's identity decides the source kind: a sales cancellation of a purchase invoice finds nothing.
        var crossed = await run.CancelAsync("purchase-crossed",
            Cancellation(invoice) with { Producer = FinanceInvoiceProducerV1.Sales });
        Assert.Contains(crossed.Issues, issue => issue.Code == FinanceInvoiceOpenItemCancellationService.NotAdmittedCode);

        Assert.Equal(CommandOutcome.Succeeded, (await run.CancelAsync("purchase-1", Cancellation(invoice))).Outcome);
        var entries = Assert.Single((await run.LedgerAsync()).Ledger!.Currencies).Entries;
        var reversal = Assert.Single(entries, entry => entry.Kind == PartyLedgerEntryKind.PayableOpenItemCancellation);
        Assert.Equal(PartyLedgerSide.Debit, reversal.Side);
        Assert.Equal("250.25", reversal.Amount.Money!.Amount);
        Assert.Equal(PartyLedgerBalanceSide.Zero, entries[^1].RunningBalance.Side);
        Assert.Equal("0", Assert.Single((await run.BalanceAsync()).Balance!.Currencies).OpenPayable.Money!.Amount);
    }

    [SqlServerFact]
    public async Task An_unknown_invoice_another_version_an_allocation_or_a_missing_authority_cancels_nothing()
    {
        await using var run = await Run.StartAsync();
        var invoice = Invoice(FinanceInvoiceProducerV1.Sales, Installment(1, "300.00"), Installment(2, "200.00"));
        Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync("refuse-1", invoice)).Outcome);
        var cancellation = Cancellation(invoice);

        var unknown = await run.CancelAsync("refuse-unknown", Cancellation(invoice) with { InvoiceId = Guid.NewGuid() });
        Assert.Contains(unknown.Issues, issue => issue.Code == FinanceInvoiceOpenItemCancellationService.NotAdmittedCode);
        var version = await run.CancelAsync("refuse-version", cancellation with { InvoiceVersion = "invoice-v2" });
        Assert.Contains(version.Issues,
            issue => issue.Code == FinanceInvoiceOpenItemCancellationService.VersionMismatchCode);
        Assert.Equal(CommandOutcome.Denied,
            (await run.CancelAsync("refuse-denied", cancellation, authority: PermissionDecision.Denied)).Outcome);
        var unavailable = await run.CancelAsync("refuse-unavailable", cancellation,
            authority: PermissionDecision.Unavailable);
        Assert.Contains(unavailable.Issues, issue => issue.Code == "finance_invoice_cancellation_authority_unavailable");
        Assert.Equal(CommandOutcome.Denied,
            (await run.CancelAsync("refuse-company", cancellation, contextCompany: CompanyB)).Outcome);
        Assert.Equal(CommandOutcome.ValidationFailed,
            (await run.CancelAsync("refuse-reason", cancellation with { Reason = " " })).Outcome);

        // An active allocation on one installment is only warned about without the confirmation (SAT-D96), nothing changes.
        await run.AllocateAsync(invoice.Installments[1].OpenItemId);
        var allocated = await run.CancelAsync("refuse-allocated", cancellation);
        Assert.Contains(allocated.Issues,
            issue => issue.Code == FinanceInvoiceOpenItemCancellationService.HasAllocationsCode);

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.InvoiceCancellations"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.InvoiceCancellationItems"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE SourceId = @id AND Status = 1 AND Version = 1",
            ("@id", invoice.InvoiceId)));
    }

    [SqlServerFact]
    public async Task A_confirmed_cancellation_releases_the_allocation_and_the_advance_stays_without_a_cash_movement()
    {
        await using var run = await Run.StartAsync();
        var invoice = Invoice(FinanceInvoiceProducerV1.Sales, Installment(1, "300.00"), Installment(2, "200.00"));
        Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync("confirm-1", invoice)).Outcome);
        var allocationId = await run.AllocateAsync(invoice.Installments[1].OpenItemId);

        var confirmation = Cancellation(invoice) with { ReleaseActiveAllocations = true };
        var confirmed = await run.CancelAsync("confirm-cancel", confirmation);
        Assert.Equal(CommandOutcome.Succeeded, confirmed.Outcome);
        Assert.Equal(2, confirmed.OpenItemIds.Count);

        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE SourceId = @id AND Status = 3 AND OutstandingAmountText = N'0'",
            ("@id", invoice.InvoiceId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Allocations WHERE Id = @id AND Status = 2 AND Version = 2 AND ReleaseReason IS NOT NULL",
            ("@id", allocationId)));
        // The 50.00 returns to the receipt's unallocated remainder: the customer advance stays.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.SourceAllocationStates WHERE UnallocatedAmountText = N'50.00' AND Version = 3"));
        // An allocation is not a ledger movement: the recorded receipt itself is untouched (same version, still recorded).
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.ReceiptDrafts WHERE Version = 2 AND AmountText = N'50.00'"));

        // The same confirmed cancellation replays; the unconfirmed payload under the same id is an identity conflict.
        var replay = await run.CancelAsync("confirm-cancel", confirmation);
        Assert.True(replay.IsReplay);
        var conflict = await run.CancelAsync("confirm-other-key", confirmation with { ReleaseActiveAllocations = false });
        Assert.Contains(conflict.Issues,
            issue => issue.Code == FinanceInvoiceOpenItemCancellationService.CancellationIdentityConflictCode);
    }

    [SqlServerFact]
    public async Task The_cancelled_status_is_checked_and_the_rollback_refuses_while_a_cancellation_is_stored()
    {
        await using var run = await Run.StartAsync();
        var invoice = Invoice(FinanceInvoiceProducerV1.Sales, Installment(1, "10.00"));
        Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync("rollback-1", invoice)).Outcome);

        var unknownStatus = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE finance.OpenItems SET Status = 4 WHERE Id = @id; SELECT @@ROWCOUNT",
            ("@id", invoice.Installments[0].OpenItemId)));
        Assert.Contains("CK_OpenItems_Status", unknownStatus.Message, StringComparison.Ordinal);

        // With no cancellation stored, the Down runs and the reapply restores the tables.
        const string Tables = "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'finance') "
            + "AND name IN (N'InvoiceCancellations', N'InvoiceCancellationItems')";
        await run.MigrateFinanceAsync(BeforeCancellations);
        Assert.Equal(0, await run.ScalarAsync<int>(Tables));
        await run.MigrateFinanceAsync(null);
        Assert.Equal(2, await run.ScalarAsync<int>(Tables));

        Assert.Equal(CommandOutcome.Succeeded, (await run.CancelAsync("rollback-1", Cancellation(invoice))).Outcome);
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.MigrateFinanceAsync(BeforeCancellations));
        Assert.Equal(51051, refused.Number);
        Assert.Equal(2, await run.ScalarAsync<int>(Tables));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE Id = @id AND Status = 3",
            ("@id", invoice.Installments[0].OpenItemId)));
    }

    [SqlServerFact]
    public async Task A_cancelled_invoice_is_not_admitted_again_and_another_company_finds_nothing_to_cancel()
    {
        await using var run = await Run.StartAsync();
        var invoice = Invoice(FinanceInvoiceProducerV1.Sales, Installment(1, "80.00"));
        Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync("late-1", invoice)).Outcome);

        // Company B, resolved and authorised in its own context, cannot see company A's invoice.
        var other = await run.CancelAsync("late-other-company", Cancellation(invoice), [PermissionDecision.Allowed], company: CompanyB);
        Assert.Contains(other.Issues, issue => issue.Code == FinanceInvoiceOpenItemCancellationService.NotAdmittedCode);

        Assert.Equal(CommandOutcome.Succeeded, (await run.CancelAsync("late-1", Cancellation(invoice))).Outcome);
        // A late or resent admission of the cancelled invoice (new admission id and key) is refused, never a reuse.
        var late = await run.AdmitAsync("late-2", invoice with
        {
            AdmissionId = Guid.NewGuid(),
            Installments = [invoice.Installments[0] with { OpenItemId = Guid.NewGuid() }],
        });
        Assert.Equal(CommandOutcome.ValidationFailed, late.Outcome);
        Assert.Contains(late.Issues,
            issue => issue.Code == FinanceInvoiceOpenItemAdmissionService.AlreadyCancelledCode);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItemSourceBatches"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE SourceId = @id AND Status = 3", ("@id", invoice.InvoiceId)));
    }

    [SqlServerFact]
    public async Task An_authority_withdrawn_before_the_save_and_a_concurrent_second_cancellation_write_nothing_more()
    {
        await using var run = await Run.StartAsync();
        var invoice = Invoice(FinanceInvoiceProducerV1.Sales, Installment(1, "90.00"), Installment(2, "10.00"));
        Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync("race-1", invoice)).Outcome);

        // Allowed at the first fence, denied right before the save: nothing is written and no audit is queued.
        var withdrawn = await run.CancelAsync("race-withdrawn", Cancellation(invoice),
            authority: [PermissionDecision.Allowed, PermissionDecision.Denied]);
        Assert.Equal(CommandOutcome.Denied, withdrawn.Outcome);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.InvoiceCancellations"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = 'finance.invoice-open-items.cancelled.v1'"));

        // Two cancellations of the same invoice under different ids at once: exactly one reverses it.
        var results = await Task.WhenAll(
            run.CancelAsync("race-a", Cancellation(invoice)),
            run.CancelAsync("race-b", Cancellation(invoice)));
        Assert.Single(results, result => result.Outcome == CommandOutcome.Succeeded);
        Assert.Single(results, result => result.Outcome is CommandOutcome.VersionConflict
            || result.Issues.Any(issue => issue.Code is FinanceInvoiceOpenItemCancellationService.AlreadyCancelledCode
                or FinanceInvoiceOpenItemCancellationService.NotAdmittedCode
                or FinanceInvoiceOpenItemCancellationService.VersionMismatchCode));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.InvoiceCancellations"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.InvoiceCancellationItems"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE SourceId = @id AND Status = 3 AND Version = 2",
            ("@id", invoice.InvoiceId)));
    }

    [SqlServerFact]
    public async Task A_cancellation_that_reads_the_open_items_after_a_concurrent_commit_is_refused_as_already_cancelled()
    {
        await using var run = await Run.StartAsync();
        var invoice = Invoice(FinanceInvoiceProducerV1.Sales, Installment(1, "7.00"));
        Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync("late-read-1", invoice)).Outcome);
        // The receipt check saw no receipt, then the winner committed: the loser reads items that are already cancelled.
        // Without its receipt in view that state is reproduced by a status-only row; the refusal must be the stable code.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "UPDATE finance.OpenItems SET Status = 3, OutstandingAmountText = '0' WHERE Id = @id; SELECT @@ROWCOUNT",
            ("@id", invoice.Installments[0].OpenItemId)));

        var loser = await run.CancelAsync("late-read-2", Cancellation(invoice));
        Assert.Equal(CommandOutcome.ValidationFailed, loser.Outcome);
        Assert.Contains(loser.Issues,
            issue => issue.Code == FinanceInvoiceOpenItemCancellationService.AlreadyCancelledCode);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.InvoiceCancellations"));
    }

    [SqlServerFact]
    public async Task An_open_item_reduced_without_an_active_allocation_is_refused_with_the_not_understood_code_and_writes_nothing()
    {
        await using var run = await Run.StartAsync();
        var invoice = Invoice(FinanceInvoiceProducerV1.Sales, Installment(1, "9.00"));
        Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync("corrupt-1", invoice)).Outcome);
        // Reduced outside any allocation (stored state the cancellation cannot explain): not the allocation warning.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "UPDATE finance.OpenItems SET OutstandingAmountText = N'4.00' WHERE Id = @id; SELECT @@ROWCOUNT",
            ("@id", invoice.Installments[0].OpenItemId)));

        var refused = await run.CancelAsync("corrupt-2", Cancellation(invoice));
        Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
        Assert.Contains(refused.Issues,
            issue => issue.Code == FinanceInvoiceOpenItemCancellationService.StoredStateNotUnderstoodCode);
        Assert.DoesNotContain(refused.Issues,
            issue => issue.Code == FinanceInvoiceOpenItemCancellationService.HasAllocationsCode);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.InvoiceCancellations"));
    }

    [SqlServerFact]
    public async Task The_rollback_refuses_while_a_cancelled_open_item_is_stored_even_without_its_receipt()
    {
        await using var run = await Run.StartAsync();
        var invoice = Invoice(FinanceInvoiceProducerV1.Sales, Installment(1, "5.00"));
        Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync("status-only", invoice)).Outcome);
        // A stray status-3 row (no cancellation receipt) would break the narrowed status check: the Down refuses first.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "UPDATE finance.OpenItems SET Status = 3, OutstandingAmountText = '0' WHERE Id = @id; SELECT @@ROWCOUNT",
            ("@id", invoice.Installments[0].OpenItemId)));

        var refused = await Assert.ThrowsAsync<SqlException>(() => run.MigrateFinanceAsync(BeforeCancellations));
        Assert.Equal(51051, refused.Number);
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'finance') "
            + "AND name IN (N'InvoiceCancellations', N'InvoiceCancellationItems')"));
    }

    private static FinanceInvoiceOpenItemAdmissionV1 Invoice(FinanceInvoiceProducerV1 producer,
        params FinanceInvoiceInstallmentV1[] installments) =>
        new(Guid.NewGuid(), producer, Guid.NewGuid(), "invoice-v1", Party, Account, Lira, null, installments);

    private static FinanceInvoiceInstallmentV1 Installment(int sequence, string amount) =>
        new(Guid.NewGuid(), Guid.NewGuid(), sequence, new DateOnly(2026, 12, 1).AddMonths(sequence - 1), amount, null);

    private static FinanceInvoiceOpenItemCancellationV1 Cancellation(FinanceInvoiceOpenItemAdmissionV1 invoice) =>
        new(Guid.NewGuid(), invoice.Producer, invoice.InvoiceId, invoice.InvoiceVersion, "SENTETIK e-Fatura RED");

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
            await using var context = run.Context(CompanyA, null);
            await context.Database.MigrateAsync();
            return run;
        }

        public async Task<FinanceOpenItemAdmissionResult> AdmitAsync(string key, FinanceInvoiceOpenItemAdmissionV1 invoice)
        {
            var (companyContext, collector) = Scope(CompanyA);
            await using var dbContext = new FinanceDbContext(Options(companyContext, collector), companyContext);
            var service = new FinanceInvoiceOpenItemAdmissionService(dbContext, companyContext,
                new FixedCurrentUser(Actor, CompanyA), new SyntheticParty(), new SyntheticAdmissionAuthority(), collector,
                new FinanceAuditEnvelope("synthetic.finance.invoice-open-items.admit", ["SENTETIK-FINANCE"], "Web"),
                TimeProvider.System);
            return await service.AdmitAsync(new FinanceContext(CompanyA, Actor, $"f2b-{Guid.NewGuid():N}", null),
                new CommandMetadata($"f2b-admit-{key}"), invoice, CancellationToken.None);
        }

        public Task<FinanceInvoiceOpenItemCancellationResultV1> CancelAsync(string key,
            FinanceInvoiceOpenItemCancellationV1 cancellation, Guid? contextCompany = null,
            PermissionDecision authority = PermissionDecision.Allowed, string actor = Actor) =>
            CancelAsync(key, cancellation, [authority], contextCompany, actor);

        /// <summary><paramref name="authority"/> answers each fence in turn; the last answer repeats.</summary>
        public async Task<FinanceInvoiceOpenItemCancellationResultV1> CancelAsync(string key,
            FinanceInvoiceOpenItemCancellationV1 cancellation, PermissionDecision[] authority,
            Guid? contextCompany = null, string actor = Actor, Guid? company = null)
        {
            var scopeCompany = company ?? CompanyA;
            var (companyContext, collector) = Scope(scopeCompany);
            await using var dbContext = new FinanceDbContext(Options(companyContext, collector), companyContext);
            var service = new FinanceInvoiceOpenItemCancellationService(dbContext, companyContext,
                new FixedCurrentUser(actor, scopeCompany), new SyntheticCancellationAuthority(authority), collector,
                new FinanceAuditEnvelope("synthetic.finance.invoice-open-items.cancel", ["SENTETIK-FINANCE"], "Web"),
                TimeProvider.System);
            return await service.CancelAsync(
                new FinanceContext(contextCompany ?? scopeCompany, actor, $"f2b-{Guid.NewGuid():N}", null),
                new CommandMetadata($"f2b-{key}"), cancellation, CancellationToken.None);
        }

        public async Task<PartyLedgerResult> LedgerAsync()
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            await using var dbContext = Context(CompanyA, null);
            return await Ledger(dbContext).ReadAuthorizedAsync(new FinanceContext(CompanyA, Actor, "f2b-ledger", null),
                new PartyLedgerRequest(Party, Account, today.AddDays(-1), today.AddDays(1)), CancellationToken.None);
        }

        public async Task<PartyBalanceResult> BalanceAsync()
        {
            await using var dbContext = Context(CompanyA, null);
            return await Ledger(dbContext).SummarizeAuthorizedAsync(
                new FinanceContext(CompanyA, Actor, "f2b-balance", null), new PartyBalanceRequest(Party, Account),
                CancellationToken.None);
        }

        /// <summary>SENTETIK: a recorded receipt with an ACTIVE allocation to the open item (outstanding unchanged).</summary>
        public async Task<Guid> AllocateAsync(Guid openItemId)
        {
            await using var db = Context(CompanyA, null);
            var now = DateTimeOffset.UtcNow;
            var sourceId = Guid.NewGuid();
            db.ReceiptDrafts.Add(new ReceiptDraftRow
            {
                Id = sourceId,
                CompanyId = CompanyA,
                Version = 2,
                PartyId = Party,
                CompanyAccountId = Account,
                TreasuryAccountId = Guid.NewGuid(),
                CurrencyId = Lira,
                AmountText = "50.00",
                ProfileId = Guid.NewGuid().ToString("D"),
                ProfileVersion = "1",
                OccurredOn = DateOnly.FromDateTime(now.UtcDateTime),
                ValueOn = DateOnly.FromDateTime(now.UtcDateTime),
                CreatedAtUtc = now,
                CreatedBy = "synthetic-f2b-fixture",
                UpdatedAtUtc = now,
                UpdatedBy = "synthetic-f2b-fixture",
                Stage = ReceiptStages.Recorded,
                RecordedAtUtc = now,
                RecordedBy = "synthetic-f2b-fixture",
            });
            db.SourceAllocationStates.Add(new FinanceSourceAllocationStateRow
            {
                CompanyId = CompanyA,
                SourceId = sourceId,
                CurrencyId = Lira,
                OriginalAmountText = "50.00",
                UnallocatedAmountText = "0.00",
                Version = 2,
                UpdatedBy = "synthetic-f2b-fixture",
                UpdatedAtUtc = now,
            });
            var item = await db.OpenItems.SingleAsync(row => row.Id == openItemId);
            item.OutstandingAmountText = FinanceExactDecimalText.TryParseNonNegative(item.OriginalAmountText, out var original)
                && FinanceExactDecimalText.TryParseNonNegative("50.00", out var fifty)
                ? original.Subtract(fifty).Text : throw new InvalidOperationException("Fixture amount.");
            var allocationId = Guid.NewGuid();
            db.Allocations.Add(new FinanceAllocationRow
            {
                Id = allocationId,
                CompanyId = CompanyA,
                SourceId = sourceId,
                OpenItemId = openItemId,
                CurrencyId = Lira,
                AmountText = "50.00",
                Reason = "SENTETIK F2b allocation fixture",
                Status = FinanceAllocationStatuses.Active,
                Version = 1,
                CreatedBy = "synthetic-f2b-fixture",
                CreatedAtUtc = now,
            });
            await db.SaveChangesAsync();
            return allocationId;
        }

        /// <summary>Moves Finance's schema to <paramref name="target"/> (null: the latest migration).</summary>
        public async Task MigrateFinanceAsync(string? target)
        {
            await using var context = Context(CompanyA, null);
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

        private static PartyLedgerService Ledger(FinanceDbContext dbContext)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            return new PartyLedgerService(dbContext, companyContext, new FixedCurrentUser(Actor, CompanyA),
                new AllowAllLedgerPermissions());
        }

        private static (CompanyContext, AuditEventCollector) Scope(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return (companyContext, new AuditEventCollector());
        }

        private FinanceDbContext Context(Guid company, AuditEventCollector? collector)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
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

    /// <summary>SENTETIK: the host-owned cancellation authority answer. Not a grant.</summary>
    private sealed class SyntheticCancellationAuthority(PermissionDecision[] decisions)
        : IFinanceInvoiceOpenItemCancellationAuthorityV1
    {
        private int calls;

        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceInvoiceProducerV1 producer,
            Guid invoiceId, CancellationToken cancellationToken) =>
            Task.FromResult(decisions[Math.Min(calls++, decisions.Length - 1)]);
    }

    /// <summary>SENTETIK: the ledger read and amount authorities. Not a grant.</summary>
    private sealed class AllowAllLedgerPermissions : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }

    private sealed class FixedCurrentUser(string userId, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
