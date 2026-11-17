using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Incoming_invoice_matching_reads_real_receipt_and_order_price_and_replays_the_frozen_policy()
    {
        await using var run = await Run.StartAsync();
        var receipt = await InvoiceRateReceiptAsync(run, new PurchaseOrderLinePrice(10m, AcceptanceUnit));
        var invoice = await StoreRateInvoiceAsync(run, receipt, 4m, 35m, finalize: false, recordedOnly: true);
        await using var scope = run.Store(CompanyA);
        var policy = new InvoiceMatchSettingsFixture();
        var service = InvoiceWorkflow(run, scope, new(_ => Task.CompletedTask), InvoiceMatchSource(scope, policy));
        var request = new PurchaseIncomingInvoiceMatchRequestV1("1",
            [new(invoice.Lines.Single().Id, receipt.OrderId, receipt.AcceptanceId, "4")]);
        var metadata = new PurchasingCommandMetadata("SENTETIK-match-owner", run.Correlation, null, AcceptanceTime);
        var result = await service.MatchAsync(invoice.Id, request, metadata);
        Assert.Equal(PurchaseIncomingInvoiceStatus.Matched, result.Invoice.Status);
        Assert.Empty(result.Invoice.Restore().Variances);
        Assert.Equal(40m, result.Invoice.Matches.Single().ExpectedNet);
        Assert.Equal(35m, result.Invoice.Matches.Single().ControlRate);
        Assert.Equal(policy.PolicyId, result.Invoice.Policy!.Id);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.InvoiceReceiptBindings WHERE InvoiceId=@id", ("@id", invoice.Id)));
        policy.Available = false;
        var replay = await service.MatchAsync(invoice.Id, request, metadata);
        Assert.True(replay.IsReplay);
        Assert.Equal(result.Invoice.Policy, replay.Invoice.Policy);
        Assert.Equal(1, policy.PolicyReads);
    }

    [SqlServerFact]
    public async Task Incoming_invoice_matching_missing_policy_rolls_back_and_another_invoice_cannot_reuse_billed_quantity()
    {
        await using var run = await Run.StartAsync();
        var receipt = await InvoiceRateReceiptAsync(run, new PurchaseOrderLinePrice(10m, AcceptanceUnit));
        var invoice = await StoreRateInvoiceAsync(run, receipt, 4m, 35m, finalize: false, recordedOnly: true);
        await using var scope = run.Store(CompanyA);
        var policy = new InvoiceMatchSettingsFixture { Available = false };
        var service = InvoiceWorkflow(run, scope, new(_ => Task.CompletedTask), InvoiceMatchSource(scope, policy));
        var request = new PurchaseIncomingInvoiceMatchRequestV1("1",
            [new(invoice.Lines.Single().Id, receipt.OrderId, receipt.AcceptanceId, "4")]);
        var metadata = new PurchasingCommandMetadata("SENTETIK-match-missing-policy", run.Correlation, null, AcceptanceTime);
        var missing = await Assert.ThrowsAsync<PurchasingServiceException>(() => service.MatchAsync(invoice.Id, request, metadata));
        Assert.Equal(503, missing.Status);
        Assert.Equal(PurchaseIncomingInvoiceStatus.Recorded, (await service.ReadAsync(invoice.Id)).Status);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.InvoiceReceiptBindings WHERE InvoiceId=@id", ("@id", invoice.Id)));
        policy.Available = true;
        await service.MatchAsync(invoice.Id, request, metadata);
        var second = await StoreRateInvoiceAsync(run, receipt, 4m, 35m, finalize: false, recordedOnly: true);
        var secondRequest = new PurchaseIncomingInvoiceMatchRequestV1("1",
            [new(second.Lines.Single().Id, receipt.OrderId, receipt.AcceptanceId, "4")]);
        var blocked = await Assert.ThrowsAsync<PurchasingServiceException>(() => service.MatchAsync(second.Id, secondRequest,
            metadata with { IdempotencyKey = "SENTETIK-match-no-double-bill" }));
        Assert.Equal(409, blocked.Status);
        Assert.Equal(PurchaseIncomingInvoice.SourceMismatch, blocked.Code);
        Assert.Equal(PurchaseIncomingInvoiceStatus.Recorded, (await service.ReadAsync(second.Id)).Status);
    }

    [SqlServerFact]
    public async Task Incoming_invoice_matching_unknown_price_and_cross_company_source_cannot_become_a_match()
    {
        await using var run = await Run.StartAsync();
        var receipt = await InvoiceRateReceiptAsync(run); // An actual unpriced order, never a guessed zero price.
        var invoice = await StoreRateInvoiceAsync(run, receipt, 4m, 35m, finalize: false, recordedOnly: true);
        await using var scope = run.Store(CompanyA);
        var service = InvoiceWorkflow(run, scope, new(_ => Task.CompletedTask), InvoiceMatchSource(scope, new()));
        var request = new PurchaseIncomingInvoiceMatchRequestV1("1",
            [new(invoice.Lines.Single().Id, receipt.OrderId, receipt.AcceptanceId, "4")]);
        var metadata = new PurchasingCommandMetadata("SENTETIK-match-no-price", run.Correlation, null, AcceptanceTime);
        Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => service.MatchAsync(invoice.Id, request, metadata))).Status);
        Assert.Equal(PurchaseIncomingInvoiceStatus.Recorded, (await service.ReadAsync(invoice.Id)).Status);
        await using var other = run.Store(CompanyB);
        Assert.Null(await ((IPurchasingIncomingInvoiceStoreV1)other.Store).FindIncomingInvoiceAsync(invoice.Id));
        var company = new CompanyContext(); company.Set(CompanyB);
        Assert.Null(await new PurchaseAcceptanceSource(other.Context, company).ReadAsync(receipt.OrderId, receipt.AcceptanceId));
    }

    private static PurchaseIncomingInvoiceMatchSource InvoiceMatchSource(StoreScope scope, InvoiceMatchSettingsFixture settings)
    {
        var company = new CompanyContext(); company.Set(CompanyA);
        return new(scope.Context, company, new PurchaseAcceptanceSource(scope.Context, company),
            new PurchaseOrderLineCostReadPort(scope.Context, company), settings, settings);
    }

    [SqlServerFact]
    public async Task Incoming_invoice_workspace_filters_company_and_shows_only_actual_unbilled_receipt_shares()
    {
        await using var run = await Run.StartAsync();
        var receipt = await InvoiceRateReceiptAsync(run, new PurchaseOrderLinePrice(10m, AcceptanceUnit));
        var first = await StoreRateInvoiceAsync(run, receipt, 2m, 35m, finalize: false);
        var next = await StoreRateInvoiceAsync(run, receipt, 2m, 35m, finalize: false, recordedOnly: true);
        await using var scope = run.Store(CompanyA);
        var company = new CompanyContext(); company.Set(CompanyA);
        var workflow = InvoiceWorkflow(run, scope, new(_ => Task.CompletedTask));
        var workspace = new PurchaseIncomingInvoiceWorkspace(scope.Context, company,
            new NumberTestUser { UserId = run.Actor }, new NumberTestAuthority(), workflow);
        var listed = await workspace.ListAsync(next.Source.OfficialNumber, 0);
        Assert.Equal(next.Id, Assert.Single(listed.Items).Id);
        var choices = await workspace.CandidatesAsync(next.Id, next.Lines.Single().Id, 0);
        Assert.Equal("2", Assert.Single(choices.Items).AvailableQuantity);
        Assert.Equal(receipt.AcceptanceId, choices.Items.Single().AcceptanceId);
        Assert.Empty((await workspace.CandidatesAsync(first.Id, first.Lines.Single().Id, 0)).Items);
        await using var other = run.Store(CompanyB);
        var otherCompany = new CompanyContext(); otherCompany.Set(CompanyB);
        // An empty tenant result never reads the company-A workflow or discloses a foreign invoice.
        var otherWorkspace = new PurchaseIncomingInvoiceWorkspace(other.Context, otherCompany,
            new NumberTestUser { UserId = run.Actor }, new NumberTestAuthority(), workflow);
        Assert.Empty((await otherWorkspace.ListAsync(next.Source.OfficialNumber, 0)).Items);
        var deniedWorkspace = new PurchaseIncomingInvoiceWorkspace(scope.Context, company,
            new NumberTestUser { UserId = run.Actor }, new NumberTestAuthority { Allowed = false }, workflow);
        Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() => deniedWorkspace.ListAsync(null, 0))).Status);
    }

    // Settings is a synthetic contract fixture; Purchasing reads, transaction, prices and quantity bindings are real SQL.
    // This does not claim end-to-end acceptance of the Settings provider.
    private sealed class InvoiceMatchSettingsFixture : ISettingsPurchaseInvoiceVariancePolicyProviderV1, ISettingsExchangeRateResolverV1
    {
        public Guid PolicyId { get; } = Guid.NewGuid();
        public bool Available { get; set; } = true;
        public int PolicyReads { get; private set; }
        public Task<SettingsPurchaseInvoiceVariancePolicyV1> GetAsync(Guid companyId, CancellationToken cancellationToken = default)
        {
            PolicyReads++;
            if (!Available) throw new SettingsPrerequisiteException(PurchaseInvoiceVariancePolicyCodesV1.NotPublished);
            return Task.FromResult(new SettingsPurchaseInvoiceVariancePolicyV1(companyId, PolicyId, 1, 0m, 0m));
        }
        public Task<ExchangeRateResolutionV1> ResolveAsync(DateOnly rateDay, Guid sourceCurrencyId, Guid targetCurrencyId,
            CancellationToken cancellationToken = default) => Task.FromResult(new ExchangeRateResolutionV1(rateDay,
                sourceCurrencyId, targetCurrencyId, "USD", ExchangeRateResolutionStatesV1.Resolved, "Tcmb", false,
                null, rateDay.AddDays(-1), "35", "SENTETIK-contract", Guid.NewGuid(), "1", null, null));
    }
}
