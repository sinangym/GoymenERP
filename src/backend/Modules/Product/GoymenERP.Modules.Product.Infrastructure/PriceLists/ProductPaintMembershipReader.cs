using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

/// <summary>The explicit company list is the only paint membership source. No product name or taxonomy is classified.</summary>
internal sealed class ProductPaintMembershipReader(ProductPriceListDbContext database, ICompanyContext companyContext,
    ICurrentUser currentUser) : IProductPaintMembershipReaderV1
{
    internal const string Source = "product.paint-memberships.v1";
    internal const int Active = (int)Domain.Products.ProductLifecycle.Active;
    public async Task<ProductPaintMembershipReadV1> GetAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var frame = Frame();
        ProductPaintMembershipReadV1 Without(ProductPaintMembershipReadOutcomeV1 outcome) => new(outcome, frame.CompanyId, productId, null, Source);
        if (!Valid(frame)) return Without(ProductPaintMembershipReadOutcomeV1.Unavailable);
        try
        {
            var version = await HeadAsync(cancellationToken);
            if (!Valid(frame)) return Without(ProductPaintMembershipReadOutcomeV1.Unavailable);
            var product = await database.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == productId, cancellationToken);
            if (!Valid(frame)) return Without(ProductPaintMembershipReadOutcomeV1.Unavailable);
            if (product is null) return Without(ProductPaintMembershipReadOutcomeV1.NotFound);
            if (product.Lifecycle != Active) return Without(ProductPaintMembershipReadOutcomeV1.Inactive);
            var line = await database.PaintMembershipLines.AsNoTracking().SingleOrDefaultAsync(x => x.Version == version && x.ProductId == productId, cancellationToken);
            if (!Valid(frame)) return Without(ProductPaintMembershipReadOutcomeV1.Unavailable);
            var observed = await database.Products.AsNoTracking().Where(x => x.Id == productId).Select(x => new { x.Version, x.Lifecycle }).SingleOrDefaultAsync(cancellationToken);
            var fresh = await HeadAsync(cancellationToken);
            if (!Valid(frame) || fresh != version || observed is null || observed.Version != product.Version || observed.Lifecycle != product.Lifecycle)
                return Without(ProductPaintMembershipReadOutcomeV1.Unavailable);
            return line is null ? Without(ProductPaintMembershipReadOutcomeV1.NoSource)
                : new(ProductPaintMembershipReadOutcomeV1.Available, frame.CompanyId, productId, Entry(product, line), Source);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { return Without(ProductPaintMembershipReadOutcomeV1.Unavailable); }
    }

    public Task<ProductPaintMembershipCatalogV1> ListAsync(CancellationToken cancellationToken = default) => ListCoreAsync(false, cancellationToken);
    internal Task<ProductPaintMembershipCatalogV1> ListForMaintenanceAsync(CancellationToken cancellationToken = default) => ListCoreAsync(true, cancellationToken);
    private async Task<ProductPaintMembershipCatalogV1> ListCoreAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var frame = Frame();
        ProductPaintMembershipCatalogV1 Without(ProductPaintMembershipCatalogOutcomeV1 outcome) => new(outcome, frame.CompanyId, "0", [], Source);
        if (!Valid(frame)) return Without(ProductPaintMembershipCatalogOutcomeV1.Unavailable);
        try
        {
            var version = await HeadAsync(cancellationToken);
            if (!Valid(frame)) return Without(ProductPaintMembershipCatalogOutcomeV1.Unavailable);
            if (version == 0) return Without(ProductPaintMembershipCatalogOutcomeV1.NoSource);
            var joined = await (from line in database.PaintMembershipLines.AsNoTracking()
                join product in database.Products.AsNoTracking() on line.ProductId equals product.Id
                where line.Version == version && (includeInactive || product.Lifecycle == Active)
                orderby product.Code, product.Id
                select new { Product = product, Line = line }).Take(ProductPaintMembershipLimitsV1.MaximumRows + 1).ToListAsync(cancellationToken);
            if (!Valid(frame) || joined.Count > ProductPaintMembershipLimitsV1.MaximumRows) return Without(ProductPaintMembershipCatalogOutcomeV1.Unavailable);
            // Re-read the complete set, including lifecycle and product versions: a concurrent global card change cannot
            // be released as a source-backed current answer. An empty real list remains distinct from no source.
            var proof = await (from line in database.PaintMembershipLines.AsNoTracking()
                join product in database.Products.AsNoTracking() on line.ProductId equals product.Id
                where line.Version == version && (includeInactive || product.Lifecycle == Active)
                orderby product.Code, product.Id
                select new { product.Id, product.Version }).Take(ProductPaintMembershipLimitsV1.MaximumRows + 1).ToListAsync(cancellationToken);
            var fresh = await HeadAsync(cancellationToken);
            if (!Valid(frame) || fresh != version || !joined.Select(x => (x.Product.Id, x.Product.Version)).SequenceEqual(proof.Select(x => (x.Id, x.Version))))
                return Without(ProductPaintMembershipCatalogOutcomeV1.Unavailable);
            return new(ProductPaintMembershipCatalogOutcomeV1.Available, frame.CompanyId, Text(version), joined.Select(x => Entry(x.Product, x.Line)).ToArray(), Source);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { return Without(ProductPaintMembershipCatalogOutcomeV1.Unavailable); }
    }

    private async Task<long> HeadAsync(CancellationToken ct) => await database.PaintMembershipVersions.AsNoTracking().MaxAsync(x => (long?)x.Version, ct) ?? 0;
    private (Guid CompanyId, string? ActorId) Frame() => (companyContext.IsResolved ? companyContext.CompanyId : Guid.Empty, currentUser.UserId);
    private bool Valid((Guid CompanyId, string? ActorId) frame) => frame.CompanyId != Guid.Empty && !string.IsNullOrWhiteSpace(frame.ActorId)
        && currentUser.IsAuthenticated && companyContext.IsResolved && companyContext.CompanyId == frame.CompanyId
        && string.Equals(currentUser.UserId, frame.ActorId, StringComparison.Ordinal) && currentUser.CanAccessCompany(frame.CompanyId);
    internal static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
    internal static ProductPaintMembershipEntryV1 Entry(PriceListProductReference product, ProductPaintMembershipLineRow line) =>
        new(product.Id, Text(product.Version), Text(line.MembershipVersion), product.Code, product.Name, Source);
}
