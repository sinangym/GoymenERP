using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesOrderUserCloseSqlServerRelationalAcceptanceTests
{
    // SAT-D478/479: real Finance admission/allocation/release/read and native Sales close. This reuses the existing
    // explicit synthetic Logistics/Inventory/Manufacturing and recorded-receipt seed; it is NOT full physical E2E.
    [SqlServerFact]
    public async Task Actual_allocation_release_after_ready_preview_blocks_native_close_until_reallocation_and_replays_once()
    {
        var downstream = new SyntheticClosureDownstream();
        await using var run = await Run.StartAsync(readySeam: true, configure: downstream.Configure);
        var draft = await run.ApprovedDraftAsync(CompanyA, "axes-settlement-change", "6");
        await run.ShippedInFullAsync(CompanyA, draft, "axes-settlement-change"); downstream.Draft = draft;
        var invoice = await FinalizeClosureInvoice(run, downstream);
        await AdmitAndAllocateClosureInvoice(run, invoice, leaveSecondOpen: false);
        await PrepareClosurePurchasing(run, draft, createRequest: false);
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersClose);
        using var client = run.Client(subject, CompanyA);
        var statusPath = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/status";
        var closePath = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/close";
        var ready = await run.JsonAsync(client, statusPath);
        Assert.True(ready.GetProperty("readyToClose").GetBoolean());
        Assert.Equal(SalesDraftStatusAxisStatesV1.Completed, ready.GetProperty("financialClosure").GetProperty("state").GetString());

        await using var finance = new SettlementChangeFinance(run);
        var original = await finance.Db.Allocations.AsNoTracking().Where(x => x.CompanyId == CompanyA && x.Status == FinanceAllocationStatuses.Active
            && finance.Db.OpenItems.Any(item => item.CompanyId == CompanyA && item.Id == x.OpenItemId && item.SourceId == invoice.Id))
            .OrderBy(x => x.Id).FirstAsync();
        var itemBefore = await finance.Db.OpenItems.AsNoTracking().SingleAsync(x => x.Id == original.OpenItemId);
        var released = await finance.Allocator.ReleaseAsync(finance.Context, new("SENTETIK-close-release"),
            new(original.Id, Text(original.Version), Text(itemBefore.Version), "SENTETIK release after ready preview"), default);
        Assert.Equal(CommandOutcome.Succeeded, released.Outcome); Assert.False(released.IsReplay);
        Assert.Equal(FinanceAllocationStatus.Released, released.Resource!.Status); finance.Db.ChangeTracker.Clear();
        Assert.Equal(FinanceSalesInvoiceSettlementStatusV1.Open, await finance.Settlement(invoice.Id));
        var financeAfterRelease = await finance.Effects(invoice.Id);
        var salesBeforeRefusal = await run.EffectsAsync(draft.Id);
        // Do not refresh the ready page first: the native command must reject its now-stale readiness itself.
        using (var refused = await client.SendAsync(Close(closePath, "SENTETIK-close-after-release", "4")))
        {
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(SalesDraftProblemCodesV1.OrderNotReadyToClose, problem.GetProperty("code").GetString());
            Assert.Equal("readyToClose", problem.GetProperty("errors")[0].GetProperty("field").GetString());
        }
        var salesAfterRefusal = await run.EffectsAsync(draft.Id);
        Assert.Equal(salesBeforeRefusal with { SalesAttempts = salesBeforeRefusal.SalesAttempts + 1 }, salesAfterRefusal);
        Assert.Equal(financeAfterRelease, await finance.Effects(invoice.Id));
        Assert.Equal(FinanceSalesInvoiceSettlementStatusV1.Open, await finance.Settlement(invoice.Id));
        Assert.Equal(0, await ClosureAuditCount(run, draft.Id));
        var open = await run.JsonAsync(client, statusPath);
        Assert.False(open.GetProperty("readyToClose").GetBoolean());
        Assert.Equal(SalesDraftStatusAxisStatesV1.Partial, open.GetProperty("financialClosure").GetProperty("state").GetString());
        Assert.Equal(SalesDraftStatusAxisStatesV1.Completed, open.GetProperty("source").GetProperty("state").GetString());
        Assert.Equal(SalesDraftStatusAxisStatesV1.Completed, open.GetProperty("invoicing").GetProperty("state").GetString());

        var itemAfter = await finance.Db.OpenItems.AsNoTracking().SingleAsync(x => x.Id == original.OpenItemId);
        var receiptSource = await finance.Db.ReceiptDrafts.AsNoTracking().SingleAsync(x => x.Id == original.SourceId);
        var allocated = await finance.Allocator.CreateAsync(finance.Context, new("SENTETIK-close-reallocate"),
            new(Guid.NewGuid(), original.SourceId, Text(receiptSource.Version), original.OpenItemId, Text(itemAfter.Version), original.AmountText,
                "SENTETIK restore allocation after preview"), default);
        Assert.Equal(CommandOutcome.Succeeded, allocated.Outcome); Assert.False(allocated.IsReplay);
        Assert.Equal(FinanceAllocationStatus.Active, allocated.Resource!.Status); Assert.NotEqual(original.Id, allocated.Resource.Id);
        finance.Db.ChangeTracker.Clear();
        Assert.Equal(FinanceSalesInvoiceSettlementStatusV1.Settled, await finance.Settlement(invoice.Id));
        var financeAfterRestore = await finance.Effects(invoice.Id);
        Assert.Equal(financeAfterRelease with { Allocations = financeAfterRelease.Allocations + 1, Active = financeAfterRelease.Active + 1,
            ClosedItems = financeAfterRelease.ClosedItems + 1, Commands = financeAfterRelease.Commands + 1 }, financeAfterRestore);
        Assert.True((await run.JsonAsync(client, statusPath)).GetProperty("readyToClose").GetBoolean());
        // A terminal failed key stays failed even when the world later becomes ready. Only a new command may close.
        using (var old = await client.SendAsync(Close(closePath, "SENTETIK-close-after-release", "4")))
        {
            Assert.Equal(HttpStatusCode.Conflict, old.StatusCode); Assert.Equal("true", Assert.Single(old.Headers.GetValues("Idempotency-Replayed")));
        }
        var beforeClose = await run.EffectsAsync(draft.Id);
        Assert.Equal(salesAfterRefusal with { AuditEvents = salesAfterRefusal.AuditEvents + 1 }, beforeClose); // Only the Finance reallocation audit.
        string originalCorrelation;
        using (var request = Close(closePath, "SENTETIK-close-after-restore", "4"))
        {
            originalCorrelation = Assert.Single(request.Headers.GetValues("X-Correlation-Id"));
            using var closed = await client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
            Assert.False(closed.Headers.Contains("Idempotency-Replayed"));
        }
        var afterClose = await run.EffectsAsync(draft.Id);
        Assert.Equal(beforeClose with { SalesAttempts = beforeClose.SalesAttempts + 1, AuditEvents = beforeClose.AuditEvents + 1,
            UserClosures = beforeClose.UserClosures + 1, DraftVersion = 5 }, afterClose);
        Assert.Equal(1, await ClosureAuditCount(run, draft.Id));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT State FROM sales.Drafts WHERE Id=@id", ("@id", draft.Id)));
        Assert.Equal(financeAfterRestore, await finance.Effects(invoice.Id));
        Assert.Equal(FinanceSalesInvoiceSettlementStatusV1.Settled, await finance.Settlement(invoice.Id));
        using (var replay = await client.SendAsync(Close(closePath, "SENTETIK-close-after-restore", "4")))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(originalCorrelation, Assert.Single(replay.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }
        Assert.Equal(afterClose, await run.EffectsAsync(draft.Id)); Assert.Equal(financeAfterRestore, await finance.Effects(invoice.Id));
        Assert.Equal(FinanceSalesInvoiceSettlementStatusV1.Settled, await finance.Settlement(invoice.Id));
        Assert.Equal(1, await ClosureAuditCount(run, draft.Id));
    }
    private static string Text(long version) => version.ToString(CultureInfo.InvariantCulture);
    private static Task<int> ClosureAuditCount(Run run, Guid draftId) => run.ScalarAsync<int>(
        "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'sales.order-draft.closed.v1' AND EntityId=@id "
        + "AND PermissionCode=N'sales.orders.close' AND EntityVersionBefore=4 AND EntityVersionAfter=5", ("@id", draftId.ToString("D")));
    private sealed record SettlementChangeEffects(int Allocations, int Active, int Released, int ClosedItems, int Commands,
        int Receipts, int MoneyEvents, int OpenItems, int Admissions, int Finalizations);
    private sealed class SettlementChangeFinance : IAsyncDisposable
    {
        private readonly Run ownerRun;
        internal FinanceDbContext Db { get; }
        internal FinanceContext Context { get; }
        internal FinanceAllocationService Allocator { get; }
        internal SettlementChangeFinance(Run run)
        {
            ownerRun = run;
            var company = new CompanyContext(); company.Set(CompanyA); var audit = new AuditEventCollector();
            var user = new ClosureOwnerUser(run.Actor); Context = new(CompanyA, run.Actor, run.Correlation, null);
            Db = new(new DbContextOptionsBuilder<FinanceDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(audit, company)).Options, company);
            Allocator = new(Db, company, user, new ClosureFinancePermissions(), audit,
                new("SENTETIK-allocation", ["SENTETIK"], "Web"), TimeProvider.System);
        }
        internal async Task<FinanceSalesInvoiceSettlementStatusV1> Settlement(Guid invoiceId)
        {
            var company = new CompanyContext(); company.Set(CompanyA);
            var answer = await new FinanceSalesInvoiceSettlementReaderV1(Db, company, new ClosureOwnerUser(Context.ActorId))
                .ReadAsync(Context, [invoiceId], _ => Task.CompletedTask);
            Assert.Equal(ReadOutcome.Found, answer.Outcome); return Assert.Single(answer.Resource!.Invoices).Status;
        }
        internal async Task<SettlementChangeEffects> Effects(Guid invoiceId) => new(
            await Db.Allocations.CountAsync(), await Db.Allocations.CountAsync(x => x.Status == FinanceAllocationStatuses.Active),
            await Db.Allocations.CountAsync(x => x.Status == FinanceAllocationStatuses.Released),
            await Db.OpenItems.CountAsync(x => x.SourceId == invoiceId && x.Status == FinanceOpenItemStatuses.Closed),
            await Db.Commands.CountAsync(), await Db.ReceiptDrafts.CountAsync(), await Db.MoneyEvents.CountAsync(),
            await Db.OpenItems.CountAsync(), await Db.OpenItemSourceBatches.CountAsync(),
            await ownerRun.ScalarAsync<int>("SELECT COUNT(*) FROM sales.InvoiceFinalizations WHERE InvoiceId=@id AND FinalizedVersion=4 AND FinanceStatus=2", ("@id", invoiceId)));
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
