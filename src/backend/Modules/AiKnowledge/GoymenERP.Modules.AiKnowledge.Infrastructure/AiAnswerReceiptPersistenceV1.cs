using System.Data;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.AiKnowledge.Domain;
using GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.AiKnowledge.Infrastructure;

/// <summary>Immutable answer evidence and separate identity; never borrows a caller transaction or stores response text.</summary>
public sealed class AiAnswerReceiptPersistenceV1(AiKnowledgeDbContext database, ICompanyContext company,
    ICurrentUser user, IAuditTransactionWriterV1 auditWriter, TimeProvider clock) : IAiAnswerReceiptPersistenceV1
{
    public async Task<bool> TryAddAsync(AiAnswerReceiptV1 receipt, AiAnswerReceiptIdentityV1 identity,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt); ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(freshConsumerFence);
        if (!AiAnswerReceiptRulesV1.IsValid(receipt) || identity.CompanyId != receipt.CompanyId || identity.AnswerId != receipt.AnswerId
            || !AiAnswerReceiptRulesV1.Token(identity.ActorId) || !AiAnswerReceiptRulesV1.Token(identity.CorrelationId)
            || !Current(receipt.CompanyId, identity.ActorId) || !CleanEntry()) return false;
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            async Task Fence()
            {
                if (!Current(receipt.CompanyId, identity.ActorId)) throw new AiAnswerReceiptAuthorityLostException();
                await freshConsumerFence(cancellationToken);
                if (!Current(receipt.CompanyId, identity.ActorId)) throw new AiAnswerReceiptAuthorityLostException();
            }
            await Fence();
            database.AnswerReceipts.Add(new AiAnswerReceiptRow { CompanyId = receipt.CompanyId, AnswerId = receipt.AnswerId,
                AnswerSha256 = receipt.AnswerSha256, CitationsSha256 = receipt.CitationsSha256, Feature = receipt.Feature,
                ModelDeployment = receipt.ModelDeployment, PolicyVersion = receipt.PolicyVersion,
                CreatedAtUtc = receipt.CreatedAtUtc, RetainUntilUtc = receipt.RetainUntilUtc });
            database.AnswerReceiptIdentities.Add(new AiAnswerReceiptIdentityRow { CompanyId = identity.CompanyId,
                AnswerId = identity.AnswerId, ActorId = identity.ActorId, CorrelationId = identity.CorrelationId });
            await Fence();
            await database.SaveChangesAsync(cancellationToken);
            await Fence();
            if (auditWriter.HasPendingDrafts) throw new InvalidOperationException("Receipt save cannot flush caller audit.");
            await Fence();
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (Exception exception)
        {
            try { await transaction.RollbackAsync(CancellationToken.None); }
            finally { try { await transaction.DisposeAsync(); } finally { database.ChangeTracker.Clear(); } }
            if (exception is DbUpdateException) return false; // Including a random AnswerId collision; never mutate/retry it.
            throw;
        }
    }

    public async Task<AiAnswerReceiptV1?> GetOwnAsync(Guid companyId, Guid answerId, string actorId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence);
        if (answerId == Guid.Empty || !Current(companyId, actorId)) return null;
        await freshConsumerFence(cancellationToken);
        if (!Current(companyId, actorId)) return null;
        var row = await (from receipt in database.AnswerReceipts.AsNoTracking()
            join identity in database.AnswerReceiptIdentities.AsNoTracking()
                on new { receipt.CompanyId, receipt.AnswerId } equals new { identity.CompanyId, identity.AnswerId }
            where receipt.CompanyId == companyId && receipt.AnswerId == answerId && identity.ActorId == actorId
            select receipt).SingleOrDefaultAsync(cancellationToken);
        await freshConsumerFence(cancellationToken);
        if (!Current(companyId, actorId) || row is null || clock.GetUtcNow() >= row.RetainUntilUtc) return null;
        var answer = new AiAnswerReceiptV1(row.CompanyId, row.AnswerId, row.AnswerSha256, row.CitationsSha256,
            row.Feature, row.ModelDeployment, row.PolicyVersion, row.CreatedAtUtc, row.RetainUntilUtc);
        return AiAnswerReceiptRulesV1.IsValid(answer) ? answer : throw new InvalidOperationException("Invalid stored answer receipt.");
    }
    private bool Current(Guid companyId, string actor) => companyId != Guid.Empty && company.IsResolved && company.CompanyId == companyId
        && user.IsAuthenticated && Guid.TryParse(user.UserId, out var currentActor) && currentActor != Guid.Empty && currentActor.ToString("D") == actor && user.CanAccessCompany(companyId);
    private bool CleanEntry() => System.Transactions.Transaction.Current is null && database.Database.IsSqlServer()
        && database.Database.CurrentTransaction is null && !database.ChangeTracker.HasChanges() && !auditWriter.HasPendingDrafts;
}
