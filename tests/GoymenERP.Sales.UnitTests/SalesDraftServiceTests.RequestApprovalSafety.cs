using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Request_runtime_composition_refuses_an_unprotected_store_without_reading_or_mutating_it(bool approve)
    {
        var world = new World(); var core = world.Service();
        Assert.Same(core, core.WithRequestApprovalSafety());
        Assert.Same(core, core.WithRequestApprovalSafety()); // No toggle that reopens the raw request decision path.
        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => approve
            ? core.ApproveRequestAsync(Guid.NewGuid(), Decision("1"), Metadata("safe-approval"))
            : core.RejectRequestAsync(Guid.NewGuid(), Decision("1", "SENTETIK neden"), Metadata("safe-rejection")));
        Assert.Equal((503, "service_unavailable"), (refused.Status, refused.Code));
        Assert.Equal(0, world.Requests!.Reads); Assert.Empty(world.Requests.Envelopes);
        Assert.Empty(world.Requests.Audits); Assert.Empty(world.Requests.Refusals);
    }

    [Theory]
    [InlineData("unauthenticated", 401)]
    [InlineData("denied", 403)]
    [InlineData("scoped", 403)]
    [InlineData("foreign-company", 403)]
    public async Task Request_runtime_composition_preserves_native_authority_precedence(string fault, int status)
    {
        var world = new World();
        if (fault == "unauthenticated") world.User.Authenticated = false;
        if (fault == "denied") world.Authority.Granted = false;
        if (fault == "scoped") world.Authority.CompanyWide = false;
        if (fault == "foreign-company") world.User.Company = Guid.NewGuid();
        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().WithRequestApprovalSafety()
            .ApproveRequestAsync(Guid.NewGuid(), Decision("1"), Metadata("safe-authority")));
        Assert.Equal(status, refused.Status); Assert.Equal(0, world.Requests!.Reads);
        Assert.Empty(world.Requests.Envelopes); Assert.Empty(world.Requests.Refusals);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("9223372036854775808")]
    [InlineData("1.0")]
    public async Task Request_runtime_composition_keeps_native_positive_Int64_shape_refusal_before_store_IO(string version)
    {
        var world = new World();
        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().WithRequestApprovalSafety()
            .ApproveRequestAsync(Guid.NewGuid(), Decision(version), Metadata("safe-version")));
        Assert.Equal((400, "validation_failed"), (refused.Status, refused.Code)); Assert.Equal(0, world.Requests!.Reads);
        Assert.Empty(world.Requests.Refusals);
    }

    [Fact]
    public async Task Request_runtime_composition_honors_initial_cancellation_before_authority_or_store()
    {
        var world = new World(); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.Service().WithRequestApprovalSafety()
            .ApproveRequestAsync(Guid.NewGuid(), Decision("1"), Metadata("safe-cancel"), cancelled.Token));
        Assert.Equal(0, world.Authority.Calls); Assert.Equal(0, world.Requests!.Reads); Assert.Empty(world.Requests.Refusals);
    }
}
