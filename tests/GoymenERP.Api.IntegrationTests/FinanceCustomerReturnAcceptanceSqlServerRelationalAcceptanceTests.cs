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
/// F-019 / API-11 v2 (SAT-D288, SAT-D291, SAT-D294): a finalised customer return invoice accepted by Finance through the
/// real <see cref="FinanceInvoiceAcceptanceService"/> against a REAL SQL Server. The return is one separate reversing
/// credit: it reduces the original invoice's outstanding amounts first, the rest stays as the customer's advance; the
/// original items keep their identity and original amount and nothing is deleted. The same acceptance replays; a second
/// acceptance of the same return document, a total above the invoice, a foreign company, another version, a cancelled
/// invoice or a missing authority writes nothing; the migration's Down refuses while a credit is stored. FIN-S10
/// (FNS-D44, FNS-D54): a partial return over several outstanding installments reduces exactly the user's selection, which
/// must total the return amount to the kurus and stay within each installment's open amount, or else writes nothing;
/// without a selection the nearest due installment is reduced first; the prefill read comes from the same calculator, is
/// fenced like the acceptance and writes nothing. Party, authority and grants are SENTETIK inputs.
/// </summary>
public sealed class FinanceCustomerReturnAcceptanceSqlServerRelationalAcceptanceTests
{
    private const string Actor = "SENTETIK-return-acceptor";
    private const string BeforeReturns = "20261114073527_AddOfficialSalesInvoiceRejectionAndWithdrawal";
    private const int DownGuardNumber = 51320;
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Lira = Guid.Parse("7c4a2a83-9e5d-4fa0-b132-4d5e6f708001");
    private static readonly Guid Party = Guid.Parse("7c4a2a83-9e5d-4fa0-b132-4d5e6f708003");
    private static readonly Guid Account = Guid.Parse("7c4a2a83-9e5d-4fa0-b132-4d5e6f708004");

    [SqlServerFact]
    public async Task A_return_reduces_the_open_item_first_and_the_same_acceptance_replays_once()
    {
        await using var run = await Run.StartAsync();
        var invoice = Invoice(Installment(1, "100.00"));
        Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync("single-1", invoice)).Outcome);
        var item = invoice.Installments[0].OpenItemId;
        var acceptance = Return(invoice, "40.00");

        var first = await run.AcceptAsync("single-1", acceptance);
        Assert.Equal(CommandOutcome.Succeeded, first.Outcome);
        Assert.False(first.IsReplay);
        Assert.Equal(acceptance.AcceptanceId, first.CreditId);
        var reduction = Assert.Single(first.Reductions);
        Assert.Equal(item, reduction.OpenItemId);
        Assert.Equal("40.00", reduction.Amount);
        Assert.Equal("0", first.AdvanceAmount);
        // The original item keeps its identity and original amount; only the projection moves (SAT-D291 (1)).
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE Id = @id AND OriginalAmountText = N'100.00' "
            + "AND OutstandingAmountText = N'60.00' AND Status = 1 AND Version = 2", ("@id", item)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.CustomerReturnCredits WHERE Id = @id AND DocumentId = @document "
            + "AND OriginalInvoiceId = @invoice AND AmountText = N'40.00' AND AppliedAmountText = N'40.00' "
            + "AND AdvanceAmountText = N'0'",
            ("@id", acceptance.AcceptanceId), ("@document", acceptance.DocumentId), ("@invoice", invoice.InvoiceId)));
        Assert.Equal("40.00", await run.ScalarAsync<string>(
            "SELECT ReducedAmountText FROM finance.CustomerReturnCreditItems WHERE CreditId = @id AND OpenItemId = @item",
            ("@id", acceptance.AcceptanceId), ("@item", item)));
        Assert.Equal("40.00", await run.ScalarAsync<string>(
            "SELECT ReturnedAmountText FROM finance.CustomerReturnInvoiceStates WHERE OriginalInvoiceId = @id AND Version = 1",
            ("@id", invoice.InvoiceId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = 'finance.customer-return-credits.accepted.v2' "
            + "AND EntityId = @id", ("@id", acceptance.AcceptanceId.ToString("D"))));

        var replay = await run.AcceptAsync("single-1", acceptance);
        Assert.Equal(CommandOutcome.Succeeded, replay.Outcome);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.Reductions, replay.Reductions);
        Assert.Equal(CommandOutcome.IdempotencyConflict,
            (await run.AcceptAsync("single-1", acceptance with { Amount = "41.00" })).Outcome);
        // A resend under a new key by another authorised user replays (SAT-D208); a changed payload under the same id is
        // an identity conflict; another id for the same return document is refused — one return is credited once.
        Assert.True((await run.AcceptAsync("single-1-resend", acceptance, actor: "SENTETIK-other-acceptor")).IsReplay);
        var changed = await run.AcceptAsync("single-1-changed", acceptance with { Amount = "41.00" });
        Assert.Contains(changed.Issues,
            issue => issue.Code == FinanceInvoiceAcceptanceService.AcceptanceIdentityConflictCode);
        var again = await run.AcceptAsync("single-1-again", acceptance with { AcceptanceId = Guid.NewGuid() });
        Assert.Contains(again.Issues, issue => issue.Code == FinanceInvoiceAcceptanceService.AlreadyAcceptedCode);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CustomerReturnCredits"));

        // A second return document: above the invoice's original total is refused; the exact rest closes the item.
        var exceeds = await run.AcceptAsync("single-2-exceeds", Return(invoice, "60.01"));
        Assert.Contains(exceeds.Issues, issue => issue.Code == FinanceInvoiceAcceptanceService.ExceedsInvoiceCode);
        var closing = await run.AcceptAsync("single-2", Return(invoice, "60.00"));
        Assert.Equal(CommandOutcome.Succeeded, closing.Outcome);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE Id = @id AND OriginalAmountText = N'100.00' "
            + "AND OutstandingAmountText = N'0' AND Status = 2 AND Version = 3", ("@id", item)));
        Assert.Equal("100.00", await run.ScalarAsync<string>(
            "SELECT ReturnedAmountText FROM finance.CustomerReturnInvoiceStates WHERE OriginalInvoiceId = @id AND Version = 2",
            ("@id", invoice.InvoiceId)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CustomerReturnCredits"));
        // No party-card movement and no money event are written (SAT-D291 (3)).
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
    }

    [SqlServerFact]
    public async Task A_return_above_the_outstanding_total_closes_the_items_and_the_rest_is_the_customers_advance()
    {
        await using var run = await Run.StartAsync();
        var invoice = Invoice(Installment(1, "600.00"), Installment(2, "400.00"));
        Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync("advance-1", invoice)).Outcome);

        // SENTETIK: the first installment is already settled (closed, nothing outstanding).
        Assert.Equal(1, await run.ScalarAsync<int>(
            "UPDATE finance.OpenItems SET Status = 2, OutstandingAmountText = N'0' WHERE Id = @id; SELECT @@ROWCOUNT",
            ("@id", invoice.Installments[0].OpenItemId)));

        var accepted = await run.AcceptAsync("advance-2", Return(invoice, "700.00"));
        Assert.Equal(CommandOutcome.Succeeded, accepted.Outcome);
        var reduction = Assert.Single(accepted.Reductions);
        Assert.Equal(invoice.Installments[1].OpenItemId, reduction.OpenItemId);
        Assert.Equal("400.00", reduction.Amount);
        Assert.Equal("300.00", accepted.AdvanceAmount);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE Id = @id AND OutstandingAmountText = N'0' AND Status = 2 "
            + "AND OriginalAmountText = N'400.00'", ("@id", invoice.Installments[1].OpenItemId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.CustomerReturnCredits WHERE AppliedAmountText = N'400.00' "
            + "AND AdvanceAmountText = N'300.00' AND AmountText = N'700.00'"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CustomerReturnCreditItems"));
    }

    [SqlServerFact]
    public async Task A_partial_return_reduces_exactly_the_users_selection_and_a_wrong_selection_writes_nothing()
    {
        await using var run = await Run.StartAsync();
        var invoice = Invoice(Installment(1, "600.00"), Installment(2, "400.00"));
        Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync("selection-1", invoice)).Outcome);
        var first = invoice.Installments[0].OpenItemId;
        var second = invoice.Installments[1].OpenItemId;

        // The screen's prefill: nearest due first, the whole 500.00 on the first installment.
        var prefill = await run.ReadAsync(Query(invoice, "500.00"));
        Assert.Equal(CommandOutcome.Succeeded, prefill.Outcome);
        Assert.True(prefill.SelectionAllowed);
        Assert.Equal([first, second], prefill.Installments.Select(row => row.OpenItemId).ToArray());
        Assert.Equal(["500.00", "0"], prefill.Installments.Select(row => row.PrefillAmount).ToArray());
        Assert.Equal(["600.00", "400.00"], prefill.Installments.Select(row => row.OutstandingAmount).ToArray());
        Assert.Equal("500.00", prefill.AppliedAmount);
        Assert.Equal("0", prefill.AdvanceAmount);

        // A selection that does not total the return amount, exceeds an installment's open amount or names an item that is
        // not the invoice's is refused and writes nothing.
        var mismatch = await run.AcceptAsync("selection-mismatch",
            Return(invoice, "500.00") with { Allocations = [Part(first, "100.00"), Part(second, "399.99")] });
        Assert.Contains(mismatch.Issues,
            issue => issue.Code == FinanceInvoiceAcceptanceService.AllocationTotalMismatchCode);
        var exceeds = await run.AcceptAsync("selection-exceeds",
            Return(invoice, "500.00") with { Allocations = [Part(first, "99.99"), Part(second, "400.01")] });
        Assert.Contains(exceeds.Issues,
            issue => issue.Code == FinanceInvoiceAcceptanceService.AllocationExceedsOpenAmountCode);
        var unknown = await run.AcceptAsync("selection-unknown",
            Return(invoice, "500.00") with { Allocations = [Part(Guid.NewGuid(), "100.00"), Part(second, "400.00")] });
        Assert.Contains(unknown.Issues, issue => issue.Code == FinanceInvoiceAcceptanceService.AllocationItemUnknownCode);
        Assert.All(new[] { mismatch, exceeds, unknown },
            result => Assert.Equal(CommandOutcome.ValidationFailed, result.Outcome));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CustomerReturnCredits"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CustomerReturnInvoiceStates"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE SourceId = @id AND Version = 1", ("@id", invoice.InvoiceId)));

        // The user's own selection is applied as submitted, not the prefill.
        var acceptance = Return(invoice, "500.00") with { Allocations = [Part(second, "400.00"), Part(first, "100.00")] };
        var accepted = await run.AcceptAsync("selection-2", acceptance);
        Assert.Equal(CommandOutcome.Succeeded, accepted.Outcome);
        Assert.Equal(
            [new FinanceInvoiceAcceptanceReductionV2(first, "100.00"), new FinanceInvoiceAcceptanceReductionV2(second, "400.00")],
            accepted.Reductions.ToArray());
        Assert.Equal("0", accepted.AdvanceAmount);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE Id = @id AND OriginalAmountText = N'600.00' "
            + "AND OutstandingAmountText = N'500.00' AND Status = 1 AND Version = 2", ("@id", first)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE Id = @id AND OriginalAmountText = N'400.00' "
            + "AND OutstandingAmountText = N'0.00' AND Status = 2 AND Version = 2", ("@id", second)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.CustomerReturnCreditItems WHERE CreditId = @id", ("@id", acceptance.AcceptanceId)));
        Assert.Equal("500.00", await run.ScalarAsync<string>(
            "SELECT ReturnedAmountText FROM finance.CustomerReturnInvoiceStates WHERE OriginalInvoiceId = @id AND Version = 1",
            ("@id", invoice.InvoiceId)));

        // The selection is part of the acceptance: the same key replays it, and the same key without it is a conflict.
        Assert.True((await run.AcceptAsync("selection-2", acceptance)).IsReplay);
        Assert.Equal(CommandOutcome.IdempotencyConflict,
            (await run.AcceptAsync("selection-2", acceptance with { Allocations = null })).Outcome);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CustomerReturnCredits"));
    }

    [SqlServerFact]
    public async Task A_partial_return_without_a_selection_reduces_the_nearest_due_installment_first_as_prefilled()
    {
        await using var run = await Run.StartAsync();
        var invoice = Invoice(Installment(1, "300.00"), Installment(2, "300.00"), Installment(3, "300.00"));
        Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync("nearest-1", invoice)).Outcome);
        var (first, second, third) = (invoice.Installments[0].OpenItemId, invoice.Installments[1].OpenItemId,
            invoice.Installments[2].OpenItemId);
        // SENTETIK: the nearest installment is already partly settled.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "UPDATE finance.OpenItems SET OutstandingAmountText = N'100.00' WHERE Id = @id; SELECT @@ROWCOUNT",
            ("@id", first)));

        var prefill = await run.ReadAsync(Query(invoice, "250.00"));
        Assert.Equal(CommandOutcome.Succeeded, prefill.Outcome);
        Assert.Equal([first, second, third], prefill.Installments.Select(row => row.OpenItemId).ToArray());
        Assert.Equal(["100.00", "150.00", "0"], prefill.Installments.Select(row => row.PrefillAmount).ToArray());

        var accepted = await run.AcceptAsync("nearest-2", Return(invoice, "250.00"));
        Assert.Equal(CommandOutcome.Succeeded, accepted.Outcome);
        Assert.Equal(
            [new FinanceInvoiceAcceptanceReductionV2(first, "100.00"), new FinanceInvoiceAcceptanceReductionV2(second, "150.00")],
            accepted.Reductions.ToArray());
        Assert.Equal("0", accepted.AdvanceAmount);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE Id = @id AND OutstandingAmountText = N'0.00' AND Status = 2 "
            + "AND Version = 2", ("@id", first)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE Id = @id AND OutstandingAmountText = N'150.00' AND Status = 1 "
            + "AND Version = 2", ("@id", second)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE Id = @id AND OutstandingAmountText = N'300.00' AND Status = 1 "
            + "AND Version = 1", ("@id", third)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CustomerReturnCreditItems"));
    }

    [SqlServerFact]
    public async Task The_prefill_read_is_fenced_like_the_acceptance_and_writes_nothing()
    {
        await using var run = await Run.StartAsync();
        var invoice = Invoice(Installment(1, "100.00"), Installment(2, "100.00"));
        Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync("prefill-1", invoice)).Outcome);
        // SENTETIK: the first installment is partly settled; 140.00 is still outstanding.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "UPDATE finance.OpenItems SET OutstandingAmountText = N'40.00' WHERE Id = @id; SELECT @@ROWCOUNT",
            ("@id", invoice.Installments[0].OpenItemId)));

        // A return above the outstanding total closes every installment, the rest is the advance and no selection is taken.
        var whole = await run.ReadAsync(Query(invoice, "150.00"));
        Assert.Equal(CommandOutcome.Succeeded, whole.Outcome);
        Assert.False(whole.SelectionAllowed);
        Assert.Equal(["40.00", "100.00"], whole.Installments.Select(row => row.PrefillAmount).ToArray());
        Assert.Equal("140.00", whole.AppliedAmount);
        Assert.Equal("10.00", whole.AdvanceAmount);

        Assert.Contains((await run.ReadAsync(Query(invoice, "200.01"))).Issues,
            issue => issue.Code == FinanceInvoiceAcceptanceService.ExceedsInvoiceCode);
        Assert.Contains((await run.ReadAsync(Query(invoice, "10.00") with { OriginalInvoiceVersion = "invoice-v2" })).Issues,
            issue => issue.Code == FinanceInvoiceAcceptanceService.VersionMismatchCode);
        Assert.Contains((await run.ReadAsync(Query(invoice, "10.00") with { OriginalInvoiceId = Guid.NewGuid() })).Issues,
            issue => issue.Code == FinanceInvoiceAcceptanceService.NotAdmittedCode);
        Assert.Equal(CommandOutcome.Denied,
            (await run.ReadAsync(Query(invoice, "10.00"), PermissionDecision.Denied)).Outcome);
        var unavailable = await run.ReadAsync(Query(invoice, "10.00"), PermissionDecision.Unavailable);
        Assert.Equal(CommandOutcome.Unavailable, unavailable.Outcome);
        Assert.Empty(unavailable.Installments);
        Assert.Equal(CommandOutcome.Denied,
            (await run.ReadAsync(Query(invoice, "10.00"), contextCompany: CompanyB)).Outcome);
        // Company B, resolved and authorised in its own context, cannot see company A's invoice.
        Assert.Contains((await run.ReadAsync(Query(invoice, "10.00"), company: CompanyB)).Issues,
            issue => issue.Code == FinanceInvoiceAcceptanceService.NotAdmittedCode);

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CustomerReturnCredits"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CustomerReturnInvoiceStates"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE SourceId = @id AND Version = 1", ("@id", invoice.InvoiceId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = 'finance.customer-return-credits.accepted.v2'"));
    }

    [SqlServerFact]
    public async Task A_return_of_a_fully_settled_invoice_is_wholly_the_customers_advance()
    {
        await using var run = await Run.StartAsync();
        var invoice = Invoice(Installment(1, "80.00"));
        Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync("settled-1", invoice)).Outcome);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "UPDATE finance.OpenItems SET Status = 2, OutstandingAmountText = N'0' WHERE Id = @id; SELECT @@ROWCOUNT",
            ("@id", invoice.Installments[0].OpenItemId)));

        var accepted = await run.AcceptAsync("settled-1", Return(invoice, "30.00"));
        Assert.Equal(CommandOutcome.Succeeded, accepted.Outcome);
        Assert.Empty(accepted.Reductions);
        Assert.Equal("30.00", accepted.AdvanceAmount);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CustomerReturnCreditItems"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE Id = @id AND Version = 1",
            ("@id", invoice.Installments[0].OpenItemId)));
    }

    [SqlServerFact]
    public async Task An_unknown_invoice_another_version_a_mismatch_another_company_or_a_missing_authority_accepts_nothing()
    {
        await using var run = await Run.StartAsync();
        var invoice = Invoice(Installment(1, "300.00"));
        Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync("refuse-1", invoice)).Outcome);
        var acceptance = Return(invoice, "10.00");

        Assert.Contains((await run.AcceptAsync("refuse-unknown", acceptance with { OriginalInvoiceId = Guid.NewGuid() }))
            .Issues, issue => issue.Code == FinanceInvoiceAcceptanceService.NotAdmittedCode);
        Assert.Contains((await run.AcceptAsync("refuse-version", acceptance with { OriginalInvoiceVersion = "invoice-v2" }))
            .Issues, issue => issue.Code == FinanceInvoiceAcceptanceService.VersionMismatchCode);
        Assert.Contains((await run.AcceptAsync("refuse-party", acceptance with { PartyId = Guid.NewGuid() }))
            .Issues, issue => issue.Code == FinanceInvoiceAcceptanceService.PartyMismatchCode);
        Assert.Contains((await run.AcceptAsync("refuse-currency", acceptance with { CurrencyId = Guid.NewGuid() }))
            .Issues, issue => issue.Code == FinanceInvoiceAcceptanceService.CurrencyMismatchCode);
        Assert.Contains((await run.AcceptAsync("refuse-functional", acceptance with { FunctionalAmount = "350.00" }))
            .Issues, issue => issue.Code == FinanceInvoiceAcceptanceService.FunctionalAmountMismatchCode);
        Assert.Contains((await run.AcceptAsync("refuse-purchasing",
                acceptance with { Producer = FinanceInvoiceProducerV1.Purchasing }))
            .Issues, issue => issue.Code == FinanceInvoiceAcceptanceService.KindUnsupportedCode);
        Assert.Equal(CommandOutcome.Denied,
            (await run.AcceptAsync("refuse-denied", acceptance, authority: PermissionDecision.Denied)).Outcome);
        var unavailable = await run.AcceptAsync("refuse-unavailable", acceptance, authority: PermissionDecision.Unavailable);
        Assert.Equal(CommandOutcome.Unavailable, unavailable.Outcome);
        Assert.Equal(CommandOutcome.Denied,
            (await run.AcceptAsync("refuse-context", acceptance, contextCompany: CompanyB)).Outcome);
        // Company B, resolved and authorised in its own context, cannot see company A's invoice.
        var other = await run.AcceptAsync("refuse-company", acceptance, [PermissionDecision.Allowed], company: CompanyB);
        Assert.Contains(other.Issues, issue => issue.Code == FinanceInvoiceAcceptanceService.NotAdmittedCode);

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CustomerReturnCredits"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CustomerReturnInvoiceStates"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE SourceId = @id AND OutstandingAmountText = N'300.00' "
            + "AND Version = 1", ("@id", invoice.InvoiceId)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = 'finance.customer-return-credits.accepted.v2'"));
    }

    [SqlServerFact]
    public async Task A_cancelled_invoice_accepts_no_return()
    {
        await using var run = await Run.StartAsync();
        var invoice = Invoice(Installment(1, "50.00"));
        Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync("cancelled-1", invoice)).Outcome);
        // SENTETIK: the invoice's open item was closed by its cancellation (status 3, nothing outstanding).
        Assert.Equal(1, await run.ScalarAsync<int>(
            "UPDATE finance.OpenItems SET Status = 3, OutstandingAmountText = N'0' WHERE Id = @id; SELECT @@ROWCOUNT",
            ("@id", invoice.Installments[0].OpenItemId)));

        var refused = await run.AcceptAsync("cancelled-1", Return(invoice, "10.00"));
        Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
        Assert.Contains(refused.Issues, issue => issue.Code == FinanceInvoiceAcceptanceService.InvoiceCancelledCode);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CustomerReturnCredits"));
    }

    [SqlServerFact]
    public async Task An_authority_withdrawn_before_the_save_and_a_concurrent_second_return_write_nothing_more()
    {
        await using var run = await Run.StartAsync();
        var invoice = Invoice(Installment(1, "100.00"));
        Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync("race-1", invoice)).Outcome);

        // Allowed at the first fence, denied right before the save: nothing is written and no audit is queued.
        var withdrawn = await run.AcceptAsync("race-withdrawn", Return(invoice, "10.00"),
            [PermissionDecision.Allowed, PermissionDecision.Denied]);
        Assert.Equal(CommandOutcome.Denied, withdrawn.Outcome);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CustomerReturnCredits"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = 'finance.customer-return-credits.accepted.v2'"));

        // Two return documents of the same invoice at once whose sum exceeds it: exactly one is credited.
        var results = await Task.WhenAll(
            run.AcceptAsync("race-a", Return(invoice, "70.00")),
            run.AcceptAsync("race-b", Return(invoice, "70.00")));
        Assert.Single(results, result => result.Outcome == CommandOutcome.Succeeded);
        Assert.Single(results, result => result.Outcome is CommandOutcome.VersionConflict
            || result.Issues.Any(issue => issue.Code == FinanceInvoiceAcceptanceService.ExceedsInvoiceCode));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CustomerReturnCredits"));
        Assert.Equal("70.00", await run.ScalarAsync<string>(
            "SELECT ReturnedAmountText FROM finance.CustomerReturnInvoiceStates WHERE OriginalInvoiceId = @id",
            ("@id", invoice.InvoiceId)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE SourceId = @id AND OutstandingAmountText = N'30.00' "
            + "AND Version = 2", ("@id", invoice.InvoiceId)));
    }

    [SqlServerFact]
    public async Task The_rollback_refuses_while_a_customer_return_credit_is_stored()
    {
        await using var run = await Run.StartAsync();
        var invoice = Invoice(Installment(1, "20.00"));
        Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync("rollback-1", invoice)).Outcome);

        // With no credit stored, the Down runs and the reapply restores the tables.
        const string Tables = "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'finance') "
            + "AND name IN (N'CustomerReturnCredits', N'CustomerReturnCreditItems', N'CustomerReturnInvoiceStates')";
        await run.MigrateFinanceAsync(BeforeReturns);
        Assert.Equal(0, await run.ScalarAsync<int>(Tables));
        await run.MigrateFinanceAsync(null);
        Assert.Equal(3, await run.ScalarAsync<int>(Tables));

        Assert.Equal(CommandOutcome.Succeeded, (await run.AcceptAsync("rollback-1", Return(invoice, "5.00"))).Outcome);
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.MigrateFinanceAsync(BeforeReturns));
        Assert.Equal(DownGuardNumber, refused.Number);
        Assert.Equal(3, await run.ScalarAsync<int>(Tables));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.CustomerReturnCredits"));

        // The stored text is checked: a reduction of zero is not a reduction.
        var zero = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE finance.CustomerReturnCreditItems SET ReducedAmountText = N''; SELECT @@ROWCOUNT"));
        Assert.Contains("CK_CustomerReturnCreditItems_Amount", zero.Message, StringComparison.Ordinal);
    }

    private static FinanceInvoiceOpenItemAdmissionV1 Invoice(params FinanceInvoiceInstallmentV1[] installments) =>
        new(Guid.NewGuid(), FinanceInvoiceProducerV1.Sales, Guid.NewGuid(), "invoice-v1", Party, Account, Lira, null,
            installments);

    private static FinanceInvoiceInstallmentV1 Installment(int sequence, string amount) =>
        new(Guid.NewGuid(), Guid.NewGuid(), sequence, new DateOnly(2026, 12, 1).AddMonths(sequence - 1), amount, null);

    private static FinanceInvoiceAcceptanceV2 Return(FinanceInvoiceOpenItemAdmissionV1 invoice, string amount) =>
        new(Guid.NewGuid(), FinanceInvoiceProducerV1.Sales, FinanceInvoiceAcceptanceKindV2.CustomerReturn, Guid.NewGuid(),
            "return-v1", invoice.InvoiceId, invoice.InvoiceVersion, Party, Account, Lira, amount, null,
            new DateOnly(2026, 11, 14));

    private static FinanceInvoiceAcceptanceAllocationV2 Part(Guid openItemId, string amount) => new(openItemId, amount);

    private static FinanceCustomerReturnInstallmentQueryV2 Query(FinanceInvoiceOpenItemAdmissionV1 invoice, string amount) =>
        new(FinanceInvoiceProducerV1.Sales, FinanceInvoiceAcceptanceKindV2.CustomerReturn, Guid.NewGuid(), invoice.InvoiceId,
            invoice.InvoiceVersion, amount);

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
            return await service.AdmitAsync(new FinanceContext(CompanyA, Actor, $"f019-{Guid.NewGuid():N}", null),
                new CommandMetadata($"f019-admit-{key}"), invoice, CancellationToken.None);
        }

        public Task<FinanceInvoiceAcceptanceResultV2> AcceptAsync(string key, FinanceInvoiceAcceptanceV2 acceptance,
            Guid? contextCompany = null, PermissionDecision authority = PermissionDecision.Allowed, string actor = Actor) =>
            AcceptAsync(key, acceptance, [authority], contextCompany, actor);

        /// <summary><paramref name="authority"/> answers each fence in turn; the last answer repeats.</summary>
        public async Task<FinanceInvoiceAcceptanceResultV2> AcceptAsync(string key, FinanceInvoiceAcceptanceV2 acceptance,
            PermissionDecision[] authority, Guid? contextCompany = null, string actor = Actor, Guid? company = null)
        {
            var scopeCompany = company ?? CompanyA;
            var (companyContext, collector) = Scope(scopeCompany);
            await using var dbContext = new FinanceDbContext(Options(companyContext, collector), companyContext);
            var service = new FinanceInvoiceAcceptanceService(dbContext, companyContext,
                new FixedCurrentUser(actor, scopeCompany), new SyntheticAcceptanceAuthority(authority), collector,
                new FinanceAuditEnvelope("synthetic.finance.invoice-acceptances.accept", ["SENTETIK-FINANCE"], "Web"),
                TimeProvider.System);
            return await service.AcceptAsync(
                new FinanceContext(contextCompany ?? scopeCompany, actor, $"f019-{Guid.NewGuid():N}", null),
                new CommandMetadata($"f019-{key}"), acceptance, CancellationToken.None);
        }

        /// <summary>The prefill read (FIN-S10) through the same service and fence as the acceptance; it writes nothing.</summary>
        public async Task<FinanceCustomerReturnInstallmentPrefillV2> ReadAsync(FinanceCustomerReturnInstallmentQueryV2 query,
            PermissionDecision authority = PermissionDecision.Allowed, Guid? contextCompany = null, Guid? company = null)
        {
            var scopeCompany = company ?? CompanyA;
            var (companyContext, collector) = Scope(scopeCompany);
            await using var dbContext = new FinanceDbContext(Options(companyContext, collector), companyContext);
            var service = new FinanceInvoiceAcceptanceService(dbContext, companyContext,
                new FixedCurrentUser(Actor, scopeCompany), new SyntheticAcceptanceAuthority([authority]), collector,
                new FinanceAuditEnvelope("synthetic.finance.invoice-acceptances.accept", ["SENTETIK-FINANCE"], "Web"),
                TimeProvider.System);
            return await service.ReadAsync(
                new FinanceContext(contextCompany ?? scopeCompany, Actor, $"f019-{Guid.NewGuid():N}", null), query,
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

    /// <summary>SENTETIK: the host-owned acceptance authority answer, one per fence. Not a grant.</summary>
    private sealed class SyntheticAcceptanceAuthority(PermissionDecision[] decisions) : IFinanceInvoiceAcceptanceAuthorityV2
    {
        private int calls;

        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceInvoiceProducerV1 producer,
            FinanceInvoiceAcceptanceKindV2 kind, Guid documentId, CancellationToken cancellationToken) =>
            Task.FromResult(decisions[Math.Min(calls++, decisions.Length - 1)]);
    }

    private sealed class FixedCurrentUser(string userId, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
