using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Lots;
using GoymenERP.Modules.Inventory.Domain.Opening;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure;

/// <summary>
/// The opening-count command (INV-OPEN-1 part 2, decision A #66 5738113623 §2) over the store's one-save
/// record (part 1).
/// <list type="bullet">
/// <item><b>Authority:</b> <see cref="InventoryPermissionManifestV1.OpeningStockRecord"/>, company-wide, measured
/// fresh (#276) before any read, and again after each owner read (the stored key, the location, each product)
/// before its answer is used — the last one before the save. Nothing is measured after the commit (ORG #321
/// P3-1, S03). An unregistered name is 503, never a grant.</item>
/// <item><b>Location:</b> Organization's current state: only an ACTIVE internal location of the company is
/// opened; inactive, external and unknown (another company's too) are refused before any write.</item>
/// <item><b>Products:</b> Product's stock eligibility for each counted product at the card version the counter
/// read, on the counted-on date. Any refused line refuses the count with per-line reasons; nothing is written.</item>
/// <item><b>ADR-011:</b> key = (company, command, upper-case SHA-256 of the trimmed key); the same actor and
/// payload replays the first answer with its first correlation (§5 under fresh authority); another actor or
/// payload is <c>idempotency_conflict</c> and discloses nothing. A conflict or a not-found answer is terminal
/// (§7): recorded alone and replayed. A same-key race (§8) re-reads the key: the loser answers the winner's
/// record.</item>
/// <item><b>Book = count:</b> the store writes each counted piece's receipt exactly as counted; a later
/// difference is ST-05 with its own second approval, never this command.</item>
/// </list>
/// </summary>
public sealed class InventoryOpeningCommandService(
    InventoryDbContext dbContext,
    IInventoryOpeningStore store,
    IInventoryLocationStatusPort locations,
    IInventoryProductStockPort products,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    TimeProvider timeProvider) : IInventoryOpeningCommandService
{
    public const string RecordCommand = "inventory.opening-count.record";
    private const int IdentityMaximumLength = 128;

    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    /// <summary>
    /// The store's refusals a key keeps for good (ADR-011 §7): a conflict with what is stored, answered 409. (A
    /// location not found is terminal too, answered 422 on its own path.)
    /// </summary>
    private static readonly HashSet<string> TerminalCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        InventoryStoreCodes.OpeningConflict,
        InventoryStoreCodes.LabelConflict,
        InventoryStoreCodes.LabelCodeTaken,
        InventoryStoreCodes.LotConflict,
        InventoryStoreCodes.MovementIdConflict,
        InventoryStoreCodes.DuplicateSource,
        InventoryRuleCodes.LabelAlreadyInStock,
    };

    public async Task<OpeningCountResultV1> RecordAsync(OpeningCountRequestV1 request, InventoryCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(cancellationToken);
        if (!string.Equals(metadata.ActorId, scope.ActorId, StringComparison.Ordinal))
        {
            throw Forbidden();
        }

        var key = metadata.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(key) || !string.Equals(key, key.Trim(), StringComparison.Ordinal)
            || key.Length > IdentityMaximumLength || key.Any(char.IsControl))
        {
            throw Validation(Issue("idempotencyKey", InventoryOpeningCodesV1.IdempotencyKeyInvalid));
        }

        if (string.IsNullOrWhiteSpace(metadata.CorrelationId) || metadata.CorrelationId.Trim().Length > IdentityMaximumLength)
        {
            throw Validation(Issue("correlationId", InventoryStoreCodes.ValueDoesNotFit));
        }

        var keyHash = KeyHash(key);
        var payloadHash = PayloadHash(request);
        if (await PriorAsync(scope, keyHash, payloadHash, cancellationToken) is { } prior)
        {
            return prior;
        }

        var count = Parse(request, scope.CompanyId);
        var command = metadata with
        {
            ActorId = scope.ActorId,
            PermissionCode = InventoryPermissionManifestV1.OpeningStockRecord,
            RoleCodes = scope.RoleCodes,
        };

        // Organization: the counted location, as it stands now.
        InventoryLocationStatus location;
        try
        {
            location = await locations.ReadAsync(scope.CompanyId, count.LocationId, cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or InventoryOpeningServiceException))
        {
            await RequireStillAsync(scope, cancellationToken);
            throw new InventoryOpeningServiceException(503, InventoryOpeningCodesV1.LocationUnavailable);
        }

        await RequireStillAsync(scope, cancellationToken);
        switch (location)
        {
            case InventoryLocationStatus.Active:
                break;
            case InventoryLocationStatus.NotFound:
                return await TerminalAsync(scope, keyHash, payloadHash, count.Id, command.CorrelationId,
                    new InventoryOpeningServiceException(422, InventoryOpeningCodesV1.LocationNotFound,
                        [Issue("locationId", InventoryOpeningCodesV1.LocationNotFound)]), cancellationToken);
            case InventoryLocationStatus.Inactive:
                throw new InventoryOpeningServiceException(422, InventoryOpeningCodesV1.LocationInactive,
                    [Issue("locationId", InventoryOpeningCodesV1.LocationInactive)]);
            case InventoryLocationStatus.External:
                throw new InventoryOpeningServiceException(422, InventoryOpeningCodesV1.LocationExternal,
                    [Issue("locationId", InventoryOpeningCodesV1.LocationExternal)]);
            default:
                throw new InventoryOpeningServiceException(503, InventoryOpeningCodesV1.LocationUnavailable);
        }

        // Product: each counted product at the version the counter read, on the counted-on date.
        var answers = new Dictionary<(Guid, string), InventoryProductStockAnswer>();
        var issues = new List<InventoryFieldIssueV1>();
        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = request.Lines[index];
            var product = (line.ProductId, line.ProductVersion);
            if (!answers.TryGetValue(product, out var answer))
            {
                try
                {
                    answer = await products.EvaluateAsync(line.ProductId, line.ProductVersion, count.CountedOn, cancellationToken);
                }
                catch (Exception exception) when (exception is not (OperationCanceledException or InventoryOpeningServiceException))
                {
                    await RequireStillAsync(scope, cancellationToken);
                    throw new InventoryOpeningServiceException(503, InventoryOpeningCodesV1.ProductEligibilityUnavailable);
                }

                await RequireStillAsync(scope, cancellationToken);
                answers[product] = answer;
            }

            if (!answer.IsEligible)
            {
                var prefix = $"lines[{index}]";
                issues.AddRange(answer.Issues.Count == 0
                    ? [Issue($"{prefix}.productId", InventoryOpeningCodesV1.ProductIneligible)]
                    : answer.Issues.Select(issue => Issue($"{prefix}.{issue.Field}", issue.Code)));
            }
        }

        if (issues.Count > 0)
        {
            throw new InventoryOpeningServiceException(422, InventoryOpeningCodesV1.ProductIneligible, issues);
        }

        // One save: the key's record, the count, its lots, labels, receipts and balances, and their audit.
        dbContext.Commands.Add(CommandRow(scope, keyHash, payloadHash, count.Id, command.CorrelationId,
            new CommandResult(201, count.Id, command.CorrelationId.Trim(), null, null)));
        InventoryWriteOutcome outcome;
        try
        {
            outcome = await store.RecordAsync(count, command, cancellationToken);
        }
        catch (Exception exception) when (exception is InventoryStoreException or InventoryRuleException)
        {
            dbContext.ChangeTracker.Clear();
            // §8 first, whatever the code: the loser of a same-key race can collide on another index of the same
            // batch (the combined SQL message may name UX_Movements_Source or the label code before the key), so a
            // winner recorded under this key is the answer before any refusal is believed.
            if (await PriorAsync(scope, keyHash, payloadHash, cancellationToken) is { } won)
            {
                return won;
            }

            var code = exception is InventoryStoreException stored ? stored.Code : ((InventoryRuleException)exception).Code;
            if (code == InventoryStoreCodes.ConcurrentChange)
            {
                throw new InventoryOpeningServiceException(409, InventoryStoreCodes.ConcurrentChange);
            }

            var refusal = RefusalFor(code)
                ?? throw new InvalidOperationException($"Açılış sayımında eşlenmemiş stok reddi: {code}.", exception);
            if (TerminalCodes.Contains(code))
            {
                return await TerminalAsync(scope, keyHash, payloadHash, count.Id, command.CorrelationId, refusal, cancellationToken);
            }

            throw refusal;
        }

        if (outcome == InventoryWriteOutcome.Replayed)
        {
            // The same count under a new key: nothing new is stocked; the key answers the count's first recording.
            var first = await dbContext.OpeningCounts.AsNoTracking()
                .Where(row => row.Id == count.Id).Select(row => row.CorrelationId).SingleAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            // The last fence that can still prevent this key's write; nothing is measured after the commit.
            await RequireStillAsync(scope, cancellationToken);
            dbContext.Commands.Add(CommandRow(scope, keyHash, payloadHash, count.Id, command.CorrelationId,
                new CommandResult(200, count.Id, first, null, null)));
            return await SaveAsync(scope, keyHash, payloadHash, cancellationToken)
                ?? new OpeningCountResultV1(View(count), IsReplay: true, first);
        }

        // Committed: the answer is the stored record's. No authority is measured after the commit (ORG #321 P3-1,
        // S03): a grant revoked now must not turn a stored count into a 403 while its key says 201.
        return new OpeningCountResultV1(View(count), IsReplay: false, command.CorrelationId.Trim());
    }

    public async Task<OpeningCountViewV1> ReadAsync(Guid countId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(cancellationToken);
        var count = await store.ReadCountAsync(countId, cancellationToken);
        await RequireStillAsync(scope, cancellationToken);
        return count is null
            ? throw new InventoryOpeningServiceException(404, InventoryOpeningCodesV1.NotFound)
            : View(count);
    }

    /// <summary>The ADR-011 §2 key hash: upper-case SHA-256 of the trimmed key (the raw key is never stored).</summary>
    internal static string KeyHash(string idempotencyKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey.Trim())));

    /// <summary>The canonical request as the client sent it, the product versions included.</summary>
    internal static string PayloadHash(OpeningCountRequestV1 request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, Json)))).ToLowerInvariant();

    /// <summary>
    /// The key's stored answer — a success or a terminal refusal — replayed with its first correlation under
    /// fresh authority (§5); another actor or payload under the key is a conflict that discloses nothing.
    /// </summary>
    private async Task<OpeningCountResultV1?> PriorAsync(Scope scope, string keyHash, string payloadHash,
        CancellationToken cancellationToken)
    {
        var prior = await dbContext.Commands.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == RecordCommand && row.IdempotencyKeyHash == keyHash, cancellationToken);
        await RequireStillAsync(scope, cancellationToken);
        if (prior is null)
        {
            return null;
        }

        if (!string.Equals(prior.PayloadHash, payloadHash, StringComparison.Ordinal)
            || !string.Equals(prior.ActorId, scope.ActorId, StringComparison.Ordinal))
        {
            throw new InventoryOpeningServiceException(409, InventoryOpeningCodesV1.IdempotencyConflict);
        }

        var result = JsonSerializer.Deserialize<CommandResult>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Kayıtlı komut sonucu okunamadı.");
        var original = result.OriginalCorrelationId ?? prior.CorrelationId;
        if (result.Status >= 400)
        {
            throw new InventoryOpeningServiceException(result.Status, result.Code!, result.Issues, isReplay: true, original);
        }

        var count = await store.ReadCountAsync(result.CountId!.Value, cancellationToken)
            ?? throw new InvalidOperationException("Kayıtlı komutun açılış sayımı okunamadı.");
        await RequireStillAsync(scope, cancellationToken);
        return new OpeningCountResultV1(View(count), IsReplay: true, original);
    }

    /// <summary>
    /// ADR-011 §7: the refusal is the key's answer for good — recorded alone (no business change) and thrown.
    /// A same-key racer that recorded first is the answer instead (§8).
    /// </summary>
    private async Task<OpeningCountResultV1> TerminalAsync(Scope scope, string keyHash, string payloadHash,
        Guid countId, string correlationId, InventoryOpeningServiceException refusal, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        dbContext.Commands.Add(CommandRow(scope, keyHash, payloadHash, countId, correlationId,
            new CommandResult(refusal.Status, null, correlationId.Trim(), refusal.Code, refusal.Issues)));
        return await SaveAsync(scope, keyHash, payloadHash, cancellationToken)
            ?? throw new InventoryOpeningServiceException(refusal.Status, refusal.Code, refusal.Issues, isReplay: false,
                originalCorrelationId: correlationId.Trim());
    }

    /// <summary>
    /// Saves the key's record alone; null when it committed. A lost same-key race (§8) answers the winner's
    /// record — or throws its terminal refusal, or the conflict when the payload or actor differ.
    /// </summary>
    private async Task<OpeningCountResultV1?> SaveAsync(Scope scope, string keyHash, string payloadHash,
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            dbContext.ChangeTracker.Clear();
            return await PriorAsync(scope, keyHash, payloadHash, cancellationToken)
                ?? throw new InventoryOpeningServiceException(409, InventoryStoreCodes.ConcurrentChange);
        }
    }

    private InventoryCommandRow CommandRow(Scope scope, string keyHash, string payloadHash, Guid countId,
        string correlationId, CommandResult result) => new InventoryCommandRow()
    {
        CompanyId = scope.CompanyId,
        CommandName = RecordCommand,
        IdempotencyKeyHash = keyHash,
        ActorId = scope.ActorId,
        PayloadHash = payloadHash,
        TargetId = countId,
        ResultJson = JsonSerializer.Serialize(result, Json),
        CorrelationId = correlationId.Trim(),
        RecordedAtUtc = timeProvider.GetUtcNow(),
    };

    /// <summary>The request as the domain's count; every malformed line is named, and nothing is read before.</summary>
    private static OpeningCount Parse(OpeningCountRequestV1 request, Guid companyId)
    {
        if (request.Lines is null || request.Lines.Count == 0)
        {
            throw Validation(Issue("lines", InventoryRuleCodes.InvalidOpeningCount));
        }

        var issues = new List<InventoryFieldIssueV1>();
        var lines = new List<OpeningCountLine>();
        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = request.Lines[index];
            var prefix = $"lines[{index}]";
            if (line is null)
            {
                issues.Add(Issue(prefix, InventoryRuleCodes.InvalidOpeningCount));
                continue;
            }

            if (string.IsNullOrWhiteSpace(line.ProductVersion) || line.ProductVersion.Length > IdentityMaximumLength)
            {
                issues.Add(Issue($"{prefix}.productVersion", InventoryOpeningCodesV1.ProductVersionInvalid));
            }

            if (!TryKilograms(line.QuantityKg, out var quantity, out var quantityCode))
            {
                issues.Add(Issue($"{prefix}.quantityKg", quantityCode));
                continue;
            }

            try
            {
                var lot = new MaterialLot(line.LotId, companyId, line.ProductId, line.HeatNumber, line.CoilNumber);
                var label = new MaterialLabel(line.LabelId, companyId, line.LabelCode, line.ProductId, line.LotId,
                    heatNumber: line.HeatNumber, coilNumber: line.CoilNumber);
                lines.Add(new OpeningCountLine(line.LineId, lot, label, quantity));
            }
            catch (InventoryRuleException exception)
            {
                issues.Add(Issue(prefix, IssueCode(exception.Code)));
            }
        }

        if (issues.Count > 0)
        {
            throw Validation([.. issues]);
        }

        try
        {
            return new OpeningCount(request.CountId, companyId, request.LocationId, request.CountedOn,
                request.SheetReference, lines);
        }
        catch (InventoryRuleException exception)
        {
            throw Validation(Issue("count", IssueCode(exception.Code)));
        }
    }

    /// <summary>A lossless kilogram text: digits with an optional point, no sign, exponent, grouping or spaces.</summary>
    private static bool TryKilograms(string? text, out Kilograms quantity, out string code)
    {
        quantity = Kilograms.Zero;
        code = InventoryRuleCodes.QuantityNotPositive;
        if (string.IsNullOrEmpty(text) || text.Length > 40 || !text.All(character => char.IsAsciiDigit(character) || character == '.')
            || !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            code = InventoryOpeningCodesV1.QuantityInvalid;
            return false;
        }

        try
        {
            quantity = Kilograms.Positive(value);
            return true;
        }
        catch (InventoryRuleException exception)
        {
            code = IssueCode(exception.Code);
            return false;
        }
    }

    /// <summary>
    /// The HTTP answer of a store or domain refusal, inside the closed sets of <see cref="InventoryOpeningCodesV1"/>;
    /// null for a code the command does not know (the caller treats it as an internal fault, never a guessed 4xx).
    /// </summary>
    internal static InventoryOpeningServiceException? RefusalFor(string code) => code switch
    {
        _ when TerminalCodes.Contains(code) => new InventoryOpeningServiceException(409, code),
        InventoryStoreCodes.CompanyContextMismatch => Forbidden(),
        InventoryStoreCodes.ValueDoesNotFit or InventoryScaleCodes.ScaleExceeded => Validation(Issue("lines", code)),
        _ when InventoryOpeningCodesV1.Status503Codes.Contains(code) => new InventoryOpeningServiceException(503, code),
        _ => null,
    };

    /// <summary>A domain rule code as a 400 issue code: inside <see cref="InventoryOpeningCodesV1.IssueCodes"/>, else the count's own.</summary>
    private static string IssueCode(string code) =>
        InventoryOpeningCodesV1.IssueCodes.Contains(code) ? code : InventoryRuleCodes.InvalidOpeningCount;

    private async Task<Scope> RequireAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new InventoryOpeningServiceException(401, InventoryOpeningCodesV1.Unauthenticated);
        }

        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw Forbidden();
        }

        const string permission = InventoryPermissionManifestV1.OpeningStockRecord;
        if (!ErpPermissions.IsKnown(permission))
        {
            // A name the central catalogue does not know is a setup gap, reported as such (never a grant).
            throw new InventoryOpeningServiceException(503, InventoryOpeningCodesV1.PermissionUnregistered);
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId;
        var scope = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide || !companyContext.IsResolved || companyContext.CompanyId != companyId
            || !string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal))
        {
            throw Forbidden();
        }

        return new Scope(companyId, actorId, [.. scope.RoleCodes.Order(StringComparer.Ordinal)]);
    }

    /// <summary>The fence after an owner read: the same actor and company, still granted, measured now.</summary>
    private async Task RequireStillAsync(Scope scope, CancellationToken cancellationToken)
    {
        var current = await RequireAsync(cancellationToken);
        if (current.CompanyId != scope.CompanyId || !string.Equals(current.ActorId, scope.ActorId, StringComparison.Ordinal))
        {
            throw Forbidden();
        }
    }

    private static OpeningCountViewV1 View(OpeningCount count) => new OpeningCountViewV1(count.Id, count.CompanyId, count.LocationId,
        count.CountedOn, count.SheetReference,
        [.. count.Lines.Select(line => new OpeningCountLineViewV1(line.Id, line.Label.Id, line.Label.Code, line.Lot.Id,
            line.Lot.HeatNumber, line.Lot.CoilNumber, line.Lot.ProductId, line.Quantity.ToString()))]);

    private static InventoryOpeningServiceException Forbidden() => new InventoryOpeningServiceException(403, InventoryOpeningCodesV1.Forbidden);

    private static InventoryOpeningServiceException Validation(params InventoryFieldIssueV1[] issues) =>
        new InventoryOpeningServiceException(400, InventoryOpeningCodesV1.ValidationFailed, issues);

    private static InventoryFieldIssueV1 Issue(string field, string code) => new InventoryFieldIssueV1(field, code);

    private sealed record Scope(Guid CompanyId, string ActorId, IReadOnlyCollection<string> RoleCodes);

    /// <summary>What the key keeps: a success (the count and its first correlation) or a terminal refusal.</summary>
    private sealed record CommandResult(int Status, Guid? CountId, string? OriginalCorrelationId, string? Code,
        IReadOnlyList<InventoryFieldIssueV1>? Issues);
}
