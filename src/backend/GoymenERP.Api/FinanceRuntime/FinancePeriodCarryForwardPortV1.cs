using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Api.FinanceRuntime;

/// <summary>FNS-D42 host bridge: each owner writes only its own schema in one borrowed transaction.</summary>
public sealed class FinancePeriodCarryForwardPortV1(AccountingDbContext accounting, ICompanyContext company,
    ICurrentUser user, IErpAuthorityRevalidationService authority, IFinancePermissionPort financePermissions,
    IAuditEventCollector audit, IAuditTransactionWriterV1 auditOwner) : IAccountingPeriodCarryForwardPortV1
{
    public async Task<AccountingPeriodCarryReceiptV1> RecordAsync(AccountingPeriodCarryRequestV1 request,
        AccountingCommandMetadata metadata, CancellationToken cancellationToken)
    {
        await using var session = await EnlistAsync(request.CompanyId, request.ActorId,
            AccountingPermissionManifestV1.PeriodsClose, metadata, cancellationToken);
        try
        {
            var result = await session.RecordAsync(Context(request.CompanyId, metadata), new FinancePeriodCarrySourceV1(request.ClosureId, request.CompanyId,
                request.SourcePeriodId, request.ClosedPeriodVersion, request.SourceStartsOn, request.SourceEndsOn, request.TargetPeriodId,
                request.TargetStartsOn, request.TargetEndsOn, request.ActorId, request.RecordedAtUtc), new CommandMetadata(metadata.IdempotencyKey),
                Envelope(metadata), cancellationToken);
            return new AccountingPeriodCarryReceiptV1(request.ClosureId, result.CompanyId, result.Source.SourcePeriodId, result.Source.TargetPeriodId,
                result.Id, result.SourceHash, result.Lines.Count);
        }
        catch (InvalidOperationException exception)
        { throw new AccountingStoreException("accounting_period_carry_source_unavailable", "Finans devir kaynağı kullanılabilir değil.", exception); }
        catch (UnauthorizedAccessException exception)
        { throw new AccountingStoreException(AccountingStoreCodes.CompanyContextMismatch, "Finans devir bağlamı değişti.", exception); }
    }

    public async Task ReverseAsync(AccountingPeriodCarryReversalV1 request, AccountingCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        await using var session = await EnlistAsync(request.CompanyId, request.ActorId,
            AccountingPermissionManifestV1.PeriodsApprove, metadata, cancellationToken);
        try
        {
            await session.ReverseAsync(Context(request.CompanyId, metadata), new FinancePeriodCarryReversalV1(request.ReopeningId, request.CompanyId,
                request.ClosureId, request.VoucherId, request.ActorId, request.RecordedAtUtc, request.Reason),
                new CommandMetadata(metadata.IdempotencyKey), Envelope(metadata), cancellationToken);
        }
        catch (InvalidOperationException exception)
        { throw new AccountingStoreException("accounting_period_carry_source_unavailable", "Finans ters devir kaynağı kullanılabilir değil.", exception); }
        catch (UnauthorizedAccessException exception)
        { throw new AccountingStoreException(AccountingStoreCodes.CompanyContextMismatch, "Finans ters devir bağlamı değişti.", exception); }
    }

    private async Task<FinancePeriodCarryForwardSession> EnlistAsync(Guid companyId, Guid actor, string permission,
        AccountingCommandMetadata metadata, CancellationToken cancellationToken)
    {
        async Task Fence(CancellationToken token)
        {
            bool Current() => company.IsResolved && company.CompanyId == companyId && user.IsAuthenticated
                && Guid.TryParse(user.UserId, out var current) && current == actor && user.UserId == metadata.ActorId
                && user.CanAccessCompany(companyId) && metadata.PermissionCode == permission;
            if (!Current()) throw new AccountingStoreException(AccountingStoreCodes.CompanyContextMismatch, "Şirket veya aktör değişti.");
            var scope = await authority.RevalidatePermissionScopeAsync(permission, companyId, token);
            if (!Current() || !scope.IsGranted || !scope.IsCompanyWide)
                throw new AccountingStoreException(AccountingStoreCodes.CompanyContextMismatch, "Dönem işlem yetkisi değişti.");
            if (metadata.FreshAuthorityFence is { } callerFence) await callerFence(token);
        }
        await Fence(cancellationToken);
        if (!accounting.Database.IsRelational() || accounting.Database.CurrentTransaction is not { } transaction)
            throw new AccountingStoreException("accounting_period_carry_source_unavailable", "Dönem ve devir aynı ilişkisel işlemi gerektirir.");
        try
        {
            return await FinancePeriodCarryForwardSession.EnlistAsync(accounting.Database.GetDbConnection(), transaction.GetDbTransaction(),
                company, user, financePermissions, Fence, audit, auditOwner, cancellationToken);
        }
        catch (UnauthorizedAccessException exception)
        { throw new AccountingStoreException(AccountingStoreCodes.CompanyContextMismatch, "Finans devir bağlamı değişti.", exception); }
        catch (InvalidOperationException exception)
        { throw new AccountingStoreException("accounting_period_carry_source_unavailable", "Finans devir işlemi kurulamadı.", exception); }
    }
    private static FinanceContext Context(Guid companyId, AccountingCommandMetadata metadata) =>
        new FinanceContext(companyId, metadata.ActorId, metadata.CorrelationId, metadata.CausationId);
    private static FinanceAuditEnvelope Envelope(AccountingCommandMetadata metadata) =>
        new FinanceAuditEnvelope(metadata.PermissionCode, metadata.RoleCodes, metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent);
}
