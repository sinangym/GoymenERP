using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using System.Net;
using System.Net.Http.Json;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Contracts;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence.Migrations;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingSourceDocumentAdmissionSqlServerRelationalAcceptanceTests
{
    private static readonly Guid VoucherChecker = Guid.Parse("e0420000-0000-4000-8000-000000000006");

    [SqlServerFact]
    public async Task Source_voucher_lifecycle_down_passes_with_legacy_draft_and_refuses_after_approval_without_losing_the_journal()
    {
        await using var run = await Run.StartAsync(true); await OpenVoucherPeriod(run);
        var guard = Assert.IsType<SqlOperation>(new AddSourceVoucherLifecycle().DownOperations[0]).Sql;
        var admitted = await run.AdmitAsync(CompanyA, Request(CompanyA));
        await run.ExecuteAsync(guard);
        await ApproveVoucher(run, admitted.VoucherDraftId!.Value, _ => Task.CompletedTask);
        var error = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51938, error.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Journals WHERE CompanyId=@c", ("@c", CompanyA)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT Status FROM accounting.SourceDocumentVoucherDrafts WHERE CompanyId=@c", ("@c", CompanyA)));
    }

    [SqlServerFact]
    public async Task Source_voucher_lifecycle_down_refuses_the_cancellation_tombstone_even_without_a_voucher()
    {
        await using var run = await Run.StartAsync(true);
        var guard = Assert.IsType<SqlOperation>(new AddSourceVoucherLifecycle().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);
        await CancelVoucherSource(run, Cancellation());
        var error = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51938, error.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.SourceInvoiceCancellations WHERE CompanyId=@c", ("@c", CompanyA)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.SourceDocumentVoucherDrafts WHERE CompanyId=@c", ("@c", CompanyA)));
    }

    [SqlServerFact]
    public async Task Source_voucher_http_reads_are_company_scoped_and_the_source_maker_cannot_approve_or_send_extra_fields()
    {
        await using var run = await Run.StartAsync(true);
        var maker = await run.GrantAsync(Preparer, ErpPermissions.AccountingJournalsRead, NormalJournalPermissionNamesV1.DraftApprove);
        var admitted = await run.AdmitAsync(CompanyA, Request(CompanyA) with { SourceActorId = maker, OfficialInvoiceNo = "SENTETIK-HTTP-1" });
        using var client = run.Client(Preparer);
        var list = await client.GetFromJsonAsync<AccountingSourceVoucherListV1>("/api/v1/accounting/source-vouchers");
        Assert.Equal("SENTETIK-HTTP-1", Assert.Single(list!.Items).OfficialInvoiceNo);
        var path = $"/api/v1/accounting/source-vouchers/{admitted.VoucherDraftId:D}/approval";
        using var approval = Command(HttpMethod.Post, path, Key("source-self-approve"));
        approval.Content = JsonContent.Create(new { expectedVersion = 1 });
        using var denied = await client.SendAsync(approval); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var malformed = Command(HttpMethod.Post, path, Key("source-extra"));
        malformed.Content = JsonContent.Create(new { expectedVersion = 1, companyId = CompanyB, periodId = Guid.NewGuid() });
        using var invalid = await client.SendAsync(malformed); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Journals WHERE CompanyId=@c", ("@c", CompanyA)));
    }

    [SqlServerFact]
    public async Task Source_voucher_approval_posts_exact_frozen_lines_once_and_a_late_authority_loss_rolls_everything_back()
    {
        await using var run = await Run.StartAsync(true);
        await OpenVoucherPeriod(run);
        var admission = await run.AdmitAsync(CompanyA, Request(CompanyA) with { OfficialInvoiceNo = "SENTETIK-INV-1" });
        var id = admission.VoucherDraftId!.Value;
        var fences = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => ApproveVoucher(run, id, _ =>
        { if (++fences == 3) throw new InvalidOperationException("SENTETIK lost final authority"); return Task.CompletedTask; }));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Journals WHERE CompanyId=@c", ("@c", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT Status FROM accounting.SourceDocumentVoucherDrafts WHERE CompanyId=@c", ("@c", CompanyA)));
        var posted = await ApproveVoucher(run, id, _ => Task.CompletedTask);
        Assert.Equal("Posted", posted.Status); Assert.Equal(id, posted.JournalId); Assert.Equal(2, posted.Version);
        Assert.Equal("SENTETIK-INV-1", posted.OfficialInvoiceNo);
        Assert.Equal(new[] { "1440", "1000", "200", "240" }, posted.Lines.Select(x => x.FunctionalAmount));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Journals WHERE CompanyId=@c", ("@c", CompanyA)));
        Assert.Equal(4, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.JournalLines WHERE CompanyId=@c", ("@c", CompanyA)));
        await Assert.ThrowsAsync<AccountingVersionConflictException>(() => ApproveVoucher(run, id, _ => Task.CompletedTask));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Journals WHERE CompanyId=@c", ("@c", CompanyA)));
    }

    [SqlServerFact]
    public async Task Source_cancel_before_finalized_delivery_tombstones_the_source_and_cannot_create_a_late_voucher()
    {
        await using var run = await Run.StartAsync(true);
        var request = Cancellation();
        Assert.Equal("Recorded", (await CancelVoucherSource(run, request)).Status);
        Assert.True((await CancelVoucherSource(run, request)).IsReplay);
        var late = await run.AdmitAsync(CompanyA, Request(CompanyA));
        Assert.Equal(AccountingSourceDocumentAdmissionReasonsV1.SourceCancelled, late.Reason);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.SourceDocumentVoucherDrafts WHERE CompanyId=@c", ("@c", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.SourceInvoiceCancellations WHERE CompanyId=@c", ("@c", CompanyA)));
        var conflict = await Assert.ThrowsAsync<AccountingStoreException>(() => CancelVoucherSource(run, request with { Reason = "SENTETIK changed" }));
        Assert.Equal("source_invoice_cancellation_conflict", conflict.Code);
    }

    [SqlServerFact]
    public async Task Unapproved_source_cancellation_preserves_lines_and_refuses_approval_instead_of_posting_a_reverse_entry()
    {
        await using var run = await Run.StartAsync(true);
        await OpenVoucherPeriod(run);
        var admitted = await run.AdmitAsync(CompanyA, Request(CompanyA));
        await CancelVoucherSource(run, Cancellation());
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT Status FROM accounting.SourceDocumentVoucherDrafts WHERE CompanyId=@c", ("@c", CompanyA)));
        Assert.Equal(4, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.SourceDocumentVoucherDraftLines WHERE CompanyId=@c", ("@c", CompanyA)));
        await Assert.ThrowsAsync<AccountingVersionConflictException>(() => ApproveVoucher(run, admitted.VoucherDraftId!.Value, _ => Task.CompletedTask));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Journals WHERE CompanyId=@c", ("@c", CompanyA)));
        await using var scope = run.Factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyB);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IAccountingSourceVoucherStoreV1>().ListAsync());
    }

    [SqlServerFact]
    public async Task Posted_source_cancellation_preserves_the_original_journal_and_leaves_reversal_period_unselected()
    {
        await using var run = await Run.StartAsync(true); await OpenVoucherPeriod(run);
        var admitted = await run.AdmitAsync(CompanyA, Request(CompanyA));
        await ApproveVoucher(run, admitted.VoucherDraftId!.Value, _ => Task.CompletedTask);
        await CancelVoucherSource(run, Cancellation());
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Journals WHERE CompanyId=@c", ("@c", CompanyA)));
        await using var scope = run.Factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        var view = await scope.ServiceProvider.GetRequiredService<IAccountingSourceVoucherStoreV1>().FindAsync(admitted.VoucherDraftId.Value);
        Assert.Equal("CancellationReversalPending", view!.Status); Assert.NotNull(view.JournalId);
    }

    [SqlServerFact]
    public async Task Concurrent_source_voucher_approval_and_cancellation_serialize_without_a_late_post_or_duplicate_journal()
    {
        await using var run = await Run.StartAsync(true); await OpenVoucherPeriod(run);
        var admitted = await run.AdmitAsync(CompanyA, Request(CompanyA));
        var cancellation = Cancellation();
        async Task<Exception?> Approve() => await Record.ExceptionAsync(() => ApproveVoucher(run, admitted.VoucherDraftId!.Value, _ => Task.CompletedTask));
        async Task<Exception?> Cancel() => await Record.ExceptionAsync(() => CancelVoucherSource(run, cancellation));
        var outcomes = await Task.WhenAll(Approve(), Cancel());
        Assert.All(outcomes.Where(x => x is not null), error => Assert.True(error is AccountingVersionConflictException
            || error is AccountingStoreException { Code: AccountingStoreCodes.ConcurrentChange or "source_voucher_not_approvable" }, error!.ToString()));
        // A deadlock victim is retried with the same immutable cancellation identity, never a new financial operation.
        Assert.Equal("Recorded", (await CancelVoucherSource(run, cancellation)).Status);
        await using var scope = run.Factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        var view = await scope.ServiceProvider.GetRequiredService<IAccountingSourceVoucherStoreV1>().FindAsync(admitted.VoucherDraftId!.Value);
        var journals = await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Journals WHERE CompanyId=@c", ("@c", CompanyA));
        Assert.Equal(view!.Status == "CancellationReversalPending" ? 1 : 0, journals);
        Assert.Contains(view.Status, new[] { "Cancelled", "CancellationReversalPending" });
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.SourceInvoiceCancellations WHERE CompanyId=@c", ("@c", CompanyA)));
    }

    [SqlServerFact]
    public async Task Source_voucher_without_its_invoice_date_open_period_remains_a_draft_without_posting()
    {
        await using var run = await Run.StartAsync(true);
        var admitted = await run.AdmitAsync(CompanyA, Request(CompanyA));
        var error = await Assert.ThrowsAsync<AccountingStoreException>(() => ApproveVoucher(run, admitted.VoucherDraftId!.Value, _ => Task.CompletedTask));
        Assert.Equal("source_voucher_period_waiting", error.Code);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM accounting.Journals WHERE CompanyId=@c", ("@c", CompanyA)));
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT Version FROM accounting.SourceDocumentVoucherDrafts WHERE CompanyId=@c", ("@c", CompanyA)));
    }

    private static AccountingCommandMetadata VoucherMetadata(Guid actor) => new(actor.ToString("D"),
        NormalJournalPermissionNamesV1.DraftApprove, ["SENTETIK"], "SENTETIK-source-voucher", null, Key("voucher"), SourceChannels.Web);
    private static AccountingSourceInvoiceCancellationV1 Cancellation() => new(CompanyA, "SENTETIK-source", "SENTETIK-cancel", null,
        Guid.NewGuid(), AccountingSourceDocumentKindsV1.SalesInvoice, SourceDocument, Invoice, 1, VoucherChecker,
        DateTimeOffset.Parse("2026-11-17T12:00:00Z"), "SENTETIK resmî iptal");
    private static async Task OpenVoucherPeriod(Run run)
    {
        await using var scope = run.Factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        var periods = scope.ServiceProvider.GetRequiredService<IAccountingPeriodStore>(); var id = Guid.NewGuid();
        var maker = Request(CompanyA).SourceActorId;
        var (created, _) = await periods.ProposePeriodAsync(id, new(2026, 11, 1), new(2026, 11, 30), maker, VoucherMetadata(maker));
        await periods.ApprovePeriodAsync(id, created.Version, VoucherChecker, VoucherMetadata(VoucherChecker));
    }
    private static async Task<AccountingSourceVoucherViewV1> ApproveVoucher(Run run, Guid id, Func<CancellationToken, Task> fence)
    {
        await using var scope = run.Factory.Services.CreateAsyncScope(); var provider = scope.ServiceProvider;
        var company = provider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
        var db = provider.GetRequiredService<AccountingDbContext>(); var audit = provider.GetRequiredService<IAuditEventCollector>();
        var posting = new AccountingStore(db, company, audit, TimeProvider.System,
            new AccountingAmountScaleVerifier(new AccountingSqlServerRelationalAcceptanceTests.SyntheticMoneyProfiles(CompanyA)));
        var store = new AccountingSourceVoucherStoreV1(db, posting, company, provider.GetRequiredService<IActiveAccountPlanReaderV1>(),
            new VoucherCurrencies(), audit, TimeProvider.System);
        return await store.ApproveAsync(id, 1, VoucherChecker, fence, VoucherMetadata(VoucherChecker));
    }
    private static async Task<AccountingSourceInvoiceCancellationAnswerV1> CancelVoucherSource(Run run, AccountingSourceInvoiceCancellationV1 request)
    {
        await using var scope = run.Factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
        return await scope.ServiceProvider.GetRequiredService<IAccountingSourceInvoiceCancellationStoreV1>()
            .CancelAsync(request, VoucherMetadata(VoucherChecker) with { ActorId = request.ActorId, CorrelationId = request.CorrelationId }, _ => Task.CompletedTask);
    }
    private sealed class VoucherCurrencies : ISettingsMoneyCandidateReader
    {
        public Task<IReadOnlyList<MoneyCurrencyCandidateV1>> GetMoneyCurrencyCandidatesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MoneyCurrencyCandidateV1>>([new(Request(CompanyA).CurrencyId, "TRY", "SENTETIK TL")]);
    }
}
