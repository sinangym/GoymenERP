using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool PriceListHeaderVersion(string value, out long version) => long.TryParse(value, NumberStyles.None,
        CultureInfo.InvariantCulture, out version) && version > 0 && version < long.MaxValue && version.ToString(CultureInfo.InvariantCulture) == value;

    private static bool ValidProductPriceListProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot)
    {
        if (kind != ProductPriceList) return snapshot.ProductPriceListProof is null;
        if (snapshot.ProductPriceListProof is not { } proof || proof.CompanyId != snapshot.CompanyId
            || proof.PriceListId == Guid.Empty || proof.VersionId != snapshot.Item.ItemId || proof.VersionNo <= 0
            || proof.HeaderVersion != snapshot.SourceVersion || !PriceListHeaderVersion(proof.HeaderVersion, out _)
            || proof.CreatedBy != snapshot.Item.PreparedBy || string.IsNullOrWhiteSpace(proof.CreatedBy)
            || proof.CreatedAtUtc != snapshot.Item.WaitingSinceUtc || proof.CreatedAtUtc.Offset != TimeSpan.Zero
            || proof.Preparers is null || proof.Preparers.Any(string.IsNullOrWhiteSpace)
            || !proof.Preparers.Where(actor => actor != proof.CreatedBy).ToHashSet(StringComparer.Ordinal)
                .SetEquals(snapshot.Item.ExcludedDeciders ?? [])
            || snapshot.Item.References.Count != 1 || snapshot.Item.References[0].Role != "price-list"
            || snapshot.Item.References[0].RecordId != proof.PriceListId || snapshot.Item.Scope is not null) return false;
        return true;
    }

    private async Task<WorkProductPriceListPreviewV1> ProductPriceListHumanPreviewAsync(WorkApprovalDecisionSnapshotV1 snapshot,
        string actor, CancellationToken token)
    {
        if (productPriceLists is null || snapshot.ProductPriceListProof is not { } proof) throw new WorkDecisionException(503);
        await RequireDecisionAuthorityAsync(ProductPriceList, ProductPriceListPermissionsV1.Publish, snapshot.CompanyId, actor, token);
        PriceListViewV1 view;
        try { view = await productPriceLists.GetAsync(proof.PriceListId, token); }
        catch (ProductPriceListException error)
        {
            await RequireDecisionAuthorityAsync(ProductPriceList, ProductPriceListPermissionsV1.Publish, snapshot.CompanyId, actor, token);
            throw new WorkDecisionException(error.Status, error.Code);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch
        {
            await RequireDecisionAuthorityAsync(ProductPriceList, ProductPriceListPermissionsV1.Publish, snapshot.CompanyId, actor, token);
            throw new WorkDecisionException(503);
        }
        await RequireDecisionAuthorityAsync(ProductPriceList, ProductPriceListPermissionsV1.Publish, snapshot.CompanyId, actor, token);
        if (view is null || view.Versions is null || view.Scope is null) throw new WorkDecisionException(503);
        var target = view.Versions.Where(row => row.VersionId == proof.VersionId && row.VersionNo == proof.VersionNo).Take(2).ToArray();
        if (view.Id != proof.PriceListId || view.Version != proof.HeaderVersion || target.Length != 1
            || target[0].State != (snapshot.Pending ? "Draft" : "Published") || target[0].CreatedBy != proof.CreatedBy
            || target[0].CreatedAtUtc != proof.CreatedAtUtc) throw new WorkDecisionException(503);
        var version = target[0];
        return new(view.Id, view.Name, view.CurrencyCode, view.Channel, view.VatBasis, version.VersionId, version.VersionNo,
            version.ValidFrom, version.ValidThrough, [.. version.Lines.Select(row => new WorkProductPriceListLineV1(row.LineId,
                row.LineNo, row.ProductId, row.ProductCode, row.ProductName, row.PriceUnitId, row.UnitPrice,
                row.FloorUnitPrice, row.LowerQuantity, row.UpperQuantity))],
            new(view.Scope.Kind, view.Scope.PartyId, view.Scope.CompanyAccountId));
    }

    private async Task<WorkDecisionResultV1> DecideProductPriceListAsync(Guid id, WorkDecisionRequestV1 request, string key,
        WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (productPriceLists is null || snapshot.ProductPriceListProof is not { } proof) throw new WorkDecisionException(503);
        if (request.ExpectedVersion != proof.HeaderVersion) throw new WorkDecisionException(409, "work_decision_version_conflict");
        var body = new PublishPriceListVersionRequestV1(request.ExpectedVersion, proof.VersionNo);
        try
        {
            var answer = await productPriceLists.PublishAsync(proof.PriceListId, body,
                new(actor, ProductPriceListPermissionsV1.Publish, [], correlation.CorrelationId, correlation.CausationId, key, "Web"), token);
            await RequireDecisionAuthorityAsync(ProductPriceList, ProductPriceListPermissionsV1.Publish, snapshot.CompanyId, actor, token);
            if (answer is null || answer.PriceList is null || answer.PriceList.Versions is null) throw new WorkDecisionException(503);
            var expected = long.Parse(request.ExpectedVersion, CultureInfo.InvariantCulture);
            var target = answer.PriceList.Versions.Where(row => row.VersionId == id && row.VersionNo == proof.VersionNo).Take(2).ToArray();
            if (answer.PublishReceipt is not { } receipt || receipt.CompanyId != snapshot.CompanyId
                || receipt.PriceListId != proof.PriceListId || receipt.VersionId != id || receipt.VersionNo != proof.VersionNo
                || receipt.ExpectedHeaderVersion != request.ExpectedVersion || receipt.HeaderVersion != (expected + 1).ToString(CultureInfo.InvariantCulture)
                || receipt.CommandName != ProductPriceListCommandsV1.PublishV1 || receipt.ActorId != actor || receipt.PublishedBy != actor
                || receipt.IdempotencyKeyHash != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))
                || receipt.RequestHash != ProductPriceListWire.PublishRequestHash(proof.PriceListId, body)
                || receipt.PublishedAtUtc.Offset != TimeSpan.Zero || answer.PriceList.Id != proof.PriceListId
                || answer.PriceList.Version != receipt.HeaderVersion || target.Length != 1 || target[0].State != "Published"
                || target[0].PublishedBy != receipt.PublishedBy || target[0].PublishedAtUtc != receipt.PublishedAtUtc
                || target[0].CreatedBy != proof.CreatedBy || target[0].CreatedAtUtc != proof.CreatedAtUtc
                || !snapshot.Pending && !answer.IsReplay || string.IsNullOrWhiteSpace(answer.OriginalCorrelationId)
                || receipt.OriginalCorrelationId != answer.OriginalCorrelationId
                || !answer.IsReplay && answer.OriginalCorrelationId != correlation.CorrelationId) throw new WorkDecisionException(503);
            return new(snapshot.CompanyId, ProductPriceList, id, "Published", answer.IsReplay, answer.OriginalCorrelationId);
        }
        catch (ProductPriceListException error) { throw new WorkDecisionException(error.Status, error.Code); }
    }
}
