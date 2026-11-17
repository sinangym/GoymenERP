using System.Data;
using System.Text.Json;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Periods;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingStore
{
    private Task<AccountingPeriodRecord> CloseWithCarryAsync(Guid periodId, long expectedVersion, Guid actor,
        AccountingCommandMetadata metadata, CancellationToken cancellationToken) => PeriodTransactionAsync(async companyId =>
    {
        var row = await dbContext.Periods.SingleOrDefaultAsync(x => x.Id == periodId, cancellationToken)
            ?? throw Refused(AccountingStoreCodes.PeriodNotFound, "Bu şirketin böyle bir dönemi yok.");
        RequireVersion(row, expectedVersion);
        var period = RestorePeriod(row);
        var before = PeriodAudit(period);
        var now = timeProvider.GetUtcNow();
        period.Close(actor, now);
        // The immediately following recorded period is the target. A later open period cannot
        // silently bypass a proposed/closed next period. Creating or opening it is a separate command.
        var target = await dbContext.Periods.AsNoTracking().Where(x => x.StartsOn > row.EndsOn)
            .OrderBy(x => x.StartsOn).FirstOrDefaultAsync(cancellationToken);
        if (target is null || target.Status != (int)AccountingPeriodStatus.Open)
            throw Refused("accounting_period_carry_target_not_open", "Kapanış ve devir için sonraki dönem önceden açılmış olmalıdır.");
        var closeId = Guid.NewGuid();
        var closedVersion = checked(row.Version + 1);
        await PeriodFenceAsync(companyId, actor, metadata, cancellationToken);
        var receipt = await CarryOwner().RecordAsync(new(closeId, companyId, periodId, closedVersion, row.StartsOn,
            row.EndsOn, target.Id, target.StartsOn, target.EndsOn, actor, now), metadata, cancellationToken);
        await PeriodFenceAsync(companyId, actor, metadata, cancellationToken);
        if (receipt.ClosureId != closeId || receipt.CompanyId != companyId || receipt.SourcePeriodId != periodId
            || receipt.TargetPeriodId != target.Id || receipt.VoucherId == Guid.Empty || receipt.LineCount < 0
            || receipt.SourceHash is not { Length: 64 } hash || hash.Any(c => !Uri.IsHexDigit(c)))
            throw Refused("accounting_period_carry_source_unavailable", "Finans devir fişinin kaynak kanıtı doğrulanamadı.");
        dbContext.PeriodClosures.Add(new AccountingPeriodClosureRow { Id = closeId, CompanyId = companyId,
            PeriodId = periodId, ClosedPeriodVersion = closedVersion, TargetPeriodId = target.Id,
            CarryVoucherId = receipt.VoucherId, SourceHash = receipt.SourceHash, LineCount = receipt.LineCount,
            ClosedBy = actor, ClosedAtUtc = now, Version = 1 });
        row.Status = (int)AccountingPeriodStatus.Closed;
        row.ClosedByUserId = actor; row.ClosedAtUtc = now;
        row.Version = closedVersion; row.UpdatedAtUtc = now;
        Audit(metadata, companyId, PeriodEntityType, periodId.ToString("D"), "accounting.period.closed.v1", "Close",
            expectedVersion, closedVersion, before, JsonSerializer.Serialize(new { period = PeriodAudit(period), carry = receipt }, Json),
            "Muhasebe dönemi ve gerçek Finans devir fişi birlikte kaydedildi.");
        var result = new AccountingPeriodRecord(period, closedVersion);
        Complete(metadata, companyId, result);
        await PeriodFenceAsync(companyId, actor, metadata, cancellationToken);
        await SaveAsync(cancellationToken);
        return result;
    }, actor, metadata, cancellationToken);

    public async Task<AccountingPeriodLifecycleViewV1?> ReadLifecycleAsync(Guid periodId, CancellationToken cancellationToken)
    {
        var companyId = RequireResolvedCompany();
        var row = await dbContext.Periods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == periodId, cancellationToken);
        if (row is null) return null;
        var closes = await dbContext.PeriodClosures.AsNoTracking().Where(x => x.PeriodId == periodId)
            .OrderBy(x => x.ClosedPeriodVersion).ToListAsync(cancellationToken);
        var reopenings = await dbContext.PeriodReopenings.AsNoTracking().Where(x => x.PeriodId == periodId)
            .OrderBy(x => x.ClosedPeriodVersion).ToListAsync(cancellationToken);
        if (RequireResolvedCompany() != companyId) throw Refused(AccountingStoreCodes.CompanyContextMismatch, "Şirket değişti.");
        return new(companyId, periodId, row.Version, closes.Select(ClosureView).ToArray(), reopenings.Select(ReopeningView).ToArray());
    }

    public Task<AccountingPeriodReopeningViewV1> ProposeReopeningAsync(Guid periodId,
        ProposeAccountingPeriodReopeningV1 request, Guid actor, AccountingCommandMetadata metadata,
        CancellationToken cancellationToken) => PeriodTransactionAsync(async companyId =>
    {
        var periodRow = await dbContext.Periods.SingleOrDefaultAsync(x => x.Id == periodId, cancellationToken)
            ?? throw Refused(AccountingStoreCodes.PeriodNotFound, "Bu şirketin böyle bir dönemi yok.");
        RequireVersion(periodRow, request.ExpectedVersion);
        var close = await dbContext.PeriodClosures.SingleOrDefaultAsync(x => x.PeriodId == periodId
            && x.ClosedPeriodVersion == periodRow.Version && x.ReopeningId == null, cancellationToken)
            ?? throw Refused("accounting_period_closure_proof_required", "Geri alınacak kapanış ve devir fişi kanıtı bulunamadı.");
        if (await dbContext.PeriodReopenings.AnyAsync(x => x.ClosureId == close.Id || x.Id == request.ProposalId, cancellationToken))
            throw Refused("accounting_period_reopening_exists", "Bu kapanış için yeniden açma önerisi zaten var.");
        var proposal = AccountingPeriodReopening.Propose(request.ProposalId, RestorePeriod(periodRow), close.Id,
            close.ClosedPeriodVersion, actor, timeProvider.GetUtcNow(), request.Reason);
        var row = new AccountingPeriodReopeningRow { Id = proposal.Id, CompanyId = companyId, PeriodId = periodId,
            ClosureId = close.Id, ClosedPeriodVersion = close.ClosedPeriodVersion, ProposedBy = actor,
            ProposedAtUtc = proposal.ProposedAtUtc, Reason = proposal.Reason, Version = 1 };
        dbContext.PeriodReopenings.Add(row);
        var result = ReopeningView(row);
        Audit(metadata, companyId, "Accounting.PeriodReopening", row.Id.ToString("D"), "accounting.period.reopening-proposed.v1",
            "ProposeReopening", null, 1, "{}", JsonSerializer.Serialize(result, Json), "Kapanışı geri alma farklı yetkili onayına önerildi.");
        Complete(metadata, companyId, result);
        await PeriodFenceAsync(companyId, actor, metadata, cancellationToken);
        await SaveAsync(cancellationToken);
        return result;
    }, actor, metadata, cancellationToken);

    public Task<AccountingPeriodReopeningViewV1> ApproveReopeningAsync(Guid periodId, Guid proposalId,
        ApproveAccountingPeriodReopeningV1 request, Guid actor, AccountingCommandMetadata metadata,
        CancellationToken cancellationToken) => PeriodTransactionAsync(async companyId =>
    {
        var periodRow = await dbContext.Periods.SingleOrDefaultAsync(x => x.Id == periodId, cancellationToken)
            ?? throw Refused(AccountingStoreCodes.PeriodNotFound, "Bu şirketin böyle bir dönemi yok.");
        RequireVersion(periodRow, request.ExpectedPeriodVersion);
        var close = await dbContext.PeriodClosures.SingleOrDefaultAsync(x => x.PeriodId == periodId
            && x.ClosedPeriodVersion == periodRow.Version && x.ReopeningId == null, cancellationToken)
            ?? throw Refused("accounting_period_closure_proof_required", "Geri alınacak kapanış ve devir fişi kanıtı bulunamadı.");
        var row = await dbContext.PeriodReopenings.SingleOrDefaultAsync(x => x.Id == proposalId && x.PeriodId == periodId
            && x.ClosureId == close.Id, cancellationToken)
            ?? throw Refused(AccountingStoreCodes.PeriodNotFound, "Bu şirketin böyle bir önerisi yok.");
        if (row.Version != request.ExpectedVersion) throw new AccountingVersionConflictException(request.ExpectedVersion, row.Version);
        var period = RestorePeriod(periodRow);
        var proposal = AccountingPeriodReopening.Restore(row.Id, period, close.Id, row.ClosedPeriodVersion,
            row.ProposedBy, row.ProposedAtUtc, row.Reason, row.ApprovedBy, row.ApprovedAtUtc);
        var now = timeProvider.GetUtcNow();
        var approved = proposal.Approve(actor, now);
        // A closed later period may have carried this balance onward. The human answer about
        // reopening that dependent chain is still pending; never reverse just one link by guess.
        if (await (from laterClose in dbContext.PeriodClosures
                   join laterPeriod in dbContext.Periods on laterClose.PeriodId equals laterPeriod.Id
                   where laterClose.ReopeningId == null && laterPeriod.EndsOn > periodRow.EndsOn
                   select laterClose.Id).AnyAsync(cancellationToken))
            throw Refused("accounting_period_carry_source_unavailable", "Sonraki kapalı devir zincirinin geri alma kuralı henüz kesinleşmedi.");
        await PeriodFenceAsync(companyId, actor, metadata, cancellationToken);
        await CarryOwner().ReverseAsync(new(row.Id, companyId, close.Id, close.CarryVoucherId, actor, now, row.Reason),
            metadata, cancellationToken);
        await PeriodFenceAsync(companyId, actor, metadata, cancellationToken);
        var beforePeriod = PeriodAudit(period);
        period.Reopen(approved);
        close.ReopeningId = row.Id; close.ReopenedBy = actor; close.ReopenedAtUtc = now; close.Version = 2;
        row.ApprovedBy = actor; row.ApprovedAtUtc = now; row.Version = approved.Version;
        periodRow.Status = (int)AccountingPeriodStatus.Open;
        periodRow.ClosedByUserId = null; periodRow.ClosedAtUtc = null;
        periodRow.Version = checked(periodRow.Version + 1); periodRow.UpdatedAtUtc = now;
        var result = ReopeningView(row);
        Audit(metadata, companyId, "Accounting.PeriodReopening", row.Id.ToString("D"), "accounting.period.reopened.v1",
            "ApproveReopening", request.ExpectedPeriodVersion, periodRow.Version, beforePeriod,
            JsonSerializer.Serialize(new { period = PeriodAudit(period), reopening = result, close.CarryVoucherId }, Json),
            "Farklı yetkili onayıyla dönem yeniden açıldı ve kapanış devir fişi terslendi.");
        Complete(metadata, companyId, result);
        await PeriodFenceAsync(companyId, actor, metadata, cancellationToken);
        await SaveAsync(cancellationToken);
        return result;
    }, actor, metadata, cancellationToken);

    private IAccountingPeriodCarryForwardPortV1 CarryOwner() => periodCarry
        ?? throw Refused("accounting_period_carry_source_unavailable", "Gerçek Finans devir sahibi kullanılabilir değil.");

    private async Task PeriodFenceAsync(Guid companyId, Guid actor, AccountingCommandMetadata metadata, CancellationToken token)
    {
        if (RequireResolvedCompany() != companyId || !Guid.TryParse(metadata.ActorId, out var metadataActor) || actor != metadataActor)
            throw Refused(AccountingStoreCodes.CompanyContextMismatch, "Dönem işleminin şirket veya aktörü değişti.");
        if (metadata.FreshAuthorityFence is { } fence) await fence(token);
        if (RequireResolvedCompany() != companyId) throw Refused(AccountingStoreCodes.CompanyContextMismatch, "Şirket değişti.");
    }

    private async Task<T> PeriodTransactionAsync<T>(Func<Guid, Task<T>> operation, Guid actor,
        AccountingCommandMetadata metadata, CancellationToken cancellationToken)
    {
        RequireMetadata(metadata);
        var companyId = RequireResolvedCompany();
        await PeriodFenceAsync(companyId, actor, metadata, cancellationToken);
        if (!dbContext.Database.IsRelational() || dbContext.Database.CurrentTransaction is not null || dbContext.ChangeTracker.HasChanges())
            throw Refused("accounting_period_carry_source_unavailable", "Dönem işlemi temiz, gerçek bir ilişkisel işlem gerektirir.");
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var result = await operation(companyId);
            await PeriodFenceAsync(companyId, actor, metadata, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception exception) when (exception is SqlException { Number: 1205 }
            || exception is DbUpdateException { InnerException: SqlException { Number: 1205 } })
        {
            dbContext.ChangeTracker.Clear();
            throw Refused(AccountingStoreCodes.ConcurrentChange, "Dönem veya devir kaynağı eşzamanlı değişti.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private static AccountingPeriodClosureViewV1 ClosureView(AccountingPeriodClosureRow row) => new(row.Id, row.CompanyId,
        row.PeriodId, row.ClosedPeriodVersion, row.TargetPeriodId, row.CarryVoucherId, row.SourceHash, row.LineCount,
        row.ClosedBy, row.ClosedAtUtc, row.ReopeningId, row.ReopenedBy, row.ReopenedAtUtc);
    private static AccountingPeriodReopeningViewV1 ReopeningView(AccountingPeriodReopeningRow row) => new(row.Id, row.CompanyId,
        row.PeriodId, row.ClosureId, row.ClosedPeriodVersion, row.ProposedBy, row.ProposedAtUtc, row.Reason,
        row.ApprovedBy, row.ApprovedAtUtc, row.Version);
}
