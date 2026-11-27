using System.Globalization;
using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

internal sealed partial class ProductPriceListStore
{
    private async Task<PriceListPublishReceiptV1?> ReadPublishReceiptAsync(Guid id, PublishPriceListVersionRequestV1 request,
        ProductPriceListCommandMetadata metadata, string actor, PriceListResultV1 answer, Func<CancellationToken, Task> fence, CancellationToken token)
    {
        await fence(token);
        var companyId = RequireResolvedCompany(); var key = IdempotencyKeyHash(metadata.IdempotencyKey);
        var hash = ProductPriceListWire.PublishRequestHash(id, request);
        var attempt = await dbContext.Attempts.AsNoTracking().SingleOrDefaultAsync(row => row.CompanyId == companyId
            && row.CommandName == ProductPriceListCommandsV1.PublishV1 && row.IdempotencyKeyHash == key
            && row.ActorId == actor && row.RequestHash == hash && row.PriceListId == id && row.Outcome == AttemptOutcomeRecorded, token);
        await fence(token);
        if (attempt is null || attempt.ActorId != actor || attempt.RequestHash != hash || attempt.IdempotencyKeyHash != key || attempt.ResultJson is null || attempt.CorrelationId != answer.OriginalCorrelationId) return null;
        var original = System.Text.Json.JsonSerializer.Deserialize<PriceListViewV1>(attempt.ResultJson, Json);
        var expected = ProductPriceListWire.ParseVersion(request.ExpectedVersion);
        if (original is null || expected is null || expected == long.MaxValue || original.Id != id
            || original.Version != (expected.Value + 1).ToString(CultureInfo.InvariantCulture)
            || answer.PriceList.Id != id || answer.PriceList.Version != original.Version) return null;
        var versions = original.Versions.Where(row => row.VersionNo == request.VersionNo).Take(2).ToArray();
        if (versions.Length != 1 || versions[0] is not { State: "Published", PublishedAtUtc: { } at, PublishedBy: { } by } published
            || published.VersionId == Guid.Empty || by != attempt.ActorId) return null;
        return new(companyId, id, published.VersionId, request.VersionNo, request.ExpectedVersion, original.Version,
            ProductPriceListCommandsV1.PublishV1, attempt.ActorId, key, hash, at.ToUniversalTime(), by, attempt.CorrelationId);
    }
}
