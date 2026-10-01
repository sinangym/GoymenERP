using System.Data;
using System.Data.Common;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

public sealed record ProductImportAuditFact(
    Guid ProductId,
    string EventType,
    long VersionAfter,
    string BeforeJson,
    string AfterJson,
    string Reason);

public sealed record ProductImportWriteResult
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal ProductImportWriteResult(IReadOnlyList<ProductImportCreatedV1> products,
        IReadOnlyList<ProductCardView> cards)
    {
        if (products.Count == 0 || products.Count != cards.Count
            || products.Select((product, index) => product.ProductId != cards[index].Product.Id).Any(mismatch => mismatch))
            throw new InvalidOperationException("Product import write facts are incomplete.");
        Products = products.ToArray();
        Cards = cards.ToArray();
        AuditFacts = cards.Select(card => new ProductImportAuditFact(card.Product.Id,
            "product.draft.created.v1", 1, "{}", JsonSerializer.Serialize(card, Json),
            "Ürün taslağı oluşturma")).ToArray();
    }

    public IReadOnlyList<ProductImportCreatedV1> Products { get; }
    public IReadOnlyList<ProductCardView> Cards { get; }
    public IReadOnlyList<ProductImportAuditFact> AuditFacts { get; }
}

/// <summary>
/// Product-owned participant for the Q16 all-or-nothing apply. It borrows the host transaction, performs the same
/// catalog checks as preview against that transaction, binds the exact source bytes/mapping/token, stages every Draft
/// and both code registrations, then flushes Product only. Audit, idempotency terminal and commit remain host-owned.
/// </summary>
public sealed class ProductImportParticipant : IAsyncDisposable
{
    private readonly ProductDbContext context;
    private readonly DbConnection connection;
    private readonly DbTransaction transaction;
    private readonly GroupSharedMasterCommandContext authority;
    private readonly TimeProvider clock;
    private bool disposed;
    private int started;
    private ProductImportWriteResult? staged;

    private ProductImportParticipant(ProductDbContext context, DbConnection connection, DbTransaction transaction,
        GroupSharedMasterCommandContext authority, TimeProvider clock)
    {
        this.context = context;
        this.connection = connection;
        this.transaction = transaction;
        this.authority = authority;
        this.clock = clock;
    }

    public static async Task<ProductImportParticipant> EnlistAsync(DbConnection connection, DbTransaction transaction,
        GroupSharedMasterCommandContext authority, CancellationToken cancellationToken, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(authority);
        cancellationToken.ThrowIfCancellationRequested();
        if (authority.Operation != GroupSharedMasterOperation.DraftCreate)
            throw new UnauthorizedAccessException("Ürün içe aktarma taslak oluşturma yetkisi gerektirir.");
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection))
            throw new InvalidOperationException("Product import requires the owner's active transaction.");
        var options = new DbContextOptionsBuilder<ProductDbContext>()
            .UseSqlServer(connection, contextOwnsConnection: false).Options;
        var context = new ProductDbContext(options);
        try
        {
            await context.Database.UseTransactionAsync(transaction, cancellationToken);
            return new(context, connection, transaction, authority, clock ?? TimeProvider.System);
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    public async Task<ProductImportWriteResult> ExecuteAsync(ReadOnlyMemory<byte> workbook,
        ProductImportApplyBindingV1 binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        EnsureActive();
        if (Interlocked.Exchange(ref started, 1) != 0)
            throw new InvalidOperationException("A Product import participant can execute only once.");
        if (context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("A Product import requires a fresh context.");

        var groups = await context.CodeGroups.AsNoTracking().ToDictionaryAsync(row => row.Id, cancellationToken);
        var codes = new HashSet<string>(
            await context.CodeRegistrations.AsNoTracking().Select(row => row.Code).ToListAsync(cancellationToken),
            StringComparer.Ordinal);
        var preview = new ProductImportWorkbookReader().Read(workbook, binding.Mapping, codes, groups.Keys.ToHashSet());
        var boundToken = ProductImportPreviewTokenV1.Bind(preview, authority.LocalActorId,
            authority.OriginCompanyId, authority.AuthorityRevision);
        if (!string.Equals(preview.FileSha256, binding.FileSha256, StringComparison.Ordinal)
            || !string.Equals(boundToken, binding.PreviewToken, StringComparison.Ordinal))
            throw new ProductValidationException([new("previewToken", ProductImportCodesV1.PreviewMismatch)]);
        if (!preview.Ready)
            throw new ProductValidationException(preview.Errors.Select(error => new ProductFieldError(
                error.RowNumber is null ? error.Field : $"rows[{error.RowNumber}].{error.Field}", error.Code)).ToArray());

        // Every accepted legacy alias reserves the global Product code namespace BEFORE any canonical code is
        // allocated. Otherwise SAC-1 could be minted for this row while SAC-1 is also its legacy alias (or a later
        // row's alias), leaving the batch to fail at the database unique key after it was already declared Ready.
        foreach (var source in preview.Rows.OrderBy(row => row.RowNumber))
            if (!codes.Add(source.LegacyCode))
                throw new ProductValidationException([new($"rows[{source.RowNumber}].legacyCode",
                    ProductImportCodesV1.ExistingLegacyCode)]);

        var counters = new Dictionary<string, ProductCodeCounter>(StringComparer.Ordinal);
        foreach (var group in RequiredCounterGroups(groups, preview.Rows))
        {
            var counter = await context.CodeCounters.SingleOrDefaultAsync(row => row.Prefix == group.Prefix, cancellationToken);
            if (counter is null)
            {
                counter = ProductCodeCounter.ForGroup(group);
                context.CodeCounters.Add(counter);
            }
            counters.Add(group.Prefix, counter);
        }

        var created = new List<ProductImportCreatedV1>(preview.Rows.Count);
        var cards = new List<ProductCardView>(preview.Rows.Count);
        var now = clock.GetUtcNow();
        foreach (var source in preview.Rows.OrderBy(row => row.RowNumber))
        {
            var group = groups[source.GroupId];
            var code = AllocateNext(counters[group.Prefix], codes);
            var product = Domain.Products.Product.CreateDraft(Guid.NewGuid(), code, source.Name, source.GroupId,
                classId: null, familyId: null, ProductRoles.NotEvaluated, now);
            var row = ProductRow.FromRecord(ProductStoredRecord.Capture(product, new ProductReferences()));
            context.Products.Add(row);
            context.CodeRegistrations.Add(ProductCodeRegistration.Current(row));
            context.CodeRegistrations.Add(ProductCodeRegistration.Legacy(row, source.LegacyCode));
            created.Add(new(row.Id, row.Code, source.LegacyCode, row.Name, row.GroupId));
            cards.Add(new(ProductDraftView.FromProduct(product, new(group.Name, null, null, null)), new ProductReferences()));
        }

        staged = new(created, cards);
        await context.FlushImportEnlistedAsync(this, cancellationToken);
        return staged;
    }

    internal static IReadOnlyList<ProductCodeGroupRow> RequiredCounterGroups(
        IReadOnlyDictionary<Guid, ProductCodeGroupRow> groups,
        IReadOnlyList<ProductImportSourceRowV1> rows) =>
        rows.Select(row => groups[row.GroupId])
            .GroupBy(group => group.Prefix, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(group => group.Prefix, StringComparer.Ordinal)
            .ToArray();

    internal static ProductCode AllocateNext(ProductCodeCounter counter, ISet<string> reservedCodes)
    {
        while (true)
        {
            var candidate = counter.Advance();
            if (reservedCodes.Add(candidate.Value)) return candidate;
        }
    }

    internal void ValidateFlush(ProductDbContext candidate)
    {
        EnsureActive();
        if (!ReferenceEquals(candidate, context) || staged is null || staged.Products.Count == 0)
            throw new InvalidOperationException("The Product import has no matching staged participant.");
        var changes = context.ChangeTracker.Entries()
            .Where(entry => entry.State is not (EntityState.Unchanged or EntityState.Detached)).ToArray();
        var ids = staged.Products.Select(value => value.ProductId).ToHashSet();
        if (changes.Count(entry => entry.Entity is ProductRow row && entry.State == EntityState.Added && ids.Contains(row.Id)) != ids.Count
            || changes.Count(entry => entry.Entity is ProductCodeRegistration registration && entry.State == EntityState.Added
                && ids.Contains(registration.ProductId) && registration.IsCurrent) != ids.Count
            || changes.Count(entry => entry.Entity is ProductCodeRegistration registration && entry.State == EntityState.Added
                && ids.Contains(registration.ProductId) && !registration.IsCurrent) != ids.Count
            || changes.Any(entry => entry.Entity is not (ProductRow or ProductCodeRegistration or ProductCodeCounter)))
            throw new InvalidOperationException("Product import must stage only its cards, current codes, legacy aliases and counters.");
    }

    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection)
            || context.Database.CurrentTransaction is not { } enlisted || !ReferenceEquals(enlisted.GetDbTransaction(), transaction))
            throw new InvalidOperationException("The owner's Product import transaction is no longer active.");
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        await context.DisposeAsync();
    }
}
