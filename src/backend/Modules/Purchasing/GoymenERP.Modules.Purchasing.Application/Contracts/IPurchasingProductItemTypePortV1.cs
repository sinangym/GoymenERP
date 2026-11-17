namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>SAT-D134/D149: the real shared Product owner supplies the item type and its independent version.
/// The host maps the owner's typed answer; no code, family or COGS classification is interpreted here.</summary>
public interface IPurchasingProductItemTypePortV1
{
    Task<PurchasingProductItemTypeSourceV1> GetAsync(Guid productId, CancellationToken token = default);
}
public enum PurchasingProductItemTypeOutcomeV1 { Unavailable = 0, Denied = 1, Found = 2 }
public enum PurchasingProductItemTypeKindV1 { Steel = 1, Scrap = 2, Service = 3 }
public sealed record PurchasingProductItemTypeSourceV1(PurchasingProductItemTypeOutcomeV1 Outcome,
    Guid CompanyId, Guid ProductId, PurchasingProductItemTypeKindV1? ItemType, long? Version);
