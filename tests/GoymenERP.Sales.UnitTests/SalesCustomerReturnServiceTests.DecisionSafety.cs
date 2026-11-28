using GoymenERP.Modules.Sales.Application.Contracts;
using static GoymenERP.Sales.UnitTests.SalesCustomerReturnFixtures;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesCustomerReturnServiceTests
{
    [Theory]
    [InlineData(true, "2")] [InlineData(false, "2")]
    [InlineData(true, "02")] [InlineData(false, "02")]
    public async Task Customer_return_decision_safety_requires_a_typed_boundary_before_any_native_store_IO(bool approve, string version)
    {
        var f = Build(Approver); var target = Draft(); target.Submit(1, "SENTETIK-submitter", Now); f.Store.Stored = target;
        f.Service.WithCustomerReturnDecisionSafety(); var body = new SalesDraftStateCommandRequest(version, approve ? null : "  SENTETIK ret  ");
        var failure = await Refused(() => approve ? f.Service.ApproveCustomerReturnAsync(ReturnId, body, Metadata)
            : f.Service.RejectCustomerReturnAsync(ReturnId, body, Metadata));
        Assert.Equal((503, "service_unavailable"), (failure.Status, failure.Code));
        Assert.Empty(f.Store.Calls); Assert.Empty(f.Store.Refusals); Assert.Equal(2L, target.Version);
    }
}
