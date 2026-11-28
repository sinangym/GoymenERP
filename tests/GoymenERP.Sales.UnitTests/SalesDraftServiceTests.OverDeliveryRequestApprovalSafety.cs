using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Over_delivery_request_guard_is_monotonic_and_missing_boundary_refuses_before_native_IO(bool approve)
    {
        var world = new World(); var service = world.Service();
        Assert.Same(service, service.WithOverDeliveryRequestApprovalSafety());
        Assert.Same(service, service.WithOverDeliveryRequestApprovalSafety());
        var failure = await Assert.ThrowsAsync<SalesDraftServiceException>(() => approve
            ? service.ApproveOverDeliveryRequestAsync(Guid.NewGuid(), 1, new("1"), Metadata("over-request-safe"))
            : service.RejectOverDeliveryRequestAsync(Guid.NewGuid(), 1, new("1", "  SENTETIK ret  "), Metadata("over-request-safe")));
        Assert.Equal((503, "service_unavailable"), (failure.Status, failure.Code));
        Assert.Empty(world.Store.Envelopes); Assert.Empty(world.Store.Refusals); Assert.Empty(world.Store.Transitions);
        Assert.Equal((0, 0), (world.Accounts.Calls, world.Products.Calls));
    }

    [Theory]
    [InlineData("unauthenticated", 401)] [InlineData("denied", 403)]
    [InlineData("scoped", 403)] [InlineData("foreign-company", 403)]
    public async Task Over_delivery_request_guard_preserves_native_authority_before_missing_boundary(string fault, int status)
    {
        var world = new World();
        if (fault == "unauthenticated") world.User.Authenticated = false;
        if (fault == "denied") world.Authority.Denied.Add(SalesPermissionManifestV1.OrdersOverDeliveryApprove);
        if (fault == "scoped") world.Authority.CompanyWide = false;
        if (fault == "foreign-company") world.User.Company = Guid.NewGuid();
        var failure = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().WithOverDeliveryRequestApprovalSafety()
            .ApproveOverDeliveryRequestAsync(Guid.NewGuid(), 1, new("1"), Metadata("over-request-authority")));
        Assert.Equal(status, failure.Status); Assert.Empty(world.Store.Envelopes); Assert.Empty(world.Store.Refusals);
    }

    [Theory]
    [InlineData(true, "0")] [InlineData(false, "0")]
    [InlineData(true, "9223372036854775808")] [InlineData(false, "1.0")]
    public async Task Over_delivery_request_guard_keeps_native_version_validation_before_boundary_refusal(bool approve, string version)
    {
        var world = new World(); var service = world.Service().WithOverDeliveryRequestApprovalSafety();
        var failure = await Assert.ThrowsAsync<SalesDraftServiceException>(() => approve
            ? service.ApproveOverDeliveryRequestAsync(Guid.NewGuid(), 1, new(version), Metadata("over-request-shape"))
            : service.RejectOverDeliveryRequestAsync(Guid.NewGuid(), 1, new(version, "SENTETIK ret"), Metadata("over-request-shape")));
        Assert.Equal((400, "validation_failed"), (failure.Status, failure.Code));
        Assert.Empty(world.Store.Envelopes); Assert.Empty(world.Store.Refusals);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Over_delivery_request_guard_initial_cancellation_never_reads_authority_or_owners(bool approve)
    {
        var world = new World(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var service = world.Service().WithOverDeliveryRequestApprovalSafety();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => approve
            ? service.ApproveOverDeliveryRequestAsync(Guid.NewGuid(), 1, new("1"), Metadata("over-request-cancel"), cancellation.Token)
            : service.RejectOverDeliveryRequestAsync(Guid.NewGuid(), 1, new("1", "SENTETIK ret"), Metadata("over-request-cancel"), cancellation.Token));
        Assert.Equal(0, world.Authority.Calls); Assert.Empty(world.Store.Envelopes); Assert.Empty(world.Store.Refusals);
    }
}
