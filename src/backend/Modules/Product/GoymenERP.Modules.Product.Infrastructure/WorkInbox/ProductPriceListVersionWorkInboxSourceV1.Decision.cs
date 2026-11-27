using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Product.Infrastructure.WorkInbox;

internal sealed partial class ProductPriceListVersionWorkInboxSourceV1
{
    private static readonly string[] PurposePermissions = [ProductPriceListPermissionsV1.Read, ProductPriceListPermissionsV1.Publish];
    public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid id, CancellationToken token) => ReadPurpose(context, id, null, token);
    public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionVersionAsync(WorkInboxSourceContextV1 context, Guid id, string version, CancellationToken token) => ReadPurpose(context, id, version, token);

    private async Task<WorkApprovalDecisionSnapshotV1?> ReadPurpose(WorkInboxSourceContextV1 context, Guid id, string? selectedHeader, CancellationToken token)
    {
        await Fence();
        var version = await database.Versions.AsNoTracking().SingleOrDefaultAsync(row => row.CompanyId == context.CompanyId && row.VersionId == id, token);
        await Fence(); if (version is null) return null;
        var list = await database.PriceLists.AsNoTracking().SingleOrDefaultAsync(row => row.CompanyId == context.CompanyId && row.Id == version.PriceListId, token);
        await Fence(); if (list is null) throw new InvalidOperationException("Price list header unavailable.");
        var header = list.Version.ToString(CultureInfo.InvariantCulture);
        if (selectedHeader is not null && (version.State != 1 || selectedHeader != header))
        {
            var parsed = ProductPriceListWire.ParseVersion(selectedHeader);
            if (parsed is null || parsed == long.MaxValue || parsed.Value.ToString(CultureInfo.InvariantCulture) != selectedHeader)
                throw new InvalidOperationException("Selected header version unavailable.");
            var hash = ProductPriceListWire.PublishRequestHash(list.Id, new(selectedHeader, version.VersionNo));
            // Exact payload/actor/command/company prefilter; Take(2) tests ambiguity, not age/history length.
            var attempts = await database.Attempts.AsNoTracking().Where(row => row.CompanyId == context.CompanyId
                && row.PriceListId == list.Id && row.CommandName == ProductPriceListCommandsV1.PublishV1 && row.Outcome == 1
                && row.ActorId == context.UserId && row.RequestHash == hash).Take(2).ToListAsync(token);
            await Fence();
            if (attempts.Count != 1 || attempts[0].ActorId != context.UserId || attempts[0].RequestHash != hash || attempts[0].ResultJson is null)
                throw new InvalidOperationException("Original price-list receipt unavailable.");
            var original = JsonSerializer.Deserialize<PriceListViewV1>(attempts[0].ResultJson!, ProductPriceListWire.Json);
            var target = original?.Versions.Where(row => row.VersionId == id && row.VersionNo == version.VersionNo).Take(2).ToArray();
            if (original is null || original.Id != list.Id || original.Version != (parsed.Value + 1).ToString(CultureInfo.InvariantCulture)
                || target is not { Length: 1 } || target[0].State != "Published" || target[0].PublishedBy != context.UserId
                || target[0].PublishedAtUtc is null || target[0].CreatedBy != version.CreatedBy || target[0].CreatedAtUtc != version.CreatedAtUtc)
                throw new InvalidOperationException("Original target version proof unavailable.");
            header = selectedHeader;
        }
        if (version.State is not (1 or 2) || list.Version <= 0 || !TryItem(new(id, list.Id, list.Name, version.VersionNo,
            version.CreatedAtUtc, version.CreatedBy, version.PreparerActorIdsJson), out var item))
            throw new InvalidOperationException("Price-list purpose identity unavailable.");
        var preparers = JsonSerializer.Deserialize<string[]>(version.PreparerActorIdsJson)!;
        return new(context.CompanyId, item, header, version.State == 1,
            ProductPriceListProof: new(context.CompanyId, list.Id, id, version.VersionNo, header,
                version.CreatedBy, version.CreatedAtUtc.ToUniversalTime(), preparers));

        async Task Fence()
        {
            var refusal = await fence.CheckAsync(context, PurposePermissions, token);
            if (refusal?.Outcome == NativeApprovalQueueOutcomeV1.Denied) throw new UnauthorizedAccessException();
            if (refusal is not null) throw new InvalidOperationException("Price-list purpose unavailable.");
        }
    }
}
