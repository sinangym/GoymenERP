using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

// Only the enlisted participant invokes this staging operation. It never commits,
// flushes foreign owners, accepts HTTP actor metadata, or applies mutable request
// objects retained by a caller across awaits/retries.
internal static class ProductDraftOperation
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
    };

    public static async Task<ProductDraftWriteResult> StageAsync(ProductDbContext context,
        GroupSharedMasterCommandContext authority, ProductCommandIntent intent, TimeProvider clock,
        CancellationToken cancellationToken, ISettingsUnitIdentityCatalogV1? units = null)
    {
        if (context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("A Product attempt requires a fresh write context.");
        var envelope = JsonSerializer.Deserialize<FrozenEnvelope>(intent.CanonicalRequestJson, Json)
            ?? throw new InvalidOperationException("The frozen Product envelope is missing.");
        var operation = envelope.Operation;
        var catalog = new ProductRegistryReferenceCatalog(context, authority, units);
        var validator = new ProductDraftValidator(catalog);
        if (operation == "product.draft.create.v1")
        {
            if (authority.Operation != GroupSharedMasterOperation.DraftCreate) throw WrongOperation();
            var request = envelope.Request.Deserialize<CreateProductDraftRequest>(Json)
                ?? throw new InvalidOperationException("The frozen create request is missing.");
            var validated = await validator.ValidateCreateAsync(request, cancellationToken);
            var code = await new ProductCodeAllocator(context).StageNextAsync(authority, validated.Group.Id, cancellationToken);
            var product = Domain.Products.Product.CreateDraft(Guid.NewGuid(), code, validated.Name,
                validated.Group.Id, validated.ClassId, validated.FamilyId, validated.Roles, clock.GetUtcNow());
            var stored = ProductStoredRecord.Capture(product, validated.References);
            var row = ProductRow.FromRecord(stored);
            context.Products.Add(row);
            context.CodeRegistrations.Add(ProductCodeRegistration.Current(row));
            // System action label, following the existing creation-audit pattern;
            // it is not presented as a user-supplied justification.
            return new(null, Card(stored, validated.Group.Name), "Ürün taslağı oluşturma");
        }
        if (operation == "product.draft.basic-edit.v1")
        {
            if (authority.Operation != GroupSharedMasterOperation.DraftBasicEdit) throw WrongOperation();
            var id = envelope.ProductId
                ?? throw new InvalidOperationException("The frozen Product target is missing.");
            var request = envelope.Request.Deserialize<EditProductDraftRequest>(Json)
                ?? throw new InvalidOperationException("The frozen edit request is missing.");
            var row = await context.Products.SingleOrDefaultAsync(product => product.Id == id, cancellationToken)
                ?? throw new ProductOperationException(ProductApiV1.NotFound, 404);
            var before = row.ToRecord();
            var product = before.RestoreProduct();
            if (product.Lifecycle != ProductLifecycle.Draft)
                throw new ProductOperationException(ProductApiV1.LifecycleConflict, 409);
            var validated = await validator.ValidateEditAsync(product, before.RestoreReferences(), request, cancellationToken);
            product.EditBasic(ProductVersion.Parse(request.ExpectedVersion), validated.Name, validated.ClassId,
                validated.FamilyId, validated.Roles, request.Reason, clock.GetUtcNow());
            var after = ProductStoredRecord.Capture(product, validated.References);
            row.ApplyEdit(after);
            return new(Card(before, validated.Group.Name), Card(after, validated.Group.Name), request.Reason);
        }
        if (operation == ProductCommandIntent.ActivateOperation)
        {
            if (authority.Operation != GroupSharedMasterOperation.Activate) throw WrongOperation();
            var id = envelope.ProductId
                ?? throw new InvalidOperationException("The frozen Product target is missing.");
            var request = envelope.Request.Deserialize<ActivateProductRequest>(Json)
                ?? throw new InvalidOperationException("The frozen activation request is missing.");
            var row = await context.Products.SingleOrDefaultAsync(product => product.Id == id, cancellationToken)
                ?? throw new ProductOperationException(ProductApiV1.NotFound, 404);
            var before = row.ToRecord();
            var product = before.RestoreProduct();
            if (product.Lifecycle != ProductLifecycle.Draft)
                throw new ProductOperationException(ProductApiV1.LifecycleConflict, 409);
            var expectedVersion = ProductVersion.Parse(request.ExpectedVersion);
            if (expectedVersion != product.Version)
                throw new ProductVersionConflictException(expectedVersion, product.Version);
            var groupName = await context.CodeGroups.AsNoTracking().Where(group => group.Id == row.GroupId)
                .Select(group => group.Name).SingleOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("The stored Product code group is missing.");
            var beforeCard = Card(before, groupName);
            // PRODUCT-TECH-FAMILY-02 adds one exact activation prerequisite for the accepted cut-sheet/plate
            // family. Family-less and the four legacy coil/slit families retain DEC-PRD-010 activation semantics.
            if (product.FamilyId == ProductInitialTechnicalDefinitionV1.HotRolledCutSheetPlateFamilyId)
            {
                var errors = await new ProductPurchaseFamilyRequirementPolicyV1(
                    new SqlProductFamilyDefinitionSource(context)).ValidateAsync(beforeCard, cancellationToken);
                if (errors.Count > 0) throw new ProductActivationValidationException(errors);
            }
            product.Activate(expectedVersion, clock.GetUtcNow());
            var after = ProductStoredRecord.Capture(product, before.RestoreReferences());
            row.ApplyActivation(after);
            // System action label, as for creation: the approval carries no user-supplied reason.
            return new(beforeCard, Card(after, groupName), "Ürün kartı onayı", "product.card.activated.v1");
        }
        if (operation is ProductCommandIntent.DeactivateOperation or ProductCommandIntent.ReactivateOperation)
        {
            var deactivate = operation == ProductCommandIntent.DeactivateOperation;
            if (authority.Operation != (deactivate ? GroupSharedMasterOperation.Deactivate : GroupSharedMasterOperation.Reactivate))
                throw WrongOperation();
            var id = envelope.ProductId
                ?? throw new InvalidOperationException("The frozen Product target is missing.");
            var request = envelope.Request.Deserialize<ChangeProductLifecycleRequest>(Json)
                ?? throw new InvalidOperationException("The frozen lifecycle request is missing.");
            var row = await context.Products.SingleOrDefaultAsync(product => product.Id == id, cancellationToken)
                ?? throw new ProductOperationException(ProductApiV1.NotFound, 404);
            var before = row.ToRecord();
            var product = before.RestoreProduct();
            var (from, to) = deactivate
                ? (ProductLifecycle.Active, ProductLifecycle.Inactive)
                : (ProductLifecycle.Inactive, ProductLifecycle.Active);
            if (product.Lifecycle != from)
                throw new ProductOperationException(ProductApiV1.LifecycleConflict, 409);
            var expectedVersion = ProductVersion.Parse(request.ExpectedVersion);
            if (deactivate) product.Deactivate(expectedVersion, clock.GetUtcNow());
            else product.Reactivate(expectedVersion, clock.GetUtcNow());
            var after = ProductStoredRecord.Capture(product, before.RestoreReferences());
            row.ApplyLifecycleChange(after, from, to);
            var groupName = await context.CodeGroups.AsNoTracking().Where(group => group.Id == row.GroupId)
                .Select(group => group.Name).SingleOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("The stored Product code group is missing.");
            // The actor's own reason (normalised in the frozen intent) is the audit reason and the history's.
            return new(Card(before, groupName), Card(after, groupName), ProductDraftValidator.NormalizeReason(request.Reason),
                deactivate ? DeactivatedEventType : ReactivatedEventType);
        }
        throw new InvalidOperationException("The frozen Product operation is unsupported.");
    }

    public const string DeactivatedEventType = "product.card.deactivated.v1";
    public const string ReactivatedEventType = "product.card.reactivated.v1";

    private static ProductCardView Card(ProductStoredRecord record, string groupName) => new(
        ProductDraftView.FromProduct(record.RestoreProduct(), new(groupName, null, null, null)), record.RestoreReferences());

    private sealed record FrozenEnvelope(string Operation, JsonElement Request, Guid? ProductId = null);

    private static UnauthorizedAccessException WrongOperation() => new("Product command authority does not match the frozen operation.");
}
