using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Requests;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// API-20: one row per company and document series — the technical display-number counter of the Sales records that are
/// not order drafts (series <c>SR</c>: sales requests). The draft keeps its own counter (<see cref="DraftNumberRow"/>).
/// Bumped with every stored record under its concurrency token; not an official document sequence (Ö8), no gap-free
/// guarantee.
/// </summary>
internal sealed class DocumentCounterRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public string Series { get; set; } = string.Empty;
    public long Next { get; set; }
    public long Version { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>
/// API-20 / API-21: the stored sales request header. Its lines are append-only; the header changes only along its decision
/// (state, decision facts, version, update time) — nothing else moves (the write guard admits exactly those columns).
/// </summary>
internal sealed class RequestRow : ISalesTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string DocumentNo { get; set; } = string.Empty;

    /// <summary>1 Submitted, 2 Approved, 3 Rejected (<see cref="SalesRequestState"/>); the decision facts follow it (check constraint).</summary>
    public int State { get; set; }
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
    public string? RejectionReason { get; set; }

    public Guid PartyId { get; set; }
    public Guid CompanyAccountId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public DateOnly? RequestedDeliveryDate { get; set; }
    public string? Note { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>One stored request line with the Product and Settings owners' snapshots as answered (the draft line's shape, unpriced). Append-only.</summary>
internal sealed class RequestLineRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid RequestId { get; set; }
    public int LineNo { get; set; }
    public Guid LineId { get; set; }

    public Guid ProductId { get; set; }
    public string ProductVersion { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public DateOnly ProductEffectiveOn { get; set; }

    public Guid StockUnitCompanyId { get; set; }
    public Guid StockUnitId { get; set; }
    public Guid StockUnitProfileId { get; set; }
    public string StockUnitProfileVersion { get; set; } = string.Empty;
    public Guid SalesUnitCompanyId { get; set; }
    public Guid SalesUnitId { get; set; }
    public Guid SalesUnitProfileId { get; set; }
    public string SalesUnitProfileVersion { get; set; } = string.Empty;
    public Guid OrderUnitBindingCompanyId { get; set; }
    public Guid OrderUnitBindingUnitId { get; set; }
    public Guid OrderUnitBindingProfileId { get; set; }
    public string OrderUnitBindingProfileVersion { get; set; } = string.Empty;

    public Guid OrderUnitCompanyId { get; set; }
    public Guid OrderUnitId { get; set; }
    public string OrderUnitCode { get; set; } = string.Empty;
    public string OrderUnitDimensionCode { get; set; } = string.Empty;
    public Guid OrderUnitProfileId { get; set; }
    public string OrderUnitProfileVersion { get; set; } = string.Empty;
    public bool OrderUnitIsKilogram { get; set; }
    public int OrderUnitProfileScale { get; set; }
    public int OrderUnitScale { get; set; }
    public bool OrderUnitIsActive { get; set; }
    public DateOnly OrderUnitEffectiveFrom { get; set; }
    public DateOnly? OrderUnitEffectiveThrough { get; set; }

    public decimal Quantity { get; set; }
    public string QuantityText { get; set; } = string.Empty;
    public DateOnly? RequestedDeliveryDate { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// ADR-SAL-001 §2 (API-20 / API-21): the sales request commands' attempt record — the draft attempt's shape in the request's
/// own table. (company, command name, uppercase SHA-256 of the trimmed idempotency key) is the identity, the raw key is never
/// stored; the canonical request hash is its content and the actor is stored to be compared. Written with the request in the
/// same save; a replay reads it first. Append-only: the EF write guard and the SQL trigger refuse every UPDATE and DELETE.
/// </summary>
internal sealed class RequestAttemptRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;

    /// <summary>1 = a stored request (<see cref="RequestId"/> set); 2 = a recorded terminal refusal (<see cref="RefusalJson"/> set) — ADR-011 §7.</summary>
    public int Outcome { get; set; }
    public Guid? RequestId { get; set; }
    public string? RefusalJson { get; set; }

    /// <summary>ADR-SAL-001 §2: kept for the shape; no request command stores a response snapshot (CK_RequestAttempts_Result).</summary>
    public string? ResultJson { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

/// <summary>The one mapping between a domain request and its rows (write) and back through the domain (read).</summary>
internal static class SalesRequestRows
{
    internal static void Add(SalesDbContext dbContext, SalesRequest request, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(request);
        dbContext.Requests.Add(new RequestRow
        {
            Id = request.Id,
            CompanyId = request.CompanyId,
            DocumentNo = request.DocumentNo,
            State = (int)request.State,
            Version = request.Version,
            DecidedBy = request.Decision?.DecidedBy,
            DecidedAtUtc = request.Decision?.DecidedAtUtc,
            RejectionReason = request.Decision?.RejectionReason,
            PartyId = request.Header.PartyId,
            CompanyAccountId = request.Header.CompanyAccountId,
            BusinessDate = request.Header.BusinessDate,
            RequestedDeliveryDate = request.Header.RequestedDeliveryDate,
            Note = request.Header.Note,
            CreatedAtUtc = request.CreatedAtUtc,
            UpdatedAtUtc = request.UpdatedAtUtc,
            CreatedBy = request.CreatedBy,
            CorrelationId = correlationId.Trim(),
        });
        foreach (var line in request.Lines)
        {
            var product = line.Product;
            var unit = line.OrderUnit;
            dbContext.RequestLines.Add(new RequestLineRow
            {
                CompanyId = request.CompanyId,
                RequestId = request.Id,
                LineNo = line.LineNo,
                LineId = line.Id,
                ProductId = product.ProductId,
                ProductVersion = product.ProductVersion,
                ProductCode = product.Code,
                ProductName = product.Name,
                ProductEffectiveOn = product.EffectiveOn,
                StockUnitCompanyId = product.StockUnit.CompanyId,
                StockUnitId = product.StockUnit.UnitId,
                StockUnitProfileId = product.StockUnit.ProfileId,
                StockUnitProfileVersion = product.StockUnit.ProfileVersion,
                SalesUnitCompanyId = product.SalesUnit.CompanyId,
                SalesUnitId = product.SalesUnit.UnitId,
                SalesUnitProfileId = product.SalesUnit.ProfileId,
                SalesUnitProfileVersion = product.SalesUnit.ProfileVersion,
                OrderUnitBindingCompanyId = product.OrderUnit.CompanyId,
                OrderUnitBindingUnitId = product.OrderUnit.UnitId,
                OrderUnitBindingProfileId = product.OrderUnit.ProfileId,
                OrderUnitBindingProfileVersion = product.OrderUnit.ProfileVersion,
                OrderUnitCompanyId = unit.CompanyId,
                OrderUnitId = unit.UnitId,
                OrderUnitCode = unit.UnitCode,
                OrderUnitDimensionCode = unit.DimensionCode,
                OrderUnitProfileId = unit.ProfileId,
                OrderUnitProfileVersion = unit.ProfileVersion,
                OrderUnitIsKilogram = unit.IsKilogram,
                OrderUnitProfileScale = unit.ProfileScale,
                OrderUnitScale = unit.Scale,
                OrderUnitIsActive = unit.IsActive,
                OrderUnitEffectiveFrom = unit.EffectiveFrom,
                OrderUnitEffectiveThrough = unit.EffectiveThrough,
                Quantity = line.Quantity.Value,
                QuantityText = line.QuantityText,
                RequestedDeliveryDate = line.RequestedDeliveryDate,
                Note = line.Note,
            });
        }
    }

    /// <summary>Rebuilds a stored request through the domain: every rule of a new request holds again; a corrupt row is refused, not repaired.</summary>
    internal static SalesRequest Restore(RequestRow row, IEnumerable<RequestLineRow> lineRows)
    {
        var lines = lineRows.OrderBy(line => line.LineNo).ToArray();
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].LineNo != index + 1)
            {
                throw new InvalidOperationException("Kayıtlı satış talebi satırları kesintisiz numaralı değil.");
            }
        }

        var request = SalesRequest.Create(row.Id, row.CompanyId, row.DocumentNo,
            new SalesRequestHeader(row.PartyId, row.CompanyAccountId, row.BusinessDate, row.RequestedDeliveryDate, row.Note),
            [.. lines.Select(line => new SalesRequestLineInput(line.LineId,
                new ProductSnapshot(line.ProductId, line.ProductVersion, line.ProductCode, line.ProductName, line.ProductEffectiveOn,
                    new UnitBindingSnapshot(line.StockUnitCompanyId, line.StockUnitId, line.StockUnitProfileId, line.StockUnitProfileVersion),
                    new UnitBindingSnapshot(line.SalesUnitCompanyId, line.SalesUnitId, line.SalesUnitProfileId, line.SalesUnitProfileVersion),
                    new UnitBindingSnapshot(line.OrderUnitBindingCompanyId, line.OrderUnitBindingUnitId, line.OrderUnitBindingProfileId,
                        line.OrderUnitBindingProfileVersion)),
                new UnitProfileSnapshot(line.OrderUnitCompanyId, line.OrderUnitId, line.OrderUnitCode, line.OrderUnitDimensionCode,
                    line.OrderUnitProfileId, line.OrderUnitProfileVersion, line.OrderUnitIsKilogram, line.OrderUnitProfileScale,
                    line.OrderUnitScale, line.OrderUnitIsActive, line.OrderUnitEffectiveFrom, line.OrderUnitEffectiveThrough),
                line.QuantityText, line.Quantity, line.RequestedDeliveryDate, line.Note))],
            row.CreatedBy, row.CreatedAtUtc);
        var decision = row.DecidedBy is { } decidedBy && row.DecidedAtUtc is { } decidedAt
            ? new SalesRequestDecision(decidedBy, decidedAt, row.RejectionReason)
            : row.DecidedBy is null && row.DecidedAtUtc is null && row.RejectionReason is null
                ? null
                : throw new InvalidOperationException("Kayıtlı satış talebi kararı eksik.");
        if (!Enum.IsDefined(typeof(SalesRequestState), row.State))
        {
            throw new InvalidOperationException("Kayıtlı satış talebi durumu tanınmıyor.");
        }

        request.Restore((SalesRequestState)row.State, decision, row.Version, row.UpdatedAtUtc);
        return request;
    }
}
