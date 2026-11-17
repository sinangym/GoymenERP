using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

public sealed record ProductCogsClassificationStageV1(int StatusCode, string? Code,
    ProductCogsClassificationViewV1? View, bool Changed, string? BeforeJson, string? AfterJson);

/// <summary>Writes only Product's immutable classification history in the host transaction. Audit/global terminal/commit belong to the host.</summary>
public sealed class ProductCogsClassificationParticipant : IAsyncDisposable
{
    public const string EventType = "product.card.cogs-classification.changed.v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ProductDbContext db;
    private readonly DbConnection connection;
    private readonly DbTransaction transaction;
    private readonly GroupSharedMasterCommandContext authority;
    private ProductCogsClassificationRow? staged;
    private bool attempted, disposed;
    public bool IsFlushed { get; private set; }
    private ProductCogsClassificationParticipant(ProductDbContext db, DbConnection connection, DbTransaction transaction,
        GroupSharedMasterCommandContext authority)
    { this.db = db; this.connection = connection; this.transaction = transaction; this.authority = authority; }
    public static async Task<ProductCogsClassificationParticipant> EnlistAsync(DbConnection connection,
        DbTransaction transaction, GroupSharedMasterCommandContext authority, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(connection); ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(authority); token.ThrowIfCancellationRequested();
        if (authority.Operation != GroupSharedMasterOperation.SetCogsClassification) throw new UnauthorizedAccessException();
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection)
            || transaction.IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Classification changes require the host's serializable transaction.");
        var db = new ProductDbContext(new DbContextOptionsBuilder<ProductDbContext>()
            .UseSqlServer(connection, contextOwnsConnection: false).Options);
        try
        {
            await db.Database.UseTransactionAsync(transaction, token);
            return new(db, connection, transaction, authority);
        }
        catch { await db.DisposeAsync(); throw; }
    }
    public async Task<ProductCogsClassificationStageV1> StageAsync(Guid productId, long expectedProductVersion,
        long expectedClassificationVersion, ProductCogsClassificationKindV1 kind, string reason, string correlationId,
        CancellationToken token = default)
    {
        EnsureActive(); token.ThrowIfCancellationRequested();
        if (attempted) throw new InvalidOperationException("One classification attempt may stage only once.");
        attempted = true;
        if (productId == Guid.Empty || expectedProductVersion < 1 || expectedClassificationVersion < 0
            || kind is not (ProductCogsClassificationKindV1.TradeGoods or ProductCogsClassificationKindV1.Manufactured)
            || !Canonical(reason, ProductCogsClassificationContractsV1.MaximumReasonLength) || !Canonical(correlationId,128))
            throw new ArgumentException("Invalid classification intent.");
        // Serializable reads hold both the actual Product version and the latest classification range through commit.
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == productId, token);
        if (product is null) { IsFlushed = true; return new(404, ProductCogsClassificationCodesV1.NotFound, null, false, null, null); }
        var current = await db.CogsClassifications.AsNoTracking().Where(x => x.ProductId == productId)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync(token);
        var before = View(authority.OriginCompanyId, product.Id, product.Version, current);
        if (product.Version != expectedProductVersion || (current?.Version ?? 0) != expectedClassificationVersion)
        { IsFlushed = true; return new(409, ProductCogsClassificationCodesV1.VersionConflict, null, false, null, null); }
        if (current?.Kind == (int)kind)
        { IsFlushed = true; return new(200, null, before, false, null, null); }
        staged = new()
        {
            ProductId = productId, Version = checked(expectedClassificationVersion + 1), RecordedProductVersion = product.Version,
            Kind = (int)kind, OriginCompanyId = authority.OriginCompanyId, ActorId = authority.LocalActorId,
            AuthorityRevision = authority.AuthorityRevision, Reason = reason, CorrelationId = correlationId,
            RecordedAtUtc = TimeProvider.System.GetUtcNow(),
        };
        db.CogsClassifications.Add(staged);
        var after = View(authority.OriginCompanyId, product.Id, product.Version, staged);
        await db.FlushCogsEnlistedAsync(this, token); IsFlushed = true;
        return new(200, null, after, true, JsonSerializer.Serialize(before,Json), JsonSerializer.Serialize(after,Json));
    }
    public static ProductCogsClassificationViewV1 View(Guid companyId, Guid productId, long productVersion,
        ProductCogsClassificationRow? row)
    {
        if (companyId == Guid.Empty || productId == Guid.Empty || productVersion < 1
            || row is not null && (row.ProductId != productId || row.Version < 1 || row.RecordedProductVersion < 1
                || row.RecordedProductVersion > productVersion || row.Kind is not (1 or 2) || row.OriginCompanyId == Guid.Empty
                || row.ActorId == Guid.Empty || !Canonical(row.AuthorityRevision,128) || !Canonical(row.Reason,1000)
                || !Canonical(row.CorrelationId,128) || row.RecordedAtUtc.Offset != TimeSpan.Zero))
            throw new InvalidOperationException("Stored classification evidence is invalid.");
        return new(companyId,productId,productVersion.ToString(CultureInfo.InvariantCulture),
            (row?.Version ?? 0).ToString(CultureInfo.InvariantCulture),
            row is null ? null : (ProductCogsClassificationKindV1)row.Kind, row?.OriginCompanyId,row?.RecordedAtUtc,
            ProductCogsClassificationContractsV1.SourceVersion);
    }
    internal void ValidateFlush(ProductDbContext candidate)
    {
        EnsureActive();
        var changes = db.ChangeTracker.Entries().Where(x => x.State is not (EntityState.Unchanged or EntityState.Detached)).ToArray();
        if (!ReferenceEquals(candidate,db) || staged is null || changes.Length != 1
            || !ReferenceEquals(changes[0].Entity,staged) || changes[0].State != EntityState.Added
            || staged.OriginCompanyId != authority.OriginCompanyId || staged.ActorId != authority.LocalActorId
            || staged.AuthorityRevision != authority.AuthorityRevision)
            throw new InvalidOperationException("Only the one enrolled append-only classification row may be flushed.");
    }
    private static bool Canonical(string? text,int maximum) => !string.IsNullOrWhiteSpace(text)
        && text.Length <= maximum && text == text.Trim() && !text.Any(char.IsControl);
    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection,connection)
            || db.Database.CurrentTransaction is not { } current || !ReferenceEquals(current.GetDbTransaction(),transaction))
            throw new InvalidOperationException("Classification transaction is no longer active.");
    }
    public async ValueTask DisposeAsync() { if (disposed) return; disposed = true; await db.DisposeAsync(); }
}
