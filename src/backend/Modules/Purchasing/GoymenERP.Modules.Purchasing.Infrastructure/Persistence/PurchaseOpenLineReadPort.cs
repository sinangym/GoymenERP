using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>
/// AT-025: the Purchasing-owned answer to <see cref="IPurchaseOpenLineReadPortV1"/> over Purchasing's own company-filtered
/// rows — the APPROVED orders' lines whose remaining (committed − accepted, where committed = ordered − technical, the
/// same shares <see cref="PurchaseSupplyReferencePort"/> answers through the domain) is above zero. Untracked, creates
/// nothing: no attempt, audit or version effect. Newest order first (created time, then id, descending, as the order list),
/// then by line number; the keyset cursor (created time, order id, line number) is bound to the company that read it.
/// Every quantity is the owner's exact value in its unit's canonical text (Ö2: trailing zeros dropped, never rounded; a
/// stored value above the unit's scale is a record that cannot be answered and fails closed).
/// </summary>
internal sealed class PurchaseOpenLineReadPort(PurchasingDbContext dbContext, ICompanyContext companyContext) : IPurchaseOpenLineReadPortV1
{
    /// <summary>The line page cap (the consumer validates the same 1–100).</summary>
    public const int MaximumLinePageSize = 100;

    private const int ApprovedStatus = (int)PurchaseOrderStatus.Approved;

    public async Task<PurchaseOpenLinePageV1> ListOpenApprovedLinesAsync(int pageSize, string? cursor, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            // Without a server-resolved company there is no tenant filter to read under: refuse, never read unscoped.
            throw new InvalidOperationException("Açık alış satırları yalnız çözümlenmiş şirket bağlamında okunur.");
        }

        if (pageSize is < 1 or > MaximumLinePageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"Satır sayfası 1–{MaximumLinePageSize} olmalıdır.");
        }

        var companyId = companyContext.CompanyId;
        var query =
            from line in dbContext.OrderLines.AsNoTracking()
            join order in dbContext.Orders.AsNoTracking() on new { line.CompanyId, Id = line.OrderId } equals new { order.CompanyId, order.Id }
            where order.Status == ApprovedStatus && line.Ordered - line.Technical - line.Accepted > 0m
            select new
            {
                order.CompanyId,
                OrderId = order.Id,
                order.CreatedAtUtc,
                order.EffectiveOn,
                order.SupplierPartyId,
                order.CompanyAccountId,
                line.LineId,
                line.LineNo,
                line.Kind,
                line.ProductId,
                line.ProductVersion,
                line.UnitId,
                line.UnitIsKilogram,
                line.Ordered,
                line.Technical,
                line.Accepted,
            };

        if (cursor is not null)
        {
            var (createdAtUtc, orderId, lineNo) = Cursor.Decode(cursor, companyId);
            query = query.Where(row => row.CreatedAtUtc < createdAtUtc
                || (row.CreatedAtUtc == createdAtUtc && (row.OrderId.CompareTo(orderId) < 0 || (row.OrderId == orderId && row.LineNo > lineNo))));
        }

        var rows = await query
            .OrderByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.OrderId).ThenBy(row => row.LineNo)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);
        var hasMore = rows.Count > pageSize;
        var page = hasMore ? rows.Take(pageSize).ToList() : rows;
        var items = new List<PurchaseOpenLineReferenceV1>(page.Count);
        foreach (var row in page)
        {
            if (row.CompanyId != companyId || string.IsNullOrWhiteSpace(row.ProductVersion) || !Enum.IsDefined(typeof(PurchaseOrderLineKind), row.Kind))
            {
                // Fail closed (FİNANS #352 INFO-1): a stored line that cannot be answered is never a guessed value on the wire.
                throw new InvalidOperationException("Kayıtlı alış satırı okunamadı.");
            }

            var unit = row.UnitIsKilogram ? QuantityUnit.Kilogram : QuantityUnit.Piece;
            var committed = row.Ordered - row.Technical;
            items.Add(new PurchaseOpenLineReferenceV1(row.CompanyId, row.OrderId, row.EffectiveOn, row.LineId, row.LineNo,
                ((PurchaseOrderLineKind)row.Kind).ToString(), row.SupplierPartyId, row.CompanyAccountId, row.ProductId, row.ProductVersion,
                row.UnitId, Text(committed, unit), Text(row.Accepted, unit), Text(committed - row.Accepted, unit)));
        }

        var last = page.LastOrDefault();
        return new PurchaseOpenLinePageV1(items, hasMore && last is not null ? Cursor.Encode(companyId, last.CreatedAtUtc, last.OrderId, last.LineNo) : null);
    }

    private static string Text(decimal value, QuantityUnit unit) => Quantity.Of(value, unit).Value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The opaque keyset cursor: company, the order's created time and id, the last answered line number (base64url).</summary>
    internal static class Cursor
    {
        private const int Length = 16 + 8 + 16 + 4;

        public static string Encode(Guid companyId, DateTimeOffset createdAtUtc, Guid orderId, int lineNo)
        {
            var bytes = new byte[Length];
            companyId.TryWriteBytes(bytes.AsSpan(0, 16));
            BitConverter.TryWriteBytes(bytes.AsSpan(16, 8), createdAtUtc.UtcTicks);
            orderId.TryWriteBytes(bytes.AsSpan(24, 16));
            BitConverter.TryWriteBytes(bytes.AsSpan(40, 4), lineNo);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static (DateTimeOffset CreatedAtUtc, Guid OrderId, int LineNo) Decode(string cursor, Guid companyId)
        {
            byte[] bytes;
            try
            {
                var padded = cursor.Replace('-', '+').Replace('_', '/');
                bytes = Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='));
            }
            catch (FormatException exception)
            {
                throw new ArgumentException("Açık alış satırı imleci çözülemedi.", nameof(cursor), exception);
            }

            if (bytes.Length != Length || new Guid(bytes.AsSpan(0, 16)) != companyId)
            {
                // Deny-first: another company's cursor is refused outright, never read as the caller's position.
                throw new ArgumentException("Açık alış satırı imleci bu şirketin değil ya da bozuk.", nameof(cursor));
            }

            var ticks = BitConverter.ToInt64(bytes, 16);
            var lineNo = BitConverter.ToInt32(bytes, 40);
            if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks || lineNo < 1)
            {
                throw new ArgumentException("Açık alış satırı imleci bozuk.", nameof(cursor));
            }

            return (new DateTimeOffset(ticks, TimeSpan.Zero), new Guid(bytes.AsSpan(24, 16)), lineNo);
        }
    }
}
