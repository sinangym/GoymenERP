namespace GoymenERP.Modules.Sales.Application.Contracts;

// Consumer adapter boundary, not a second catalogue or master owner.
// Central API implements this over existing authorized Counterparty page read.
// It requests active parties/accounts, no role requirement, code ascending,
// no count, and preserves the owner's protected cursor. All context is server-bound.
public interface ISalesAccountSelectionSourceV1
{
    Task<SalesReferenceReadResult<SalesAccountSelectionPageV1>> GetPageAsync(
        SalesAccountSelectionRequestV1 request, CancellationToken cancellationToken = default);
}

public sealed record SalesAccountSelectionRequestV1(Guid CompanyId, string ActorId,
    string SessionBinding, string? Q, int PageSize, string? Cursor);
public sealed record SalesAccountSelectionPageV1(
    IReadOnlyList<SalesAccountSelectionPartyV1> Items, string? NextCursor);
public sealed record SalesAccountSelectionPartyV1(Guid Id, string Code, int Kind,
    string LegalName, string? TradeName, bool IsActive, string Version,
    IReadOnlyList<SalesAccountSelectionAccountV1> CompanyAccounts);
public sealed record SalesAccountSelectionAccountV1(Guid Id, Guid CompanyId,
    int Roles, bool IsActive, string Version);
