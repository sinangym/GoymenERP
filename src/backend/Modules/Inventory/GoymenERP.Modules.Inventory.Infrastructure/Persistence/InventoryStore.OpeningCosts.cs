using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// SAT-D304 / SAT-D372 opening-stock cost: submit the manually entered unit cost of one (location, product) position,
/// and decide it once, by another person. The stock-scrap (ST-02) shape.
/// <list type="bullet">
/// <item><b>Submit:</b> a positive base-currency cost per stock kilogram at the ledger's internal scale, with the
/// mandatory reason, for a position the cost ledger has never tracked and where the company holds its own stock. One
/// pending submission per position at a time. A submission changes no cost ledger.</item>
/// <item><b>Decide:</b> only once (the decision key is the submission), and only by another actor than the submitter
/// (giren ≠ onaylayan). An approval opens the moving-average position through
/// <see cref="AverageCostCalculator.ApplyOpeningCost"/> for the company's own stock on hand at the location measured at
/// approval, and keeps that figure on the decision; a position the ledger meanwhile tracks is refused, never
/// overwritten. The unique position index of the ledger refuses a concurrent opener at the save. A rejection changes
/// nothing but the status.</item>
/// <item><b>Replay (ADR-011, R3):</b> the Idempotency-Key per command, and the submission id / submission decision as
/// the business identity: the same content answers the stored result with its first correlation and no second effect;
/// other content is a conflict.</item>
/// </list>
/// Everything is one save with its command row and audit events; nothing is ever updated or deleted.
/// </summary>
public sealed partial class InventoryStore : IInventoryOpeningCostWriterV1
{
    internal const string SubmitOpeningCostCommand = "inventory.opening-cost.submit.v1";
    internal const string DecideOpeningCostCommand = "inventory.opening-cost.decide.v1";
    internal const int OpeningCostMaximumReasonLength = 1000;
    private const string OpeningCostEntityType = "Inventory.OpeningCost";
    private const int OpeningCostMaximumMetadataLength = 128;
    private const int OpeningCostMaximumTextLength = 40;

    /// <summary>The cost column is decimal(18, 4): at most 14 integer digits.</summary>
    private const decimal OpeningCostExclusiveUpperBound = 100_000_000_000_000m;

    private const string OpeningCostTextFormat = "0.####";

    public async Task<InventoryOpeningCostResultV1> SubmitOpeningCostAsync(
        SubmitInventoryOpeningCostV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireMetadata(metadata);
        RequireOpeningCostMetadata(metadata);
        RequireResolvedCompany();
        cancellationToken.ThrowIfCancellationRequested();

        var (unitCost, reason) = ParseOpeningCost(request);
        var companyId = companyContext.CompanyId;
        var keyHash = GenealogyHash(metadata.IdempotencyKey.Trim());
        var payloadHash = GenealogyPayloadHash(new
        {
            request.OpeningCostId,
            request.LocationId,
            request.ProductId,
            unitCostPerKg = OpeningCostText(unitCost),
            reason,
        });
        if (await FindOpeningCostKeyReplayAsync(SubmitOpeningCostCommand, keyHash, payloadHash, metadata.ActorId,
                freshAuthorityFence, cancellationToken) is { } replay)
        {
            return replay;
        }

        if (await FindOpeningCostIdentityReplayAsync(request.OpeningCostId, payloadHash, freshAuthorityFence,
                cancellationToken) is { } identityReplay)
        {
            return identityReplay;
        }

        await RequireOpeningCostPositionAsync(companyId, request.LocationId, request.ProductId, freshAuthorityFence,
            cancellationToken);

        var pending = await dbContext.OpeningCosts.AsNoTracking()
            .Where(row => row.LocationId == request.LocationId && row.ProductId == request.ProductId)
            .Where(row => !dbContext.OpeningCostDecisions.Any(decision => decision.OpeningCostId == row.Id))
            .AnyAsync(cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (pending)
        {
            throw OpeningCostError(409, InventoryOpeningCostCodesV1.PendingExists);
        }

        DbUpdateException? concurrentChange = null;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                var row = new OpeningCostRow
                {
                    Id = request.OpeningCostId,
                    CompanyId = companyId,
                    LocationId = request.LocationId,
                    ProductId = request.ProductId,
                    UnitCostPerKg = unitCost,
                    Reason = reason,
                    PayloadHash = payloadHash,
                    RecordedAtUtc = timeProvider.GetUtcNow(),
                    RecordedBy = metadata.ActorId.Trim(),
                    CorrelationId = metadata.CorrelationId.Trim(),
                };
                dbContext.OpeningCosts.Add(row);
                var view = OpeningCostView(row, decision: null);
                dbContext.Commands.Add(OpeningCostCommandRow(SubmitOpeningCostCommand, companyId, keyHash, payloadHash,
                    metadata, row.Id, view));
                Audit(metadata, companyId, OpeningCostEntityType, row.Id, "inventory.opening-cost.submitted.v1",
                    "Submit", versionBefore: null, versionAfter: 1, before: "{}",
                    after: JsonSerializer.Serialize(view, Json), row.Reason);

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

        if (await FindOpeningCostKeyReplayAsync(SubmitOpeningCostCommand, keyHash, payloadHash, metadata.ActorId,
                freshAuthorityFence, cancellationToken) is { } winner)
        {
            return winner;
        }

        if (await FindOpeningCostIdentityReplayAsync(request.OpeningCostId, payloadHash, freshAuthorityFence,
                cancellationToken) is { } identityWinner)
        {
            return identityWinner;
        }

        throw OpeningCostError(409, InventoryOpeningCostCodesV1.ConcurrentChange, concurrentChange);
    }

    public async Task<InventoryOpeningCostResultV1> DecideOpeningCostAsync(
        Guid openingCostId,
        DecideInventoryOpeningCostV1 request,
        InventoryCommandMetadata metadata,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireMetadata(metadata);
        RequireOpeningCostMetadata(metadata);
        RequireResolvedCompany();
        cancellationToken.ThrowIfCancellationRequested();

        if (openingCostId == Guid.Empty || !Enum.IsDefined(request.Decision)
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > OpeningCostMaximumReasonLength)
        {
            throw OpeningCostError(400, InventoryOpeningCostCodesV1.ValidationFailed);
        }

        var reason = request.Reason.Trim();
        var decisionCode = request.Decision == InventoryOpeningCostDecisionKindV1.Approve
            ? DecisionApproved
            : DecisionRejected;
        var companyId = companyContext.CompanyId;
        var keyHash = GenealogyHash(metadata.IdempotencyKey.Trim());
        var payloadHash = GenealogyPayloadHash(new { openingCostId, decision = decisionCode, reason });
        if (await FindOpeningCostKeyReplayAsync(DecideOpeningCostCommand, keyHash, payloadHash, metadata.ActorId,
                freshAuthorityFence, cancellationToken) is { } replay)
        {
            return replay;
        }

        if (await FindOpeningCostDecisionReplayAsync(openingCostId, payloadHash, metadata.ActorId,
                freshAuthorityFence, cancellationToken) is { } decisionReplay)
        {
            return decisionReplay;
        }

        var submission = await dbContext.OpeningCosts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == openingCostId, cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (submission is null)
        {
            throw OpeningCostError(404, InventoryOpeningCostCodesV1.NotFound);
        }

        // SAT-D304 (1): giren ≠ onaylayan. The submitter decides neither way.
        if (string.Equals(submission.RecordedBy, metadata.ActorId.Trim(), StringComparison.Ordinal))
        {
            throw OpeningCostError(409, InventoryOpeningCostCodesV1.SelfApproval);
        }

        DbUpdateException? concurrentChange = null;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                decimal? costedStockKg = null;
                if (decisionCode == DecisionApproved)
                {
                    var stockOnHandKg = await RequireOpeningCostPositionAsync(companyId, submission.LocationId,
                        submission.ProductId, freshAuthorityFence, cancellationToken);
                    var ledger = new InventoryAverageCostLedgerWriter(dbContext, companyContext, timeProvider);
                    try
                    {
                        _ = await ledger.StageOpeningCostAsync(submission.LocationId, submission.ProductId,
                            stockOnHandKg, submission.UnitCostPerKg, cancellationToken);
                    }
                    catch (InvalidOperationException exception)
                    {
                        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
                        throw OpeningCostError(409, InventoryOpeningCostCodesV1.LedgerAlreadyTracked, exception);
                    }
                    catch (ArgumentException exception)
                    {
                        // The stored cost or the measured stock breaks the core's rule: the stored state itself is not
                        // trustworthy, so nothing is opened.
                        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
                        throw OpeningCostError(503, InventoryOpeningCostCodesV1.Unavailable, exception);
                    }

                    await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
                    costedStockKg = stockOnHandKg;
                }

                var decision = new OpeningCostDecisionRow
                {
                    CompanyId = companyId,
                    OpeningCostId = submission.Id,
                    Decision = decisionCode,
                    Reason = reason,
                    CostedStockKg = costedStockKg,
                    PayloadHash = payloadHash,
                    DecidedAtUtc = timeProvider.GetUtcNow(),
                    DecidedBy = metadata.ActorId.Trim(),
                    CorrelationId = metadata.CorrelationId.Trim(),
                };
                dbContext.OpeningCostDecisions.Add(decision);
                var before = OpeningCostView(submission, decision: null);
                var view = OpeningCostView(submission, decision);
                dbContext.Commands.Add(OpeningCostCommandRow(DecideOpeningCostCommand, companyId, keyHash, payloadHash,
                    metadata, submission.Id, view));
                Audit(metadata, companyId, OpeningCostEntityType, submission.Id,
                    decisionCode == DecisionApproved
                        ? "inventory.opening-cost.approved.v1"
                        : "inventory.opening-cost.rejected.v1",
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

        if (await FindOpeningCostKeyReplayAsync(DecideOpeningCostCommand, keyHash, payloadHash, metadata.ActorId,
                freshAuthorityFence, cancellationToken) is { } winner)
        {
            return winner;
        }

        if (await FindOpeningCostDecisionReplayAsync(openingCostId, payloadHash, metadata.ActorId, freshAuthorityFence,
                cancellationToken) is { } decisionWinner)
        {
            return decisionWinner;
        }

        throw OpeningCostError(409, InventoryOpeningCostCodesV1.ConcurrentChange, concurrentChange);
    }

    /// <summary>
    /// The position may be opened: the cost ledger holds no state for it (known or unreadable), and the company holds
    /// its own stock of the product at the location (measured as <see cref="InventoryAverageCostLedgerRows.CostedStock"/>
    /// measures it, restricted to the location). Returns that stock on hand. A location the company does not have holds
    /// no stock, so it is refused the same way.
    /// </summary>
    private async Task<decimal> RequireOpeningCostPositionAsync(
        Guid companyId,
        Guid locationId,
        Guid productId,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken)
    {
        var tracked = await dbContext.AverageCostLedger.AsNoTracking()
            .AnyAsync(row => row.CompanyId == companyId && row.LocationId == locationId && row.ProductId == productId,
                cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (tracked)
        {
            throw OpeningCostError(409, InventoryOpeningCostCodesV1.LedgerAlreadyTracked);
        }

        var stockOnHandKg = await InventoryAverageCostLedgerRows.CostedStock(dbContext, companyId, productId)
            .Where(balance => balance.LocationId == locationId)
            .SumAsync(balance => (decimal?)balance.QuantityKg, cancellationToken) ?? 0m;
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (stockOnHandKg <= 0m)
        {
            throw OpeningCostError(409, InventoryOpeningCostCodesV1.NoCostedStock);
        }

        return stockOnHandKg;
    }

    private static (decimal UnitCost, string Reason) ParseOpeningCost(SubmitInventoryOpeningCostV1 request)
    {
        if (request.OpeningCostId == Guid.Empty || request.LocationId == Guid.Empty || request.ProductId == Guid.Empty
            || !TryOpeningCost(request.UnitCostPerKg, out var unitCost)
            || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length > OpeningCostMaximumReasonLength)
        {
            throw OpeningCostError(400, InventoryOpeningCostCodesV1.ValidationFailed);
        }

        return (unitCost, request.Reason.Trim());
    }

    /// <summary>
    /// Canonical decimal text: ASCII digits with at most one '.', positive (a missing cost is "yok", never zero), at most
    /// <see cref="AverageCostPrecision.InternalScale"/> decimals and within the stored precision.
    /// </summary>
    internal static bool TryOpeningCost(string? value, out decimal unitCost)
    {
        unitCost = 0m;
        if (string.IsNullOrEmpty(value) || value.Length > OpeningCostMaximumTextLength
            || value.Count(character => character == '.') > 1
            || value.Any(character => !char.IsAsciiDigit(character) && character != '.')
            || !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            || parsed <= 0m || parsed >= OpeningCostExclusiveUpperBound
            || !AverageCostPrecision.IsAtInternalScale(parsed))
        {
            return false;
        }

        unitCost = parsed;
        return true;
    }

    private static string OpeningCostText(decimal value) =>
        value.ToString(OpeningCostTextFormat, CultureInfo.InvariantCulture);

    private async Task<InventoryOpeningCostResultV1?> FindOpeningCostKeyReplayAsync(
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
            throw OpeningCostError(409, InventoryOpeningCostCodesV1.IdempotencyConflict);
        }

        var stored = JsonSerializer.Deserialize<OpeningCostCommandResult>(prior.ResultJson, Json)
            ?? throw OpeningCostError(503, InventoryOpeningCostCodesV1.Unavailable);
        return new(stored.OpeningCost, true, prior.CorrelationId);
    }

    /// <summary>The same submission id: the same content is the stored submission again; other content is a conflict.</summary>
    private async Task<InventoryOpeningCostResultV1?> FindOpeningCostIdentityReplayAsync(
        Guid openingCostId,
        string payloadHash,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken)
    {
        var stored = await dbContext.OpeningCosts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == openingCostId, cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (stored is null)
        {
            return null;
        }

        if (!string.Equals(stored.PayloadHash, payloadHash, StringComparison.Ordinal))
        {
            throw OpeningCostError(409, InventoryOpeningCostCodesV1.Conflict);
        }

        // The submission as it was submitted: a later decision is the decision's answer, not this one's.
        return new(OpeningCostView(stored, decision: null), true, stored.CorrelationId);
    }

    /// <summary>
    /// A submission already decided: the same decision and note from its decider is that decision again; anything else
    /// is refused.
    /// </summary>
    private async Task<InventoryOpeningCostResultV1?> FindOpeningCostDecisionReplayAsync(
        Guid openingCostId,
        string payloadHash,
        string actorId,
        Func<CancellationToken, Task> freshAuthorityFence,
        CancellationToken cancellationToken)
    {
        var decision = await dbContext.OpeningCostDecisions.AsNoTracking()
            .SingleOrDefaultAsync(row => row.OpeningCostId == openingCostId, cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (decision is null)
        {
            return null;
        }

        if (!string.Equals(decision.PayloadHash, payloadHash, StringComparison.Ordinal))
        {
            throw OpeningCostError(409, InventoryOpeningCostCodesV1.AlreadyDecided);
        }

        var submission = await dbContext.OpeningCosts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == openingCostId, cancellationToken);
        await RequireFreshAuthorityAsync(freshAuthorityFence, cancellationToken);
        if (submission is null)
        {
            throw OpeningCostError(503, InventoryOpeningCostCodesV1.Unavailable);
        }

        if (DecisionReplayRefusal(decision.DecidedBy, submission.RecordedBy, actorId,
                InventoryOpeningCostCodesV1.SelfApproval, InventoryOpeningCostCodesV1.AlreadyDecided) is { } refusal)
        {
            throw OpeningCostError(409, refusal);
        }

        return new(OpeningCostView(submission, decision), true, decision.CorrelationId);
    }

    private InventoryCommandRow OpeningCostCommandRow(
        string command,
        Guid companyId,
        string keyHash,
        string payloadHash,
        InventoryCommandMetadata metadata,
        Guid targetId,
        InventoryOpeningCostViewV1 view) => new()
        {
            CompanyId = companyId,
            CommandName = command,
            IdempotencyKeyHash = keyHash,
            ActorId = metadata.ActorId,
            PayloadHash = payloadHash,
            TargetId = targetId,
            ResultJson = JsonSerializer.Serialize(new OpeningCostCommandResult(view), Json),
            CorrelationId = metadata.CorrelationId.Trim(),
            RecordedAtUtc = timeProvider.GetUtcNow(),
        };

    private static InventoryOpeningCostViewV1 OpeningCostView(OpeningCostRow row, OpeningCostDecisionRow? decision)
    {
        var status = decision?.Decision switch
        {
            DecisionApproved => InventoryOpeningCostStatusesV1.Approved,
            DecisionRejected => InventoryOpeningCostStatusesV1.Rejected,
            null => InventoryOpeningCostStatusesV1.PendingApproval,
            _ => throw OpeningCostError(503, InventoryOpeningCostCodesV1.Unavailable),
        };
        return new InventoryOpeningCostViewV1(
            row.Id,
            row.CompanyId,
            row.LocationId,
            row.ProductId,
            OpeningCostText(row.UnitCostPerKg),
            row.Reason,
            row.RecordedAtUtc,
            status,
            decision is null
                ? null
                : new InventoryOpeningCostDecisionViewV1(
                    status,
                    decision.DecidedAtUtc,
                    decision.Reason,
                    decision.CostedStockKg is { } costed ? Kilograms.Of(costed).ToString() : null));
    }

    private static void RequireOpeningCostMetadata(InventoryCommandMetadata metadata)
    {
        if (!string.Equals(metadata.IdempotencyKey, metadata.IdempotencyKey.Trim(), StringComparison.Ordinal)
            || metadata.IdempotencyKey.Length > OpeningCostMaximumMetadataLength
            || metadata.IdempotencyKey.Any(char.IsControl)
            || !string.Equals(metadata.CorrelationId, metadata.CorrelationId.Trim(), StringComparison.Ordinal)
            || metadata.CorrelationId.Length > OpeningCostMaximumMetadataLength)
        {
            throw OpeningCostError(400, InventoryOpeningCostCodesV1.ValidationFailed);
        }
    }

    private static InventoryOpeningCostException OpeningCostError(int status, string code, Exception? inner = null) =>
        new(status, code, inner);

    private sealed record OpeningCostCommandResult(InventoryOpeningCostViewV1 OpeningCost);
}
