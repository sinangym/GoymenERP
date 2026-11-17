namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>Internal read for the Inventory owner's validated, company-scoped committed cost inbox.
/// Returns only the existing TL Accounting journal amount profile. No user impersonation, profile
/// creation, write permission or default scale. This is not an HTTP capability.</summary>
public interface ISettingsInventoryCostProfileReaderV1
{
    Task<MoneyProfileV1> ReadAsync(Guid companyId, DateOnly effectiveOn, CancellationToken cancellationToken = default);
}
