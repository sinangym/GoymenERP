using System.Data.Common;
using System.Globalization;
using GoymenERP.Api.PurchasingRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D306/D392/D393/D396: genuine Purchasing invoice, immutable match and acceptance, genuine Inventory receipt and actual
/// PurchaseGoodsReceiptHost read. Only the current user identity and already-authorized seed metadata are synthetic. NOT_RUN.
/// </summary>
public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Supplier_return_invoice_cost_uses_official_line_net_and_original_measured_kg_without_writes_or_tax_allocation()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedReturnInvoiceCostReceiptAsync(run, 5m);
        await StoreReturnCostInvoiceAsync(run, receipt, 4m, 80m);
        var before = await ReturnCostSourceCountsAsync(run);
        var answer = await ReadReturnInvoiceCostAsync(run, CompanyA, CompanyA, receipt.Match.AcceptanceId);
        Assert.Equal(16m, answer.UnitCostTryPerKg); // 80 net / actual 5kg, never purchase quantity 4 or payable 81.
        Assert.Equal(before, await ReturnCostSourceCountsAsync(run));
        Assert.Null((await ReadReturnInvoiceCostAsync(run, CompanyB, CompanyB, receipt.Match.AcceptanceId)).UnitCostTryPerKg);
        Assert.Null((await ReadReturnInvoiceCostAsync(run, CompanyA, CompanyA, Guid.NewGuid())).UnitCostTryPerKg);
        var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            ReadReturnInvoiceCostAsync(run, CompanyB, CompanyA, receipt.Match.AcceptanceId));
        Assert.Equal(403, denied.Status);
    }

    [SqlServerFact]
    public async Task Supplier_return_invoice_cost_uses_frozen_foreign_line_TL_amount_before_separate_Finance_ack()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedReturnInvoiceCostReceiptAsync(run, 5m, foreign: true);
        var invoice = await StoreReturnCostInvoiceAsync(run, receipt, 4m, 40m);
        Assert.Equal(280m, (await ReadReturnInvoiceCostAsync(run, CompanyA, CompanyA, receipt.Match.AcceptanceId)).UnitCostTryPerKg);
        await using var scope = run.Store(CompanyA);
        var store = (IPurchasingIncomingInvoiceStoreV1)scope.Store;
        var current = (await store.FindIncomingInvoiceAsync(invoice.Id))!;
        var key = "return-cost-finance-ack-" + invoice.Id.ToString("N");
        await store.MutateIncomingInvoiceAsync(invoice.Id, PurchaseIncomingInvoiceCommandsV1.AcknowledgeFinance,
            run.Envelope(key), Hash(key), [], (value, _) =>
            {
                value.AcknowledgeFinance(current.Version, current.AdmissionId!.Value, AcceptanceTime.AddMinutes(1));
                return Task.CompletedTask;
            }, _ => Task.CompletedTask);
        Assert.Equal(280m, (await ReadReturnInvoiceCostAsync(run, CompanyA, CompanyA, receipt.Match.AcceptanceId)).UnitCostTryPerKg);
    }

    [SqlServerFact]
    public async Task Supplier_return_missing_pending_partial_and_nonunique_invoice_cost_use_existing_manual_exception_path()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedReturnInvoiceCostReceiptAsync(run, 5m);
        Assert.Null((await ReadReturnInvoiceCostAsync(run, CompanyA, CompanyA, receipt.Match.AcceptanceId)).UnitCostTryPerKg);
        await StoreReturnCostInvoiceAsync(run, receipt, 2m, 20m, finalize: false);
        Assert.Null((await ReadReturnInvoiceCostAsync(run, CompanyA, CompanyA, receipt.Match.AcceptanceId)).UnitCostTryPerKg);

        var split = await SeedReturnInvoiceCostReceiptAsync(run, 5m);
        await StoreReturnCostInvoiceAsync(run, split, 2m, 20m);
        Assert.Null((await ReadReturnInvoiceCostAsync(run, CompanyA, CompanyA, split.Match.AcceptanceId)).UnitCostTryPerKg);
        await StoreReturnCostInvoiceAsync(run, split, 2m, 30m);
        // Two different official prices do not silently select the first/last or establish a new weighted-price policy.
        Assert.Null((await ReadReturnInvoiceCostAsync(run, CompanyA, CompanyA, split.Match.AcceptanceId)).UnitCostTryPerKg);

        var shared = await SeedReturnInvoiceCostReceiptAsync(run, 5m);
        var other = await SeedReturnInvoiceCostReceiptAsync(run, 7m);
        await StoreReturnCostInvoiceAsync(run, shared, 4m, 80m, extraAcceptance: other);
        // Even full receipt coverage is insufficient when one official net amount also belongs to another acceptance.
        Assert.Null((await ReadReturnInvoiceCostAsync(run, CompanyA, CompanyA, shared.Match.AcceptanceId)).UnitCostTryPerKg);
    }

    [SqlServerFact]
    public async Task Supplier_return_proven_invoice_cost_outside_exact_four_decimal_contract_is_503_never_manual_or_rounded()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedReturnInvoiceCostReceiptAsync(run, 3m);
        await StoreReturnCostInvoiceAsync(run, receipt, 4m, 1m);
        var error = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            ReadReturnInvoiceCostAsync(run, CompanyA, CompanyA, receipt.Match.AcceptanceId));
        Assert.Equal((503, PurchaseSupplierReturnCodesV1.InvoiceCostInvalid), (error.Status, error.Code));
    }

    [SqlServerFact]
    public async Task Supplier_return_invoice_corruption_or_missing_physical_proof_is_503_without_replacement_price()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedReturnInvoiceCostReceiptAsync(run, 5m);
        var invoice = await StoreReturnCostInvoiceAsync(run, receipt, 4m, 80m);
        await run.ExecuteAsync("UPDATE purchasing.IncomingInvoiceRevisions SET SnapshotJson=JSON_MODIFY(SnapshotJson,'$.companyId',@other) WHERE InvoiceId=@id AND Status=3",
            ("@other", CompanyB.ToString("D")), ("@id", invoice.Id));
        var error = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            ReadReturnInvoiceCostAsync(run, CompanyA, CompanyA, receipt.Match.AcceptanceId));
        Assert.Equal((503, PurchaseSupplierReturnCodesV1.InvoiceCostInvalid), (error.Status, error.Code));

        var version = await SeedReturnInvoiceCostReceiptAsync(run, 5m);
        var versionInvoice = await StoreReturnCostInvoiceAsync(run, version, 4m, 80m);
        await run.ExecuteAsync("UPDATE purchasing.IncomingInvoiceRevisions SET SnapshotJson=JSON_MODIFY(SnapshotJson,'$.matches[0].orderVersion',999) WHERE InvoiceId=@id AND Status=3",
            ("@id", versionInvoice.Id));
        var versionError = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            ReadReturnInvoiceCostAsync(run, CompanyA, CompanyA, version.Match.AcceptanceId));
        Assert.Equal((503, PurchaseSupplierReturnCodesV1.InvoiceCostInvalid), (versionError.Status, versionError.Code));

        var missing = await SeedReturnInvoiceCostReceiptAsync(run, 5m);
        await StoreReturnCostInvoiceAsync(run, missing, 4m, 80m);
        // Deliberately corrupt the real Inventory owner proof. No fake reader supplies a guessed stock mass.
        await run.ExecuteAsync("UPDATE inventory.Labels SET RequiredDocumentsJson='{}' WHERE CompanyId=@company AND Id=@label",
            ("@company", CompanyA), ("@label", missing.Stock.LabelId));
        var physical = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            ReadReturnInvoiceCostAsync(run, CompanyA, CompanyA, missing.Match.AcceptanceId));
        Assert.Equal((503, PurchaseSupplierReturnCodesV1.InvoiceCostInvalid), (physical.Status, physical.Code));
    }

    [SqlServerFact]
    public async Task Supplier_return_invoice_cost_refreshes_actor_company_and_membership_after_await_and_refuses_separate_connection_in_borrowed_tx()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedReturnInvoiceCostReceiptAsync(run, 5m);
        await StoreReturnCostInvoiceAsync(run, receipt, 4m, 80m);
        foreach (var loss in new[] { "actor", "company", "membership" })
        {
            var company = new CompanyContext(); company.Set(CompanyA);
            var user = new ReturnCostUser(run.Actor, CompanyA);
            var changed = false;
            var fault = new ReturnCostAuthorityLossInterceptor(() =>
            {
                changed = true;
                if (loss == "actor") user.UserId = Guid.NewGuid().ToString("D");
                else if (loss == "company") company.Set(CompanyB);
                else user.Allowed = false;
            });
            var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
                ReadReturnInvoiceCostAsync(run, CompanyA, CompanyA, receipt.Match.AcceptanceId, company, user, fault));
            Assert.True(changed);
            Assert.Equal((403, "forbidden"), (denied.Status, denied.Code));
        }

        await using var scope = run.Factory.Services.CreateAsyncScope();
        var ownerCompany = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); ownerCompany.Set(CompanyA);
        var db = scope.ServiceProvider.GetRequiredService<PurchasingDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        var reader = new PurchaseSupplierReturnInvoiceCostReadPort(db, ownerCompany, new ReturnCostUser(run.Actor, CompanyA),
            scope.ServiceProvider.GetRequiredService<IPurchaseGoodsReceiptReadsV1>());
        var unavailable = await Assert.ThrowsAsync<PurchasingServiceException>(() => reader.ReadAsync(CompanyA, receipt.Match.AcceptanceId, default));
        Assert.Equal((503, PurchaseSupplierReturnCodesV1.InvoiceCostInvalid), (unavailable.Status, unavailable.Code));
        Assert.NotNull(tx.GetDbTransaction().Connection);
        await tx.RollbackAsync();
    }

    private static async Task<ReturnCostReceipt> SeedReturnInvoiceCostReceiptAsync(Run run, decimal measuredKg, bool foreign = false)
    {
        var currency = foreign ? SettingsCurrencyIdentitiesV1.UnitedStatesDollar : SettingsCurrencyIdentitiesV1.TurkishLira;
        var (order, line, version) = await InvoiceCostRateOrderAsync(run,
            PurchaseOrderMoney.Of(currency, foreign ? "USD" : "TRY", InvoiceCostRateProfile, 1, 2), Day);
        var company = new CompanyContext(); company.Set(CompanyA);
        var actor = new ReturnCostUser(run.Actor, CompanyA);
        await using (var inventory = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.Schema))
            .Options, company)) await inventory.Database.MigrateAsync();
        var acceptance = Guid.NewGuid();
        var location = await run.ScalarAsync<Guid>("SELECT TOP(1) Id FROM organization.Locations WHERE CompanyId=@company ORDER BY Id", ("@company", CompanyA));
        var key = "SENTETIK-return-cost-accept-" + acceptance.ToString("N");
        InventoryGoodsReceiptRequestV1 stock;
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = (DbTransaction)await connection.BeginTransactionAsync();
            await using var purchase = await PurchaseAcceptanceParticipant.EnlistAsync(connection, transaction, CompanyA);
            var accepted = await purchase.StageAsync(AcceptanceRequest(new AcceptanceSeed(order, line, AcceptanceUnit, version), acceptance, "4"),
                run.Envelope(key));
            stock = new(CompanyA, acceptance, order, line, accepted.PurchaseOrderVersion, AcceptanceProduct, "product-v1", AcceptanceUnit,
                Guid.NewGuid(), Guid.NewGuid(), "SENTETIK-RET-" + acceptance.ToString("N"), null, null, [], StockPartyKind.Company, CompanyA,
                StockPartyKind.Company, CompanyA, location, BlockState.Free, Guid.NewGuid(), measuredKg.ToString("0.###", CultureInfo.InvariantCulture), AcceptanceTime);
            await using var physical = await InventoryGoodsReceiptParticipant.EnlistAsync(connection, transaction, CompanyA,
                new ApprovedSettingsPrerequisiteProvider(company, actor));
            await physical.StageAsync(stock, new(run.Actor, "purchasing.goods-receipt.record", ["SENTETIK-PURCHASING"],
                run.Correlation, null, key, "Integration"));
            await transaction.CommitAsync();
        }
        return new(new(CompanyA, Guid.Empty, acceptance, order, line, long.Parse(stock.PurchaseOrderVersionAfter, CultureInfo.InvariantCulture),
            AcceptanceSupplier, AcceptanceAccount, currency, AcceptanceUnit, 4m, 40m, foreign ? 35m : null, Day), stock);
    }

    private static async Task<PurchaseIncomingInvoice> StoreReturnCostInvoiceAsync(Run run, ReturnCostReceipt receipt,
        decimal quantity, decimal net, bool finalize = true, ReturnCostReceipt? extraAcceptance = null)
    {
        var id = Guid.NewGuid(); var lineId = Guid.NewGuid();
        var foreign = receipt.Match.CurrencyId != SettingsCurrencyIdentitiesV1.TurkishLira;
        var amounts = new PurchaseInvoiceAmounts(net, 2m, 1m);
        var functional = foreign ? new PurchaseInvoiceAmounts(net * 35m, 70m, 35m) : null;
        var invoice = PurchaseIncomingInvoice.Record(id, CompanyA, AcceptanceSupplier, AcceptanceAccount,
            receipt.Match.CurrencyId, SettingsCurrencyIdentitiesV1.TurkishLira, 2, 2, foreign ? 35m : null,
            new(Guid.NewGuid(), 1, new string('a', 64), "SENTETIK-RETURN-" + id.ToString("N"), Guid.NewGuid(), Day), amounts, functional,
            [new(lineId, 1, AcceptanceProduct, AcceptanceUnit, quantity + (extraAcceptance is null ? 0m : 4m), "material", "resale", 20m, amounts, functional)],
            [new(Guid.NewGuid(), Guid.NewGuid(), 1, Day.AddDays(30), amounts.Payable, functional?.Payable)], run.Actor, AcceptanceTime);
        await using var scope = run.Store(CompanyA);
        var store = (IPurchasingIncomingInvoiceStoreV1)scope.Store;
        var key = "return-cost-record-" + id.ToString("N");
        await store.RecordIncomingInvoiceAsync(invoice, run.Envelope(key), Hash(key), _ => Task.CompletedTask);
        key = "return-cost-match-" + id.ToString("N");
        var matches = new List<PurchaseInvoiceReceiptMatch> { receipt.Match with
            { InvoiceLineId = lineId, Quantity = quantity, ExpectedNet = extraAcceptance is null ? net : net / 2m } };
        if (extraAcceptance is not null) matches.Add(extraAcceptance.Match with { InvoiceLineId = lineId, ExpectedNet = net / 2m });
        await store.MutateIncomingInvoiceAsync(id, PurchaseIncomingInvoiceCommandsV1.Match, run.Envelope(key), Hash(key), matches.Select(x => x.OrderId).ToArray(),
            (value, _) => { value.Match(1, matches,
                new(Guid.NewGuid(), 1, 0m, 0m), run.Actor); return Task.CompletedTask; }, _ => Task.CompletedTask);
        if (finalize)
        {
            key = "return-cost-finalize-" + id.ToString("N");
            await store.MutateIncomingInvoiceAsync(id, PurchaseIncomingInvoiceCommandsV1.Finalize, run.Envelope(key), Hash(key), [],
                (value, _) => { value.Finalize(2, Guid.NewGuid(), run.Actor, AcceptanceTime); return Task.CompletedTask; }, _ => Task.CompletedTask);
        }
        return invoice;
    }

    private static async Task<PurchaseSupplierReturnInvoiceCostV1> ReadReturnInvoiceCostAsync(Run run, Guid contextCompany, Guid askedCompany,
        Guid acceptanceId, CompanyContext? explicitCompany = null, ReturnCostUser? explicitUser = null, IInterceptor? interceptor = null)
    {
        await using var scope = run.Factory.Services.CreateAsyncScope();
        var company = explicitCompany ?? new CompanyContext(); if (explicitCompany is null) company.Set(contextCompany);
        var builder = new DbContextOptionsBuilder<PurchasingDbContext>().UseSqlServer(run.Factory.DatabaseConnectionString);
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        await using var database = new PurchasingDbContext(builder.Options, company);
        await using var inventory = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString).Options, company);
        var physical = new PurchaseGoodsReceiptHost(database, scope.ServiceProvider.GetRequiredService<IOrganizationLocationCandidatesV1>(),
            scope.ServiceProvider.GetRequiredService<IOrganizationLocationStatusReaderV1>(), new PurchaseAcceptanceSource(database, company),
            new InventoryGoodsReceiptSourceService(inventory, company), scope.ServiceProvider, TimeProvider.System);
        var reader = new PurchaseSupplierReturnInvoiceCostReadPort(database, company,
            explicitUser ?? new ReturnCostUser(run.Actor, contextCompany), physical);
        return await reader.ReadAsync(askedCompany, acceptanceId, default);
    }

    private static async Task<(int InvoiceAttempts, int Audit, int Movements, long OrderVersions)> ReturnCostSourceCountsAsync(Run run) => (
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.IncomingInvoiceAttempts"),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents"),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements"),
        await run.ScalarAsync<long>("SELECT SUM(Version) FROM purchasing.PurchaseOrders"));

    private sealed record ReturnCostReceipt(PurchaseInvoiceReceiptMatch Match, InventoryGoodsReceiptRequestV1 Stock);
    private sealed class ReturnCostUser(string actor, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId { get; set; } = actor;
        public bool Allowed { get; set; } = true;
        public IReadOnlySet<Guid> AllowedCompanyIds => Allowed ? new HashSet<Guid> { company } : new HashSet<Guid>();
        public bool CanAccessCompany(Guid companyId) => Allowed && company == companyId;
    }
    private sealed class ReturnCostAuthorityLossInterceptor(Action loseAuthority) : DbCommandInterceptor
    {
        private bool fired;
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!fired) { fired = true; loseAuthority(); }
            return ValueTask.FromResult(result);
        }
    }
}
