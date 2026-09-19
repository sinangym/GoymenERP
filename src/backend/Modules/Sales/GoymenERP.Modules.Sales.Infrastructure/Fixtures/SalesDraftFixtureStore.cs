using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Sales.Infrastructure.Fixtures;

/// <summary>
/// Sales's write port for one explicit synthetic order draft (coordinator decision W1/C, browser suite
/// s04): a gated fixture writer for a write the product has no endpoint for — a Preparing draft whose
/// owner snapshots are synthetic, so that the approval axis (SALES-A1) can be exercised in a real browser
/// while the positive create path still waits for the Product activation and the GDC data. Sales does
/// not know the gate: the port is added to DI only by the host's gated fixture-writer activation, and no
/// route or seeder reaches it.
/// <list type="bullet">
/// <item>The draft is built through the domain (<see cref="SalesOrderDraft.Create"/>, <c>AddLine</c>,
/// <c>RequireStorable</c>) and written as exactly the rows the store writes (<see cref="SalesDraftRows"/>),
/// so the store's Restore rebuilds it; a request the domain refuses is not stored.</item>
/// <item>It writes no DraftAttempt (a seed is not a command; nothing replays it) and no AuditEvent
/// (decision C: fixture data is not business activity). Every later transition is a real command.</item>
/// <item>The document number and every product name must carry the caller's label (the suite's
/// <c>SENTETIK browser-suite:…</c> prefix), so every such row is traceable to its fixture. The
/// DraftNumbers counter is untouched: the synthetic number has its own format.</item>
/// <item>A re-run accepts the draft already present under the id with the same document number, whatever
/// state the axis has since reached; a different draft under the same id fails.</item>
/// </list>
/// </summary>
public interface ISalesDraftFixtureStore
{
    Task<SalesDraftFixtureOutcome> EnsureAsync(SalesDraftFixtureRequest request, CancellationToken cancellationToken);
}

/// <summary>One explicit synthetic Preparing draft; the domain validates every field at build time.</summary>
public sealed record SalesDraftFixtureRequest(
    Guid DraftId,
    Guid CompanyId,
    string DocumentNo,
    string Label,
    DateOnly BusinessDate,
    SalesDraftFixtureOwners Owners,
    IReadOnlyList<SalesDraftFixtureLine> Lines,
    string CreatedBy,
    string CorrelationId,
    DateTimeOffset At);

/// <summary>The synthetic owner identities the header and the money snapshot name (no owner is read).</summary>
public sealed record SalesDraftFixtureOwners(
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CurrencyId,
    Guid MoneyProfileId,
    DateOnly EffectiveFrom);

/// <summary>One synthetic line: the product and unit snapshots are built from these values, one binding for stock, sales and order unit.</summary>
public sealed record SalesDraftFixtureLine(
    Guid LineId,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    Guid UnitId,
    Guid UnitProfileId,
    string UnitCode,
    string DimensionCode,
    bool IsKilogram,
    int Scale,
    string QuantityText,
    decimal Quantity);

/// <summary><paramref name="Added"/> is false when the draft was already present (a re-run); <paramref name="Version"/> is the stored one.</summary>
public sealed record SalesDraftFixtureOutcome(Guid DraftId, long Version, bool Added);

public static class SalesDraftFixtureStoreRegistration
{
    public static IServiceCollection AddSalesDraftFixtureStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<ISalesDraftFixtureStore, SalesDraftFixtureStore>();
        return services;
    }
}

internal sealed class SalesDraftFixtureStore(SalesDbContext dbContext, ICompanyContext companyContext) : ISalesDraftFixtureStore
{
    private const string SnapshotVersion = "1";
    private const int MoneyScale = 2;

    public async Task<SalesDraftFixtureOutcome> EnsureAsync(SalesDraftFixtureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Owners);
        ArgumentNullException.ThrowIfNull(request.Lines);
        if (string.IsNullOrWhiteSpace(request.Label))
        {
            throw new InvalidOperationException("A fixture draft needs the suite's label.");
        }

        if (!request.DocumentNo.StartsWith(request.Label, StringComparison.Ordinal)
            || request.Lines.Any(line => !line.ProductName.StartsWith(request.Label, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("A fixture draft's document number and product names must carry the suite's label.");
        }

        if (string.IsNullOrWhiteSpace(request.CreatedBy) || string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            throw new InvalidOperationException("A fixture draft names its writer and its correlation.");
        }

        // The context is the ambient one of the fixture runner's scope: bound here, and only to the request's company.
        if (!companyContext.IsResolved)
        {
            companyContext.Set(request.CompanyId);
        }
        else if (companyContext.CompanyId != request.CompanyId)
        {
            throw new InvalidOperationException("A fixture draft is written in its own company's context only.");
        }

        var draft = Build(request);
        var existing = await dbContext.Drafts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == request.DraftId, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.DocumentNo, draft.DocumentNo, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A different Sales draft already uses this fixture id.");
            }

            return new SalesDraftFixtureOutcome(existing.Id, existing.Version, Added: false);
        }

        SalesDraftRows.Add(dbContext, draft, request.CreatedBy, request.CorrelationId);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new SalesDraftFixtureOutcome(draft.Id, draft.Version, Added: true);
    }

    /// <summary>The domain builds and validates the draft exactly as a real create would; only the owner snapshots are synthetic.</summary>
    private static SalesOrderDraft Build(SalesDraftFixtureRequest request)
    {
        var owners = request.Owners;
        var header = new SalesDraftHeader(owners.PartyId, owners.CompanyAccountId, owners.CurrencyId, request.BusinessDate);
        var money = new MoneyProfileSnapshot(request.CompanyId, owners.CurrencyId, SalesOrderDraft.MoneyOperation, owners.MoneyProfileId,
            SnapshotVersion, request.BusinessDate, IsActive: true, owners.EffectiveFrom, EffectiveThrough: null, MoneyScale);
        var draft = SalesOrderDraft.Create(request.DraftId, request.CompanyId, request.DocumentNo, header, money, request.At);
        foreach (var line in request.Lines)
        {
            var unit = new UnitProfileSnapshot(request.CompanyId, line.UnitId, line.UnitCode, line.DimensionCode, line.UnitProfileId,
                SnapshotVersion, line.IsKilogram, line.Scale, line.Scale, IsActive: true, owners.EffectiveFrom, EffectiveThrough: null);
            var binding = new UnitBindingSnapshot(request.CompanyId, line.UnitId, line.UnitProfileId, SnapshotVersion);
            var product = new ProductSnapshot(line.ProductId, SnapshotVersion, line.ProductCode, line.ProductName, request.BusinessDate,
                binding, binding, binding);
            draft.AddLine(draft.Version, new SalesDraftLineInput(line.LineId, product, unit, line.QuantityText, line.Quantity), request.At);
        }

        draft.RequireStorable();
        return draft;
    }
}
