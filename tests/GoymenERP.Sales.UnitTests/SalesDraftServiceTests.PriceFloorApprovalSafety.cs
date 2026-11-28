using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    private static SalesDraftPriceFloorExceptionApprovalRequest FloorSafetyRequest(string version = "1") =>
        new(version, [new(Guid.NewGuid(), "SENTETIK gerekçe")]);

    [Fact]
    public async Task Floor_runtime_composition_is_monotonic_and_refuses_an_unprotected_store_before_native_IO()
    {
        var world = new World(); world.Bindings = world.Bindings with { PriceFloorExceptionApprovePermission = SchedulePermission };
        var service = world.Service(); Assert.Same(service, service.WithPriceFloorApprovalSafety()); Assert.Same(service, service.WithPriceFloorApprovalSafety());
        var failure = await Assert.ThrowsAsync<SalesDraftServiceException>(() => service.ApprovePriceFloorExceptionsAsync(
            Guid.NewGuid(), FloorSafetyRequest(), Metadata("SENTETIK-floor-guard")));
        Assert.Equal((503, "service_unavailable"), (failure.Status, failure.Code));
        Assert.Empty(world.Store.Transitions); Assert.Empty(world.Store.Envelopes); Assert.Equal(0, world.Store.Attempts); Assert.Empty(world.Store.Refusals);
    }

    [Theory]
    [InlineData("unauthenticated", 401)]
    [InlineData("denied", 403)]
    [InlineData("scoped", 403)]
    [InlineData("foreign-company", 403)]
    public async Task Floor_runtime_composition_preserves_native_authority_precedence(string fault, int status)
    {
        var world = new World(); world.Bindings = world.Bindings with { PriceFloorExceptionApprovePermission = SchedulePermission };
        if (fault == "unauthenticated") world.User.Authenticated = false;
        if (fault == "denied") world.Authority.Granted = false;
        if (fault == "scoped") world.Authority.CompanyWide = false;
        if (fault == "foreign-company") world.User.Company = Guid.NewGuid();
        var failure = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().WithPriceFloorApprovalSafety()
            .ApprovePriceFloorExceptionsAsync(Guid.NewGuid(), FloorSafetyRequest(), Metadata("SENTETIK-floor-authority")));
        Assert.Equal(status, failure.Status); Assert.Empty(world.Store.Transitions); Assert.Empty(world.Store.Refusals);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("9223372036854775808")]
    [InlineData("1.0")]
    public async Task Floor_runtime_composition_preserves_native_authorized_version_validation(string version)
    {
        var world = new World(); world.Bindings = world.Bindings with { PriceFloorExceptionApprovePermission = SchedulePermission };
        var failure = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().WithPriceFloorApprovalSafety()
            .ApprovePriceFloorExceptionsAsync(Guid.NewGuid(), FloorSafetyRequest(version), Metadata("SENTETIK-floor-version")));
        Assert.Equal((400, "validation_failed"), (failure.Status, failure.Code));
        Assert.Equal("expectedVersion", Assert.Single(failure.Errors).Field); Assert.Empty(world.Store.Transitions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Floor_runtime_composition_preserves_native_missing_and_null_line_shape_errors(bool nullEntry)
    {
        var world = new World(); world.Bindings = world.Bindings with { PriceFloorExceptionApprovePermission = SchedulePermission };
        var request = new SalesDraftPriceFloorExceptionApprovalRequest("1", nullEntry ? [null!] : null!);
        var failure = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().WithPriceFloorApprovalSafety()
            .ApprovePriceFloorExceptionsAsync(Guid.NewGuid(), request, Metadata("SENTETIK-floor-lines")));
        Assert.Equal((400, "validation_failed"), (failure.Status, failure.Code));
        Assert.Equal("lines", Assert.Single(failure.Errors).Field); Assert.Empty(world.Store.Transitions);
    }

    [Fact]
    public async Task Floor_runtime_composition_honors_initial_cancellation_without_authority_or_native_mutation()
    {
        var world = new World(); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.Service().WithPriceFloorApprovalSafety()
            .ApprovePriceFloorExceptionsAsync(Guid.NewGuid(), FloorSafetyRequest(), Metadata("SENTETIK-floor-cancel"), cancelled.Token));
        Assert.Equal(0, world.Authority.Calls); Assert.Empty(world.Store.Transitions); Assert.Empty(world.Store.Refusals);
    }
}
