using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Quote_runtime_guard_is_monotonic_and_never_treats_a_synthetic_store_as_owned_SQL(bool approve)
    {
        var world = new World(); var core = world.Service();
        Assert.Same(core, core.WithQuoteApprovalSafety()); Assert.Same(core, core.WithQuoteApprovalSafety());
        var refusal = await Assert.ThrowsAsync<SalesDraftServiceException>(async () =>
        {
            if (approve) await core.ApproveExpiredQuoteConversionAsync(Guid.NewGuid(), new("1"), Metadata("quote-safe"));
            else await core.RejectQuoteConversionRequestAsync(Guid.NewGuid(), 1, new("1", "SENTETIK ret"), Metadata("quote-safe"));
        });
        Assert.Equal((503, "service_unavailable"), (refusal.Status, refusal.Code));
        Assert.Empty(world.Quotes!.Envelopes); Assert.Empty(world.Store.Envelopes);
        Assert.Equal((0, 0), (world.Accounts.Calls, world.Products.Calls));
    }

    [Theory]
    [InlineData("unauthenticated", 401)]
    [InlineData("denied", 403)]
    [InlineData("scoped", 403)]
    [InlineData("foreign-company", 403)]
    public async Task Quote_runtime_guard_preserves_native_authority_before_dependency_refusal(string fault, int status)
    {
        var world = new World();
        if (fault == "unauthenticated") world.User.Authenticated = false;
        if (fault == "denied") world.Authority.Denied.Add(SalesPermissionManifestV1.QuotesExpiredConversionApprove);
        if (fault == "scoped") world.Authority.CompanyWide = false;
        if (fault == "foreign-company") world.User.Company = Guid.NewGuid();
        var refusal = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().WithQuoteApprovalSafety()
            .ApproveExpiredQuoteConversionAsync(Guid.NewGuid(), new("1"), Metadata("quote-authority")));
        Assert.Equal(status, refusal.Status); Assert.Empty(world.Quotes!.Envelopes); Assert.Empty(world.Store.Envelopes);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("9223372036854775808")]
    [InlineData("1.0")]
    public async Task Quote_runtime_guard_keeps_native_version_shape_before_unconfigured_store(string version)
    {
        var world = new World();
        var refusal = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().WithQuoteApprovalSafety()
            .ApproveExpiredQuoteConversionAsync(Guid.NewGuid(), new(version), Metadata("quote-version")));
        Assert.Equal((400, "validation_failed"), (refusal.Status, refusal.Code)); Assert.Empty(world.Quotes!.Envelopes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Quote_runtime_guard_honors_initial_cancellation_without_authority_or_store(bool approve)
    {
        var world = new World(); var core = world.Service().WithQuoteApprovalSafety(); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            if (approve) await core.ApproveExpiredQuoteConversionAsync(Guid.NewGuid(), new("1"), Metadata("quote-cancel"), cancelled.Token);
            else await core.RejectQuoteConversionRequestAsync(Guid.NewGuid(), 1, new("1", "SENTETIK ret"), Metadata("quote-cancel"), cancelled.Token);
        });
        Assert.Equal(0, world.Authority.Calls); Assert.Empty(world.Quotes!.Envelopes); Assert.Empty(world.Store.Envelopes);
    }
}
