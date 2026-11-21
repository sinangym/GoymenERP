using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Modules.Inventory.Infrastructure;

/// <summary>Fresh independent owner transaction, only AFTER the physical owner confirms rollback.</summary>
internal sealed class InventoryNegativeStockAttempts(ICompanyContext company, ICurrentUser user,
    IErpAuthorityRevalidationService authority, IServiceScopeFactory scopes, InventoryDbContext reads)
    : IInventoryNegativeStockAttemptRecorderV1, IInventoryNegativeStockAttemptsV1
{
    public async Task RecordAfterRollbackAsync(Exception refusal, CancellationToken cancellationToken = default)
    {
        var proof = InventoryNegativeStockAttemptEvidenceException.FindEvidence(refusal);
        if (proof is null) return; // An allocation shortage, version fault or arbitrary 409 supplies no physical proof.
        Validate(proof);
        var identity = await RequireAsync(null, [proof.Metadata.PermissionCode], cancellationToken);
        if (identity.CompanyId != proof.Shortfall.Position.CompanyId || identity.ActorId != proof.Metadata.ActorId)
            throw Error(403, InventoryNegativeStockAttemptCodesV1.Forbidden);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(identity.CompanyId);
            var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditEventCollector>();
            var auditWriter = scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            if (database.ChangeTracker.Entries().Any() || database.Database.CurrentTransaction is not null
                || auditWriter.HasPendingDrafts) throw new InvalidOperationException("A clean refusal scope is required.");
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable,
                cancellationToken);
            try
            {
                var position = proof.Shortfall.Position;
                // Physical validation and count approval take this guard BEFORE the boundary/attempt/physical rows.
                // The proof's count version was captured under that same lock before the refused stock read.
                await InventoryNegativeStockValidationGuard.AcquireAsync(database, identity.CompanyId, cancellationToken);
                var boundary = await database.NegativeStockCountBoundaries.AsNoTracking()
                    .Where(row => row.CompanyId == identity.CompanyId && row.ProductId == position.ProductId
                        && row.LocationId == position.LocationId)
                    .OrderByDescending(row => row.Version).FirstOrDefaultAsync(cancellationToken);
                if ((boundary?.Version ?? 0L) < proof.ObservedCountBoundaryVersion!.Value)
                    throw new InvalidOperationException("A recorded pre-validation count boundary cannot disappear.");
                // This closes the rollback-to-insert gap with a REAL later approved count, never a timestamp or balance.
                var laterCount = await database.NegativeStockCountBoundaries.AsNoTracking()
                    .Where(row => row.CompanyId == identity.CompanyId && row.ProductId == position.ProductId
                        && row.LocationId == position.LocationId && row.Version > proof.ObservedCountBoundaryVersion.Value)
                    .OrderBy(row => row.Version).FirstOrDefaultAsync(cancellationToken);
                if (laterCount is not null)
                {
                    var actual = await (from count in database.StockCounts.AsNoTracking()
                                        join decision in database.StockCountDecisions.AsNoTracking()
                                            on new { count.CompanyId, CountId = count.Id }
                                            equals new { decision.CompanyId, decision.CountId }
                                        where count.CompanyId == identity.CompanyId && count.Id == laterCount.StockCountId
                                        select new { Count = count, Decision = decision }).SingleOrDefaultAsync(cancellationToken);
                    if (actual is null || actual.Count.ProductId != position.ProductId || actual.Count.LocationId != position.LocationId
                        || actual.Decision.Decision != 1 || actual.Count.CountedKg == actual.Count.BookKg
                        || actual.Decision.DecidedAtUtc != laterCount.DecidedAtUtc || actual.Decision.DecidedBy != laterCount.DecidedBy)
                        throw new InvalidOperationException("The later boundary must name its actual approved matching count.");
                }
                // No FK/read of a physical row holds a rolled-back label lock.
                _ = await database.NegativeStockAttempts.Where(row => row.CompanyId == identity.CompanyId
                    && row.ProductId == position.ProductId && row.LocationId == position.LocationId)
                    .LongCountAsync(cancellationToken);
                var keyHash = Hash(proof.Metadata.IdempotencyKey);
                var prior = await database.NegativeStockAttempts.SingleOrDefaultAsync(row => row.CompanyId == identity.CompanyId
                    && row.MovementId == proof.MovementId && row.ActorId == identity.ActorId && row.KeyHash == keyHash,
                    cancellationToken);
                await RequireAsync(identity, [proof.Metadata.PermissionCode], cancellationToken);
                if (prior is not null)
                {
                    if (prior.PayloadHash != proof.MovementPayloadHash || prior.PermissionCode != proof.Metadata.PermissionCode
                        || prior.SourceChannel != proof.Metadata.SourceChannel)
                        throw new InvalidOperationException("The refusal identity is already bound to different facts.");
                    await transaction.CommitAsync(cancellationToken);
                    return;
                }
                var shortfall = proof.Shortfall;
                var row = new NegativeStockAttemptRow
                {
                    Id = Guid.NewGuid(), CompanyId = identity.CompanyId, MovementId = proof.MovementId,
                    ActorId = identity.ActorId, KeyHash = keyHash, PayloadHash = proof.MovementPayloadHash,
                    PermissionCode = proof.Metadata.PermissionCode, CorrelationId = proof.Metadata.CorrelationId,
                    SourceChannel = proof.Metadata.SourceChannel, RefusedAtUtc = proof.RefusedAtUtc,
                    ProductId = position.ProductId, LocationId = position.LocationId, LabelId = position.LabelId,
                    LabelCode = proof.LabelCode, MovementKind = (int)shortfall.MovementKind,
                    LineNumber = shortfall.LineNumber!.Value, SourceKind = shortfall.Source.Kind,
                    SourceId = shortfall.Source.Id, SourceLineId = shortfall.Source.LineId,
                    RequestedKg = shortfall.Requested.Value, AvailableKg = shortfall.Available.Value,
                    MissingKg = checked(shortfall.Requested.Value - shortfall.Available.Value),
                    ObservedCountBoundaryVersion = proof.ObservedCountBoundaryVersion,
                };
                database.NegativeStockAttempts.Add(row);
                if (laterCount is not null)
                    database.NegativeStockAttemptResolutions.Add(new NegativeStockAttemptResolutionRow()
                    {
                        CompanyId = identity.CompanyId, AttemptId = row.Id, StockCountId = laterCount.StockCountId,
                        CountBoundaryVersion = laterCount.Version, DecidedAtUtc = laterCount.DecidedAtUtc,
                        DecidedBy = laterCount.DecidedBy,
                    });
                var auditDraft = new AuditEventDraft(identity.CompanyId, "inventory.negative-stock-attempt.recorded.v1",
                    "Inventory.NegativeStockAttempt", row.Id.ToString("D"), "Record", null, 1,
                    AuditActorTypes.User, identity.ActorId, proof.Metadata.PermissionCode, proof.Metadata.RoleCodes,
                    "{}", JsonSerializer.Serialize(new { row.MovementId, row.ProductId, row.LocationId, row.LabelId,
                        row.RequestedKg, row.AvailableKg, row.MissingKg, row.RefusedAtUtc, row.ObservedCountBoundaryVersion,
                        ResolvingStockCountId = laterCount?.StockCountId, ResolvingCountBoundaryVersion = laterCount?.Version }),
                    "Fiziksel stoğu eksiye düşürecek işlem durduruldu; deneme kaydedildi.", null, AuditAiInvolvement.None,
                    null, proof.Metadata.SourceChannel, proof.Metadata.ClientIpAddress, proof.Metadata.UserAgent,
                    proof.Metadata.CorrelationId, proof.Metadata.CausationId, proof.Metadata.IdempotencyKey);
                // Inventory's legacy context interceptor sees an EMPTY collector. Only the Audit owner's
                // explicit borrowed-transaction writer may append this new surface's audit event.
                await database.SaveChangesAsync(cancellationToken);
                audit.Enqueue(auditDraft);
                await auditWriter.WritePendingAsync(transaction.GetDbTransaction(), cancellationToken);
                await RequireAsync(identity, [proof.Metadata.PermissionCode], cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                auditWriter.DiscardPending();
                // A rollback failure is not a successful recording. The caller reports unavailable.
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (InventoryNegativeStockAttemptException) { throw; }
        catch (Exception exception)
        {
            await RequireAsync(identity, [proof.Metadata.PermissionCode], cancellationToken);
            throw Error(503, InventoryNegativeStockAttemptCodesV1.Unavailable, exception);
        }
    }

    public async Task<InventoryNegativeStockAttemptPageV1> SearchAsync(InventoryNegativeStockAttemptQueryV1 query,
        CancellationToken cancellationToken = default)
    {
        var permissions = new[] { ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead };
        var identity = await RequireAsync(null, permissions, cancellationToken);
        if (query is null || query.Limit is < 1 or > 100 || query.ProductId == Guid.Empty || query.LocationId == Guid.Empty)
            throw Error(400, InventoryNegativeStockAttemptCodesV1.InvalidQuery);
        try
        {
            var selection = reads.NegativeStockAttempts.AsNoTracking().Where(row => row.CompanyId == identity.CompanyId);
            if (query.ProductId is { } product) selection = selection.Where(row => row.ProductId == product);
            if (query.LocationId is { } location) selection = selection.Where(row => row.LocationId == location);
            // One joined statement owns both open/closed membership and its displayed immutable resolution.
            // A count committing between separate SELECTs must not turn an open-only page into a closed row.
            var joined = from attempt in selection
                         join resolution in reads.NegativeStockAttemptResolutions.AsNoTracking()
                             on new { attempt.CompanyId, AttemptId = attempt.Id }
                             equals new { resolution.CompanyId, resolution.AttemptId } into matches
                         from resolution in matches.DefaultIfEmpty()
                         where !query.OpenOnly || resolution == null
                         orderby attempt.RefusedAtUtc descending, attempt.Id descending
                         select new { Attempt = attempt, Resolution = resolution };
            var rows = await joined.Take(query.Limit + 1).ToArrayAsync(cancellationToken);
            await RequireAsync(identity, permissions, cancellationToken);
            return new InventoryNegativeStockAttemptPageV1(identity.CompanyId, rows.Take(query.Limit).Select(item =>
            {
                var row = item.Attempt;
                return new InventoryNegativeStockAttemptViewV1(
                row.Id, row.CompanyId, row.MovementId, InventoryNegativeStockAttemptApiV1.CommandName, row.ActorId,
                row.RefusedAtUtc, row.ProductId, row.LocationId, row.LabelId, row.LabelCode,
                Enum.IsDefined((StockMovementKind)row.MovementKind) ? ((StockMovementKind)row.MovementKind).ToString()
                    : throw Error(503, InventoryNegativeStockAttemptCodesV1.Unavailable),
                row.SourceKind, row.SourceId, row.SourceLineId, Kg(row.RequestedKg), Kg(row.AvailableKg), Kg(row.MissingKg),
                item.Resolution is { } resolution
                    ? new InventoryNegativeStockAttemptResolutionV1(resolution.StockCountId, resolution.DecidedAtUtc, resolution.DecidedBy) : null);
            }).ToArray(),
                rows.Length > query.Limit);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            await RequireAsync(identity, permissions, cancellationToken);
            throw exception as InventoryNegativeStockAttemptException
                ?? Error(503, InventoryNegativeStockAttemptCodesV1.Unavailable, exception);
        }
    }

    private async Task<Scope> RequireAsync(Scope? expected, IReadOnlyCollection<string> permissions, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId))
            throw Error(expected is null ? 401 : 403, expected is null
                ? InventoryNegativeStockAttemptCodesV1.Unauthenticated : InventoryNegativeStockAttemptCodesV1.Forbidden);
        var current = new Scope(company.IsResolved ? company.CompanyId : Guid.Empty, user.UserId);
        if (expected is not null && current != expected) throw Error(403, InventoryNegativeStockAttemptCodesV1.Forbidden);
        Check(current);
        foreach (var permission in permissions)
        {
            PermissionScope grant;
            try { grant = await authority.RevalidatePermissionScopeAsync(permission, current.CompanyId, token); }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) { Check(current); throw Error(503, InventoryNegativeStockAttemptCodesV1.Unavailable, exception); }
            token.ThrowIfCancellationRequested();
            Check(current);
            if (!grant.IsGranted || !grant.IsCompanyWide) throw Error(403, InventoryNegativeStockAttemptCodesV1.Forbidden);
        }
        return current;
    }
    private void Check(Scope current)
    {
        if (!company.IsResolved || current.CompanyId == Guid.Empty || company.CompanyId != current.CompanyId
            || !user.IsAuthenticated || user.UserId != current.ActorId || !user.CanAccessCompany(current.CompanyId))
            throw Error(403, InventoryNegativeStockAttemptCodesV1.Forbidden);
    }
    private static void Validate(InventoryNegativeStockAttemptEvidenceV1 proof)
    {
        static bool Text(string? value, int max = 128) => !string.IsNullOrWhiteSpace(value) && value.Length <= max
            && value == value.Trim() && !value.Any(char.IsControl);
        var mass = proof.Shortfall;
        var metadata = proof.Metadata;
        if (proof.MovementId == Guid.Empty || mass is null || metadata is null || mass.LineNumber is null
            || proof.ObservedCountBoundaryVersion is null or < 0
            || proof.RefusedAtUtc.Offset != TimeSpan.Zero || !Text(proof.LabelCode)
            || proof.MovementPayloadHash.Length != 64 || !proof.MovementPayloadHash.All(char.IsAsciiHexDigit)
            || !Text(metadata.ActorId) || !Text(metadata.PermissionCode) || !Text(metadata.CorrelationId)
            || !Text(metadata.IdempotencyKey) || !Text(metadata.SourceChannel) || metadata.RoleCodes is null
            || metadata.RoleCodes.Any(role => !Text(role))
            || mass.Requested.Value > 999999999999999.999m || mass.Available.Value < -999999999999999.999m
            || mass.Requested.Value - mass.Available.Value > 999999999999999.999m)
            throw Error(503, InventoryNegativeStockAttemptCodesV1.Unavailable);
    }
    private static string Kg(decimal amount) => amount.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static InventoryNegativeStockAttemptException Error(int status, string code, Exception? inner = null) => new InventoryNegativeStockAttemptException(status, code, inner);
    private sealed record Scope(Guid CompanyId, string ActorId);
}
