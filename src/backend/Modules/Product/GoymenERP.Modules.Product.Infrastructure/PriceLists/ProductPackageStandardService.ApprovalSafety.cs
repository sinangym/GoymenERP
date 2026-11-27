using System.Data;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

internal sealed partial class ProductPackageStandardService
{
    private OwnedDecision? ownedDecision;

    // Runtime DI exposes only the guarded facade. Direct core construction remains a synthetic-test seam.
    internal async Task<ProductPackageStandardResultV2> RunOwnedDecisionAsync(string command,
        ProductPackageStandardCommandMetadataV2 metadata, IAuditTransactionWriterV1? writer,
        Func<CancellationToken, Task<ProductPackageStandardResultV2>> execute, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        token.ThrowIfCancellationRequested();
        try { return await RunAsync(replayOnly: false); }
        catch (DecisionSaveRace) { return await RunAsync(replayOnly: true); }

        async Task<ProductPackageStandardResultV2> RunAsync(bool replayOnly)
        {
            // A rejected entry owns nothing: preserve caller transactions, dirty entries and pending drafts.
            if (writer is null || ownedDecision is not null || !database.Database.IsRelational()
                || System.Transactions.Transaction.Current is not null || database.Database.CurrentTransaction is not null
                || database.ChangeTracker.HasChanges() || writer.HasPendingDrafts)
                return Unstored(Problem(503, ProductPackageStandardProblemCodesV2.ProviderUnavailable,
                    "Paket standardı kararı kullanılamıyor.", "Bağımsız ve temiz işlem sınırı doğrulanamadı."), metadata);
            RequireMetadata(metadata);
            if (command is not (ApproveCommandName or RejectCommandName)) throw new InvalidOperationException("Unknown package decision.");
            var state = new OwnedDecision(command, metadata.CompanyId, metadata.ActorId, replayOnly, writer);
            IDbContextTransaction? transaction = null;
            var committed = false;
            try
            {
                ownedDecision = state;
                transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                await RequireOwnedDecisionFenceAsync(state, token);
                var answer = await execute(token);
                if (answer.Answer.StatusCode == 204)
                    answer = answer with { DecisionReceipt = await ReadDecisionReceiptAsync(command, metadata, token) };
                await RequireOwnedDecisionFenceAsync(state, token);
                if (answer.Answer.StatusCode == 204 && !answer.IsReplay && !state.Saved)
                    throw new InvalidOperationException("Package decision success has no saved native receipt.");
                if (writer.HasPendingDrafts) throw new InvalidOperationException("Package decision Audit remains pending.");
                if (database.ChangeTracker.HasChanges()) throw new InvalidOperationException("Package decision has unflushed owner changes.");
                await RequireOwnedDecisionFenceAsync(state, token);
                await transaction.CommitAsync(token); committed = true;
                var completed = transaction; transaction = null;
                try { await completed.DisposeAsync(); }
                catch (Exception error) { throw new InvalidOperationException("Package decision transaction disposal failed.", error); }
                await RequireOwnedDecisionFenceAsync(state, token);
                return answer;
            }
            catch (Exception failure)
            {
                var faults = new List<Exception>();
                if (transaction is not null)
                {
                    if (!committed) try { await transaction.RollbackAsync(CancellationToken.None); } catch (Exception error) { faults.Add(error); }
                    try { await transaction.DisposeAsync(); } catch (Exception error) { faults.Add(error); }
                }
                try { database.ChangeTracker.Clear(); } catch (Exception error) { faults.Add(error); }
                try { writer.DiscardPending(); } catch (Exception error) { faults.Add(error); }
                if (faults.Count > 0) throw new AggregateException("Package decision cleanup failed; replay is unsafe.", [failure, .. faults]);
                if (!replayOnly && !committed && state.SaveAttempted && !state.Saved && failure is DbUpdateException)
                    throw new DecisionSaveRace();
                throw;
            }
            finally { ownedDecision = null; }
        }
    }

    private async Task RequireOwnedDecisionFenceAsync(OwnedDecision state, CancellationToken token)
    {
        RequireContext();
        var scope = await authority.RevalidatePermissionScopeAsync(ApprovePermission, state.CompanyId, token);
        RequireContext();
        if (!scope.IsGranted || !scope.IsCompanyWide) throw new UnauthorizedAccessException();
        void RequireContext()
        {
            token.ThrowIfCancellationRequested();
            if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId) || currentUser.UserId != state.ActorId
                || !companyContext.IsResolved || companyContext.CompanyId != state.CompanyId || !currentUser.CanAccessCompany(state.CompanyId))
                throw new UnauthorizedAccessException();
        }
    }

    private async Task<ProductPackageStandardResultV2> SaveOwnedDecisionAsync(string command, ProductPackageStandardAnswerV2 answer,
        string key, string hash, ProductPackageStandardCommandMetadataV2 metadata, CancellationToken token)
    {
        var state = ownedDecision ?? throw new InvalidOperationException("Package decision transaction is absent.");
        if (state.ReplayOnly || command != state.Command || metadata.CompanyId != state.CompanyId || metadata.ActorId != state.ActorId)
            throw new InvalidOperationException("Package decision cannot write another command or a replay-only result.");
        await RequireOwnedDecisionFenceAsync(state, token);
        AddAttempt(command, answer, key, hash, metadata);
        if (state.Writer.HasPendingDrafts)
        {
            await state.Writer.WritePendingAsync(database.Database.CurrentTransaction!.GetDbTransaction(), token);
            await RequireOwnedDecisionFenceAsync(state, token);
        }
        if (state.Writer.HasPendingDrafts) throw new InvalidOperationException("Package decision Audit remains pending.");
        state.SaveAttempted = true;
        await database.SaveChangesAsync(token); state.Saved = true;
        await RequireOwnedDecisionFenceAsync(state, token);
        return new(answer, false, metadata.CorrelationId);
    }

    private sealed record OwnedDecision(string Command, Guid CompanyId, string ActorId, bool ReplayOnly, IAuditTransactionWriterV1 Writer)
    { public bool SaveAttempted; public bool Saved; }
    private sealed class DecisionSaveRace : Exception;
}

/// <summary>One runtime facade for every native package alias. Only decisions gain ownership; preparation and reads delegate unchanged.</summary>
internal sealed class GuardedProductPackageStandardService(ProductPackageStandardService core, IAuditTransactionWriterV1? writer)
    : IProductPackageStandardServiceV2, IProductPackageStandardReaderV2, IProductPackageStandardServiceV3, IProductPackageStandardReaderV3
{
    public Task<ProductPackageStandardResultV2> ApproveAsync(long version, ApproveProductPackageStandardVersionRequestV3 request,
        ProductPackageStandardCommandMetadataV2 metadata, CancellationToken token = default) => core.RunOwnedDecisionAsync(
            ProductPackageStandardService.ApproveCommandName, metadata, writer, current => core.ApproveAsync(version, request, metadata, current), token);
    public Task<ProductPackageStandardResultV2> RejectAsync(long version, RejectProductPackageStandardVersionRequestV3 request,
        ProductPackageStandardCommandMetadataV2 metadata, CancellationToken token = default) => core.RunOwnedDecisionAsync(
            ProductPackageStandardService.RejectCommandName, metadata, writer, current => core.RejectAsync(version, request, metadata, current), token);
    public Task<ProductPackageStandardTableViewV2> GetCurrentAsync(CancellationToken token = default) => core.GetCurrentAsync(token);
    public Task<ProductPackageStandardResultV2> ImportAsync(ImportProductPackageStandardsRequestV2 request, ProductPackageStandardCommandMetadataV2 metadata, CancellationToken token = default) => core.ImportAsync(request, metadata, token);
    public Task<ProductPackageStandardResultV2> SetAsync(Guid productId, SetProductPackageStandardRequestV2 request, ProductPackageStandardCommandMetadataV2 metadata, CancellationToken token = default) => core.SetAsync(productId, request, metadata, token);
    public Task<ProductPackageStandardReadV2> GetAsync(Guid productId, CancellationToken token = default) => ((IProductPackageStandardReaderV2)core).GetAsync(productId, token);
    public Task<ProductPackageStandardReadV3> GetAsync(Guid productId, Guid? customerId, DateOnly on, CancellationToken token = default) => ((IProductPackageStandardReaderV3)core).GetAsync(productId, customerId, on, token);
    public Task<ProductPackageStandardApprovalViewV3> GetApprovalStateAsync(CancellationToken token = default) => core.GetApprovalStateAsync(token);
    public Task<ProductPackageStandardApprovalViewV3> GetApprovalStateForApproverAsync(CancellationToken token = default) => core.GetApprovalStateForApproverAsync(token);
    public Task<ProductPackageStandardImportFileListV3> ListImportsAsync(CancellationToken token = default) => core.ListImportsAsync(token);
    public Task<ProductPackageStandardImportReportV3?> GetImportReportAsync(Guid id, CancellationToken token = default) => core.GetImportReportAsync(id, token);
    public Task<ProductPackageStandardResultV2> SetCustomerAsync(Guid customerId, Guid productId, SetProductPackageStandardRequestV2 request, ProductPackageStandardCommandMetadataV2 metadata, CancellationToken token = default) => core.SetCustomerAsync(customerId, productId, request, metadata, token);
}
