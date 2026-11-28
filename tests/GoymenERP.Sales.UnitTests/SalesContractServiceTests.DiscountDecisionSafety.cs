using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesContractServiceTests
{
    [Theory]
    [InlineData(true, "1")]
    [InlineData(false, "1")]
    [InlineData(true, "01")]
    [InlineData(false, "01")]
    public async Task Contract_discount_safety_requires_its_typed_owner_before_native_replay_or_mutation(bool approve, string version)
    {
        var f = Build(Manager);
        f.Store.Stored = Pending();
        f.Service.WithContractDiscountDecisionSafety();
        var body = new SalesDraftStateCommandRequest(version, approve ? null : "  SENTETIK raw reason  ");
        var failure = await Refused(() => approve
            ? f.Service.ApproveContractDiscountAsync(ContractId, body, Metadata)
            : f.Service.RejectContractDiscountAsync(ContractId, body, Metadata));
        Assert.Equal((503, "service_unavailable"), (failure.Status, failure.Code));
        Assert.Empty(f.Store.Calls);
        Assert.Empty(f.Store.Refusals);
        Assert.Equal(Pending().State, f.Store.Stored.State);
        Assert.Equal(1L, f.Store.Stored.Version);
    }
}
