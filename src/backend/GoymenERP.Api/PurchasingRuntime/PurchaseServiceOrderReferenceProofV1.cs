using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Api.PurchasingRuntime;

/// <summary>Actual Inventory/Product owners. A customer's stock never authorizes an outgoing own-material subcontract.</summary>
public sealed class PurchaseServiceOrderReferenceProofV1(IInventoryParentRollValidationV1 labels,
    IProductStockEligibilityV1 products, ICompanyContext company, ICurrentUser user) : IPurchaseServiceOrderReferenceProofV1
{
    public async Task ValidateAsync(Guid companyId, DateOnly businessDate, IReadOnlyList<PurchaseServiceMaterial> materials,
        IReadOnlyList<PurchaseServiceOutput> outputs, CancellationToken token)
    {
        var actor = user.UserId; RequireCurrent();
        try
        {
            foreach (var material in materials)
            {
                var label = await labels.ValidateAsync(new(material.LabelId, material.LabelVersion), token); RequireCurrent();
                if (label.CompanyId != companyId || label.LabelId != material.LabelId || label.LabelVersion != material.LabelVersion
                    || label.ProductId != material.ProductId || label.ProductVersion != material.ProductVersion || !label.DraftSelectable
                    || label.Positions is not { Count: > 0 } || label.Positions.Any(x => x.OwnerKind != "Company" || x.OwnerId != companyId))
                    throw new PurchasingServiceException(422, "purchasing_subcontract_own_material_required");
                var product = await products.EvaluateAsync(new(material.ProductId, material.ProductVersion, businessDate), token); RequireCurrent();
                if (product.Snapshot is not { } snapshot || product.Errors.Count != 0 || snapshot.ProductId != material.ProductId
                    || snapshot.ProductVersion != material.ProductVersion || snapshot.EffectiveOn != businessDate)
                    throw new PurchasingServiceException(422, "purchasing_subcontract_product_invalid");
            }
            foreach (var output in outputs)
            {
                var product = await products.EvaluateAsync(new(output.ProductId, output.ProductVersion, businessDate), token); RequireCurrent();
                if (product.Snapshot is not { } snapshot || product.Errors.Count != 0 || snapshot.ProductId != output.ProductId
                    || snapshot.ProductVersion != output.ProductVersion || snapshot.EffectiveOn != businessDate)
                    throw new PurchasingServiceException(422, "purchasing_subcontract_product_invalid");
            }
        }
        catch (InventoryParentRollException exception) { throw new PurchasingServiceException(exception.StatusCode, exception.Code); }
        void RequireCurrent()
        {
            if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(actor) || user.UserId != actor || !company.IsResolved
                || companyId == Guid.Empty || company.CompanyId != companyId || !user.CanAccessCompany(companyId))
                throw new PurchasingServiceException(403, "forbidden");
        }
    }
}
public static class PurchaseServiceOrderHostComposition
{
    public static IServiceCollection AddPurchaseServiceOrderHostV1(this IServiceCollection services)
    { services.AddScoped<IPurchaseServiceOrderReferenceProofV1, PurchaseServiceOrderReferenceProofV1>(); return services; }
}
