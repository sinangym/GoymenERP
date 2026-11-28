using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Risk_decision_guard_is_monotonic_and_requires_real_owned_store_before_native_IO(bool approve)
    {
        var world = new World(); var store = new FakeRiskExceptionStore(); var service = RiskService(world, store);
        Assert.Same(service, service.WithRiskExceptionDecisionSafety());
        Assert.Same(service, service.WithRiskExceptionDecisionSafety());
        var failure = await Assert.ThrowsAsync<SalesDraftServiceException>(() => approve
            ? service.ApproveRiskExceptionAsync(Guid.NewGuid(), 1, new("1"), Metadata("risk-safe"))
            : service.RejectRiskExceptionAsync(Guid.NewGuid(), 1, new("1", "  SENTETIK ret  "), Metadata("risk-safe")));
        Assert.Equal((503, "service_unavailable"), (failure.Status, failure.Code));
        Assert.Empty(store.Writes); Assert.Empty(store.Refusals); Assert.Empty(world.Store.Envelopes);
        Assert.All(world.Authority.Measured, name => Assert.Equal(RiskApprovePermission, name));
    }

    [Theory]
    [InlineData("unauthenticated", 401)] [InlineData("denied", 403)]
    [InlineData("scoped", 403)] [InlineData("foreign-company", 403)]
    public async Task Risk_decision_guard_keeps_native_authority_precedence_before_boundary_refusal(string fault, int status)
    {
        var world = new World(); var store = new FakeRiskExceptionStore();
        if (fault == "unauthenticated") world.User.Authenticated = false;
        if (fault == "denied") world.Authority.Denied.Add(RiskApprovePermission);
        if (fault == "scoped") world.Authority.CompanyWide = false;
        if (fault == "foreign-company") world.User.Company = Guid.NewGuid();
        var failure = await Assert.ThrowsAsync<SalesDraftServiceException>(() => RiskService(world, store).WithRiskExceptionDecisionSafety()
            .ApproveRiskExceptionAsync(Guid.NewGuid(), 1, new("1"), Metadata("risk-authority")));
        Assert.Equal(status, failure.Status); Assert.Empty(store.Writes); Assert.Empty(store.Refusals); Assert.Empty(world.Store.Envelopes);
    }

    [Theory]
    [InlineData(true, "0")] [InlineData(false, "0")]
    [InlineData(true, "9223372036854775808")] [InlineData(false, "1.0")]
    public async Task Risk_decision_guard_keeps_native_version_shape_before_boundary_refusal(bool approve, string version)
    {
        var world = new World(); var store = new FakeRiskExceptionStore(); var service = RiskService(world, store).WithRiskExceptionDecisionSafety();
        var failure = await Assert.ThrowsAsync<SalesDraftServiceException>(() => approve
            ? service.ApproveRiskExceptionAsync(Guid.NewGuid(), 1, new(version), Metadata("risk-shape"))
            : service.RejectRiskExceptionAsync(Guid.NewGuid(), 1, new(version, "SENTETIK ret"), Metadata("risk-shape")));
        Assert.Equal((400, "validation_failed"), (failure.Status, failure.Code)); Assert.Empty(store.Writes); Assert.Empty(store.Refusals);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Risk_decision_initial_cancel_never_measures_authority_or_reads_native_owners(bool approve)
    {
        var world = new World(); var store = new FakeRiskExceptionStore(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var service = RiskService(world, store).WithRiskExceptionDecisionSafety();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => approve
            ? service.ApproveRiskExceptionAsync(Guid.NewGuid(), 1, new("1"), Metadata("risk-cancel"), cancellation.Token)
            : service.RejectRiskExceptionAsync(Guid.NewGuid(), 1, new("1", "SENTETIK ret"), Metadata("risk-cancel"), cancellation.Token));
        Assert.Equal(0, world.Authority.Calls); Assert.Empty(store.Writes); Assert.Empty(store.Refusals);
    }

    [Fact]
    public async Task Risk_decision_completed_first_authority_await_cannot_ignore_cancellation()
    {
        var world = new World(); var store = new FakeRiskExceptionStore(); using var cancellation = new CancellationTokenSource();
        world.Authority.OnRead = (_, _) => cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RiskService(world, store).WithRiskExceptionDecisionSafety()
            .ApproveRiskExceptionAsync(Guid.NewGuid(), 1, new("1"), Metadata("risk-late-cancel"), cancellation.Token));
        Assert.Equal(1, world.Authority.Calls); Assert.Empty(store.Writes); Assert.Empty(store.Refusals); Assert.Empty(world.Store.Envelopes);
    }
}
