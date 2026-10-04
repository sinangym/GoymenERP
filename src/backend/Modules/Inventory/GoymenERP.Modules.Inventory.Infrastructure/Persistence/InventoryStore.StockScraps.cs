using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Scraps;
using GoymenERP.Modules.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// ST-02 scrap write-off: record a scrap of one stocked label, and decide it once. The stock-count (AT-037) shape.
/// <list type="bullet">
/// <item><b>Record:</b> the label at the version the recorder saw, holding stock in exactly one stored position; the
/// scrap is positive and at most the book kg, and must leave at least the label's active allocations. The book kg,
/// the position and the scrap kg are kept with the mandatory reason. No stock changes and no label version is bumped:
/// a recorded scrap is a request, not an effect.</item>
/// <item><b>Decide:</b> only once (the decision key is the scrap), and only by another actor than the recorder —
/// fail-closed while the scrap approval authority is undecided. An approval posts exactly one <c>Scrap</c> movement
/// through the ledger (no negative stock, one physical place, label version bumped) and only while the label is still
/// at the recorded version; otherwise the scrap is stale. A scrap that would leave less than the label's active
/// allocations is refused. A rejection posts nothing.</item>
/// <item><b>Replay (ADR-011, R3):</b> the Idempotency-Key per command, and the scrap id / scrap decision as the
/// business identity: the same content answers the stored result with its first correlation and no second effect;
/// other content is a conflict.</item>
/// </list>
/// Everything is one save with its command row and audit events; nothing is ever updated or deleted.
/// </summary>
public sealed partial class InventoryStore : IInventoryStockScrapWriterV1, IInventoryStockScrapReaderV1
{
    internal const string RecordStockScrapCommand = "inventory.stock-scrap.record.v1";
    internal const string DecideStockScrapCommand = "inventory.stock-scrap.decide.v1";
    private const string StockScrapEntityType = "Inventory.StockScrap";

    public async Task<InventoryStockScrapResultV1> RecordStockScrapAsync(
        RecordInventoryStockScrapV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireMetadata(metadata);
        RequireStockScrapMetadata(metadata);
        RequireResolvedCompany();
        cancellationToken.ThrowIfCancellationRequested();

        var (expectedVersion, scrapKg, reason) = ParseStockScrap(request);
        var companyId = companyContext.CompanyId;
        var keyHash = GenealogyHash(metadata.IdempotencyKey.Trim());
        var payloadHash = GenealogyPayloadHash(new
        {
            request.ScrapId,
            request.LabelId,
            expectedVersion,
            scrapKg = scrapKg.ToString(),
            scrappedAtUtc = request.ScrappedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            reason,
        });
        if (await FindStockScrapKeyReplayAsync(RecordStockScrapCommand, keyHash, payloadHash, metadata.ActorId,
                freshAuthorityFence, cancellationToken) is { } replay)
        {
            return replay;
        }

        if (await FindStockScrapIdentityReplayAsync(request.ScrapId, payloadHash, freshAuthorityFence,
                cancellationToken) is { } identityReplay)
        {
            return identityReplay;
        }

        var label = await dbContext.Labels.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == request.LabelId, cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (label is null)
        {
            throw StockScrapError(404, InventoryStockScrapCodesV1.LabelNotFound);
        }
        if (label.Version != expectedVersion)
        {
            throw StockScrapError(409, InventoryStockScrapCodesV1.LabelVersionConflict);
        }

        var balances = await dbContext.Balances.AsNoTracking()
            .Where(row => row.LabelId == label.Id && row.QuantityKg > 0m)
            .ToListAsync(cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (balances.Count != 1)
        {
            throw StockScrapError(409, InventoryStockScrapCodesV1.PositionConflict);
        }

        var balance = balances[0];
        if (balance.ProductId != label.ProductId)
        {
            throw StockScrapError(503, InventoryStockScrapCodesV1.Unavailable);
        }

        // Fail-closed: writing off another party's goods, or our goods in another party's custody (a fason or
        // external keeper, where loss/damage is its own event under DEC-STK-008), is not decided.
        if (balance.OwnerKind != (int)StockPartyKind.Company || balance.OwnerId != companyId
            || balance.CustodianKind != (int)StockPartyKind.Company || balance.CustodianId != companyId)
        {
            throw StockScrapError(409, InventoryStockScrapCodesV1.NotOwnStock);
        }

        if (scrapKg.Value > balance.QuantityKg)
        {
            throw StockScrapError(409, InventoryStockScrapCodesV1.ExceedsBook);
        }

        StockScrap scrap;
        try
        {
            scrap = new StockScrap(request.ScrapId, companyId,
                Position(balance.CompanyId, balance.OwnerKind, balance.OwnerId, balance.CustodianKind,
                    balance.CustodianId, balance.LocationId, IdentityOnly(label), balance.Block),
                Kilograms.Positive(balance.QuantityKg), scrapKg, request.ScrappedAtUtc, reason);
        }
        catch (InventoryRuleException exception)
        {
            throw StockScrapError(400, InventoryStockScrapCodesV1.ValidationFailed, exception);
        }

        await RequireScrapLeavesAllocationsAsync(label.Id, scrap.Remaining.Value, freshAuthorityFence,
            cancellationToken);

        var scale = await kilogramScale.CheckAsync(companyId,
            DateOnly.FromDateTime(request.ScrappedAtUtc.UtcDateTime), [scrap.Scrap], cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (!scale.IsAccepted)
        {
            throw StockScrapError(400, scale.Code ?? InventoryStockScrapCodesV1.ValidationFailed);
        }

        DbUpdateException? concurrentChange = null;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                var row = new StockScrapRow
                {
                    Id = scrap.Id,
                    CompanyId = companyId,
                    LabelId = label.Id,
                    LabelVersion = label.Version,
                    OwnerKind = balance.OwnerKind,
                    OwnerId = balance.OwnerId,
                    CustodianKind = balance.CustodianKind,
                    CustodianId = balance.CustodianId,
                    LocationId = balance.LocationId,
                    ProductId = balance.ProductId,
                    Block = balance.Block,
                    BookKg = scrap.Book.Value,
                    ScrapKg = scrap.Scrap.Value,
                    ScrappedAtUtc = scrap.ScrappedAtUtc,
                    Reason = scrap.Reason,
                    PayloadHash = payloadHash,
                    RecordedAtUtc = timeProvider.GetUtcNow(),
                    RecordedBy = metadata.ActorId.Trim(),
                    CorrelationId = metadata.CorrelationId.Trim(),
                };
                dbContext.StockScraps.Add(row);
                var view = StockScrapView(row, decision: null);
                dbContext.Commands.Add(StockScrapCommandRow(RecordStockScrapCommand, companyId, keyHash, payloadHash,
                    metadata, row.Id, view));
                Audit(metadata, companyId, StockScrapEntityType, row.Id, "inventory.stock-scrap.recorded.v1", "Record",
                    versionBefore: null, versionAfter: 1, before: "{}", after: JsonSerializer.Serialize(view, Json), row.Reason);

                await dbContext.SaveChangesAsync(cancellationToken);
                await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new(view, false, row.CorrelationId);
            }
            catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
                || IsUniqueConstraintViolation(exception))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                dbContext.ChangeTracker.Clear();
                concurrentChange = exception;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                dbContext.ChangeTracker.Clear();
                throw;
            }
        }

        if (await FindStockScrapKeyReplayAsync(RecordStockScrapCommand, keyHash, payloadHash, metadata.ActorId,
                freshAuthorityFence, cancellationToken) is { } winner)
        {
            return winner;
        }

        if (await FindStockScrapIdentityReplayAsync(request.ScrapId, payloadHash, freshAuthorityFence,
                cancellationToken) is { } identityWinner)
        {
            return identityWinner;
        }

        throw StockScrapError(409, InventoryStockScrapCodesV1.ConcurrentChange, concurrentChange);
    }

    public async Task<InventoryStockScrapResultV1> DecideStockScrapAsync(
        Guid scrapId,
        DecideInventoryStockScrapV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireMetadata(metadata);
        RequireStockScrapMetadata(metadata);
        RequireResolvedCompany();
        cancellationToken.ThrowIfCancellationRequested();

        if (scrapId == Guid.Empty || !Enum.IsDefined(request.Decision)
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > StockScrap.MaximumReasonLength)
        {
            throw StockScrapError(400, InventoryStockScrapCodesV1.ValidationFailed);
        }

        var reason = request.Reason.Trim();
        var decisionCode = request.Decision == InventoryStockScrapDecisionKindV1.Approve ? DecisionApproved : DecisionRejected;
        var companyId = companyContext.CompanyId;
        var keyHash = GenealogyHash(metadata.IdempotencyKey.Trim());
        var payloadHash = GenealogyPayloadHash(new { scrapId, decision = decisionCode, reason });
        if (await FindStockScrapKeyReplayAsync(DecideStockScrapCommand, keyHash, payloadHash, metadata.ActorId,
                freshAuthorityFence, cancellationToken) is { } replay)
        {
            return replay;
        }

        if (await FindScrapDecisionReplayAsync(scrapId, payloadHash, metadata.ActorId,
                freshAuthorityFence, cancellationToken) is { } decisionReplay)
        {
            return decisionReplay;
        }

        var scrapRow = await dbContext.StockScraps.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == scrapId, cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (scrapRow is null)
        {
            throw StockScrapError(404, InventoryStockScrapCodesV1.ScrapNotFound);
        }

        // Fail-closed maker≠checker: the scrap approval authority is undecided, so every scrap is decided by someone
        // other than its recorder (the AT-037 stock-count rule, applied to every write-off).
        if (string.Equals(scrapRow.RecordedBy, metadata.ActorId.Trim(), StringComparison.Ordinal))
        {
            throw StockScrapError(409, InventoryStockScrapCodesV1.SelfApproval);
        }

        DbUpdateException? concurrentChange = null;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                var now = timeProvider.GetUtcNow();
                Guid? movementId = null;
                if (decisionCode == DecisionApproved)
                {
                    var label = await dbContext.Labels
                        .SingleOrDefaultAsync(row => row.Id == scrapRow.LabelId, cancellationToken);
                    await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
                    if (label is null)
                    {
                        throw StockScrapError(503, InventoryStockScrapCodesV1.Unavailable);
                    }

                    if (label.Version != scrapRow.LabelVersion)
                    {
                        throw StockScrapError(409, InventoryStockScrapCodesV1.Stale);
                    }

                    var scrap = RestoreStockScrap(scrapRow, label);
                    await RequireScrapLeavesAllocationsAsync(scrapRow.LabelId, scrap.Remaining.Value,
                        freshAuthorityFence, cancellationToken);

                    var movement = scrap.WriteOff(Guid.NewGuid(), now);
                    try
                    {
                        if (await StageMovementAsync(movement, metadata, recordAudit: false, cancellationToken)
                            != InventoryWriteOutcome.Recorded)
                        {
                            throw StockScrapError(503, InventoryStockScrapCodesV1.Unavailable);
                        }

                        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
                        Audit(metadata, movement.CompanyId, MovementEntityType, movement.Id,
                            "inventory.movement.recorded.v1", "Record",
                            versionBefore: null, versionAfter: 1, before: "{}",
                            after: MovementAudit(movement, PayloadHash(movement)),
                            $"Stok hareketi ({movement.Kind})");
                    }
                    catch (InventoryStoreException exception)
                    {
                        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
                        throw exception.Code switch
                        {
                            InventoryScaleCodes.ScaleExceeded => StockScrapError(400, exception.Code, exception),
                            InventoryStoreCodes.DuplicateSource or InventoryStoreCodes.SourceGrainMismatch
                                or InventoryStoreCodes.ConcurrentChange =>
                                StockScrapError(409, InventoryStockScrapCodesV1.ConcurrentChange, exception),
                            _ when InventoryStockScrapHttpCodesV1.Status503Codes.Contains(exception.Code) =>
                                StockScrapError(503, exception.Code, exception),
                            _ => StockScrapError(503, InventoryStockScrapCodesV1.Unavailable, exception),
                        };
                    }
                    catch (InventoryRuleException exception)
                    {
                        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
                        // The label is still at the recorded version, so the ledger agrees with the stored book; a
                        // refusal here means the stored state itself is not trustworthy.
                        throw StockScrapError(503, InventoryStockScrapCodesV1.Unavailable, exception);
                    }

                    movementId = movement.Id;
                }

                var decision = new StockScrapDecisionRow
                {
                    CompanyId = companyId,
                    ScrapId = scrapRow.Id,
                    Decision = decisionCode,
                    Reason = reason,
                    MovementId = movementId,
                    PayloadHash = payloadHash,
                    DecidedAtUtc = now,
                    DecidedBy = metadata.ActorId.Trim(),
                    CorrelationId = metadata.CorrelationId.Trim(),
                };
                dbContext.StockScrapDecisions.Add(decision);
                var before = StockScrapView(scrapRow, decision: null);
                var view = StockScrapView(scrapRow, decision);
                dbContext.Commands.Add(StockScrapCommandRow(DecideStockScrapCommand, companyId, keyHash, payloadHash,
                    metadata, scrapRow.Id, view));
                Audit(metadata, companyId, StockScrapEntityType, scrapRow.Id,
                    decisionCode == DecisionApproved
                        ? "inventory.stock-scrap.approved.v1"
                        : "inventory.stock-scrap.rejected.v1",
                    decisionCode == DecisionApproved ? "Approve" : "Reject",
                    versionBefore: 1, versionAfter: 2, before: JsonSerializer.Serialize(before, Json),
                    after: JsonSerializer.Serialize(view, Json), reason);

                await dbContext.SaveChangesAsync(cancellationToken);
                await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new(view, false, decision.CorrelationId);
            }
            catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
                || IsUniqueConstraintViolation(exception))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                dbContext.ChangeTracker.Clear();
                concurrentChange = exception;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                dbContext.ChangeTracker.Clear();
                throw;
            }
        }

        if (await FindStockScrapKeyReplayAsync(DecideStockScrapCommand, keyHash, payloadHash, metadata.ActorId,
                freshAuthorityFence, cancellationToken) is { } winner)
        {
            return winner;
        }

        if (await FindScrapDecisionReplayAsync(scrapId, payloadHash, metadata.ActorId, freshAuthorityFence,
                cancellationToken) is { } decisionWinner)
        {
            return decisionWinner;
        }

        throw StockScrapError(409, InventoryStockScrapCodesV1.ConcurrentChange, concurrentChange);
    }

    public async Task<InventoryLabelStockScrapsV1?> ReadStockScrapsAsync(
        Guid labelId,
        int limit = InventoryStockScrapApiV1.LabelScrapsLimit,
        CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        if (labelId == Guid.Empty || limit is < 1 or > InventoryStockScrapApiV1.LabelScrapsLimit)
        {
            throw StockScrapError(400, InventoryStockScrapCodesV1.ValidationFailed);
        }

        if (!await dbContext.Labels.AsNoTracking().AnyAsync(row => row.Id == labelId, cancellationToken))
        {
            return null;
        }

        var scraps = await dbContext.StockScraps.AsNoTracking()
            .Where(row => row.LabelId == labelId)
            .OrderByDescending(row => row.RecordedAtUtc).ThenByDescending(row => row.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        var windowFull = scraps.Count > limit;
        if (windowFull)
        {
            scraps.RemoveAt(scraps.Count - 1);
        }

        var ids = scraps.Select(row => row.Id).ToArray();
        var decisions = await dbContext.StockScrapDecisions.AsNoTracking()
            .Where(row => ids.Contains(row.ScrapId))
            .ToDictionaryAsync(row => row.ScrapId, cancellationToken);
        return new InventoryLabelStockScrapsV1(labelId,
            [.. scraps.Select(row => StockScrapView(row, decisions.GetValueOrDefault(row.Id)))], windowFull);
    }

    private static (long ExpectedVersion, Kilograms Scrap, string Reason) ParseStockScrap(RecordInventoryStockScrapV1 request)
    {
        if (request.ScrapId == Guid.Empty || request.LabelId == Guid.Empty
            || !TryVersion(request.ExpectedLabelVersion, out var version)
            || !TryQuantity(request.ScrapKg, out var scrap)
            || request.ScrappedAtUtc.Offset != TimeSpan.Zero
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > StockScrap.MaximumReasonLength)
        {
            throw StockScrapError(400, InventoryStockScrapCodesV1.ValidationFailed);
        }

        return (version, scrap, request.Reason.Trim());
    }

    /// <summary>A reservation is never silently broken: what stays on the label must cover its active allocations.</summary>
    private async Task RequireScrapLeavesAllocationsAsync(
        Guid labelId,
        decimal remainingKg,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken)
    {
        var reserved = await dbContext.Allocations.AsNoTracking()
            .Where(row => row.LabelId == labelId && row.Status == (int)StockAllocationStatus.Active)
            .SumAsync(row => (decimal?)row.RemainingKg, cancellationToken) ?? 0m;
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (reserved > remainingKg)
        {
            throw StockScrapError(409, InventoryStockScrapCodesV1.AllocationConflict);
        }
    }

    private async Task<InventoryStockScrapResultV1?> FindStockScrapKeyReplayAsync(
        string command,
        string keyHash,
        string payloadHash,
        string actorId,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken)
    {
        var prior = await dbContext.Commands.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == command && row.IdempotencyKeyHash == keyHash, cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (prior is null)
        {
            return null;
        }

        if (!string.Equals(prior.PayloadHash, payloadHash, StringComparison.Ordinal)
            || !string.Equals(prior.ActorId, actorId, StringComparison.Ordinal))
        {
            throw StockScrapError(409, InventoryStockScrapCodesV1.IdempotencyConflict);
        }

        var stored = JsonSerializer.Deserialize<StockScrapCommandResult>(prior.ResultJson, Json)
            ?? throw StockScrapError(503, InventoryStockScrapCodesV1.Unavailable);
        return new(stored.Scrap, true, prior.CorrelationId);
    }

    /// <summary>The same scrap id: the same content is the stored scrap again; other content is a conflict.</summary>
    private async Task<InventoryStockScrapResultV1?> FindStockScrapIdentityReplayAsync(
        Guid scrapId,
        string payloadHash,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken)
    {
        var stored = await dbContext.StockScraps.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == scrapId, cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (stored is null)
        {
            return null;
        }

        if (!string.Equals(stored.PayloadHash, payloadHash, StringComparison.Ordinal))
        {
            throw StockScrapError(409, InventoryStockScrapCodesV1.ScrapConflict);
        }

        // The recorded scrap as it was recorded: a later decision is the decision's answer, not this one's.
        return new(StockScrapView(stored, decision: null), true, stored.CorrelationId);
    }

    /// <summary>A scrap already decided: the same decision and note from its decider is that decision again; anything else is refused.</summary>
    private async Task<InventoryStockScrapResultV1?> FindScrapDecisionReplayAsync(
        Guid scrapId,
        string payloadHash,
        string actorId,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken)
    {
        var decision = await dbContext.StockScrapDecisions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.ScrapId == scrapId, cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (decision is null)
        {
            return null;
        }

        if (!string.Equals(decision.PayloadHash, payloadHash, StringComparison.Ordinal))
        {
            throw StockScrapError(409, InventoryStockScrapCodesV1.AlreadyDecided);
        }

        var scrap = await dbContext.StockScraps.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == scrapId, cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (scrap is null)
        {
            throw StockScrapError(503, InventoryStockScrapCodesV1.Unavailable);
        }
        if (DecisionReplayRefusal(decision.DecidedBy, scrap.RecordedBy, actorId, InventoryStockScrapCodesV1.SelfApproval,
                InventoryStockScrapCodesV1.AlreadyDecided) is { } refusal)
        {
            throw StockScrapError(409, refusal);
        }

        return new(StockScrapView(scrap, decision), true, decision.CorrelationId);
    }

    private InventoryCommandRow StockScrapCommandRow(
        string command,
        Guid companyId,
        string keyHash,
        string payloadHash,
        InventoryCommandMetadata metadata,
        Guid targetId,
        InventoryStockScrapViewV1 view) => new()
        {
            CompanyId = companyId,
            CommandName = command,
            IdempotencyKeyHash = keyHash,
            ActorId = metadata.ActorId,
            PayloadHash = payloadHash,
            TargetId = targetId,
            ResultJson = JsonSerializer.Serialize(new StockScrapCommandResult(view), Json),
            CorrelationId = metadata.CorrelationId.Trim(),
            RecordedAtUtc = timeProvider.GetUtcNow(),
        };

    /// <summary>Rebuilds a stored scrap through the domain: a stored row that breaks a rule is unavailable, not repaired.</summary>
    private static StockScrap RestoreStockScrap(StockScrapRow row, LabelRow label)
    {
        if (label.Id != row.LabelId || label.ProductId != row.ProductId)
        {
            throw StockScrapError(503, InventoryStockScrapCodesV1.Unavailable);
        }

        try
        {
            return new StockScrap(row.Id, row.CompanyId,
                Position(row.CompanyId, row.OwnerKind, row.OwnerId, row.CustodianKind, row.CustodianId, row.LocationId,
                    IdentityOnly(label), row.Block),
                Kilograms.Positive(row.BookKg), Kilograms.Positive(row.ScrapKg), row.ScrappedAtUtc, row.Reason);
        }
        catch (InventoryRuleException exception)
        {
            throw StockScrapError(503, InventoryStockScrapCodesV1.Unavailable, exception);
        }
    }

    private static InventoryStockScrapViewV1 StockScrapView(StockScrapRow row, StockScrapDecisionRow? decision)
    {
        var status = decision?.Decision switch
        {
            DecisionApproved => InventoryStockScrapStatusesV1.Approved,
            DecisionRejected => InventoryStockScrapStatusesV1.Rejected,
            null => InventoryStockScrapStatusesV1.PendingApproval,
            _ => throw StockScrapError(503, InventoryStockScrapCodesV1.Unavailable),
        };
        return new InventoryStockScrapViewV1(
            row.Id,
            row.CompanyId,
            row.LabelId,
            row.LabelVersion.ToString(CultureInfo.InvariantCulture),
            ((StockPartyKind)row.OwnerKind).ToString(),
            row.OwnerId,
            ((StockPartyKind)row.CustodianKind).ToString(),
            row.CustodianId,
            row.LocationId,
            row.ProductId,
            ((BlockState)row.Block).ToString(),
            Kilograms.Of(row.BookKg).ToString(),
            Kilograms.Of(row.ScrapKg).ToString(),
            Kilograms.Of(row.BookKg - row.ScrapKg).ToString(),
            row.ScrappedAtUtc,
            row.Reason,
            row.RecordedAtUtc,
            status,
            decision is null
                ? null
                : new InventoryStockScrapDecisionViewV1(
                    decision.Decision == DecisionApproved ? InventoryStockScrapStatusesV1.Approved : InventoryStockScrapStatusesV1.Rejected,
                    decision.DecidedAtUtc,
                    decision.Reason,
                    decision.MovementId));
    }

    private static void RequireStockScrapMetadata(InventoryCommandMetadata metadata)
    {
        if (!string.Equals(metadata.IdempotencyKey, metadata.IdempotencyKey.Trim(), StringComparison.Ordinal)
            || metadata.IdempotencyKey.Length > 128 || metadata.IdempotencyKey.Any(char.IsControl)
            || !string.Equals(metadata.CorrelationId, metadata.CorrelationId.Trim(), StringComparison.Ordinal)
            || metadata.CorrelationId.Length > 128)
        {
            throw StockScrapError(400, InventoryStockScrapCodesV1.ValidationFailed);
        }
    }

    private static InventoryStockScrapException StockScrapError(int status, string code, Exception? inner = null) =>
        new(status, code, inner);

    private sealed record StockScrapCommandResult(InventoryStockScrapViewV1 Scrap);
}
