using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Fixtures;

/// <summary>
/// Purchasing's narrow write port for one synthetic Draft order used by browser suite pur01. The host
/// exposes it only inside the disposable fixture-writer envelope. It creates no attempt or audit record:
/// the fixture is a precondition, while approval and replay remain real API commands.
/// </summary>
public interface IPurchaseOrderFixtureStore
{
    Task<PurchaseOrderFixtureOutcome> EnsureAsync(PurchaseOrderFixtureRequest request, CancellationToken cancellationToken);
}

public sealed record PurchaseOrderFixtureRequest(
    Guid OrderId,
    Guid CompanyId,
    Guid SupplierPartyId,
    Guid CompanyAccountId,
    DateOnly EffectiveOn,
    PurchaseOrderFixtureLine Line,
    string Label,
    string CreatedBy,
    string CorrelationId,
    DateTimeOffset At);

public sealed record PurchaseOrderFixtureLine(
    Guid LineId,
    Guid ProductId,
    string ProductVersion,
    Guid UnitId,
    bool IsKilogram,
    decimal Quantity);

public sealed record PurchaseOrderFixtureOutcome(Guid OrderId, long Version, PurchaseOrderStatus Status, bool Added);

public static class PurchaseOrderFixtureStoreRegistration
{
    public static IServiceCollection AddPurchaseOrderFixtureStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IPurchaseOrderFixtureStore, PurchaseOrderFixtureStore>();
        return services;
    }
}

internal sealed class PurchaseOrderFixtureStore(PurchasingDbContext dbContext, ICompanyContext companyContext)
    : IPurchaseOrderFixtureStore
{
    private const string FixturePrefix = "SENTETIK browser-suite:";

    public async Task<PurchaseOrderFixtureOutcome> EnsureAsync(
        PurchaseOrderFixtureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Line);
        Validate(request);
        BindCompany(request.CompanyId);

        PurchaseOrder order = Build(request);
        PurchaseOrderRow? existing = await dbContext.Orders.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == request.OrderId, cancellationToken);
        if (existing is not null)
        {
            PurchaseOrderLineRow? line = await dbContext.OrderLines.AsNoTracking()
                .SingleOrDefaultAsync(row => row.OrderId == request.OrderId, cancellationToken);
            if (!Matches(existing, line, request))
            {
                throw new InvalidOperationException("A different Purchasing order already uses this fixture id.");
            }

            return new PurchaseOrderFixtureOutcome(
                existing.Id,
                existing.Version,
                (PurchaseOrderStatus)existing.Status,
                Added: false);
        }

        dbContext.Orders.Add(new PurchaseOrderRow
        {
            Id = order.Id,
            CompanyId = request.CompanyId,
            SupplierPartyId = order.SupplierPartyId,
            CompanyAccountId = request.CompanyAccountId,
            SourceRequestId = null,
            Status = (int)order.Status,
            Version = order.Version,
            EffectiveOn = request.EffectiveOn,
            CreatedAtUtc = order.CreatedAtUtc,
            UpdatedAtUtc = order.UpdatedAtUtc,
            CreatedBy = request.CreatedBy.Trim(),
            CorrelationId = request.CorrelationId.Trim(),
        });
        PurchaseOrderLine orderLine = order.Lines.Single();
        dbContext.OrderLines.Add(new PurchaseOrderLineRow
        {
            CompanyId = request.CompanyId,
            OrderId = order.Id,
            LineNo = 1,
            LineId = orderLine.Id,
            Kind = (int)orderLine.Kind,
            ParentLineId = null,
            SourceRequestLineId = null,
            ProductId = orderLine.ProductId,
            ProductVersion = request.Line.ProductVersion,
            UnitId = orderLine.UnitId,
            UnitIsKilogram = request.Line.IsKilogram,
            Ordered = orderLine.Quantity.Value,
            Technical = 0m,
            Accepted = 0m,
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return new PurchaseOrderFixtureOutcome(order.Id, order.Version, order.Status, Added: true);
    }

    private void BindCompany(Guid companyId)
    {
        if (!companyContext.IsResolved)
        {
            companyContext.Set(companyId);
        }
        else if (companyContext.CompanyId != companyId)
        {
            throw new InvalidOperationException("A fixture order is written in its own company's context only.");
        }
    }

    private static PurchaseOrder Build(PurchaseOrderFixtureRequest request)
    {
        PurchaseOrder order = PurchaseOrder.Create(request.OrderId, request.CompanyId, request.SupplierPartyId, request.At);
        Quantity quantity = Quantity.Positive(
            request.Line.Quantity,
            request.Line.IsKilogram ? QuantityUnit.Kilogram : QuantityUnit.Piece);
        order.AddLine(order.Version, request.Line.LineId, request.Line.ProductId, request.Line.UnitId, quantity, request.At);
        return order;
    }

    private static void Validate(PurchaseOrderFixtureRequest request)
    {
        if (request.OrderId == Guid.Empty || request.CompanyId == Guid.Empty || request.SupplierPartyId == Guid.Empty
            || request.CompanyAccountId == Guid.Empty || request.Line.LineId == Guid.Empty
            || request.Line.ProductId == Guid.Empty || request.Line.UnitId == Guid.Empty)
        {
            throw new InvalidOperationException("A fixture order needs every declared identity.");
        }

        if (string.IsNullOrWhiteSpace(request.Label)
            || !request.Label.StartsWith(FixturePrefix, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(request.CreatedBy)
            || !request.CreatedBy.StartsWith(request.Label, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            throw new InvalidOperationException("A fixture order needs the suite label, writer and correlation.");
        }

        string productVersion = request.Line.ProductVersion;
        if (productVersion.Length == 0 || productVersion[0] == '0'
            || !long.TryParse(productVersion, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed)
            || parsed <= 0 || parsed.ToString(CultureInfo.InvariantCulture) != productVersion)
        {
            throw new InvalidOperationException("A fixture order needs a canonical positive product version.");
        }
    }

    private static bool Matches(
        PurchaseOrderRow order,
        PurchaseOrderLineRow? line,
        PurchaseOrderFixtureRequest request)
    {
        bool stateMatches = ((PurchaseOrderStatus)order.Status, order.Version) switch
        {
            (PurchaseOrderStatus.Draft, 2) => true,
            (PurchaseOrderStatus.Approved, 3) => true,
            _ => false,
        };
        return stateMatches
            && order.CompanyId == request.CompanyId
            && order.SupplierPartyId == request.SupplierPartyId
            && order.CompanyAccountId == request.CompanyAccountId
            && order.SourceRequestId is null
            && order.EffectiveOn == request.EffectiveOn
            && string.Equals(order.CreatedBy, request.CreatedBy.Trim(), StringComparison.Ordinal)
            && line is not null
            && line.CompanyId == request.CompanyId
            && line.LineNo == 1
            && line.LineId == request.Line.LineId
            && line.Kind == (int)PurchaseOrderLineKind.General
            && line.ParentLineId is null
            && line.SourceRequestLineId is null
            && line.ProductId == request.Line.ProductId
            && string.Equals(line.ProductVersion, request.Line.ProductVersion, StringComparison.Ordinal)
            && line.UnitId == request.Line.UnitId
            && line.UnitIsKilogram == request.Line.IsKilogram
            && line.Ordered == request.Line.Quantity
            && line.Technical == 0m
            && line.Accepted == 0m;
    }
}
