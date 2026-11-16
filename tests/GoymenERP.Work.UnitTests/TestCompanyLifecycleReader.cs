using GoymenERP.BuildingBlocks.Multitenancy;
namespace GoymenERP.Work.UnitTests;
internal sealed class TestCompanyLifecycleReader : ICompanyLifecycleReadPortV1
{
    public CompanyLifecycleStateV1 State { get; set; } = CompanyLifecycleStateV1.Active;
    public bool Unavailable { get; set; }
    public Task<CompanyLifecycleViewV1?> ReadAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        Unavailable ? throw new InvalidOperationException("Synthetic lifecycle failure")
            : Task.FromResult<CompanyLifecycleViewV1?>(new(companyId, State, true, 1));
}
