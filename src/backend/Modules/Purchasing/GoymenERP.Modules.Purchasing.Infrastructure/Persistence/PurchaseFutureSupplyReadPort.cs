using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Purchasing.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>
/// F-017 F17-1 (ALIS_F017 v1 §3.1/§3.3, ALS-D82): the Purchasing-owned answer to <see cref="IPurchaseFutureSupplyReadPortV1"/> over
/// Purchasing's own company-filtered rows. The allocatable amount of a general line folds its technical slices in, so a share
/// accepted or cancelled on a slice lowers the general line's allocatable and nothing is counted twice (§3.3: "teknik dilimler
/// genel satırın içindedir; iki kez sayılmaz"; the same open rule as <c>PurchaseSalesLinkedSupplyComposer</c>):
/// committed = (ordered − technical) + Σ slice ordered, accepted = own + Σ slice accepted, cancelled = own + Σ slice cancelled,
/// closed = own + Σ slice closed (API-11), definitive returned = own + Σ slice returned (ALC-S6),
/// allocatable = committed − accepted − cancelled − closed − definitive returned. A negative allocatable is unreadable and fails closed.
/// Untracked; creates nothing (no attempt, audit or version effect). The list's keyset cursor (business date, order id, line
/// number) is bound to the company and the product that read it.
/// </summary>
internal sealed class PurchaseFutureSupplyReadPort(PurchasingDbContext dbContext, ICompanyContext companyContext) : IPurchaseFutureSupplyReadPortV1
{
    private const int ApprovedStatus = (int)PurchaseOrderStatus.Approved;
    private const int GeneralKind = (int)PurchaseOrderLineKind.General;
    private const int TechnicalKind = (int)PurchaseOrderLineKind.Technical;
    private const int SalesSource = (int)PurchaseRequestSourceKind.Sales;

    private static readonly PurchaseFutureSupplyAnswerV1 NotFound = new(PurchaseFutureSupplyOutcomeV1.NotFound, null, null, null);

    public async Task<PurchaseFutureSupplyAnswerV1> GetAllocatableLineAsync(Guid orderId, Guid lineId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCompanyResolved() || orderId == Guid.Empty || lineId == Guid.Empty)
        {
            // Fail-closed: no resolved company (no tenant filter to read under) or no identity is "not found", never an unscoped read.
            return NotFound;
        }

        var companyId = companyContext.CompanyId;
        var line = await dbContext.OrderLines.AsNoTracking()
            .Where(row => row.OrderId == orderId && row.LineId == lineId)
            .Select(row => new LineFacts(row.CompanyId, row.OrderId, row.LineId, row.LineNo, row.Kind, row.ParentLineId,
                row.SourceRequestLineId, row.ProductId, row.ProductVersion, row.UnitId, row.UnitIsKilogram, row.Ordered, row.Technical,
                row.Accepted, row.Cancelled, row.Closed, row.DefinitiveReturned))
            .SingleOrDefaultAsync(cancellationToken);
        if (line is null)
        {
            return NotFound;
        }

        var order = await dbContext.Orders.AsNoTracking()
            .Where(row => row.Id == orderId)
            .Select(row => new OrderFacts(row.CompanyId, row.Id, row.SupplierPartyId, row.CompanyAccountId, row.SourceRequestId,
                row.Status, row.Version, row.EffectiveOn))
            .SingleOrDefaultAsync(cancellationToken);
        if (order is null)
        {
            // The line's order must exist under the same company (the store writes both together); a line without it is NotFound.
            return NotFound;
        }

        if (line.CompanyId != companyId || order.CompanyId != companyId || !Enum.IsDefined(typeof(PurchaseOrderLineKind), line.Kind)
            || !Enum.IsDefined(typeof(PurchaseOrderStatus), order.Status))
        {
            throw Unreadable();
        }

        if (line.Kind == TechnicalKind)
        {
            if (line.ParentLineId is not { } parentLineId || parentLineId == Guid.Empty)
            {
                throw Unreadable();
            }

            return new PurchaseFutureSupplyAnswerV1(PurchaseFutureSupplyOutcomeV1.NotAllocatable, null,
                PurchaseFutureSupplyStatusesV1.TechnicalLine, parentLineId);
        }

        var slices = await dbContext.OrderLines.AsNoTracking()
            .Where(row => row.OrderId == orderId && row.ParentLineId == lineId && row.Kind == TechnicalKind)
            .GroupBy(row => row.OrderId)
            .Select(group => new SliceTotals(group.Sum(row => row.Ordered), group.Sum(row => row.Accepted), group.Sum(row => row.Cancelled),
                group.Sum(row => row.Closed), group.Sum(row => row.DefinitiveReturned)))
            .SingleOrDefaultAsync(cancellationToken) ?? SliceTotals.None;

        var salesIntent = await SalesIntentAsync(order.SourceRequestId, line.SourceRequestLineId is { } requestLineId ? [requestLineId] : [],
            cancellationToken);
        var answerLine = Compose(companyId, order, line, slices, salesIntent);
        return order.Status == ApprovedStatus
            ? new PurchaseFutureSupplyAnswerV1(PurchaseFutureSupplyOutcomeV1.Allocatable, answerLine, null, null)
            : new PurchaseFutureSupplyAnswerV1(PurchaseFutureSupplyOutcomeV1.NotAllocatable,
                answerLine with { AllocatableText = "0" }, ((PurchaseOrderStatus)order.Status).ToString(), null);
    }

    public async Task<PurchaseFutureSupplyPageV1> ListAllocatableLinesAsync(Guid productId, int pageSize, string? cursor, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (pageSize is < 1 or > PurchaseFutureSupplyPageV1.MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"Satır sayfası 1–{PurchaseFutureSupplyPageV1.MaximumPageSize} olmalıdır.");
        }

        if (!IsCompanyResolved() || productId == Guid.Empty)
        {
            // Fail-closed: without a resolved company there is nothing to read under; the answer is an empty page, never unscoped rows.
            return new PurchaseFutureSupplyPageV1([], null);
        }

        var companyId = companyContext.CompanyId;
        var technical = dbContext.OrderLines.AsNoTracking().Where(row => row.Kind == TechnicalKind);
        var folded =
            from line in dbContext.OrderLines.AsNoTracking()
            join order in dbContext.Orders.AsNoTracking() on new { line.CompanyId, Id = line.OrderId } equals new { order.CompanyId, order.Id }
            where order.Status == ApprovedStatus && line.Kind == GeneralKind && line.ProductId == productId
            let sliceOrdered = technical
                .Where(slice => slice.CompanyId == line.CompanyId && slice.OrderId == line.OrderId && slice.ParentLineId == line.LineId)
                .Sum(slice => (decimal?)slice.Ordered) ?? 0m
            let sliceAccepted = technical
                .Where(slice => slice.CompanyId == line.CompanyId && slice.OrderId == line.OrderId && slice.ParentLineId == line.LineId)
                .Sum(slice => (decimal?)slice.Accepted) ?? 0m
            let sliceCancelled = technical
                .Where(slice => slice.CompanyId == line.CompanyId && slice.OrderId == line.OrderId && slice.ParentLineId == line.LineId)
                .Sum(slice => (decimal?)slice.Cancelled) ?? 0m
            let sliceClosed = technical
                .Where(slice => slice.CompanyId == line.CompanyId && slice.OrderId == line.OrderId && slice.ParentLineId == line.LineId)
                .Sum(slice => (decimal?)slice.Closed) ?? 0m
            let sliceReturned = technical
                .Where(slice => slice.CompanyId == line.CompanyId && slice.OrderId == line.OrderId && slice.ParentLineId == line.LineId)
                .Sum(slice => (decimal?)slice.DefinitiveReturned) ?? 0m
            where line.Ordered - line.Technical + sliceOrdered - line.Accepted - sliceAccepted - line.Cancelled - sliceCancelled
                - line.Closed - sliceClosed - line.DefinitiveReturned - sliceReturned > 0m
            select new
            {
                order.CompanyId,
                OrderId = order.Id,
                order.SupplierPartyId,
                order.CompanyAccountId,
                order.SourceRequestId,
                order.Status,
                order.Version,
                order.EffectiveOn,
                LineCompanyId = line.CompanyId,
                line.LineId,
                line.LineNo,
                line.Kind,
                line.ParentLineId,
                line.SourceRequestLineId,
                line.ProductId,
                line.ProductVersion,
                line.UnitId,
                line.UnitIsKilogram,
                line.Ordered,
                line.Technical,
                line.Accepted,
                line.Cancelled,
                line.Closed,
                line.DefinitiveReturned,
                SliceOrdered = sliceOrdered,
                SliceAccepted = sliceAccepted,
                SliceCancelled = sliceCancelled,
                SliceClosed = sliceClosed,
                SliceReturned = sliceReturned,
            };

        if (cursor is not null)
        {
            var (effectiveOn, orderId, lineNo) = Cursor.Decode(cursor, companyId, productId);
            folded = folded.Where(row => row.EffectiveOn > effectiveOn
                || (row.EffectiveOn == effectiveOn && (row.OrderId.CompareTo(orderId) > 0 || (row.OrderId == orderId && row.LineNo > lineNo))));
        }

        var rows = await folded
            .OrderBy(row => row.EffectiveOn).ThenBy(row => row.OrderId).ThenBy(row => row.LineNo)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);
        var hasMore = rows.Count > pageSize;
        var page = hasMore ? rows.Take(pageSize).ToList() : rows;

        var items = new List<PurchaseFutureSupplyLineV1>(page.Count);
        foreach (var orderGroup in page.GroupBy(row => row.OrderId))
        {
            var first = orderGroup.First();
            var requestLineIds = orderGroup.Select(row => row.SourceRequestLineId).OfType<Guid>().ToArray();
            var salesIntent = await SalesIntentAsync(first.SourceRequestId, requestLineIds, cancellationToken);
            foreach (var row in orderGroup)
            {
                if (row.LineCompanyId != companyId || row.CompanyId != companyId || row.ProductId != productId
                    || row.Status != ApprovedStatus)
                {
                    throw Unreadable();
                }

                var order = new OrderFacts(row.CompanyId, row.OrderId, row.SupplierPartyId, row.CompanyAccountId, row.SourceRequestId,
                    row.Status, row.Version, row.EffectiveOn);
                var line = new LineFacts(row.LineCompanyId, row.OrderId, row.LineId, row.LineNo, row.Kind, row.ParentLineId,
                    row.SourceRequestLineId, row.ProductId, row.ProductVersion, row.UnitId, row.UnitIsKilogram, row.Ordered, row.Technical,
                    row.Accepted, row.Cancelled, row.Closed, row.DefinitiveReturned);
                items.Add(Compose(companyId, order, line, new SliceTotals(row.SliceOrdered, row.SliceAccepted, row.SliceCancelled, row.SliceClosed, row.SliceReturned),
                    salesIntent));
            }
        }

        // GroupBy keeps the first-seen order of the groups and of the rows inside each, so the page order is unchanged.
        var last = page.LastOrDefault();
        return new PurchaseFutureSupplyPageV1(items,
            hasMore && last is not null ? Cursor.Encode(companyId, productId, last.EffectiveOn, last.OrderId, last.LineNo) : null);
    }

    private bool IsCompanyResolved() => companyContext.IsResolved && companyContext.CompanyId != Guid.Empty;

    /// <summary>
    /// The sales intent of the given request lines of one order's source request: only a request line that came from a Sales order
    /// carries one (§2); every other line has none. Keyed by the request line identity.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, SalesIntent>> SalesIntentAsync(Guid? sourceRequestId, IReadOnlyCollection<Guid> requestLineIds,
        CancellationToken cancellationToken)
    {
        if (sourceRequestId is not { } requestId || requestLineIds.Count == 0)
        {
            return new Dictionary<Guid, SalesIntent>();
        }

        var rows = await dbContext.RequestLines.AsNoTracking()
            .Where(row => row.RequestId == requestId && requestLineIds.Contains(row.LineId) && row.SourceKind == SalesSource)
            .Select(row => new { row.LineId, row.SourceDocumentId, row.SourceDocumentLineId })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.LineId, row => new SalesIntent(row.SourceDocumentId, row.SourceDocumentLineId));
    }

    private static PurchaseFutureSupplyLineV1 Compose(Guid companyId, OrderFacts order, LineFacts line, SliceTotals slices,
        IReadOnlyDictionary<Guid, SalesIntent> salesIntent)
    {
        if (string.IsNullOrWhiteSpace(line.ProductVersion) || line.Kind != GeneralKind || line.ParentLineId is not null)
        {
            throw Unreadable();
        }

        var unit = line.UnitIsKilogram ? QuantityUnit.Kilogram : QuantityUnit.Piece;
        var committed = line.Ordered - line.Technical + slices.Ordered;
        var accepted = line.Accepted + slices.Accepted;
        var cancelled = line.Cancelled + slices.Cancelled;
        var closed = line.Closed + slices.Closed;
        var returned = line.DefinitiveReturned + slices.DefinitiveReturned;
        var allocatable = committed - accepted - cancelled - closed - returned;
        if (committed < 0m || accepted < 0m || cancelled < 0m || closed < 0m || returned < 0m || allocatable < 0m)
        {
            // Fail closed: an over-accepted or over-cancelled record is never answered as a guessed (clamped) amount.
            throw Unreadable();
        }

        var intent = line.SourceRequestLineId is { } requestLineId && salesIntent.TryGetValue(requestLineId, out var found) ? found : null;
        return new PurchaseFutureSupplyLineV1(companyId, order.OrderId, line.LineId, line.LineNo, order.SupplierPartyId, order.CompanyAccountId,
            line.ProductId, line.ProductVersion, line.UnitId, PurchaseOrderLineKind.General.ToString(), Text(committed, unit), Text(accepted, unit),
            Text(cancelled, unit), Text(closed, unit), Text(allocatable, unit), intent?.SalesOrderId, intent?.SalesOrderLineId, order.Version, Text(returned, unit));
    }

    private static string Text(decimal value, QuantityUnit unit) => Quantity.Of(value, unit).Value.ToString(CultureInfo.InvariantCulture);

    private static InvalidOperationException Unreadable() => new("Kayıtlı alış satırı okunamadı.");

    private sealed record OrderFacts(Guid CompanyId, Guid OrderId, Guid SupplierPartyId, Guid CompanyAccountId, Guid? SourceRequestId, int Status,
        long Version, DateOnly EffectiveOn);

    private sealed record LineFacts(Guid CompanyId, Guid OrderId, Guid LineId, int LineNo, int Kind, Guid? ParentLineId, Guid? SourceRequestLineId,
        Guid ProductId, string ProductVersion, Guid UnitId, bool UnitIsKilogram, decimal Ordered, decimal Technical, decimal Accepted, decimal Cancelled,
        decimal Closed, decimal DefinitiveReturned);

    private sealed record SliceTotals(decimal Ordered, decimal Accepted, decimal Cancelled, decimal Closed, decimal DefinitiveReturned)
    {
        public static readonly SliceTotals None = new(0m, 0m, 0m, 0m, 0m);
    }

    private sealed record SalesIntent(Guid? SalesOrderId, Guid? SalesOrderLineId);

    /// <summary>The opaque keyset cursor: company, product, the order's business date and id, the last answered line number (base64url).</summary>
    internal static class Cursor
    {
        private const int Length = 16 + 16 + 4 + 16 + 4;

        public static string Encode(Guid companyId, Guid productId, DateOnly effectiveOn, Guid orderId, int lineNo)
        {
            var bytes = new byte[Length];
            companyId.TryWriteBytes(bytes.AsSpan(0, 16));
            productId.TryWriteBytes(bytes.AsSpan(16, 16));
            BitConverter.TryWriteBytes(bytes.AsSpan(32, 4), effectiveOn.DayNumber);
            orderId.TryWriteBytes(bytes.AsSpan(36, 16));
            BitConverter.TryWriteBytes(bytes.AsSpan(52, 4), lineNo);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static (DateOnly EffectiveOn, Guid OrderId, int LineNo) Decode(string cursor, Guid companyId, Guid productId)
        {
            byte[] bytes;
            try
            {
                var padded = cursor.Replace('-', '+').Replace('_', '/');
                bytes = Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='));
            }
            catch (FormatException exception)
            {
                throw new ArgumentException("Gelecek tedarik satırı imleci çözülemedi.", nameof(cursor), exception);
            }

            if (bytes.Length != Length || new Guid(bytes.AsSpan(0, 16)) != companyId || new Guid(bytes.AsSpan(16, 16)) != productId)
            {
                // Deny-first: another company's or another product's cursor is refused outright, never read as the caller's position.
                throw new ArgumentException("Gelecek tedarik satırı imleci bu şirketin ya da bu ürünün değil veya bozuk.", nameof(cursor));
            }

            var dayNumber = BitConverter.ToInt32(bytes, 32);
            var lineNo = BitConverter.ToInt32(bytes, 52);
            if (dayNumber < DateOnly.MinValue.DayNumber || dayNumber > DateOnly.MaxValue.DayNumber || lineNo < 1)
            {
                throw new ArgumentException("Gelecek tedarik satırı imleci bozuk.", nameof(cursor));
            }

            return (DateOnly.FromDayNumber(dayNumber), new Guid(bytes.AsSpan(36, 16)), lineNo);
        }
    }
}
