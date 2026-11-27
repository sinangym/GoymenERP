using GoymenERP.Api.WorkRuntime;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class WorkPurchasingRequestDecisionHostTests
{
    [Fact]
    public async Task Invoice_variance_purpose_forwards_only_native_approval_human_reason_and_original_receipt()
    {
        var run = new Run(invoiceVariance: true); run.Source.Pending = false;
        var preview = await run.Host.PreviewAsync(WorkDecisionHostV1.PurchaseInvoiceVariance, Item, default);
        Assert.True(preview.ApprovalReasonRequired); Assert.False(preview.RejectionSupported); Assert.False(preview.Pending);
        const string reason = "  SENTETIK gerçek fark onayı  ";
        var result = await run.Host.DecideAsync(WorkDecisionHostV1.PurchaseInvoiceVariance, Item, new("2", true, reason), "variance-key", default);
        Assert.Equal("Matched", result.State); Assert.True(result.Replayed); Assert.Equal("SENTETIK-original", result.OriginalCorrelationId);
        var call = Assert.Single(run.Calls);
        Assert.Equal(("ApproveVarianceAsync", Item), (call.Method, call.Id));
        Assert.Equal(new PurchaseIncomingInvoiceVarianceApprovalRequestV1("2", reason), call.Body);
        Assert.Equal(("variance-key", "SENTETIK-correlation"), (call.Metadata.IdempotencyKey, call.Metadata.CorrelationId));
        Assert.Contains(PurchaseIncomingInvoicePermissionNamesV1.ApproveVariance, run.Raw.Asked);
        Assert.Contains(PurchasingPermissionManifestV1.OrdersPriceRead, run.Raw.Asked);
        Assert.DoesNotContain(PurchaseIncomingInvoicePermissionNamesV1.Read, run.Raw.Asked);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("overlong")]
    public async Task Invoice_variance_purpose_preserves_native_missing_or_overlong_reason_validation(string? reason)
    {
        var run = new Run(invoiceVariance: true);
        if (reason == "overlong") reason = new string('x', 2001);
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.PurchaseInvoiceVariance,
            Item, new("2", true, reason), "invalid", default))).Status);
        Assert.Equal(reason, Assert.IsType<PurchaseIncomingInvoiceVarianceApprovalRequestV1>(Assert.Single(run.Calls).Body).Reason);
        Assert.False(run.NativeEffect);
    }

    [Fact]
    public async Task Invoice_variance_native_two_thousand_character_reason_is_forwarded_without_truncation()
    {
        var run = new Run(invoiceVariance: true); var reason = " " + new string('x', 2000) + " ";
        await run.Host.DecideAsync(WorkDecisionHostV1.PurchaseInvoiceVariance, Item, new("2", true, reason), "native-limit", default);
        Assert.Equal(reason, Assert.IsType<PurchaseIncomingInvoiceVarianceApprovalRequestV1>(Assert.Single(run.Calls).Body).Reason);
    }

    [Fact]
    public async Task Invoice_variance_reject_or_missing_owner_never_forwards_another_workflow()
    {
        var run = new Run(invoiceVariance: true);
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.PurchaseInvoiceVariance,
            Item, new("2", false, "SENTETIK ret"), "reject", default))).Status);
        Assert.Empty(run.Calls);
        run = new Run(invoiceVariance: true, ownerAvailable: false);
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.PurchaseInvoiceVariance,
            Item, new("2", true, "SENTETIK onay"), "missing", default))).Status);
        Assert.Empty(run.Calls);
    }

    [Fact]
    public async Task Invoice_variance_requires_its_existing_native_price_permission_and_fresh_delegation()
    {
        var run = new Run(invoiceVariance: true); run.Raw.Denied.Add(PurchasingPermissionManifestV1.OrdersPriceRead);
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(WorkDecisionHostV1.PurchaseInvoiceVariance, Item, default))).Status);
        Assert.Empty(run.Calls);
        run = new Run(invoiceVariance: true); run.BeforeFence = () => run.Delegations.Allowed = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.PurchaseInvoiceVariance,
            Item, new("2", true, "SENTETIK onay", Guid.NewGuid()), "late", default))).Status);
        Assert.False(run.NativeEffect);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Invoice_variance_foreign_native_result_is_not_success(bool foreignCompany)
    {
        var run = new Run(invoiceVariance: true);
        if (foreignCompany) run.ResultCompany = Guid.NewGuid(); else run.ResultId = Guid.NewGuid();
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.PurchaseInvoiceVariance,
            Item, new("2", true, "SENTETIK onay"), "foreign", default))).Status);
    }

    private static PurchaseIncomingInvoice VarianceTransportInvoice()
    {
        var currency = Guid.NewGuid(); var unit = Guid.NewGuid(); var line = Guid.NewGuid();
        var invoice = PurchaseIncomingInvoice.Record(Item, Company, Company, Company, currency, currency, 2, 2, null,
            new(Guid.NewGuid(), 1, new string('a', 64), "SENTETIK-FARK", Guid.NewGuid(), new(2026, 11, 27)), new(40m, 8m, 0m), null,
            [new(line, 1, Company, unit, 4m, "material", "resale", 20m, new(40m, 8m, 0m), null)],
            [new(Guid.NewGuid(), Guid.NewGuid(), 1, new(2026, 12, 27), 48m, null)], "SENTETIK-recorder", DateTimeOffset.UtcNow);
        invoice.Match(1, [new(Company, line, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, Company, Company,
            currency, unit, 4m, 30m, null, new(2026, 11, 27))], new(Guid.NewGuid(), 1, 0m, 0m), "SENTETIK-matcher");
        return invoice;
    }
}
