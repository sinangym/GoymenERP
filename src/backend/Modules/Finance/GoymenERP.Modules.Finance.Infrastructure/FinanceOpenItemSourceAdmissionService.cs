using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// Admits approved official invoice-installment facts as Finance-owned open items. It has no host
/// registration by design: the real approved-file adapter and authority mapping are still absent.
/// </summary>
public sealed class FinanceOpenItemSourceAdmissionService(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinanceOpenItemSourceAuthority authority,
    IApprovedFinanceOpenItemSourcePort sourcePort,
    IFinancePartyAccountPort parties,
    IAuditEventCollector auditEvents,
    FinanceAuditEnvelope envelope,
    TimeProvider timeProvider) : IFinanceOpenItemSourceAdmissionService
{
    internal const string CommandName = "finance.open-item-source.admit.v1";
    private const int IdentityMaximumLength = 128;
    private const string EntityType = "Finance.OpenItemSourceBatch";
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    public async Task<FinanceOpenItemAdmissionResult> AdmitAsync(
        FinanceContext context,
        CommandMetadata metadata,
        FinanceOpenItemSourceReference source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(source);
        if (!ValidRequestIdentity(context, metadata))
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                new ValidationIssue("request", "identity_required"));
        }
        if (await FenceAsync(context, source.BatchId, cancellationToken) is { } startRefusal)
        {
            return startRefusal;
        }
        var referenceIssues = FinanceOpenItemSourcePolicy.ValidateReference(source);
        if (referenceIssues.Count > 0)
        {
            return Refused(context, CommandOutcome.ValidationFailed, [.. referenceIssues]);
        }

        var payloadHash = PayloadHash(source);
        if (await PriorAsync(context, metadata, source, payloadHash, cancellationToken) is { } prior)
        {
            return prior;
        }

        var sourceRead = await sourcePort.GetApprovedAsync(context, source, cancellationToken);
        if (await FenceAsync(context, source.BatchId, cancellationToken) is { } sourceFence)
        {
            return sourceFence;
        }
        if (sourceRead.Outcome != ReadOutcome.Found || sourceRead.Resource is null)
        {
            return sourceRead.Outcome switch
            {
                ReadOutcome.Denied => Refused(context, CommandOutcome.Denied),
                ReadOutcome.Unavailable => Refused(context, CommandOutcome.Unavailable,
                    new ValidationIssue("source", "finance_open_item_source_unavailable")),
                _ => Refused(context, CommandOutcome.ValidationFailed,
                    new ValidationIssue("source", "finance_open_item_source_not_found")),
            };
        }

        var batch = sourceRead.Resource;
        var batchIssues = FinanceOpenItemSourcePolicy.ValidateBatch(context, source, batch);
        if (batchIssues.Count > 0)
        {
            return Refused(context, CommandOutcome.ValidationFailed, [.. batchIssues]);
        }

        foreach (var row in batch.Rows)
        {
            var partyRead = await parties.GetAuthorizedAsync(
                context, row.PartyId, row.CompanyAccountId, cancellationToken);
            if (await FenceAsync(context, source.BatchId, cancellationToken) is { } partyFence)
            {
                return partyFence;
            }
            if (partyRead.Outcome != ReadOutcome.Found || partyRead.Resource is not { } party)
            {
                return partyRead.Outcome switch
                {
                    ReadOutcome.Denied => Refused(context, CommandOutcome.Denied),
                    ReadOutcome.Unavailable => Refused(context, CommandOutcome.Unavailable,
                        new ValidationIssue("companyAccountId", "party_account_unavailable")),
                    _ => Refused(context, CommandOutcome.ValidationFailed,
                        new ValidationIssue("companyAccountId", "party_account_not_found")),
                };
            }
            if (party.CompanyId != context.CompanyId || party.PartyId != row.PartyId
                || party.CompanyAccountId != row.CompanyAccountId || !party.IsActive || !party.IsPartyActive)
            {
                return Refused(context, CommandOutcome.ValidationFailed,
                    new ValidationIssue("companyAccountId", "party_account_ineligible"));
            }
        }

        var sourceIds = batch.Rows.Select(row => row.SourceDocumentId).Distinct().ToArray();
        var existing = await dbContext.OpenItems
            .Where(row => row.SourceKind == FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment
                && sourceIds.Contains(row.SourceId))
            .ToListAsync(cancellationToken);
        if (await FenceAsync(context, source.BatchId, cancellationToken) is { } existingFence)
        {
            return existingFence;
        }

        var reused = existing.Count > 0;
        if (reused && (existing.Count != batch.Rows.Count || batch.Rows.Any(row =>
                existing.SingleOrDefault(candidate => candidate.SourceId == row.SourceDocumentId
                    && candidate.SourceLineId == row.SourceInstallmentId) is not { } candidate
                || !FinanceOpenItemSourcePolicy.MatchesExisting(row, candidate))))
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                new ValidationIssue("sourceRowIdentity", "finance_open_item_source_identity_conflict"));
        }

        var now = timeProvider.GetUtcNow();
        if (!reused)
        {
            foreach (var row in batch.Rows)
            {
                dbContext.OpenItems.Add(new FinanceOpenItemRow
                {
                    Id = row.OpenItemId,
                    CompanyId = context.CompanyId,
                    SourceKind = FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment,
                    SourceId = row.SourceDocumentId,
                    SourceLineId = row.SourceInstallmentId,
                    SourceVersion = row.SourceVersion,
                    SourceBatchId = batch.BatchId,
                    SourceBatchVersion = batch.BatchVersion,
                    SourceBatchChecksum = batch.BatchChecksum,
                    PartyId = row.PartyId,
                    CompanyAccountId = row.CompanyAccountId,
                    CurrencyId = row.CurrencyId,
                    OriginalAmountText = row.Amount,
                    OutstandingAmountText = row.Amount,
                    DueOn = row.DueOn,
                    Status = FinanceOpenItemStatuses.Open,
                    Version = 1,
                    CreatedBy = context.ActorId,
                    CreatedAtUtc = now,
                    UpdatedBy = context.ActorId,
                    UpdatedAtUtc = now,
                });
            }
        }

        var result = new FinanceOpenItemAdmissionResult(
            CommandOutcome.Succeeded, batch.Rows.Select(row => row.OpenItemId).ToArray(),
            reused, context.CorrelationId, []);
        var batchRow = new FinanceOpenItemSourceBatchRow
        {
            Id = batch.BatchId,
            CompanyId = context.CompanyId,
            SourceKind = FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment,
            SourceVersion = batch.BatchVersion,
            SourceChecksum = batch.BatchChecksum,
            IdempotencyKeyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey),
            PayloadHash = payloadHash,
            ActorId = context.ActorId,
            ResultJson = JsonSerializer.Serialize(result, Json),
            CorrelationId = context.CorrelationId,
            CausationId = context.CausationId,
            AdmittedAtUtc = now,
        };
        dbContext.OpenItemSourceBatches.Add(batchRow);
        Audit(context, metadata, batchRow, batch.Rows.Count, reused);
        return await SaveAsync(context, metadata, source, payloadHash, result, cancellationToken);
    }

    private async Task<FinanceOpenItemAdmissionResult?> PriorAsync(
        FinanceContext context,
        CommandMetadata metadata,
        FinanceOpenItemSourceReference source,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        var keyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var prior = await dbContext.OpenItemSourceBatches.AsNoTracking().SingleOrDefaultAsync(
            row => row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (prior is not null)
        {
            if (prior.PayloadHash != payloadHash || prior.ActorId != context.ActorId)
            {
                return Refused(context, CommandOutcome.IdempotencyConflict);
            }
            if (await FenceAsync(context, prior.Id, cancellationToken) is { } keyFence)
            {
                return keyFence;
            }
            return Stored(prior);
        }

        prior = await dbContext.OpenItemSourceBatches.AsNoTracking().SingleOrDefaultAsync(
            row => row.Id == source.BatchId, cancellationToken);
        if (prior is null)
        {
            return null;
        }
        if (prior.SourceKind != FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment
            || prior.SourceVersion != source.BatchVersion || prior.SourceChecksum != source.BatchChecksum)
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                new ValidationIssue("batchId", "finance_open_item_batch_identity_conflict"));
        }
        if (await FenceAsync(context, prior.Id, cancellationToken) is { } batchFence)
        {
            return batchFence;
        }
        return Stored(prior);
    }

    private async Task<FinanceOpenItemAdmissionResult> SaveAsync(
        FinanceContext context,
        CommandMetadata metadata,
        FinanceOpenItemSourceReference source,
        string payloadHash,
        FinanceOpenItemAdmissionResult result,
        CancellationToken cancellationToken)
    {
        if (await FenceAsync(context, source.BatchId, cancellationToken) is { } fenced)
        {
            dbContext.ChangeTracker.Clear();
            return fenced;
        }
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            dbContext.ChangeTracker.Clear();
            return await PriorAsync(context, metadata, source, payloadHash, cancellationToken)
                ?? Refused(context, CommandOutcome.VersionConflict,
                    new ValidationIssue("request", "concurrent_change"));
        }
    }

    private async Task<FinanceOpenItemAdmissionResult?> FenceAsync(
        FinanceContext context,
        Guid batchId,
        CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return Refused(context, CommandOutcome.Denied);
        }
        var decision = await authority.CheckAsync(context, batchId, cancellationToken);
        if (decision != PermissionDecision.Allowed)
        {
            return decision == PermissionDecision.Denied
                ? Refused(context, CommandOutcome.Denied)
                : Refused(context, CommandOutcome.Unavailable,
                    new ValidationIssue("permission", "finance_open_item_source_authority_unavailable"));
        }
        return SameContext(context) ? null : Refused(context, CommandOutcome.Denied);
    }

    private void Audit(
        FinanceContext context,
        CommandMetadata metadata,
        FinanceOpenItemSourceBatchRow batch,
        int itemCount,
        bool reused) =>
        auditEvents.Enqueue(new AuditEventDraft(
            context.CompanyId, "finance.open-item-source.admitted.v1", EntityType,
            batch.Id.ToString("D"), "Admit", null, 1, AuditActorTypes.User, context.ActorId,
            envelope.PermissionCode, envelope.RoleCodes, "{}",
            JsonSerializer.Serialize(new
            {
                batch.SourceKind,
                batch.SourceVersion,
                batch.SourceChecksum,
                ItemCount = itemCount,
                ReusedExistingIdentity = reused,
            }, Json),
            reused ? "Canonical source identities reused; no second open item created"
                : "Approved source batch admitted as open items",
            ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null,
            envelope.SourceChannel, envelope.ClientIpAddress, envelope.UserAgent,
            context.CorrelationId, context.CausationId, metadata.IdempotencyKey));

    private static FinanceOpenItemAdmissionResult Stored(FinanceOpenItemSourceBatchRow row) =>
        (JsonSerializer.Deserialize<FinanceOpenItemAdmissionResult>(row.ResultJson, Json)
            ?? throw new InvalidOperationException("Stored open-item admission result cannot be read."))
        with { IsReplay = true };

    private static string PayloadHash(FinanceOpenItemSourceReference source) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            CommandName + "|" + JsonSerializer.Serialize(source, Json)))).ToLowerInvariant();

    private static FinanceOpenItemAdmissionResult Refused(
        FinanceContext context,
        CommandOutcome outcome,
        params ValidationIssue[] issues) =>
        new(outcome, [], false, context.CorrelationId, issues);

    private bool ValidRequestIdentity(FinanceContext context, CommandMetadata metadata) =>
        SameContext(context)
        && !string.IsNullOrWhiteSpace(metadata.IdempotencyKey)
        && metadata.IdempotencyKey.Length <= IdentityMaximumLength
        && string.Equals(metadata.IdempotencyKey, metadata.IdempotencyKey.Trim(), StringComparison.Ordinal)
        && context.ActorId.Length <= IdentityMaximumLength
        && context.CorrelationId.Length <= IdentityMaximumLength;

    private bool SameContext(FinanceContext context) =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
        && companyContext.CompanyId == context.CompanyId
        && currentUser.CanAccessCompany(context.CompanyId);
}
