using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.PriceLists;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

/// <summary>The audit/idempotency envelope of one write; every value is the service's server-side context.</summary>
internal sealed record PriceListWriteEnvelope(
    string ActorId,
    string PermissionCode,
    IReadOnlyCollection<string> RoleCodes,
    string CorrelationId,
    string? CausationId,
    string IdempotencyKey,
    string SourceChannel,
    string? ClientIpAddress,
    string? UserAgent);

/// <summary>ADR-011 §7: a terminal refusal recorded under the key and answered again on the same key.</summary>
internal sealed record PriceListTerminalRefusal(int Status, string Code, IReadOnlyList<ProductPriceListFieldErrorV1> Errors);

internal sealed record PriceListReplay(PriceList? PriceList, PriceListTerminalRefusal? Refusal, string OriginalCorrelationId);

internal sealed record PriceListStoreResult(PriceList PriceList, bool IsReplay, string OriginalCorrelationId);

/// <summary>The audit facts of one command.</summary>
internal sealed record PriceListAudit(string EventType, string Operation, string Reason);

/// <summary>A product card as the price-list checks see it: identity, lifecycle, the sellable role and its own units.</summary>
internal sealed record PriceListProductFacts(Guid Id, string Code, string Name, int Lifecycle, bool? Sellable, Guid? StockUnitId, Guid? SalesUnitId);

internal sealed class PriceListStoreException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}

internal static class PriceListStoreCodes
{
    public const string CompanyContextMismatch = "product_price_list_company_context_mismatch";
    public const string SameKeyPayloadMismatch = "product_price_list_same_key_payload_mismatch";
    public const string ConcurrentChange = "product_price_list_concurrent_change";
    public const string InvalidCursor = "product_price_list_invalid_cursor";
}

/// <summary>
/// The price-list store of the server-resolved company (the Logistics store's shape). One call is one save: the list
/// rows, the attempt record and the audit event commit together or not at all. A stored list is rebuilt through the
/// domain (<see cref="PriceList.Restore"/>), so a corrupt row is refused, not repaired. Another company's list is
/// invisible (null → 404, never 403). Replay identity (ADR-011): (company, command, SHA-256 of the key); the raw key
/// is never stored; the same key with the same content and actor answers the stored result, another content or actor
/// is one conflict.
/// </summary>
internal sealed class ProductPriceListStore(
    ProductPriceListDbContext dbContext,
    ICompanyContext companyContext,
    IAuditEventCollector auditEvents,
    TimeProvider timeProvider)
{
    private const string EntityType = "Product.PriceList";
    private const int AttemptOutcomeRecorded = 1;
    private const int AttemptOutcomeRefused = 2;
    private const int ProductActive = (int)Domain.Products.ProductLifecycle.Active;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PriceListReplay?> FindReplayAsync(PriceListWriteEnvelope envelope, string requestHash, string commandName,
        CancellationToken cancellationToken)
    {
        RequireResolvedCompany();
        var attempt = await FindAttemptAsync(commandName, IdempotencyKeyHash(envelope.IdempotencyKey), cancellationToken);
        if (attempt is null)
        {
            return null;
        }

        RequireSameContent(attempt, envelope.ActorId, requestHash);
        if (attempt.Outcome == AttemptOutcomeRecorded && attempt.PriceListId is { } priceListId)
        {
            var stored = await LoadAsync(priceListId, cancellationToken)
                ?? throw new InvalidOperationException("Kayıtlı denemenin fiyat listesi bulunamadı.");
            return new PriceListReplay(stored, null, attempt.CorrelationId);
        }

        var refusal = attempt.RefusalJson is null ? null : JsonSerializer.Deserialize<PriceListTerminalRefusal>(attempt.RefusalJson, Json);
        if (refusal is null || refusal.Status is < 400 or > 599 || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null)
        {
            throw new InvalidOperationException("Kayıtlı terminal ret okunamadı.");
        }

        return new PriceListReplay(null, refusal, attempt.CorrelationId);
    }

    public async Task RecordRefusalAsync(PriceListWriteEnvelope envelope, string requestHash, string commandName,
        PriceListTerminalRefusal refusal, CancellationToken cancellationToken)
    {
        var companyId = RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        if (await FindAttemptAsync(commandName, keyHash, cancellationToken) is not null)
        {
            throw new PriceListStoreException(PriceListStoreCodes.ConcurrentChange, "Bu anahtar zaten bir deneme taşıyor.");
        }

        dbContext.ChangeTracker.Clear();
        var row = Attempt(companyId, commandName, keyHash, requestHash, envelope, null);
        row.Outcome = AttemptOutcomeRefused;
        row.RefusalJson = JsonSerializer.Serialize(refusal, Json);
        dbContext.Attempts.Add(row);
        await SaveAsync(cancellationToken);
    }

    public Task<bool> NameExistsAsync(string name, Guid? exceptPriceListId, CancellationToken cancellationToken) =>
        dbContext.PriceLists.AsNoTracking().AnyAsync(row => row.Name == name && row.Id != exceptPriceListId, cancellationToken);

    public async Task<PriceListStoreResult> CreateAsync(PriceListWriteEnvelope envelope, string requestHash, PriceList priceList,
        CancellationToken cancellationToken)
    {
        var companyId = RequireResolvedCompany();
        if (priceList.CompanyId != companyId)
        {
            throw new PriceListStoreException(PriceListStoreCodes.CompanyContextMismatch, "Liste aktif şirketin değil.");
        }

        if (await dbContext.PriceLists.AsNoTracking().AnyAsync(row => row.Id == priceList.Id, cancellationToken))
        {
            throw new PriceListStoreException(PriceListStoreCodes.ConcurrentChange, "Bu liste kimliği kullanılıyor.");
        }

        dbContext.PriceLists.Add(new PriceListRow
        {
            Id = priceList.Id,
            CompanyId = companyId,
            Name = priceList.Name,
            CurrencyCode = priceList.CurrencyCode,
            Channel = (int)priceList.Channel,
            VatBasis = (int)priceList.VatBasis,
            Version = priceList.Version,
            CreatedAtUtc = priceList.CreatedAtUtc,
            UpdatedAtUtc = priceList.UpdatedAtUtc,
            CreatedBy = priceList.CreatedBy,
            CorrelationId = envelope.CorrelationId,
        });
        foreach (var version in priceList.Versions)
        {
            dbContext.Versions.Add(VersionRow(companyId, priceList.Id, version));
            foreach (var line in version.Lines)
            {
                dbContext.Lines.Add(LineRow(companyId, priceList.Id, version.VersionNo, line));
            }
        }

        dbContext.Attempts.Add(Attempt(companyId, ProductPriceListCommandsV1.CreateV1, IdempotencyKeyHash(envelope.IdempotencyKey),
            requestHash, envelope, priceList.Id));
        Audit(envelope, companyId, priceList.Id, new PriceListAudit("product.price-list.created.v1", "Create", "Fiyat listesi oluşturuldu"),
            null, priceList.Version, "{}", Snapshot(priceList));
        await SaveAsync(cancellationToken);
        return new PriceListStoreResult(priceList, false, envelope.CorrelationId);
    }

    /// <summary>
    /// One mutation of the company's stored list in ONE save: <paramref name="mutate"/> is the domain transition (and the
    /// service's product checks); the header, the changed versions and a Draft's lines follow it, with the attempt and the
    /// audit event. Unknown list → null. A lost race is <see cref="PriceListStoreCodes.ConcurrentChange"/>, nothing stored.
    /// </summary>
    public async Task<PriceListStoreResult?> MutateAsync(PriceListWriteEnvelope envelope, string requestHash, string commandName,
        Guid priceListId, Func<PriceList, DateTimeOffset, Task> mutate, PriceListAudit audit, CancellationToken cancellationToken)
    {
        var companyId = RequireResolvedCompany();
        var row = await dbContext.PriceLists.SingleOrDefaultAsync(candidate => candidate.Id == priceListId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var versionRows = await dbContext.Versions.Where(version => version.PriceListId == priceListId).ToListAsync(cancellationToken);
        var lineRows = await dbContext.Lines.Where(line => line.PriceListId == priceListId).ToListAsync(cancellationToken);
        var priceList = Restore(row, versionRows, lineRows);
        var versionBefore = priceList.Version;
        var before = Snapshot(priceList);
        var draftBefore = priceList.DraftVersion?.VersionNo;
        var now = timeProvider.GetUtcNow();
        await mutate(priceList, now);
        if (priceList.Version != versionBefore + 1 || priceList.Id != row.Id || priceList.CompanyId != companyId)
        {
            throw new InvalidOperationException("Bir komut tam olarak bir sürüm ilerletir.");
        }

        row.Name = priceList.Name;
        row.Version = priceList.Version;
        row.UpdatedAtUtc = priceList.UpdatedAtUtc;
        foreach (var version in priceList.Versions)
        {
            var stored = versionRows.SingleOrDefault(candidate => candidate.VersionNo == version.VersionNo);
            if (stored is null)
            {
                dbContext.Versions.Add(VersionRow(companyId, priceList.Id, version));
                foreach (var line in version.Lines)
                {
                    dbContext.Lines.Add(LineRow(companyId, priceList.Id, version.VersionNo, line));
                }

                continue;
            }

            if (stored.State == (int)PriceListVersionState.Published)
            {
                continue;
            }

            stored.ValidFrom = version.ValidFrom;
            stored.ValidThrough = version.ValidThrough;
            stored.State = (int)version.State;
            stored.PublishedAtUtc = version.PublishedAtUtc;
            stored.PublishedBy = version.PublishedBy;
            if (version.VersionNo == draftBefore)
            {
                ReplaceDraftLines(companyId, priceList.Id, version, lineRows.Where(line => line.VersionNo == version.VersionNo).ToList());
            }
        }

        dbContext.Attempts.Add(Attempt(companyId, commandName, IdempotencyKeyHash(envelope.IdempotencyKey), requestHash, envelope, priceList.Id));
        Audit(envelope, companyId, priceList.Id, audit, versionBefore, priceList.Version, before, Snapshot(priceList));
        await SaveAsync(cancellationToken);
        return new PriceListStoreResult(priceList, false, envelope.CorrelationId);
    }

    public async Task<PriceList?> LoadAsync(Guid priceListId, CancellationToken cancellationToken)
    {
        RequireResolvedCompany();
        if (priceListId == Guid.Empty)
        {
            return null;
        }

        var row = await dbContext.PriceLists.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == priceListId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var versionRows = await dbContext.Versions.AsNoTracking().Where(version => version.PriceListId == priceListId).ToListAsync(cancellationToken);
        var lineRows = await dbContext.Lines.AsNoTracking().Where(line => line.PriceListId == priceListId).ToListAsync(cancellationToken);
        return Restore(row, versionRows, lineRows);
    }

    /// <summary>The company's lists, most recently changed first, by an opaque company-bound cursor.</summary>
    public async Task<(IReadOnlyList<PriceList> Items, string? NextCursor)> ListAsync(int pageSize, string? cursor, CancellationToken cancellationToken)
    {
        var companyId = RequireResolvedCompany();
        var query = dbContext.PriceLists.AsNoTracking();
        if (cursor is not null)
        {
            var (updatedAtUtc, id) = ListCursor.Decode(cursor, companyId);
            query = query.Where(row => row.UpdatedAtUtc < updatedAtUtc || (row.UpdatedAtUtc == updatedAtUtc && row.Id.CompareTo(id) < 0));
        }

        var rows = await query.OrderByDescending(row => row.UpdatedAtUtc).ThenByDescending(row => row.Id).Take(pageSize + 1)
            .ToListAsync(cancellationToken);
        var page = rows.Take(pageSize).ToList();
        var ids = page.Select(row => row.Id).ToList();
        var versionRows = await dbContext.Versions.AsNoTracking().Where(version => ids.Contains(version.PriceListId)).ToListAsync(cancellationToken);
        var lineRows = await dbContext.Lines.AsNoTracking().Where(line => ids.Contains(line.PriceListId)).ToListAsync(cancellationToken);
        var items = page.Select(row => Restore(row, versionRows.Where(version => version.PriceListId == row.Id).ToList(),
            lineRows.Where(line => line.PriceListId == row.Id).ToList())).ToList();
        var next = rows.Count > pageSize ? ListCursor.Encode(companyId, page[^1].UpdatedAtUtc, page[^1].Id) : null;
        return (items, next);
    }

    /// <summary>The published lines of this company's Sales-channel lists that price the product in one of the units.</summary>
    public async Task<IReadOnlyList<PriceList>> FindListsPricingAsync(Guid productId, string currencyCode, CancellationToken cancellationToken)
    {
        RequireResolvedCompany();
        var listIds = await dbContext.Lines.AsNoTracking().Where(line => line.ProductId == productId).Select(line => line.PriceListId)
            .Distinct().ToListAsync(cancellationToken);
        var rows = await dbContext.PriceLists.AsNoTracking()
            .Where(row => listIds.Contains(row.Id) && row.CurrencyCode == currencyCode && row.Channel == (int)PriceListChannel.Sales)
            .ToListAsync(cancellationToken);
        var ids = rows.Select(row => row.Id).ToList();
        var versionRows = await dbContext.Versions.AsNoTracking().Where(version => ids.Contains(version.PriceListId)).ToListAsync(cancellationToken);
        var lineRows = await dbContext.Lines.AsNoTracking().Where(line => ids.Contains(line.PriceListId)).ToListAsync(cancellationToken);
        return rows.Select(row => Restore(row, versionRows.Where(version => version.PriceListId == row.Id).ToList(),
            lineRows.Where(line => line.PriceListId == row.Id).ToList())).ToList();
    }

    // ---- the Product card, read-only ------------------------------------------------------------

    public async Task<IReadOnlyDictionary<Guid, PriceListProductFacts>> ReadProductsAsync(IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return new Dictionary<Guid, PriceListProductFacts>();
        }

        var ids = productIds.Distinct().ToList();
        var rows = await dbContext.Products.AsNoTracking().Where(product => ids.Contains(product.Id)).ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.Id, Facts);
    }

    /// <summary>Active, sellable cards whose code or name contains the text, code order, at most <paramref name="take"/>.</summary>
    public async Task<IReadOnlyList<PriceListProductFacts>> SearchProductsAsync(string? text, int take, CancellationToken cancellationToken)
    {
        var query = dbContext.Products.AsNoTracking().Where(product => product.Lifecycle == ProductActive && product.SellableValue == true);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var term = text.Trim();
            query = query.Where(product => product.Name.Contains(term) || product.Code.Contains(term));
        }

        var rows = await query.OrderBy(product => product.Code).Take(take).ToListAsync(cancellationToken);
        return rows.Select(Facts).ToList();
    }

    private static PriceListProductFacts Facts(PriceListProductReference row)
    {
        Guid? stock = null;
        Guid? sales = null;
        try
        {
            using var document = JsonDocument.Parse(row.ReferencesJson);
            if (document.RootElement.TryGetProperty("units", out var units) && units.ValueKind == JsonValueKind.Object)
            {
                stock = ReadUnit(units, "stock");
                sales = ReadUnit(units, "sales");
            }
        }
        catch (JsonException)
        {
            // A corrupt reference snapshot offers no unit: the card cannot be priced (fail closed), it is not repaired here.
        }

        return new PriceListProductFacts(row.Id, row.Code, row.Name, row.Lifecycle, row.SellableValue, stock, sales);
    }

    private static Guid? ReadUnit(JsonElement units, string name) =>
        units.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.TryGetGuid(out var id) && id != Guid.Empty
            ? id
            : null;

    // ---- rows ------------------------------------------------------------------------------------

    private void ReplaceDraftLines(Guid companyId, Guid priceListId, PriceListVersion version, List<PriceListLineRow> stored)
    {
        foreach (var row in stored.Where(row => version.Lines.All(line => line.LineId != row.LineId)))
        {
            dbContext.Lines.Remove(row);
        }

        foreach (var line in version.Lines)
        {
            var row = stored.SingleOrDefault(candidate => candidate.LineId == line.LineId);
            if (row is null)
            {
                dbContext.Lines.Add(LineRow(companyId, priceListId, version.VersionNo, line));
                continue;
            }

            row.LineNo = line.LineNo;
            row.ProductId = line.ProductId;
            row.PriceUnitId = line.PriceUnitId;
            row.UnitPrice = line.UnitPrice;
            row.FloorUnitPrice = line.FloorUnitPrice;
        }
    }

    private static PriceListVersionRow VersionRow(Guid companyId, Guid priceListId, PriceListVersion version) => new()
    {
        CompanyId = companyId,
        PriceListId = priceListId,
        VersionNo = version.VersionNo,
        VersionId = version.Id,
        ValidFrom = version.ValidFrom,
        ValidThrough = version.ValidThrough,
        State = (int)version.State,
        CreatedAtUtc = version.CreatedAtUtc,
        CreatedBy = version.CreatedBy,
        PublishedAtUtc = version.PublishedAtUtc,
        PublishedBy = version.PublishedBy,
    };

    private static PriceListLineRow LineRow(Guid companyId, Guid priceListId, int versionNo, PriceListLine line) => new()
    {
        CompanyId = companyId,
        PriceListId = priceListId,
        VersionNo = versionNo,
        LineNo = line.LineNo,
        LineId = line.LineId,
        ProductId = line.ProductId,
        PriceUnitId = line.PriceUnitId,
        UnitPrice = line.UnitPrice,
        FloorUnitPrice = line.FloorUnitPrice,
    };

    private static PriceList Restore(PriceListRow row, IReadOnlyList<PriceListVersionRow> versionRows, IReadOnlyList<PriceListLineRow> lineRows)
    {
        var versions = versionRows.Select(version => PriceListVersion.Restore(version.VersionId, version.VersionNo, version.ValidFrom,
            version.ValidThrough, (PriceListVersionState)version.State,
            lineRows.Where(line => line.VersionNo == version.VersionNo)
                .Select(line => new PriceListLine(line.LineId, line.LineNo, line.ProductId, line.PriceUnitId, line.UnitPrice, line.FloorUnitPrice))
                .ToList(),
            version.CreatedBy, version.CreatedAtUtc, version.PublishedBy, version.PublishedAtUtc)).ToList();
        return PriceList.Restore(row.Id, row.CompanyId, row.Name, row.CurrencyCode, (PriceListChannel)row.Channel,
            (PriceListVatBasis)row.VatBasis, row.Version, row.CreatedBy, row.CreatedAtUtc, row.UpdatedAtUtc, versions);
    }

    private Task<PriceListAttemptRow?> FindAttemptAsync(string commandName, string keyHash, CancellationToken cancellationToken) =>
        dbContext.Attempts.AsNoTracking().SingleOrDefaultAsync(row => row.CommandName == commandName && row.IdempotencyKeyHash == keyHash,
            cancellationToken);

    private static void RequireSameContent(PriceListAttemptRow attempt, string actorId, string requestHash)
    {
        // ADR-011 §3: another content or another actor under the same key is one conflict that discloses nothing.
        if (!string.Equals(attempt.ActorId, actorId, StringComparison.Ordinal)
            || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new PriceListStoreException(PriceListStoreCodes.SameKeyPayloadMismatch,
                "Aynı Idempotency-Key başka bir istekle geldi; ilk cevap bu isteğe verilmez.");
        }
    }

    private PriceListAttemptRow Attempt(Guid companyId, string commandName, string keyHash, string requestHash,
        PriceListWriteEnvelope envelope, Guid? priceListId) => new()
    {
        CompanyId = companyId,
        CommandName = commandName,
        IdempotencyKeyHash = keyHash,
        RequestHash = requestHash,
        ActorId = envelope.ActorId,
        Outcome = AttemptOutcomeRecorded,
        PriceListId = priceListId,
        CorrelationId = envelope.CorrelationId,
        RecordedAtUtc = timeProvider.GetUtcNow(),
    };

    private void Audit(PriceListWriteEnvelope envelope, Guid companyId, Guid priceListId, PriceListAudit audit, long? versionBefore,
        long versionAfter, string before, string after) =>
        auditEvents.Enqueue(new AuditEventDraft(companyId, audit.EventType, EntityType, priceListId.ToString("D"), audit.Operation,
            versionBefore, versionAfter, AuditActorTypes.User, envelope.ActorId, envelope.PermissionCode, envelope.RoleCodes, before, after,
            audit.Reason, ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null, envelope.SourceChannel,
            envelope.ClientIpAddress, envelope.UserAgent, envelope.CorrelationId, envelope.CausationId, envelope.IdempotencyKey));

    internal static string Snapshot(PriceList priceList) => JsonSerializer.Serialize(new
    {
        name = priceList.Name,
        currencyCode = priceList.CurrencyCode,
        channel = priceList.Channel.ToString(),
        vatBasis = priceList.VatBasis.ToString(),
        version = priceList.Version,
        versions = priceList.Versions.Select(version => new
        {
            versionNo = version.VersionNo,
            state = version.State.ToString(),
            validFrom = ProductPriceListWire.FormatDate(version.ValidFrom),
            validThrough = version.ValidThrough is { } through ? ProductPriceListWire.FormatDate(through) : null,
            publishedBy = version.PublishedBy,
            lines = version.Lines.Select(line => new
            {
                lineId = line.LineId,
                productId = line.ProductId,
                priceUnitId = line.PriceUnitId,
                unitPrice = ProductPriceListWire.FormatPrice(line.UnitPrice),
                floorUnitPrice = line.FloorUnitPrice is { } floor ? ProductPriceListWire.FormatPrice(floor) : null,
            }),
        }),
    }, Json);

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            dbContext.ChangeTracker.Clear();
            throw new PriceListStoreException(PriceListStoreCodes.ConcurrentChange, "Liste başka bir yazmayla değişti; hiçbir şey kaydedilmedi.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Unique-key races only (the attempt key, the list id or name, one draft per list, a published start).
            dbContext.ChangeTracker.Clear();
            throw new PriceListStoreException(PriceListStoreCodes.ConcurrentChange,
                "Kalıcı tekillik kuralı başka bir yazmanın lehine sonuçlandı; hiçbir şey kaydedilmedi.", exception);
        }
    }

    private Guid RequireResolvedCompany() =>
        companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
            ? companyContext.CompanyId
            : throw new PriceListStoreException(PriceListStoreCodes.CompanyContextMismatch, "Aktif şirket bağlamı olmadan fiyat listesi işlenmez.");

    /// <summary>ADR-011 §2: the attempt identity is the uppercase SHA-256 of the key; the raw key is never stored.</summary>
    public static string IdempotencyKeyHash(string idempotencyKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)));

    /// <summary>The opaque, company-bound list cursor: base64url of company | updatedAtUtc ticks | id.</summary>
    internal static class ListCursor
    {
        public static string Encode(Guid companyId, DateTimeOffset updatedAtUtc, Guid id)
        {
            var bytes = new byte[40];
            companyId.TryWriteBytes(bytes.AsSpan(0, 16));
            BitConverter.TryWriteBytes(bytes.AsSpan(16, 8), updatedAtUtc.UtcTicks);
            id.TryWriteBytes(bytes.AsSpan(24, 16));
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static (DateTimeOffset UpdatedAtUtc, Guid Id) Decode(string cursor, Guid companyId)
        {
            byte[] bytes;
            try
            {
                var padded = cursor.Replace('-', '+').Replace('_', '/');
                bytes = Convert.FromBase64String(padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '='));
            }
            catch (FormatException exception)
            {
                throw new PriceListStoreException(PriceListStoreCodes.InvalidCursor, "Liste imleci çözülemedi.", exception);
            }

            if (bytes.Length != 40 || new Guid(bytes.AsSpan(0, 16)) != companyId)
            {
                throw new PriceListStoreException(PriceListStoreCodes.InvalidCursor, "Liste imleci bu şirketin değil ya da bozuk.");
            }

            return (new DateTimeOffset(BitConverter.ToInt64(bytes, 16), TimeSpan.Zero), new Guid(bytes.AsSpan(24, 16)));
        }
    }
}
