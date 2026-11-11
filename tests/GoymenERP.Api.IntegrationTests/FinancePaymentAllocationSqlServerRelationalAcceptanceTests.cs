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
/// F3a / API-20 (DEC-FIN-006): a RECORDED manual bank payment applied to an admitted purchase invoice's PAYABLE items
/// through the real <see cref="FinancePaymentAllocationService"/> against a REAL SQL Server. One save writes the
/// allocation, the payment balance, the item, the command record and the audit; the same key replays; a receivable,
/// an over-allocation, a payment not recorded, a stale version or a missing authority writes nothing new; release
/// restores both balances; another company sees nothing; F2b's cancellation refuses while a payment allocation is
/// active; and the migration's Down refuses while one is stored. No ledger row and no money event are created.
/// Party, account, payment and authority are SENTETIK inputs.
/// </summary>
public sealed class FinancePaymentAllocationSqlServerRelationalAcceptanceTests
{
    private const string Actor = "SENTETIK-payment-allocator";
    private const string BeforePaymentAllocations = "20261107131505_AddInvoiceCancellations";
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Lira = Guid.Parse("7c3f1f72-8d4c-4e9f-a021-3c4d5e6f7001");
    private static readonly Guid Euro = Guid.Parse("7c3f1f72-8d4c-4e9f-a021-3c4d5e6f7002");
    private static readonly Guid Party = Guid.Parse("7c3f1f72-8d4c-4e9f-a021-3c4d5e6f7003");
    private static readonly Guid Account = Guid.Parse("7c3f1f72-8d4c-4e9f-a021-3c4d5e6f7004");

    [SqlServerFact]
    public async Task A_recorded_payment_closes_a_payable_in_one_save_and_the_same_key_replays_without_a_ledger_row()
    {
        await using var run = await Run.StartAsync();
        var invoice = await run.AdmitAsync(FinanceInvoiceProducerV1.Purchasing, "600.00", "400.00");
        var payment = await run.RecordPaymentAsync("1000.00");
        var ledgerBefore = Assert.Single((await run.LedgerAsync()).Ledger!.Currencies).Entries.Count;
        var request = Allocate(payment, invoice.Installments[0].OpenItemId, "600");

        var first = await run.CreateAsync("close-1", request);
        Assert.Equal(CommandOutcome.Succeeded, first.Outcome);
        Assert.False(first.IsReplay);
        Assert.Equal("600", first.Resource!.Amount);
        Assert.Equal(FinanceAllocationStatus.Active, first.Resource.Status);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE Id = @id AND Status = 2 AND OutstandingAmountText = '0.00' "
            + "AND Version = 2", ("@id", invoice.Installments[0].OpenItemId)));
        Assert.Equal("400.00", await run.ScalarAsync<string>(
            "SELECT UnallocatedAmountText FROM finance.PaymentAllocationStates WHERE PaymentId = @id AND Version = 1",
            ("@id", payment)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Commands WHERE CommandName = 'finance.payment-allocation.create.v1' "
            + "AND ManualBankPaymentId = @id AND DraftId IS NULL AND FinanceAllocationId IS NULL", ("@id", payment)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = 'finance.payment-allocation.created.v1' "
            + "AND EntityId = @id", ("@id", request.AllocationId.ToString("D"))));

        var replay = await run.CreateAsync("close-1", request);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.OriginalCorrelationId, replay.OriginalCorrelationId);
        Assert.Equal(first.Resource, replay.Resource);
        Assert.Equal(CommandOutcome.IdempotencyConflict,
            (await run.CreateAsync("close-1", request with { Reason = "SENTETIK baska neden" })).Outcome);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PaymentAllocations"));

        // DEC-FIN-006: no ledger row and no money event; the open payable falls by the allocated amount only.
        var ledger = Assert.Single((await run.LedgerAsync()).Ledger!.Currencies);
        Assert.Equal(ledgerBefore, ledger.Entries.Count);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutgoingMoneyEvents"));
        Assert.Equal("400.00",
            Assert.Single((await run.BalanceAsync()).Balance!.Currencies).OpenPayable.Money!.Amount);

        var page = await run.ListAsync(CompanyA);
        Assert.Equal(ReadOutcome.Found, page.Outcome);
        Assert.Equal(request.AllocationId, Assert.Single(page.Resource!.Items).Id);
        var detail = await run.DetailAsync(CompanyA, request.AllocationId);
        Assert.Equal("2", detail.Resource!.OpenItemVersion);
        Assert.Equal(payment, detail.Resource.Allocation.PaymentId);
    }

    [SqlServerFact]
    public async Task A_receivable_an_over_allocation_or_a_payment_not_recorded_is_a_recorded_refusal_that_writes_nothing()
    {
        await using var run = await Run.StartAsync();
        var purchase = await run.AdmitAsync(FinanceInvoiceProducerV1.Purchasing, "500.00");
        var sale = await run.AdmitAsync(FinanceInvoiceProducerV1.Sales, "500.00");
        var payment = await run.RecordPaymentAsync("300.00");
        var payable = purchase.Installments[0].OpenItemId;

        var toReceivable = Allocate(payment, sale.Installments[0].OpenItemId, "1");
        var receivable = await run.CreateAsync("refuse-direction", toReceivable);
        Assert.Equal(CommandOutcome.ValidationFailed, receivable.Outcome);
        Assert.Contains(receivable.Issues, issue => issue.Code == "finance_allocation_direction_mismatch");
        // The refusal is durable: the same key replays it, and another payload under that key is a conflict.
        var replayed = await run.CreateAsync("refuse-direction", toReceivable);
        Assert.True(replayed.IsReplay);
        Assert.Equal(receivable.Issues, replayed.Issues);
        Assert.Equal(CommandOutcome.IdempotencyConflict,
            (await run.CreateAsync("refuse-direction", toReceivable with { Amount = "2" })).Outcome);

        var overPayment = await run.CreateAsync("refuse-source", Allocate(payment, payable, "300.01"));
        Assert.Contains(overPayment.Issues, issue => issue.Code == "finance_allocation_exceeds_source");
        var big = await run.RecordPaymentAsync("900.00");
        var overItem = await run.CreateAsync("refuse-item", Allocate(big, payable, "500.001"));
        Assert.Contains(overItem.Issues, issue => issue.Code == "finance_allocation_exceeds_open_item");

        var approved = await run.RecordPaymentAsync("100.00", ManualBankPaymentStatuses.Approved);
        var notRecorded = await run.CreateAsync("refuse-approved", Allocate(approved, payable, "1"));
        Assert.Contains(notRecorded.Issues, issue => issue.Code == "finance_allocation_reference_invalid");
        var euro = await run.RecordPaymentAsync("100.00", currency: Euro);
        var otherCurrency = await run.CreateAsync("refuse-currency", Allocate(euro, payable, "1"));
        Assert.Contains(otherCurrency.Issues, issue => issue.Code == "finance_allocation_reference_invalid");
        var withoutEvent = await run.RecordPaymentAsync("100.00", withMoneyEvent: false);
        var noEvent = await run.CreateAsync("refuse-event", Allocate(withoutEvent, payable, "1"));
        Assert.Contains(noEvent.Issues, issue => issue.Code == "finance_allocation_reference_invalid");
        var unknown = await run.CreateAsync("refuse-unknown", Allocate(Guid.NewGuid(), payable, "1"));
        Assert.Contains(unknown.Issues, issue => issue.Code == "finance_allocation_reference_not_found"
            && issue.Field == "paymentId");

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PaymentAllocations"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PaymentAllocationStates"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE Id = @id AND Status = 1 AND Version = 1 "
            + "AND OutstandingAmountText = '500.00'", ("@id", payable)));
        Assert.Equal(7, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Commands WHERE CommandName = 'finance.payment-allocation.create.v1'"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType LIKE 'finance.payment-allocation.%'"));
    }

    [SqlServerFact]
    public async Task An_amount_with_more_decimals_than_the_payment_or_the_payable_is_a_durable_refusal_and_a_boundary_amount_is_accepted()
    {
        // FNS-D16 (Sinan 2026-11-11): the allocation scale never exceeds the payment's and never grows the payable's text.
        await using var run = await Run.StartAsync();
        var purchase = await run.AdmitAsync(FinanceInvoiceProducerV1.Purchasing, "500.00");
        var payment = await run.RecordPaymentAsync("300.00");
        var payable = purchase.Installments[0].OpenItemId;

        var tooPrecise = Allocate(payment, payable, "100.000");
        var refused = await run.CreateAsync("scale-refused", tooPrecise);
        Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
        Assert.Contains(refused.Issues, issue => issue.Code == "finance_allocation_amount_invalid"
            && issue.Field == "amount");
        var replayed = await run.CreateAsync("scale-refused", tooPrecise);
        Assert.True(replayed.IsReplay);
        Assert.Equal(refused.Issues, replayed.Issues);
        Assert.Equal(CommandOutcome.IdempotencyConflict,
            (await run.CreateAsync("scale-refused", tooPrecise with { Amount = "100.00" })).Outcome);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PaymentAllocations"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PaymentAllocationStates"));

        // The boundary: a scale-0 amount and a scale-2 amount both keep both stored balance texts at scale 2.
        Assert.Equal(CommandOutcome.Succeeded, (await run.CreateAsync("scale-zero", Allocate(payment, payable, "100"))).Outcome);
        var fresh = await run.CreateAsync("scale-two",
            Allocate(payment, payable, "50.25") with { ExpectedOpenItemVersion = "2" });
        Assert.Equal(CommandOutcome.Succeeded, fresh.Outcome);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE Id = @id AND OutstandingAmountText = '349.75'",
            ("@id", payable)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.PaymentAllocationStates WHERE PaymentId = @id AND UnallocatedAmountText = '149.75'",
            ("@id", payment)));
    }

    [SqlServerFact]
    public async Task A_release_restores_the_payment_and_the_payable_once_and_a_stale_release_is_refused()
    {
        await using var run = await Run.StartAsync();
        var invoice = await run.AdmitAsync(FinanceInvoiceProducerV1.Purchasing, "500.00");
        var item = invoice.Installments[0].OpenItemId;
        var payment = await run.RecordPaymentAsync("1000.00");
        var first = Allocate(payment, item, "300.00");
        Assert.Equal(CommandOutcome.Succeeded, (await run.CreateAsync("release-a", first)).Outcome);
        var second = Allocate(payment, item, "200.00") with { ExpectedOpenItemVersion = "2" };
        Assert.Equal(CommandOutcome.Succeeded, (await run.CreateAsync("release-b", second)).Outcome);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE Id = @id AND Status = 2 AND Version = 3", ("@id", item)));

        var release = new ReleaseFinanceAllocationRequest(first.AllocationId, "1", "3", "SENTETIK yanlis dagitim");
        var released = await run.ReleaseAsync("release-1", release);
        Assert.Equal(CommandOutcome.Succeeded, released.Outcome);
        Assert.Equal(FinanceAllocationStatus.Released, released.Resource!.Status);
        Assert.Equal("2", released.Resource.Version);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE Id = @id AND Status = 1 AND Version = 4 "
            + "AND OutstandingAmountText = '300.00'", ("@id", item)));
        Assert.Equal("800.00", await run.ScalarAsync<string>(
            "SELECT UnallocatedAmountText FROM finance.PaymentAllocationStates WHERE PaymentId = @id AND Version = 3",
            ("@id", payment)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Commands WHERE CommandName = 'finance.payment-allocation.release.v1' "
            + "AND ManualBankPaymentId = @id", ("@id", payment)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = 'finance.payment-allocation.released.v1' "
            + "AND EntityId = @id", ("@id", first.AllocationId.ToString("D"))));
        Assert.True((await run.ReleaseAsync("release-1", release)).IsReplay);

        // A second release under a new key: the stale version is a recorded conflict, the current one a state refusal.
        var stale = await run.ReleaseAsync("release-2", release);
        Assert.Equal(CommandOutcome.VersionConflict, stale.Outcome);
        Assert.Equal(CommandOutcome.VersionConflict, (await run.ReleaseAsync("release-2", release)).Outcome);
        var again = await run.ReleaseAsync("release-3", release with { ExpectedAllocationVersion = "2", ExpectedOpenItemVersion = "4" });
        Assert.Contains(again.Issues, issue => issue.Code == "finance_allocation_state_invalid");
        var missing = await run.ReleaseAsync("release-4", release with { AllocationId = Guid.NewGuid() });
        Assert.Contains(missing.Issues, issue => issue.Code == "finance_allocation_not_found");
        Assert.Equal("800.00", await run.ScalarAsync<string>(
            "SELECT UnallocatedAmountText FROM finance.PaymentAllocationStates WHERE PaymentId = @id", ("@id", payment)));
    }

    [SqlServerFact]
    public async Task Two_concurrent_allocations_of_one_payment_and_a_stale_item_version_never_overspend_it()
    {
        await using var run = await Run.StartAsync();
        var invoice = await run.AdmitAsync(FinanceInvoiceProducerV1.Purchasing, "700.00", "700.00");
        var payment = await run.RecordPaymentAsync("1000.00");

        var results = await Task.WhenAll(
            run.CreateAsync("race-a", Allocate(payment, invoice.Installments[0].OpenItemId, "700")),
            run.CreateAsync("race-b", Allocate(payment, invoice.Installments[1].OpenItemId, "700")));
        Assert.Single(results, result => result.Outcome == CommandOutcome.Succeeded);
        Assert.Single(results, result => result.Outcome == CommandOutcome.VersionConflict
            || result.Issues.Any(issue => issue.Code == "finance_allocation_exceeds_source"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PaymentAllocations"));
        Assert.Equal("300.00", await run.ScalarAsync<string>(
            "SELECT UnallocatedAmountText FROM finance.PaymentAllocationStates WHERE PaymentId = @id", ("@id", payment)));

        // The expected item version is the concurrency token the caller saw: a stale one is a recorded conflict.
        var closed = results.Single(result => result.Outcome == CommandOutcome.Succeeded).Resource!.OpenItemId;
        var open = invoice.Installments.Select(installment => installment.OpenItemId).Single(id => id != closed);
        var stale = await run.CreateAsync("race-stale", Allocate(payment, open, "1") with { ExpectedOpenItemVersion = "2" });
        Assert.Equal(CommandOutcome.VersionConflict, stale.Outcome);
        Assert.Contains(stale.Issues, issue => issue.Code == "version_conflict");
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PaymentAllocations"));
    }

    [SqlServerFact]
    public async Task Another_company_or_a_missing_or_withdrawn_authority_sees_and_writes_nothing()
    {
        await using var run = await Run.StartAsync();
        var invoice = await run.AdmitAsync(FinanceInvoiceProducerV1.Purchasing, "100.00");
        var item = invoice.Installments[0].OpenItemId;
        var payment = await run.RecordPaymentAsync("100.00");

        // Company B, resolved and authorised in its own context, cannot see company A's payment or item.
        var other = await run.CreateAsync("tenant-b", Allocate(payment, item, "1"), company: CompanyB);
        Assert.Contains(other.Issues, issue => issue.Code == "finance_allocation_reference_not_found");
        Assert.Equal(CommandOutcome.Denied,
            (await run.CreateAsync("tenant-mismatch", Allocate(payment, item, "1"), contextCompany: CompanyB)).Outcome);
        Assert.Equal(CommandOutcome.Denied,
            (await run.CreateAsync("tenant-denied", Allocate(payment, item, "1"), [PermissionDecision.Denied])).Outcome);
        Assert.Equal(CommandOutcome.Unavailable,
            (await run.CreateAsync("tenant-unavailable", Allocate(payment, item, "1"), [PermissionDecision.Unavailable])).Outcome);
        var withdrawn = await run.CreateAsync("tenant-withdrawn", Allocate(payment, item, "1"),
            [PermissionDecision.Allowed, PermissionDecision.Denied]);
        Assert.Equal(CommandOutcome.Denied, withdrawn.Outcome);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.PaymentAllocations"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Commands WHERE CommandName = 'finance.payment-allocation.create.v1' "
            + "AND CompanyId = @company", ("@company", CompanyA)));

        var allocated = Allocate(payment, item, "100");
        Assert.Equal(CommandOutcome.Succeeded, (await run.CreateAsync("tenant-a", allocated)).Outcome);
        Assert.Empty((await run.ListAsync(CompanyB)).Resource!.Items);
        Assert.Equal(ReadOutcome.NotFound, (await run.DetailAsync(CompanyB, allocated.AllocationId)).Outcome);
        var otherRelease = await run.ReleaseAsync("tenant-b-release",
            new ReleaseFinanceAllocationRequest(allocated.AllocationId, "1", "2", "SENTETIK"), company: CompanyB);
        Assert.Contains(otherRelease.Issues, issue => issue.Code == "finance_allocation_not_found");
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.PaymentAllocations WHERE Id = @id AND Status = 1",
            ("@id", allocated.AllocationId)));
    }

    [SqlServerFact]
    public async Task An_invoice_cancellation_warns_while_a_payable_has_an_active_payment_allocation_and_releases_it_once_confirmed()
    {
        await using var run = await Run.StartAsync();
        var invoice = await run.AdmitAsync(FinanceInvoiceProducerV1.Purchasing, "250.00", "250.00");
        var payment = await run.RecordPaymentAsync("100.00");
        var allocation = Allocate(payment, invoice.Installments[1].OpenItemId, "100");
        Assert.Equal(CommandOutcome.Succeeded, (await run.CreateAsync("cancel-a", allocation)).Outcome);
        var moneyEvents = await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents");

        // SAT-D96: without the confirmation the cancellation only warns and writes nothing.
        var refused = await run.CancelAsync("cancel-1", invoice);
        Assert.Contains(refused.Issues,
            issue => issue.Code == FinanceInvoiceOpenItemCancellationService.HasAllocationsCode);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.InvoiceCancellations"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.PaymentAllocations WHERE Id = @id AND Status = 1",
            ("@id", allocation.AllocationId)));

        // Confirmed: the allocation is released with the cancellation; the payment's money event is untouched.
        Assert.Equal(CommandOutcome.Succeeded,
            (await run.CancelAsync("cancel-2", invoice, releaseActiveAllocations: true)).Outcome);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.PaymentAllocations WHERE Id = @id AND Status = 2",
            ("@id", allocation.AllocationId)));
        Assert.Equal("100.00", await run.ScalarAsync<string>(
            "SELECT UnallocatedAmountText FROM finance.PaymentAllocationStates WHERE PaymentId = @id",
            ("@id", payment)));
        Assert.Equal(moneyEvents, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE SourceId = @id AND Status = 3", ("@id", invoice.InvoiceId)));
    }

    [SqlServerFact]
    public async Task The_payment_allocations_migrations_Down_passes_on_an_empty_store_and_refuses_while_an_allocation_exists()
    {
        await using var run = await Run.StartAsync();
        const string Tables = "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'finance') "
            + "AND name IN (N'PaymentAllocations', N'PaymentAllocationStates')";
        var invoice = await run.AdmitAsync(FinanceInvoiceProducerV1.Purchasing, "10.00");
        var payment = await run.RecordPaymentAsync("10.00");

        // A recorded payment and a payable alone are harmless: the Down runs and the reapply restores the tables.
        await run.MigrateFinanceAsync(BeforePaymentAllocations);
        Assert.Equal(0, await run.ScalarAsync<int>(Tables));
        await run.MigrateFinanceAsync(null);
        Assert.Equal(2, await run.ScalarAsync<int>(Tables));

        var allocation = Allocate(payment, invoice.Installments[0].OpenItemId, "10");
        Assert.Equal(CommandOutcome.Succeeded, (await run.CreateAsync("rollback-1", allocation)).Outcome);
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.MigrateFinanceAsync(BeforePaymentAllocations));
        Assert.Equal(51051, refused.Number);
        Assert.Equal(2, await run.ScalarAsync<int>(Tables));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.PaymentAllocations WHERE Id = @id", ("@id", allocation.AllocationId)));

        // The database itself admits only the two allocation states.
        var unknownStatus = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE finance.PaymentAllocations SET Status = 3 WHERE Id = @id; SELECT @@ROWCOUNT",
            ("@id", allocation.AllocationId)));
        Assert.Contains("CK_PaymentAllocations_Status", unknownStatus.Message, StringComparison.Ordinal);
    }

    private static CreateFinancePaymentAllocationRequest Allocate(Guid payment, Guid openItem, string amount) =>
        new(Guid.NewGuid(), payment, "3", openItem, "1", amount, "SENTETIK tedarikci odemesi dagitimi");

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

        public async Task<FinanceInvoiceOpenItemAdmissionV1> AdmitAsync(FinanceInvoiceProducerV1 producer,
            params string[] amounts)
        {
            var invoice = new FinanceInvoiceOpenItemAdmissionV1(Guid.NewGuid(), producer, Guid.NewGuid(), "invoice-v1",
                Party, Account, Lira, null,
                [.. amounts.Select((amount, index) => new FinanceInvoiceInstallmentV1(Guid.NewGuid(), Guid.NewGuid(),
                    index + 1, new DateOnly(2026, 12, 1).AddMonths(index), amount, null))]);
            var (companyContext, collector) = Scope(CompanyA);
            await using var dbContext = new FinanceDbContext(Options(companyContext, collector), companyContext);
            var service = new FinanceInvoiceOpenItemAdmissionService(dbContext, companyContext,
                new FixedCurrentUser(Actor, CompanyA), new SyntheticParty(), new SyntheticAdmissionAuthority(), collector,
                new FinanceAuditEnvelope("synthetic.finance.invoice-open-items.admit", ["SENTETIK-FINANCE"], "Web"),
                TimeProvider.System);
            var result = await service.AdmitAsync(new FinanceContext(CompanyA, Actor, $"f3a-{Guid.NewGuid():N}", null),
                new CommandMetadata($"f3a-admit-{invoice.InvoiceId:N}"), invoice, CancellationToken.None);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            return invoice;
        }

        /// <summary>SENTETIK: a manual bank payment at version 3 and, when recorded, its one outgoing money event.</summary>
        public async Task<Guid> RecordPaymentAsync(string amount, int status = ManualBankPaymentStatuses.Recorded,
            Guid? currency = null, bool withMoneyEvent = true)
        {
            await using var db = Context(CompanyA, null);
            var now = DateTimeOffset.UtcNow;
            var today = DateOnly.FromDateTime(now.UtcDateTime);
            var id = Guid.NewGuid();
            var treasury = Guid.NewGuid();
            db.ManualBankPayments.Add(new ManualBankPaymentRow
            {
                Id = id,
                CompanyId = CompanyA,
                Version = 3,
                PartyId = Party,
                CompanyAccountId = Account,
                TreasuryAccountId = treasury,
                CurrencyId = currency ?? Lira,
                AmountText = amount,
                ProfileId = Guid.NewGuid().ToString("D"),
                ProfileVersion = "1",
                OccurredOn = today,
                ValueOn = today,
                Status = status,
                PreparedBy = "SENTETIK-preparer",
                PreparedAtUtc = now,
                ApprovedBy = "SENTETIK-approver",
                ApprovedAtUtc = now,
                RecordedBy = status == ManualBankPaymentStatuses.Recorded ? "SENTETIK-recorder" : null,
                RecordedAtUtc = status == ManualBankPaymentStatuses.Recorded ? now : null,
                BankResultConfirmed = status == ManualBankPaymentStatuses.Recorded,
            });
            if (withMoneyEvent && status == ManualBankPaymentStatuses.Recorded)
            {
                db.OutgoingMoneyEvents.Add(new OutgoingMoneyEventRow
                {
                    Id = Guid.NewGuid(),
                    CompanyId = CompanyA,
                    PaymentId = id,
                    PartyId = Party,
                    CompanyAccountId = Account,
                    TreasuryAccountId = treasury,
                    CurrencyId = currency ?? Lira,
                    AmountText = amount,
                    ProfileId = Guid.NewGuid().ToString("D"),
                    ProfileVersion = "1",
                    OccurredOn = today,
                    ValueOn = today,
                    RecordedAtUtc = now,
                    RecordedBy = "SENTETIK-recorder",
                    CorrelationId = "SENTETIK-f3a-payment",
                });
            }
            await db.SaveChangesAsync();
            return id;
        }

        public Task<FinancePaymentAllocationCommandResult> CreateAsync(string key,
            CreateFinancePaymentAllocationRequest request, PermissionDecision[]? authority = null,
            Guid? contextCompany = null, Guid? company = null) =>
            WithServiceAsync(authority, contextCompany, company, (service, context) =>
                service.CreateAsync(context, new CommandMetadata($"f3a-{key}"), request, CancellationToken.None));

        public Task<FinancePaymentAllocationCommandResult> ReleaseAsync(string key,
            ReleaseFinanceAllocationRequest request, Guid? company = null) =>
            WithServiceAsync(null, null, company, (service, context) =>
                service.ReleaseAsync(context, new CommandMetadata($"f3a-{key}"), request, CancellationToken.None));

        public async Task<ReadResult<FinancePaymentAllocationPage>> ListAsync(Guid company)
        {
            var (companyContext, _) = Scope(company);
            await using var dbContext = Context(company, null);
            return await new FinancePaymentAllocationReadService(dbContext, companyContext,
                    new FixedCurrentUser(Actor, company), new SyntheticPermissions([PermissionDecision.Allowed]))
                .ListAuthorizedAsync(new FinanceContext(company, Actor, "f3a-list", null), CancellationToken.None);
        }

        public async Task<ReadResult<FinancePaymentAllocationDetail>> DetailAsync(Guid company, Guid allocationId)
        {
            var (companyContext, _) = Scope(company);
            await using var dbContext = Context(company, null);
            return await new FinancePaymentAllocationReadService(dbContext, companyContext,
                    new FixedCurrentUser(Actor, company), new SyntheticPermissions([PermissionDecision.Allowed]))
                .GetAuthorizedAsync(new FinanceContext(company, Actor, "f3a-detail", null), allocationId,
                    CancellationToken.None);
        }

        public async Task<FinanceInvoiceOpenItemCancellationResultV1> CancelAsync(string key,
            FinanceInvoiceOpenItemAdmissionV1 invoice, bool releaseActiveAllocations = false)
        {
            var (companyContext, collector) = Scope(CompanyA);
            await using var dbContext = new FinanceDbContext(Options(companyContext, collector), companyContext);
            var service = new FinanceInvoiceOpenItemCancellationService(dbContext, companyContext,
                new FixedCurrentUser(Actor, CompanyA), new SyntheticCancellationAuthority(), collector,
                new FinanceAuditEnvelope("synthetic.finance.invoice-open-items.cancel", ["SENTETIK-FINANCE"], "Web"),
                TimeProvider.System);
            return await service.CancelAsync(new FinanceContext(CompanyA, Actor, $"f3a-{Guid.NewGuid():N}", null),
                new CommandMetadata($"f3a-{key}"),
                new FinanceInvoiceOpenItemCancellationV1(Guid.NewGuid(), invoice.Producer, invoice.InvoiceId,
                    invoice.InvoiceVersion, "SENTETIK e-Fatura RED", releaseActiveAllocations), CancellationToken.None);
        }

        public async Task<PartyLedgerResult> LedgerAsync()
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            await using var dbContext = Context(CompanyA, null);
            return await Ledger(dbContext).ReadAuthorizedAsync(new FinanceContext(CompanyA, Actor, "f3a-ledger", null),
                new PartyLedgerRequest(Party, Account, today.AddDays(-1), today.AddDays(1)), CancellationToken.None);
        }

        public async Task<PartyBalanceResult> BalanceAsync()
        {
            await using var dbContext = Context(CompanyA, null);
            return await Ledger(dbContext).SummarizeAuthorizedAsync(
                new FinanceContext(CompanyA, Actor, "f3a-balance", null), new PartyBalanceRequest(Party, Account),
                CancellationToken.None);
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

        /// <summary><paramref name="authority"/> answers each fence in turn; the last answer repeats.</summary>
        private async Task<FinancePaymentAllocationCommandResult> WithServiceAsync(PermissionDecision[]? authority,
            Guid? contextCompany, Guid? company,
            Func<FinancePaymentAllocationService, FinanceContext, Task<FinancePaymentAllocationCommandResult>> call)
        {
            var scopeCompany = company ?? CompanyA;
            var (companyContext, collector) = Scope(scopeCompany);
            await using var dbContext = new FinanceDbContext(Options(companyContext, collector), companyContext);
            var service = new FinancePaymentAllocationService(dbContext, companyContext,
                new FixedCurrentUser(Actor, scopeCompany),
                new SyntheticPermissions(authority ?? [PermissionDecision.Allowed]), collector,
                new FinanceAuditEnvelope("synthetic.finance.allocations.write", ["SENTETIK-FINANCE"], "Web"),
                TimeProvider.System);
            return await call(service,
                new FinanceContext(contextCompany ?? scopeCompany, Actor, $"f3a-{Guid.NewGuid():N}", null));
        }

        private static PartyLedgerService Ledger(FinanceDbContext dbContext)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            return new PartyLedgerService(dbContext, companyContext, new FixedCurrentUser(Actor, CompanyA),
                new SyntheticPermissions([PermissionDecision.Allowed]));
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
    private sealed class SyntheticCancellationAuthority : IFinanceInvoiceOpenItemCancellationAuthorityV1
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceInvoiceProducerV1 producer,
            Guid invoiceId, CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }

    /// <summary>SENTETIK: the Finance permission answers, one per fence in turn (the last repeats). Not a grant.</summary>
    private sealed class SyntheticPermissions(PermissionDecision[] decisions) : IFinancePermissionPort
    {
        private int calls;

        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) =>
            Task.FromResult(decisions[Math.Min(Interlocked.Increment(ref calls) - 1, decisions.Length - 1)]);
    }

    private sealed class FixedCurrentUser(string userId, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
