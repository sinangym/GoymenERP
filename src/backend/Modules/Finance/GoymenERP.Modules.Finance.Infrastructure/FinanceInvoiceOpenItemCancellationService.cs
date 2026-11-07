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
/// F2b / API-11 (SAT-D96, FNS-D14): closes the open items of an admitted invoice that its producer cancelled, with a
/// reversal and without deleting anything. One save writes the cancellation receipt (key hash, payload hash, actor,
/// answer), one reversal row per open item (its original amount), the items' new status (Cancelled, outstanding zero)
/// and the audit event, all or nothing. An item with an active allocation is refused: what happens to an allocated
/// invoice is undecided (Issue #66 NEEDS-SINAN). No host registration yet: the producers' cancellation commands wire it
/// when they exist; until then nothing calls it.
/// </summary>
public sealed class FinanceInvoiceOpenItemCancellationService(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinanceInvoiceOpenItemCancellationAuthorityV1 authority,
    IAuditEventCollector auditEvents,
    FinanceAuditEnvelope envelope,
    TimeProvider timeProvider) : IFinanceInvoiceOpenItemCancellationPortV1
{
    internal const string CommandName = "finance.invoice-open-items.cancel.v1";
    internal const string NotAdmittedCode = "finance_invoice_not_admitted";
    internal const string VersionMismatchCode = "finance_invoice_version_mismatch";
    internal const string HasAllocationsCode = "finance_invoice_cancellation_has_allocations";
    internal const string AlreadyCancelledCode = "finance_invoice_already_cancelled";
    internal const string CancellationIdentityConflictCode = "finance_invoice_cancellation_identity_conflict";
    private const int TokenMaximumLength = 128;
    private const string EntityType = "Finance.InvoiceOpenItemCancellation";
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    public async Task<FinanceInvoiceOpenItemCancellationResultV1> CancelAsync(
        FinanceContext context,
        CommandMetadata metadata,
        FinanceInvoiceOpenItemCancellationV1 cancellation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(cancellation);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context))
        {
            return Refused(context, CommandOutcome.Denied);
        }
        if (!ValidRequestIdentity(context, metadata))
        {
            return Refused(context, CommandOutcome.ValidationFailed, new ValidationIssue("request", "identity_required"));
        }

        var issues = Validate(cancellation, out var kind);
        if (issues.Count > 0)
        {
            return Refused(context, CommandOutcome.ValidationFailed, [.. issues]);
        }

        if (await FenceAsync(context, cancellation, cancellationToken) is { } fenced)
        {
            return fenced;
        }

        var payloadHash = PayloadHash(cancellation);
        if (await PriorAsync(context, metadata, cancellation.CancellationId, payloadHash, cancellationToken) is { } prior)
        {
            return prior;
        }

        // The invoice was cancelled already, by another cancellation id: the same invoice is not reversed twice.
        var invoiceId = cancellation.InvoiceId;
        if (await dbContext.InvoiceCancellations.AsNoTracking()
            .AnyAsync(row => row.SourceKind == kind && row.InvoiceId == invoiceId, cancellationToken))
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                new ValidationIssue("invoiceId", AlreadyCancelledCode));
        }

        var items = await dbContext.OpenItems
            .Where(row => row.SourceKind == kind && row.SourceId == invoiceId)
            .OrderBy(row => row.DueOn).ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
        if (items.Count == 0)
        {
            return Refused(context, CommandOutcome.ValidationFailed, new ValidationIssue("invoiceId", NotAdmittedCode));
        }
        if (items.Any(row => row.SourceVersion != cancellation.InvoiceVersion))
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                new ValidationIssue("invoiceVersion", VersionMismatchCode));
        }

        var itemIds = items.Select(row => row.Id).ToArray();
        var allocated = await dbContext.Allocations.AsNoTracking()
            .AnyAsync(row => itemIds.Contains(row.OpenItemId) && row.Status == FinanceAllocationStatuses.Active,
                cancellationToken);
        if (allocated || items.Any(row => row.Status != FinanceOpenItemStatuses.Open
            || !FinanceExactDecimalText.Equal(row.OutstandingAmountText, row.OriginalAmountText)))
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                new ValidationIssue("invoiceId", HasAllocationsCode));
        }

        var now = timeProvider.GetUtcNow();
        foreach (var item in items)
        {
            dbContext.InvoiceCancellationItems.Add(new FinanceInvoiceCancellationItemRow
            {
                CompanyId = context.CompanyId,
                CancellationId = cancellation.CancellationId,
                OpenItemId = item.Id,
                ReversedAmountText = item.OriginalAmountText,
            });
            item.Status = FinanceOpenItemStatuses.Cancelled;
            item.OutstandingAmountText = "0";
            item.Version = checked(item.Version + 1);
            item.UpdatedBy = context.ActorId;
            item.UpdatedAtUtc = now;
        }

        var result = new FinanceInvoiceOpenItemCancellationResultV1(CommandOutcome.Succeeded, itemIds, false,
            context.CorrelationId, []);
        dbContext.InvoiceCancellations.Add(new FinanceInvoiceCancellationRow
        {
            Id = cancellation.CancellationId,
            CompanyId = context.CompanyId,
            SourceKind = kind,
            InvoiceId = cancellation.InvoiceId,
            InvoiceVersion = cancellation.InvoiceVersion,
            Reason = cancellation.Reason,
            IdempotencyKeyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey),
            PayloadHash = payloadHash,
            ActorId = context.ActorId,
            ResultJson = JsonSerializer.Serialize(result, Json),
            CorrelationId = context.CorrelationId,
            CausationId = context.CausationId,
            CancelledAtUtc = now,
        });
        Audit(context, metadata, cancellation, kind, items.Count);
        // The authority is measured again right before the save: a grant withdrawn meanwhile writes nothing.
        if (await FenceAsync(context, cancellation, cancellationToken) is { } late)
        {
            dbContext.ChangeTracker.Clear();
            return late;
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
            return await PriorAsync(context, metadata, cancellation.CancellationId, payloadHash, cancellationToken)
                ?? Refused(context, CommandOutcome.VersionConflict, new ValidationIssue("request", "concurrent_change"));
        }
    }

    /// <summary>The pure shape rules; <paramref name="kind"/> follows the producer, as on admission.</summary>
    internal static IReadOnlyList<ValidationIssue> Validate(FinanceInvoiceOpenItemCancellationV1 cancellation,
        out string kind)
    {
        var issues = new List<ValidationIssue>();
        kind = cancellation.Producer switch
        {
            FinanceInvoiceProducerV1.Sales => FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment,
            FinanceInvoiceProducerV1.Purchasing => FinanceOpenItemSourceKinds.ApprovedOfficialIncomingInvoiceInstallment,
            _ => string.Empty,
        };
        if (kind.Length == 0)
        {
            issues.Add(new ValidationIssue("producer", "finance_invoice_producer_invalid"));
        }
        if (cancellation.CancellationId == Guid.Empty || cancellation.InvoiceId == Guid.Empty)
        {
            issues.Add(new ValidationIssue("cancellation", "identity_required"));
        }
        if (!IsToken(cancellation.InvoiceVersion))
        {
            issues.Add(new ValidationIssue("invoiceVersion", "finance_invoice_version_invalid"));
        }
        if (string.IsNullOrWhiteSpace(cancellation.Reason)
            || cancellation.Reason.Length > FinanceInvoiceCancellationRules.ReasonMaximumLength
            || !string.Equals(cancellation.Reason, cancellation.Reason.Trim(), StringComparison.Ordinal)
            || cancellation.Reason.Any(char.IsControl))
        {
            issues.Add(new ValidationIssue("reason", "finance_invoice_cancellation_reason_invalid"));
        }
        return issues;
    }

    private async Task<FinanceInvoiceOpenItemCancellationResultV1?> FenceAsync(FinanceContext context,
        FinanceInvoiceOpenItemCancellationV1 cancellation, CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return Refused(context, CommandOutcome.Denied);
        }

        var decision = await authority.CheckAsync(context, cancellation.Producer, cancellation.InvoiceId,
            cancellationToken);
        if (decision != PermissionDecision.Allowed)
        {
            return decision == PermissionDecision.Denied
                ? Refused(context, CommandOutcome.Denied)
                : Refused(context, CommandOutcome.Unavailable,
                    new ValidationIssue("permission", "finance_invoice_cancellation_authority_unavailable"));
        }

        return SameContext(context) ? null : Refused(context, CommandOutcome.Denied);
    }

    /// <summary>
    /// The stored answer of this cancellation, found by key (ADR-011: same key, payload and actor replay; otherwise a
    /// conflict) or by <see cref="FinanceInvoiceOpenItemCancellationV1.CancellationId"/>: a resend under a new key, e.g.
    /// by another authorised user (SAT-D208), replays when its payload is the same and is refused when it is not.
    /// </summary>
    private async Task<FinanceInvoiceOpenItemCancellationResultV1?> PriorAsync(FinanceContext context,
        CommandMetadata metadata, Guid cancellationId, string payloadHash, CancellationToken cancellationToken)
    {
        var keyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var prior = await dbContext.InvoiceCancellations.AsNoTracking()
            .SingleOrDefaultAsync(row => row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (prior is not null)
        {
            return prior.PayloadHash != payloadHash || prior.ActorId != context.ActorId
                ? Refused(context, CommandOutcome.IdempotencyConflict)
                : Stored(prior);
        }

        prior = await dbContext.InvoiceCancellations.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == cancellationId, cancellationToken);
        if (prior is null)
        {
            return null;
        }

        return prior.PayloadHash != payloadHash
            ? Refused(context, CommandOutcome.ValidationFailed,
                new ValidationIssue("cancellationId", CancellationIdentityConflictCode))
            : Stored(prior);
    }

    private static FinanceInvoiceOpenItemCancellationResultV1 Stored(FinanceInvoiceCancellationRow prior) =>
        (JsonSerializer.Deserialize<FinanceInvoiceOpenItemCancellationResultV1>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Stored invoice cancellation result cannot be read."))
        with { IsReplay = true };

    private void Audit(FinanceContext context, CommandMetadata metadata, FinanceInvoiceOpenItemCancellationV1 cancellation,
        string kind, int itemCount) =>
        auditEvents.Enqueue(new AuditEventDraft(
            context.CompanyId, "finance.invoice-open-items.cancelled.v1", EntityType,
            cancellation.CancellationId.ToString("D"), "Cancel", null, 1, AuditActorTypes.User, context.ActorId,
            envelope.PermissionCode, envelope.RoleCodes, "{}",
            JsonSerializer.Serialize(new
            {
                Producer = cancellation.Producer.ToString(),
                SourceKind = kind,
                cancellation.InvoiceId,
                cancellation.InvoiceVersion,
                OpenItemCount = itemCount,
            }, Json),
            cancellation.Reason,
            ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null,
            envelope.SourceChannel, envelope.ClientIpAddress, envelope.UserAgent,
            context.CorrelationId, context.CausationId, metadata.IdempotencyKey));

    private static string PayloadHash(FinanceInvoiceOpenItemCancellationV1 cancellation) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            CommandName + "|" + JsonSerializer.Serialize(cancellation, Json)))).ToLowerInvariant();

    private static FinanceInvoiceOpenItemCancellationResultV1 Refused(FinanceContext context, CommandOutcome outcome,
        params ValidationIssue[] issues) =>
        new(outcome, [], false, context.CorrelationId, issues);

    private bool ValidRequestIdentity(FinanceContext context, CommandMetadata metadata) =>
        !string.IsNullOrWhiteSpace(metadata.IdempotencyKey)
        && metadata.IdempotencyKey.Length <= TokenMaximumLength
        && string.Equals(metadata.IdempotencyKey, metadata.IdempotencyKey.Trim(), StringComparison.Ordinal)
        && context.ActorId.Length <= TokenMaximumLength
        && context.CorrelationId.Length <= TokenMaximumLength;

    private static bool IsToken(string? value) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= TokenMaximumLength
        && string.Equals(value, value.Trim(), StringComparison.Ordinal)
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' or ':' or '/');

    private bool SameContext(FinanceContext context) =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
        && companyContext.CompanyId == context.CompanyId
        && currentUser.CanAccessCompany(context.CompanyId);
}
