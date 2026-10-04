using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Counts;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// ST-05 / AT-037 (DEC-STK-007): record a count of one stocked label, and decide its difference once.
/// <list type="bullet">
/// <item><b>Record:</b> the label at the version the counter saw, holding stock in exactly one stored position.
/// The book kg, the position and the counted kg are kept with the mandatory reason. No stock changes, no label
/// version is bumped: a count is evidence, not an effect. A zero difference closes at once
/// (<c>NoDifference</c>).</item>
/// <item><b>Decide:</b> only a count with a difference, only once (the decision key is the count), and only by
/// another actor than the counter — fail-closed while the critical-difference policy is undecided
/// (INV-R20-R21-01 Q2). An approval posts exactly one <c>CountAdjustment</c> movement through the ledger (no
/// negative stock, one physical place, label version bumped) and only while the label is still at the counted
/// version; otherwise the count is stale and must be counted again. A shortage below the label's active
/// allocations is refused. A rejection posts nothing.</item>
/// <item><b>Replay (ADR-011, R3):</b> the Idempotency-Key per command, and the count id / count decision as the
/// business identity: the same content answers the stored result with its first correlation and no second
/// effect; other content is a conflict.</item>
/// </list>
/// Everything is one save with its command row and audit events; nothing is ever updated or deleted.
/// </summary>
public sealed partial class InventoryStore : IInventoryStockCountWriterV1, IInventoryStockCountReaderV1
{
    internal const string RecordStockCountCommand = "inventory.stock-count.record.v1";
    internal const string DecideStockCountCommand = "inventory.stock-count.decide.v1";
    private const string StockCountEntityType = "Inventory.StockCount";
    private const int DecisionApproved = 1;
    private const int DecisionRejected = 2;

    public async Task<InventoryStockCountResultV1> RecordStockCountAsync(
        RecordInventoryStockCountV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireMetadata(metadata);
        RequireStockCountMetadata(metadata);
        RequireResolvedCompany();
        cancellationToken.ThrowIfCancellationRequested();

        var (expectedVersion, counted, reason) = ParseStockCount(request);
        var companyId = companyContext.CompanyId;
        var keyHash = GenealogyHash(metadata.IdempotencyKey.Trim());
        var payloadHash = GenealogyPayloadHash(new
        {
            request.CountId,
            request.LabelId,
            expectedVersion,
            countedKg = counted.ToString(),
            countedAtUtc = request.CountedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            reason,
        });
        if (await FindStockCountKeyReplayAsync(RecordStockCountCommand, keyHash, payloadHash, metadata.ActorId,
                freshAuthorityFence, cancellationToken) is { } replay)
        {
            return replay;
        }

        if (await FindStockCountIdentityReplayAsync(request.CountId, payloadHash, freshAuthorityFence,
                cancellationToken) is { } identityReplay)
        {
            return identityReplay;
        }

        var label = await dbContext.Labels.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == request.LabelId, cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (label is null)
        {
            throw StockCountError(404, InventoryStockCountCodesV1.LabelNotFound);
        }
        if (label.Version != expectedVersion)
        {
            throw StockCountError(409, InventoryStockCountCodesV1.LabelVersionConflict);
        }

        var balances = await dbContext.Balances.AsNoTracking()
            .Where(row => row.LabelId == label.Id && row.QuantityKg > 0m)
            .ToListAsync(cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (balances.Count != 1)
        {
            throw StockCountError(409, InventoryStockCountCodesV1.PositionConflict);
        }

        var balance = balances[0];
        if (balance.ProductId != label.ProductId)
        {
            throw StockCountError(503, InventoryStockCountCodesV1.Unavailable);
        }

        StockCount count;
        try
        {
            count = new StockCount(request.CountId, companyId,
                Position(balance.CompanyId, balance.OwnerKind, balance.OwnerId, balance.CustodianKind,
                    balance.CustodianId, balance.LocationId, IdentityOnly(label), balance.Block),
                Kilograms.Positive(balance.QuantityKg), counted, request.CountedAtUtc, reason);
        }
        catch (InventoryRuleException exception)
        {
            throw StockCountError(400, InventoryStockCountCodesV1.ValidationFailed, exception);
        }

        var checkedQuantities = new[] { counted.Value, Math.Abs(count.Difference) }
            .Where(value => value > 0m).Select(Kilograms.Positive).ToArray();
        if (checkedQuantities.Length > 0)
        {
            var scale = await kilogramScale.CheckAsync(companyId,
                DateOnly.FromDateTime(request.CountedAtUtc.UtcDateTime), checkedQuantities, cancellationToken);
            await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
            if (!scale.IsAccepted)
            {
                throw StockCountError(400, scale.Code ?? InventoryStockCountCodesV1.ValidationFailed);
            }
        }

        DbUpdateException? concurrentChange = null;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                var row = new StockCountRow
                {
                    Id = count.Id,
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
                    BookKg = count.Book.Value,
                    CountedKg = count.Counted.Value,
                    CountedAtUtc = count.CountedAtUtc,
                    Reason = count.Reason,
                    PayloadHash = payloadHash,
                    RecordedAtUtc = timeProvider.GetUtcNow(),
                    RecordedBy = metadata.ActorId.Trim(),
                    CorrelationId = metadata.CorrelationId.Trim(),
                };
                dbContext.StockCounts.Add(row);
                var view = StockCountView(row, decision: null);
                dbContext.Commands.Add(StockCountCommandRow(RecordStockCountCommand, companyId, keyHash, payloadHash,
                    metadata, row.Id, view));
                Audit(metadata, companyId, StockCountEntityType, row.Id, "inventory.stock-count.recorded.v1", "Record",
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

        if (await FindStockCountKeyReplayAsync(RecordStockCountCommand, keyHash, payloadHash, metadata.ActorId,
                freshAuthorityFence, cancellationToken) is { } winner)
        {
            return winner;
        }

        if (await FindStockCountIdentityReplayAsync(request.CountId, payloadHash, freshAuthorityFence,
                cancellationToken) is { } identityWinner)
        {
            return identityWinner;
        }

        throw StockCountError(409, InventoryStockCountCodesV1.ConcurrentChange, concurrentChange);
    }

    public async Task<InventoryStockCountResultV1> DecideStockCountAsync(
        Guid countId,
        DecideInventoryStockCountV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireMetadata(metadata);
        RequireStockCountMetadata(metadata);
        RequireResolvedCompany();
        cancellationToken.ThrowIfCancellationRequested();

        if (countId == Guid.Empty || !Enum.IsDefined(request.Decision)
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > StockCount.MaximumReasonLength)
        {
            throw StockCountError(400, InventoryStockCountCodesV1.ValidationFailed);
        }

        var reason = request.Reason.Trim();
        var decisionCode = request.Decision == InventoryStockCountDecisionKindV1.Approve ? DecisionApproved : DecisionRejected;
        var companyId = companyContext.CompanyId;
        var keyHash = GenealogyHash(metadata.IdempotencyKey.Trim());
        var payloadHash = GenealogyPayloadHash(new { countId, decision = decisionCode, reason });
        if (await FindStockCountKeyReplayAsync(DecideStockCountCommand, keyHash, payloadHash, metadata.ActorId,
                freshAuthorityFence, cancellationToken) is { } replay)
        {
            return replay;
        }

        if (await FindDecisionReplayAsync(countId, payloadHash, metadata.ActorId,
                freshAuthorityFence, cancellationToken) is { } decisionReplay)
        {
            return decisionReplay;
        }

        var countRow = await dbContext.StockCounts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == countId, cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (countRow is null)
        {
            throw StockCountError(404, InventoryStockCountCodesV1.CountNotFound);
        }
        if (countRow.CountedKg == countRow.BookKg)
        {
            throw StockCountError(409, InventoryStockCountCodesV1.NotPending);
        }

        // Fail-closed maker≠checker (DEC-STK-007 "kritik farkta ikinci onay"; the threshold is undecided,
        // INV-R20-R21-01 Q2): every non-zero difference is decided by someone other than its counter.
        if (string.Equals(countRow.RecordedBy, metadata.ActorId.Trim(), StringComparison.Ordinal))
        {
            throw StockCountError(409, InventoryStockCountCodesV1.SelfApproval);
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
                        .SingleOrDefaultAsync(row => row.Id == countRow.LabelId, cancellationToken);
                    await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
                    if (label is null)
                    {
                        throw StockCountError(503, InventoryStockCountCodesV1.Unavailable);
                    }

                    if (label.Version != countRow.LabelVersion)
                    {
                        throw StockCountError(409, InventoryStockCountCodesV1.Stale);
                    }

                    var count = RestoreStockCount(countRow, label);
                    if (count.Difference < 0m)
                    {
                        var reserved = await dbContext.Allocations.AsNoTracking()
                            .Where(row => row.LabelId == countRow.LabelId
                                && row.Status == (int)StockAllocationStatus.Active)
                            .SumAsync(row => (decimal?)row.RemainingKg, cancellationToken) ?? 0m;
                        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
                        if (reserved > count.Counted.Value)
                        {
                            throw StockCountError(409, InventoryStockCountCodesV1.AllocationConflict);
                        }
                    }

                    var movement = count.Adjustment(Guid.NewGuid(), now);
                    try
                    {
                        if (await StageMovementAsync(movement, metadata, recordAudit: false, cancellationToken)
                            != InventoryWriteOutcome.Recorded)
                        {
                            throw StockCountError(503, InventoryStockCountCodesV1.Unavailable);
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
                            InventoryScaleCodes.ScaleExceeded => StockCountError(400, exception.Code, exception),
                            InventoryStoreCodes.DuplicateSource or InventoryStoreCodes.SourceGrainMismatch
                                or InventoryStoreCodes.ConcurrentChange =>
                                StockCountError(409, InventoryStockCountCodesV1.ConcurrentChange, exception),
                            _ when InventoryStockCountHttpCodesV1.Status503Codes.Contains(exception.Code) =>
                                StockCountError(503, exception.Code, exception),
                            _ => StockCountError(503, InventoryStockCountCodesV1.Unavailable, exception),
                        };
                    }
                    catch (InventoryRuleException exception)
                    {
                        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
                        // The label is still at the counted version, so the ledger agrees with the stored book; a
                        // refusal here means the stored state itself is not trustworthy.
                        throw StockCountError(503, InventoryStockCountCodesV1.Unavailable, exception);
                    }

                    movementId = movement.Id;
                }

                var decision = new StockCountDecisionRow
                {
                    CompanyId = companyId,
                    CountId = countRow.Id,
                    Decision = decisionCode,
                    Reason = reason,
                    MovementId = movementId,
                    PayloadHash = payloadHash,
                    DecidedAtUtc = now,
                    DecidedBy = metadata.ActorId.Trim(),
                    CorrelationId = metadata.CorrelationId.Trim(),
                };
                dbContext.StockCountDecisions.Add(decision);
                var before = StockCountView(countRow, decision: null);
                var view = StockCountView(countRow, decision);
                dbContext.Commands.Add(StockCountCommandRow(DecideStockCountCommand, companyId, keyHash, payloadHash,
                    metadata, countRow.Id, view));
                Audit(metadata, companyId, StockCountEntityType, countRow.Id,
                    decisionCode == DecisionApproved
                        ? "inventory.stock-count.approved.v1"
                        : "inventory.stock-count.rejected.v1",
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

        if (await FindStockCountKeyReplayAsync(DecideStockCountCommand, keyHash, payloadHash, metadata.ActorId,
                freshAuthorityFence, cancellationToken) is { } winner)
        {
            return winner;
        }

        if (await FindDecisionReplayAsync(countId, payloadHash, metadata.ActorId, freshAuthorityFence,
                cancellationToken) is { } decisionWinner)
        {
            return decisionWinner;
        }

        throw StockCountError(409, InventoryStockCountCodesV1.ConcurrentChange, concurrentChange);
    }

    public async Task<InventoryLabelStockCountsV1?> ReadStockCountsAsync(
        Guid labelId,
        int limit = InventoryStockCountApiV1.LabelCountsLimit,
        CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        if (labelId == Guid.Empty || limit is < 1 or > InventoryStockCountApiV1.LabelCountsLimit)
        {
            throw StockCountError(400, InventoryStockCountCodesV1.ValidationFailed);
        }

        if (!await dbContext.Labels.AsNoTracking().AnyAsync(row => row.Id == labelId, cancellationToken))
        {
            return null;
        }

        var counts = await dbContext.StockCounts.AsNoTracking()
            .Where(row => row.LabelId == labelId)
            .OrderByDescending(row => row.RecordedAtUtc).ThenByDescending(row => row.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        var windowFull = counts.Count > limit;
        if (windowFull)
        {
            counts.RemoveAt(counts.Count - 1);
        }

        var ids = counts.Select(row => row.Id).ToArray();
        var decisions = await dbContext.StockCountDecisions.AsNoTracking()
            .Where(row => ids.Contains(row.CountId))
            .ToDictionaryAsync(row => row.CountId, cancellationToken);
        return new InventoryLabelStockCountsV1(labelId,
            [.. counts.Select(row => StockCountView(row, decisions.GetValueOrDefault(row.Id)))], windowFull);
    }

    private static (long ExpectedVersion, Kilograms Counted, string Reason) ParseStockCount(RecordInventoryStockCountV1 request)
    {
        if (request.CountId == Guid.Empty || request.LabelId == Guid.Empty
            || !TryVersion(request.ExpectedLabelVersion, out var version)
            || !TryCountedQuantity(request.CountedKg, out var counted)
            || request.CountedAtUtc.Offset != TimeSpan.Zero
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > StockCount.MaximumReasonLength)
        {
            throw StockCountError(400, InventoryStockCountCodesV1.ValidationFailed);
        }

        return (version, counted, request.Reason.Trim());
    }

    /// <summary>A counted amount: canonical non-negative kg text with at most 3 decimals ("0" = the piece is gone).</summary>
    private static bool TryCountedQuantity(string? value, out Kilograms quantity)
    {
        quantity = Kilograms.Zero;
        if (string.IsNullOrEmpty(value) || value.Length > 40
            || value.Count(character => character == '.') > 1
            || value.Any(character => !char.IsAsciiDigit(character) && character != '.')
            || !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            || parsed < 0m || ((decimal.GetBits(parsed)[3] >> 16) & 0xFF) > Kilograms.MaximumDecimals)
        {
            return false;
        }

        quantity = Kilograms.Of(parsed);
        return true;
    }

    private async Task<InventoryStockCountResultV1?> FindStockCountKeyReplayAsync(
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
            throw StockCountError(409, InventoryStockCountCodesV1.IdempotencyConflict);
        }

        var stored = JsonSerializer.Deserialize<StockCountCommandResult>(prior.ResultJson, Json)
            ?? throw StockCountError(503, InventoryStockCountCodesV1.Unavailable);
        return new(stored.Count, true, prior.CorrelationId);
    }

    /// <summary>The same count id: the same content is the stored count again; other content is a conflict.</summary>
    private async Task<InventoryStockCountResultV1?> FindStockCountIdentityReplayAsync(
        Guid countId,
        string payloadHash,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken)
    {
        var stored = await dbContext.StockCounts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == countId, cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (stored is null)
        {
            return null;
        }

        if (!string.Equals(stored.PayloadHash, payloadHash, StringComparison.Ordinal))
        {
            throw StockCountError(409, InventoryStockCountCodesV1.CountConflict);
        }

        // The recorded count as it was recorded: a later decision is the decision's answer, not this one's.
        return new(StockCountView(stored, decision: null), true, stored.CorrelationId);
    }

    /// <summary>A count already decided: the same decision and note from its decider is that decision again; anything else is refused.</summary>
    private async Task<InventoryStockCountResultV1?> FindDecisionReplayAsync(
        Guid countId,
        string payloadHash,
        string actorId,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken)
    {
        var decision = await dbContext.StockCountDecisions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CountId == countId, cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (decision is null)
        {
            return null;
        }

        if (!string.Equals(decision.PayloadHash, payloadHash, StringComparison.Ordinal))
        {
            throw StockCountError(409, InventoryStockCountCodesV1.AlreadyDecided);
        }

        var count = await dbContext.StockCounts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == countId, cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (count is null)
        {
            throw StockCountError(503, InventoryStockCountCodesV1.Unavailable);
        }
        if (DecisionReplayRefusal(decision.DecidedBy, count.RecordedBy, actorId, InventoryStockCountCodesV1.SelfApproval,
                InventoryStockCountCodesV1.AlreadyDecided) is { } refusal)
        {
            throw StockCountError(409, refusal);
        }

        return new(StockCountView(count, decision), true, decision.CorrelationId);
    }

    /// <summary>
    /// A decided count or scrap is replayed only to its decider. The same decision content from anyone else is not
    /// their replay: the recorder meets the maker≠checker refusal, any other actor the already-decided one. Null is
    /// the decider's own replay.
    /// </summary>
    internal static string? DecisionReplayRefusal(
        string decidedBy,
        string recordedBy,
        string actorId,
        string selfApprovalCode,
        string alreadyDecidedCode)
    {
        var actor = actorId.Trim();
        if (string.Equals(decidedBy, actor, StringComparison.Ordinal))
        {
            return null;
        }

        return string.Equals(recordedBy, actor, StringComparison.Ordinal) ? selfApprovalCode : alreadyDecidedCode;
    }

    private InventoryCommandRow StockCountCommandRow(
        string command,
        Guid companyId,
        string keyHash,
        string payloadHash,
        InventoryCommandMetadata metadata,
        Guid targetId,
        InventoryStockCountViewV1 view) => new()
        {
            CompanyId = companyId,
            CommandName = command,
            IdempotencyKeyHash = keyHash,
            ActorId = metadata.ActorId,
            PayloadHash = payloadHash,
            TargetId = targetId,
            ResultJson = JsonSerializer.Serialize(new StockCountCommandResult(view), Json),
            CorrelationId = metadata.CorrelationId.Trim(),
            RecordedAtUtc = timeProvider.GetUtcNow(),
        };

    /// <summary>Rebuilds a stored count through the domain: a stored row that breaks a rule is unavailable, not repaired.</summary>
    private static StockCount RestoreStockCount(StockCountRow row, LabelRow label)
    {
        if (label.Id != row.LabelId || label.ProductId != row.ProductId)
        {
            throw StockCountError(503, InventoryStockCountCodesV1.Unavailable);
        }

        try
        {
            return new StockCount(row.Id, row.CompanyId,
                Position(row.CompanyId, row.OwnerKind, row.OwnerId, row.CustodianKind, row.CustodianId, row.LocationId,
                    IdentityOnly(label), row.Block),
                Kilograms.Positive(row.BookKg), Kilograms.Of(row.CountedKg), row.CountedAtUtc, row.Reason);
        }
        catch (InventoryRuleException exception)
        {
            throw StockCountError(503, InventoryStockCountCodesV1.Unavailable, exception);
        }
    }

    private static InventoryStockCountViewV1 StockCountView(StockCountRow row, StockCountDecisionRow? decision)
    {
        var difference = row.CountedKg - row.BookKg;
        var status = decision?.Decision switch
        {
            DecisionApproved => InventoryStockCountStatusesV1.Approved,
            DecisionRejected => InventoryStockCountStatusesV1.Rejected,
            null when difference == 0m => InventoryStockCountStatusesV1.NoDifference,
            null => InventoryStockCountStatusesV1.PendingApproval,
            _ => throw StockCountError(503, InventoryStockCountCodesV1.Unavailable),
        };
        return new InventoryStockCountViewV1(
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
            Kilograms.Of(row.CountedKg).ToString(),
            SignedKilograms(difference),
            row.CountedAtUtc,
            row.Reason,
            row.RecordedAtUtc,
            status,
            decision is null
                ? null
                : new InventoryStockCountDecisionViewV1(
                    decision.Decision == DecisionApproved ? InventoryStockCountStatusesV1.Approved : InventoryStockCountStatusesV1.Rejected,
                    decision.DecidedAtUtc,
                    decision.Reason,
                    decision.MovementId));
    }

    /// <summary>A signed difference as canonical kg text: no trailing zeros, a leading minus for a shortage.</summary>
    internal static string SignedKilograms(decimal value) =>
        value < 0m ? "-" + Kilograms.Of(-value).ToString() : Kilograms.Of(value).ToString();

    private static void RequireStockCountMetadata(InventoryCommandMetadata metadata)
    {
        if (!string.Equals(metadata.IdempotencyKey, metadata.IdempotencyKey.Trim(), StringComparison.Ordinal)
            || metadata.IdempotencyKey.Length > 128 || metadata.IdempotencyKey.Any(char.IsControl)
            || !string.Equals(metadata.CorrelationId, metadata.CorrelationId.Trim(), StringComparison.Ordinal)
            || metadata.CorrelationId.Length > 128)
        {
            throw StockCountError(400, InventoryStockCountCodesV1.ValidationFailed);
        }
    }

    private static InventoryStockCountException StockCountError(int status, string code, Exception? inner = null) =>
        new(status, code, inner);

    private sealed record StockCountCommandResult(InventoryStockCountViewV1 Count);
}
