using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AccountingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Journal_reversal_pending_bulk_actor_query_preserves_company_page_and_native_KLN_D17_exclusion()
    {
        await using var run = await Run.StartAsync(); var period = await run.OpenSeptemberAsync(CompanyA);
        var proposals = new List<Guid>(); var journals = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            await using var seed = run.Store(CompanyA); var id = Guid.NewGuid(); proposals.Add(id);
            var journal = (await seed.Postings.ReceiveAsync(Request(CompanyA, "pending-bulk-" + id.ToString("N"), Guid.NewGuid(), Accounts(CompanyA)), period, Metadata(run))).Journal!;
            journals.Add(journal.Id);
            var proposal = AccountingJournalReversalPolicyV1.Propose(CompanyA, run.Preparer,
                new(id, journal.Id, period, InSeptember.TransactionDate, InSeptember.AccountingDate, InSeptember.ValueDate, "SENTETIK bulk source"), RecordedAt.AddMinutes(i));
            await ((IAccountingJournalReversalProposalStoreV1)seed.Postings).CreateAsync(proposal, _ => Task.CompletedTask, Metadata(run));
        }
        // Schema-valid retained owner facts isolate the SQL reader. This is not a Sales cancellation E2E fixture.
        var first = PendingSourceVoucher(journals[0]); var second = PendingSourceVoucher(journals[1]);
        await SeedPendingSourceFacts(CompanyA, db =>
        {
            db.SourceDocumentVoucherDrafts.AddRange(first, second);
            db.SourceInvoiceCancellations.Add(Cancellation(first, CompanyA));
        });
        await SeedPendingSourceFacts(CompanyB, db => db.SourceInvoiceCancellations.Add(Cancellation(second, CompanyB)));
        var probe = new PendingSourceQueryProbe(); await using var scope = new ReversalApprovalScope(run, extra: probe);
        var source = new AccountingJournalReversalWorkInboxSourceV1(scope.Db, new(scope.Company, scope.User, scope.Authority));
        var context = new WorkInboxSourceContextV1(CompanyA, run.Actor.ToString("D"), run.Correlation, null);
        var result = await source.ListPendingAsync(context, 2, default);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome); Assert.Equal(CompanyA, result.CompanyId);
        Assert.True(result.HasMore); Assert.Equal(proposals.Take(2), result.Items!.Select(x => x.ItemId));
        Assert.Equal(run.Actor.ToString("D"), Assert.Single(result.Items![0].ExcludedDeciders!)); Assert.True(result.Items[0].Excludes(run.Actor.ToString("D")));
        Assert.Null(result.Items[1].ExcludedDeciders); // Another company's cancellation is not this company's KLN-D17 source.
        Assert.Equal(2, probe.Commands.Count);
        Assert.Single(probe.Commands.Where(x => x.Contains("JournalReversalProposals", StringComparison.Ordinal)));
        Assert.Single(probe.Commands.Where(x => x.Contains("SourceDocumentVoucherDrafts", StringComparison.Ordinal) && x.Contains("SourceInvoiceCancellations", StringComparison.Ordinal)));
        scope.AssertClean();
        probe.Commands.Clear(); scope.Authority.Read = () => probe.Commands.Count < 2;
        var denied = await source.ListPendingAsync(context, 2, default);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, denied.Outcome); Assert.Null(denied.Items); Assert.Equal(2, probe.Commands.Count);
        scope.AssertClean();

        AccountingSourceDocumentVoucherDraftRow PendingSourceVoucher(Guid journal) => new()
        {
            CompanyId = CompanyA, Id = journal, DocumentKind = "sales-invoice", SourceDocumentId = Guid.NewGuid(), InvoiceId = Guid.NewGuid(),
            FinalizedVersion = 1, SourceActorId = run.Actor, PartyId = Guid.NewGuid(), CurrencyId = Currency, DocumentDate = InSeptember.TransactionDate,
            PeriodId = period, MappingVersion = 1, Status = 2, JournalId = journal, ApprovedBy = run.Preparer,
            ApprovedAtUtc = RecordedAt, Version = 2, CreatedAtUtc = RecordedAt, UpdatedAtUtc = RecordedAt,
        };
        AccountingSourceInvoiceCancellationRow Cancellation(AccountingSourceDocumentVoucherDraftRow voucher, Guid company) => new()
        {
            CompanyId = company, CancellationId = Guid.NewGuid(), DocumentKind = voucher.DocumentKind, SourceDocumentId = voucher.SourceDocumentId,
            InvoiceId = voucher.InvoiceId, FinalizedVersion = voucher.FinalizedVersion, CancelledBy = run.Preparer, CancelledAtUtc = RecordedAt,
            Reason = "SENTETIK", RequestHash = new string('A', 64), CorrelationId = run.Correlation, RecordedAtUtc = RecordedAt,
        };
        async Task SeedPendingSourceFacts(Guid company, Action<AccountingDbContext> seed)
        {
            var binding = new CompanyContext(); binding.Set(company);
            await using var db = new AccountingDbContext(new DbContextOptionsBuilder<AccountingDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AccountingDbContext.Schema)).Options, binding);
            seed(db); await db.SaveChangesAsync();
        }
    }
    private sealed class PendingSourceQueryProbe : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken token = default)
        { Commands.Add(command.CommandText); return ValueTask.FromResult(result); }
    }
}
