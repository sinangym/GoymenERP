namespace GoymenERP.Modules.Organization.Domain.Common;

/// <summary>
/// An Organization row that is not a coded master (<see cref="TenantEntity"/>) but still belongs to exactly one company.
/// The DbContext write guard applies the same company fence to every such row: the company is set, never changes and
/// matches the resolved company context.
/// </summary>
public interface ICompanyOwnedRecord
{
    Guid CompanyId { get; }
}
