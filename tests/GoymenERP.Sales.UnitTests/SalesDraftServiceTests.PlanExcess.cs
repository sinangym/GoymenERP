using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_loading_above_original_plan_is_known_only_with_the_real_separate_approved_capacity(bool proven)
    {
        var world = new World(); var approved = await ApprovedAsync(world); var line = approved.Lines[0].LineId;
        var (_, loadings) = Owners(world);
        loadings.Answer = order => new(Company, order, [new SalesOrderLineLoadingV1(line, KgUnit, 1, 1m, 1.2m, 1.2m, 0m, proven ? 0.2m : null)]);
        var preview = await world.Service().GetCancellationImpactAsync(approved.Draft.Id);
        Assert.False(preview.Cancellable);
        Assert.Equal(proven ? SalesCancellationImpactStatusesV1.Present : SalesCancellationImpactStatusesV1.Unreadable,
            preview.Sources.Single(source => source.Source == SalesCancellationImpactSourcesV1.LoadingWithoutDispatchNote).Status);
        Assert.Equal(proven ? "1.2" : null, Assert.Single(preview.Lines).Loaded);
        Assert.Empty(world.Store.Cancellations);
    }
}
