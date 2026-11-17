using System.Data;
using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

/// <summary>BS-10 / URT-D25: explicit Excel membership followed by versioned ERP edits. Full versions and terminal answers
/// are immutable. Serializable owner reads hold the table and real Product versions until the Audit owner and command commit.</summary>
internal sealed class ProductPaintMembershipService(ProductPriceListDbContext database, ICompanyContext companyContext,
    ICurrentUser currentUser, IErpAuthorityRevalidationService authority, IAuditEventCollector auditEvents,
    IAuditTransactionWriterV1 auditWriter, IRefusedAttemptRecorderV1 refusedAttempts, TimeProvider clock,
    ProductPaintMembershipReader reader) : IProductPaintMembershipServiceV1
{
    internal const string ImportCommand = "product.paint-memberships.import.v1";
    internal const string SetCommand = "product.paint-memberships.set.v1";
    private const string Permission = ProductPaintMembershipPermissionsV1.Manage;
    private const string EntityType = "Product.PaintMemberships";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ProductPaintMembershipCatalogV1> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var frame = Capture();
        await FenceAsync(frame, cancellationToken);
        var result = await reader.ListForMaintenanceAsync(cancellationToken);
        await FenceAsync(frame, cancellationToken);
        if (result.CompanyId != frame.CompanyId || result.Outcome == ProductPaintMembershipCatalogOutcomeV1.Unavailable) throw Unavailable();
        return result;
    }

    public async Task<ProductPaintMembershipImportPreviewV1> ValidateImportAsync(ValidateProductPaintMembershipImportV1 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var frame = Capture();
        await FenceAsync(frame, cancellationToken);
        try
        {
            var parsed = Parse(request.FileName, request.ContentBase64, request.ProductCodeColumn);
            var preview = await PreviewAsync(parsed, frame, cancellationToken);
            await FenceAsync(frame, cancellationToken);
            return preview;
        }
        catch (Exception exception) when (exception is not ProductPaintMembershipException and not UnauthorizedAccessException and not OperationCanceledException)
        { await FenceAsync(frame, cancellationToken); throw Unavailable(); }
    }

    public Task<ProductPaintMembershipMutationV1> ApplyImportAsync(ApplyProductPaintMembershipImportV1 request,
        ProductPaintMembershipCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsync(ImportCommand, null, request, metadata, async frame =>
        {
            if (!Version(request.ExpectedVersion, out var expected) || !Reason(request.Reason, out var reason)
                || !Label(request.SourceLabel, ProductPaintMembershipLimitsV1.MaximumSourceLabelLength, out var label)) return Refuse(400, ProductPaintMembershipCodesV1.InvalidRequest);
            var parsed = Parse(request.FileName, request.ContentBase64, request.ProductCodeColumn);
            var preview = await PreviewAsync(parsed, frame, cancellationToken);
            if (expected != long.Parse(preview.Version, CultureInfo.InvariantCulture)) return Refuse(409, ProductPaintMembershipCodesV1.VersionConflict);
            if (!preview.Ready) return Refuse(422, ProductPaintMembershipCodesV1.InvalidFile);
            if (!string.Equals(preview.FileSha256, request.FileSha256, StringComparison.Ordinal)
                || !string.Equals(preview.PreviewToken, request.PreviewToken, StringComparison.Ordinal)) return Refuse(409, ProductPaintMembershipCodesV1.PreviewMismatch);
            var current = await CurrentAsync(expected, frame, cancellationToken);
            var changed = preview.Rows.Where(x => !x.AlreadyMember).ToArray();
            if (current.Count + changed.Length > ProductPaintMembershipLimitsV1.MaximumRows) return Refuse(422, ProductPaintMembershipCodesV1.InvalidFile);
            if (changed.Length == 0) return Success(frame.CompanyId, expected, [], []);
            var next = checked(expected + 1);
            AddHeader(next, "Import", label, request.FileName!.Trim(), preview.FileSha256, reason, frame, metadata);
            Carry(current, next);
            foreach (var row in changed) database.PaintMembershipLines.Add(new ProductPaintMembershipLineRow
            {
                CompanyId = frame.CompanyId, Version = next, ProductId = row.ProductId, MembershipVersion = next,
                RecordedProductVersion = long.Parse(row.ProductVersion, CultureInfo.InvariantCulture),
            });
            var entries = changed.Select(row => new ProductPaintMembershipEntryV1(row.ProductId, row.ProductVersion,
                Text(next), row.ProductCode, row.ProductName, ProductPaintMembershipReader.Source)).ToArray();
            return Changed(frame.CompanyId, expected, next, entries, [], reason);
        }, cancellationToken);
    }

    public Task<ProductPaintMembershipMutationV1> SetAsync(Guid productId, SetProductPaintMembershipV1 request,
        ProductPaintMembershipCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsync(SetCommand, productId, request, metadata, async frame =>
        {
            if (productId == Guid.Empty || !Version(request.ExpectedVersion, out var expected)
                || !Version(request.ExpectedProductVersion, out var expectedProduct) || expectedProduct == 0
                || !Version(request.ExpectedMembershipVersion, out var expectedMember) || !Reason(request.Reason, out var reason)) return Refuse(400, ProductPaintMembershipCodesV1.InvalidRequest);
            var head = await ReadAsync(() => HeadAsync(cancellationToken), frame, cancellationToken);
            if (head == 0) return Refuse(409, ProductPaintMembershipCodesV1.NoSource);
            if (head != expected) return Refuse(409, ProductPaintMembershipCodesV1.VersionConflict);
            var product = await ReadAsync(() => database.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Id == productId, cancellationToken), frame, cancellationToken);
            if (product is null) return Refuse(404, ProductPaintMembershipCodesV1.NotFound);
            if (product.Version != expectedProduct) return Refuse(409, ProductPaintMembershipCodesV1.VersionConflict);
            var current = await CurrentAsync(head, frame, cancellationToken);
            var existing = current.SingleOrDefault(x => x.ProductId == productId);
            if ((existing?.MembershipVersion ?? 0) != expectedMember) return Refuse(409, ProductPaintMembershipCodesV1.VersionConflict);
            if (request.IsMember && product.Lifecycle != ProductPaintMembershipReader.Active) return Refuse(422, ProductPaintMembershipCodesV1.Inactive);
            if (request.IsMember == (existing is not null)) return Success(frame.CompanyId, head, [], []);
            var next = checked(head + 1);
            AddHeader(next, "Edit", "ERP", null, null, reason, frame, metadata);
            Carry(current.Where(x => x.ProductId != productId), next);
            if (request.IsMember)
            {
                var line = new ProductPaintMembershipLineRow { CompanyId = frame.CompanyId, Version = next,
                    ProductId = productId, MembershipVersion = next, RecordedProductVersion = product.Version };
                database.PaintMembershipLines.Add(line);
                return Changed(frame.CompanyId, head, next, [ProductPaintMembershipReader.Entry(product, line)], [], reason);
            }
            return Changed(frame.CompanyId, head, next, [], [productId], reason);
        }, cancellationToken);
    }

    private async Task<ProductPaintMembershipMutationV1> ExecuteAsync<T>(string command, Guid? productId, T request,
        ProductPaintMembershipCommandMetadataV1 metadata, Func<Frame, Task<Decision>> decide, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var frame = Capture();
        ValidateMetadata(frame, metadata);
        await FenceAsync(frame, ct);
        if (database.Database.CurrentTransaction is not null || database.ChangeTracker.HasChanges()
            || auditWriter.HasPendingDrafts) throw Unavailable();
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new { ProductId = productId, Request = request });
        var ownedAudit = false;
        var committed = false;
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var prior = await ReadAsync(() => AttemptAsync(command, keyHash, ct), frame, ct);
            if (prior is not null)
            {
                await FenceAsync(frame, ct);
                // Replay belongs to this actor/company/payload and returns its immutable original response, regardless
                // of later product or membership changes. No audit row or version is written again.
                return Replay(prior, requestHash, metadata);
            }
            Decision decision;
            try { decision = await decide(frame); }
            catch (ProductPaintMembershipException exception) when (exception.StatusCode is 400 or 404 or 409 or 422)
            { decision = Refuse(exception.StatusCode, exception.Code); }
            await FenceAsync(frame, ct);
            var result = decision.Result is null ? null : decision.Result with { OriginalCorrelationId = metadata.CorrelationId };
            var answer = new Answer(decision.Status, decision.Code, result);
            database.PaintMembershipAttempts.Add(new ProductPaintMembershipAttemptRow { CompanyId = frame.CompanyId,
                CommandName = command, IdempotencyKeyHash = keyHash, RequestHash = requestHash, ActorId = frame.ActorId,
                StatusCode = decision.Status, ResultJson = JsonSerializer.Serialize(answer, Json), CorrelationId = metadata.CorrelationId, CreatedAtUtc = clock.GetUtcNow() });
            await database.SaveChangesAsync(ct);
            await FenceAsync(frame, ct);
            // Queue only after the owner's save: its legacy sidecar interceptor must never take this new draft.
            // The Audit module owns the actual insert and borrows this same transaction.
            if (decision.Status >= 400)
            {
                ownedAudit = true;
                refusedAttempts.Record(new RefusedAttemptV1 { CompanyId = frame.CompanyId, CommandName = command,
                    Operation = command == ImportCommand ? "ImportPaintMemberships" : "SetPaintMembership", TargetType = EntityType,
                    TargetId = productId?.ToString("D") ?? RefusedAttemptV1.NoTarget, ActorType = AuditActorTypes.User,
                    ActorId = frame.ActorId, PermissionCode = Permission, RoleCodes = frame.RoleCodes,
                    HttpStatus = decision.Status, ProblemCode = decision.Code!, RefusalClass = RefusalClass(decision.Code!),
                    CorrelationId = metadata.CorrelationId, CausationId = metadata.CausationId, SourceChannel = metadata.SourceChannel, IdempotencyKey = metadata.IdempotencyKey });
            }
            else if (decision.BeforeVersion != decision.AfterVersion)
            {
                ownedAudit = true;
                auditEvents.Enqueue(new AuditEventDraft(frame.CompanyId, "product.paint-memberships.changed.v1", EntityType,
                    frame.CompanyId.ToString("D"), command == ImportCommand ? "ImportPaintMemberships" : "SetPaintMembership",
                    decision.BeforeVersion, decision.AfterVersion, AuditActorTypes.User, frame.ActorId, Permission, frame.RoleCodes,
                    JsonSerializer.Serialize(new { Version = Text(decision.BeforeVersion) }, Json), JsonSerializer.Serialize(new
                    {
                        Result = result,
                        Source = database.ChangeTracker.Entries<ProductPaintMembershipVersionRow>()
                            .Where(x => x.Entity.CompanyId == frame.CompanyId && x.Entity.Version == decision.AfterVersion)
                            .Select(x => new { x.Entity.SourceKind, x.Entity.SourceLabel, x.Entity.FileName, x.Entity.FileSha256 }).Single(),
                    }, Json),
                    decision.Reason!, null, AuditAiInvolvement.None, null, metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
            }
            if (ownedAudit) await auditWriter.WritePendingAsync(transaction.GetDbTransaction(), ct);
            await FenceAsync(frame, ct);
            await transaction.CommitAsync(ct);
            committed = true;
            return Unwrap(answer);
        }
        catch (DbUpdateException)
        {
            await RollbackAsync(transaction);
            if (ownedAudit) auditWriter.DiscardPending();
            database.ChangeTracker.Clear();
            await FenceAsync(frame, ct);
            var winner = await ReadAsync(() => AttemptAsync(command, keyHash, ct), frame, ct);
            if (winner is not null) return Replay(winner, requestHash, metadata);
            throw Unavailable(); // an unrecorded database race is retryable; never manufacture a terminal answer
        }
        catch
        {
            if (committed) throw;
            await RollbackAsync(transaction);
            if (ownedAudit) auditWriter.DiscardPending();
            database.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<ProductPaintMembershipImportPreviewV1> PreviewAsync(ProductPaintMembershipWorkbookResult parsed, Frame frame, CancellationToken ct)
    {
        var version = await ReadAsync(() => HeadAsync(ct), frame, ct);
        var membership = await CurrentAsync(version, frame, ct);
        var products = new List<PriceListProductReference>();
        foreach (var part in parsed.Rows.Select(x => x.ProductCode).Distinct(StringComparer.Ordinal).Chunk(500))
        {
            var codes = part.ToArray();
            products.AddRange(await ReadAsync(() => database.Products.AsNoTracking().Where(x => codes.Contains(x.Code)).ToListAsync(ct), frame, ct));
        }
        var rows = new List<ProductPaintMembershipImportRowV1>();
        var errors = parsed.Errors.ToList();
        foreach (var row in parsed.Rows)
        {
            // Exact text match only: SQL's case-insensitive card collation is not an alternate code mapping rule.
            var matches = products.Where(x => string.Equals(x.Code, row.ProductCode, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1) { errors.Add(new(row.RowNumber, "productCode", ProductPaintMembershipCodesV1.NotFound, "Ürün kodu tek bir mevcut ürün kartıyla eşleşmedi.")); continue; }
            var product = matches[0];
            if (product.Lifecycle != ProductPaintMembershipReader.Active || product.Version <= 0)
            { errors.Add(new(row.RowNumber, "productCode", ProductPaintMembershipCodesV1.Inactive, "Ürün kartı etkin değil.")); continue; }
            var member = membership.SingleOrDefault(x => x.ProductId == product.Id);
            rows.Add(new(row.RowNumber, product.Id, Text(product.Version), Text(member?.MembershipVersion ?? 0), product.Code, product.Name, member is not null));
        }
        var observed = new List<(Guid Id, long Version, int Lifecycle)>();
        foreach (var part in products.Select(x => x.Id).Distinct().Chunk(500))
        {
            var ids = part.ToArray();
            var proof = await ReadAsync(() => database.Products.AsNoTracking().Where(x => ids.Contains(x.Id))
                .Select(x => new { x.Id, x.Version, x.Lifecycle }).ToListAsync(ct), frame, ct);
            observed.AddRange(proof.Select(x => (x.Id, x.Version, x.Lifecycle)));
        }
        var freshVersion = await ReadAsync(() => HeadAsync(ct), frame, ct);
        if (freshVersion != version || !products.Select(x => (x.Id, x.Version, x.Lifecycle)).OrderBy(x => x.Id).SequenceEqual(observed.OrderBy(x => x.Id)))
            throw new ProductPaintMembershipException(409, ProductPaintMembershipCodesV1.PreviewMismatch);
        var token = IdempotencyHash.ComputePayload(new { frame.CompanyId, frame.ActorId, Version = version,
            parsed.FileSha256, parsed.ProductCodeColumn, parsed.SourceRowCount, Rows = rows, Errors = errors });
        return new(frame.CompanyId, Text(version), parsed.FileSha256, parsed.ProductCodeColumn, token, parsed.SourceRowCount, rows, errors);
    }

    private async Task<List<ProductPaintMembershipLineRow>> CurrentAsync(long version, Frame frame, CancellationToken ct)
    {
        var result = await ReadAsync(() => database.PaintMembershipLines.AsNoTracking().Where(x => x.Version == version)
            .OrderBy(x => x.ProductId).Take(ProductPaintMembershipLimitsV1.MaximumRows + 1).ToListAsync(ct), frame, ct);
        if (result.Count > ProductPaintMembershipLimitsV1.MaximumRows) throw Unavailable();
        return result;
    }
    private void Carry(IEnumerable<ProductPaintMembershipLineRow> lines, long version)
    {
        foreach (var line in lines) database.PaintMembershipLines.Add(new ProductPaintMembershipLineRow { CompanyId = line.CompanyId,
            Version = version, ProductId = line.ProductId, MembershipVersion = line.MembershipVersion, RecordedProductVersion = line.RecordedProductVersion });
    }
    private void AddHeader(long version, string kind, string label, string? fileName, string? hash, string reason, Frame frame, ProductPaintMembershipCommandMetadataV1 metadata) =>
        database.PaintMembershipVersions.Add(new ProductPaintMembershipVersionRow { CompanyId = frame.CompanyId, Version = version,
            SourceKind = kind, SourceLabel = label, FileName = fileName, FileSha256 = hash, Reason = reason,
            ActorId = frame.ActorId, CorrelationId = metadata.CorrelationId, CreatedAtUtc = clock.GetUtcNow() });
    private Task<ProductPaintMembershipAttemptRow?> AttemptAsync(string command, string hash, CancellationToken ct) =>
        database.PaintMembershipAttempts.AsNoTracking().SingleOrDefaultAsync(x => x.CommandName == command && x.IdempotencyKeyHash == hash, ct);
    private async Task<long> HeadAsync(CancellationToken ct) => await database.PaintMembershipVersions.AsNoTracking().MaxAsync(x => (long?)x.Version, ct) ?? 0;
    private async Task<T> ReadAsync<T>(Func<Task<T>> read, Frame frame, CancellationToken ct)
    { await FenceAsync(frame, ct); var result = await read(); await FenceAsync(frame, ct); return result; }
    private Frame Capture()
    {
        var frame = new Frame(companyContext.IsResolved ? companyContext.CompanyId : Guid.Empty, currentUser.UserId ?? string.Empty);
        RequireIdentity(frame); return frame;
    }
    private void RequireIdentity(Frame frame)
    {
        if (frame.CompanyId == Guid.Empty || string.IsNullOrWhiteSpace(frame.ActorId) || !currentUser.IsAuthenticated || !companyContext.IsResolved
            || companyContext.CompanyId != frame.CompanyId || !string.Equals(currentUser.UserId, frame.ActorId, StringComparison.Ordinal)
            || !currentUser.CanAccessCompany(frame.CompanyId)) throw new UnauthorizedAccessException();
    }
    private async Task FenceAsync(Frame frame, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); RequireIdentity(frame);
        var scope = await authority.RevalidatePermissionScopeAsync(Permission, frame.CompanyId, ct);
        RequireIdentity(frame);
        if (!scope.IsGranted || !scope.IsCompanyWide) throw new UnauthorizedAccessException();
        frame.RoleCodes = scope.RoleCodes.Order(StringComparer.Ordinal).ToArray();
    }
    private static void ValidateMetadata(Frame frame, ProductPaintMembershipCommandMetadataV1 metadata)
    {
        if (frame.ActorId.Length > 128 || !Label(metadata.SourceChannel, 64, out _) || metadata.CompanyId != frame.CompanyId || !string.Equals(metadata.ActorId, frame.ActorId, StringComparison.Ordinal)
            || !Label(metadata.CorrelationId, 128, out _) || !Label(metadata.IdempotencyKey, 128, out _)
            || metadata.CausationId is not null && !Label(metadata.CausationId, 128, out _)) throw new ProductPaintMembershipException(400, ProductPaintMembershipCodesV1.InvalidRequest);
    }
    internal static ProductPaintMembershipWorkbookResult Parse(string? fileName, string? base64, string? column)
    {
        if (!Label(fileName, 255, out var name) || !name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            || name.IndexOfAny(['/', '\\', ':']) >= 0 || string.IsNullOrWhiteSpace(base64)
            || base64.Length > ((ProductPaintMembershipLimitsV1.MaximumFileBytes + 2) / 3) * 4)
            throw new ProductPaintMembershipException(422, ProductPaintMembershipCodesV1.InvalidFile);
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64); }
        catch (FormatException) { throw new ProductPaintMembershipException(422, ProductPaintMembershipCodesV1.InvalidFile); }
        if (bytes.Length is 0 or > ProductPaintMembershipLimitsV1.MaximumFileBytes) throw new ProductPaintMembershipException(422, ProductPaintMembershipCodesV1.InvalidFile);
        return ProductPaintMembershipWorkbook.Read(bytes, column ?? ProductPaintMembershipLimitsV1.DefaultProductCodeColumn);
    }
    internal static bool Version(string? value, out long version) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out version) && version >= 0 && Text(version) == value;
    private static bool Reason(string? value, out string reason)
    {
        reason = value?.Trim() ?? string.Empty;
        return reason.Length > 0 && reason.Length <= ProductPaintMembershipLimitsV1.MaximumReasonLength
            && !reason.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t'));
    }
    private static bool Label(string? value, int maximum, out string text)
    { text = value?.Trim() ?? string.Empty; return text.Length > 0 && text.Length <= maximum && !text.Any(char.IsControl); }
    private ProductPaintMembershipMutationV1 Replay(ProductPaintMembershipAttemptRow row, string hash, ProductPaintMembershipCommandMetadataV1 metadata)
    {
        if (row.CompanyId != metadata.CompanyId || !string.Equals(row.ActorId, metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(row.RequestHash, hash, StringComparison.Ordinal)) throw new ProductPaintMembershipException(409, ProductPaintMembershipCodesV1.IdempotencyConflict);
        var answer = JsonSerializer.Deserialize<Answer>(row.ResultJson, Json) ?? throw Unavailable();
        if (answer.Status != row.StatusCode || answer.Status == 200 && (answer.Result is null || answer.Result.CompanyId != metadata.CompanyId
            || answer.Result.IsReplay || answer.Result.OriginalCorrelationId != row.CorrelationId || !Version(answer.Result.Version, out _))) throw Unavailable();
        if (answer.Status >= 400)
        {
            if (string.IsNullOrWhiteSpace(answer.Code) || answer.Result is not null) throw Unavailable();
            refusedAttempts.NoteReplayedRefusal(RefusalClass(answer.Code));
            throw new ProductPaintMembershipException(answer.Status, answer.Code, true, row.CorrelationId);
        }
        return Unwrap(answer) with { IsReplay = true, OriginalCorrelationId = row.CorrelationId };
    }
    private static ProductPaintMembershipMutationV1 Unwrap(Answer answer) => answer.Status == 200 && answer.Result is not null
        ? answer.Result : throw new ProductPaintMembershipException(answer.Status, answer.Code ?? ProductPaintMembershipCodesV1.Unavailable);
    private static string RefusalClass(string code) => code == ProductPaintMembershipCodesV1.IdempotencyConflict ? RefusedAttemptClassesV1.Idempotency
        : code is ProductPaintMembershipCodesV1.VersionConflict or ProductPaintMembershipCodesV1.PreviewMismatch ? RefusedAttemptClassesV1.Concurrency : RefusedAttemptClassesV1.Validation;
    private static string Text(long version) => version.ToString(CultureInfo.InvariantCulture);
    private static async Task RollbackAsync(IDbContextTransaction transaction)
    {
        try { await transaction.RollbackAsync(CancellationToken.None); }
        catch (System.Data.Common.DbException) { /* SQL may already have aborted a deadlock victim. No success is returned. */ }
        catch (InvalidOperationException) { /* The aborted transaction may already be completed. */ }
        finally { await transaction.DisposeAsync(); }
    }
    private static ProductPaintMembershipException Unavailable() => new(503, ProductPaintMembershipCodesV1.Unavailable);
    private static Decision Refuse(int status, string code) => new(status, code, null, 0, 0, null);
    private static Decision Success(Guid company, long version, IReadOnlyList<ProductPaintMembershipEntryV1> changed, IReadOnlyList<Guid> removed) =>
        new(200, null, new(company, Text(version), changed, false, string.Empty, removed), version, version, null);
    private static Decision Changed(Guid company, long before, long after, IReadOnlyList<ProductPaintMembershipEntryV1> changed, IReadOnlyList<Guid> removed, string reason) =>
        new(200, null, new(company, Text(after), changed, false, string.Empty, removed), before, after, reason);
    private sealed record Decision(int Status, string? Code, ProductPaintMembershipMutationV1? Result, long BeforeVersion, long AfterVersion, string? Reason);
    private sealed record Answer(int Status, string? Code, ProductPaintMembershipMutationV1? Result);
    private sealed record Frame(Guid CompanyId, string ActorId) { internal string[] RoleCodes { get; set; } = []; }
}
