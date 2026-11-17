namespace GoymenERP.Modules.Inventory.Application;

/// <summary>STK-S8: actual source labels for the existing transfer planning command, not a reservation promise.</summary>
public interface IInventoryTransferPlanCandidatesV1
{
    Task<InventoryTransferPlanCandidatesV1> SearchAsync(string? query, int limit = 25,
        CancellationToken cancellationToken = default);
}

public sealed record InventoryTransferPlanCandidatesV1(Guid CompanyId, string? Query, int Limit,
    IReadOnlyList<InventoryTransferPlanCandidateV1> Items, bool SearchWindowFull);

/// <summary>The full physical label mass comes from the existing Inventory position projector, never a UI sum.</summary>
public sealed record InventoryTransferPlanCandidateV1(Guid LabelId, string LabelCode, string LabelVersion,
    Guid ProductId, string? ProductVersion, Guid SourceLocationId, string SourceCustodianKind, Guid SourceCustodianId,
    string PhysicalKg, string BlockedPhysicalKg, string ActiveAllocationKg, string DocumentStatus);
