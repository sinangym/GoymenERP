using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class ManufacturingCostDifferenceStoreV1
{
    public Task<AccountingApiResult<ManufacturingCostDifferenceViewV1>> RunApprovalAsync(
        Func<bool, Task<AccountingApiResult<ManufacturingCostDifferenceViewV1>>> attempt,
        Func<CancellationToken, Task> freshFence, CancellationToken token = default) =>
        posting.RunManufacturingCostDifferenceApprovalAsync(this, db, audit, attempt, freshFence, token);
    private bool IsOwnedApproval => posting.IsOwnedManufacturingCostDifferenceApproval(this, db, audit);
}
