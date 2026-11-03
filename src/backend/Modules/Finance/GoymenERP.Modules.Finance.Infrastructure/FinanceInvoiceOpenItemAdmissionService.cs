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
/// F2a / MOD-04: admits a finalised Sales or Purchasing invoice as Finance open items, one per installment, with the
/// direction its producer fixes (MOD-15: sales → receivable, purchasing → payable). One save writes the admission receipt
/// (the existing <c>OpenItemSourceBatches</c> row: key hash, payload hash, actor, result), the open items and the audit
/// event, all or nothing. Idempotent: the same key and payload replay; an invoice already admitted with the same rows is a
/// reuse; with other rows it is refused. No host registration yet: the producers' finalisation ports (Sales S5,
/// Purchasing A5) wire it when they exist; until then nothing calls it.
/// </summary>
public sealed class FinanceInvoiceOpenItemAdmissionService(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinancePartyAccountPort parties,
    IFinanceInvoiceOpenItemAdmissionAuthorityV1 authority,
    IAuditEventCollector auditEvents,
    FinanceAuditEnvelope envelope,
    TimeProvider timeProvider) : IFinanceInvoiceOpenItemAdmissionPortV1
{
    internal const string CommandName = "finance.invoice-open-items.admit.v1";
    internal const string AlreadyAdmittedCode = "finance_invoice_already_admitted";
    private const int TokenMaximumLength = 128;
    private const string EntityType = "Finance.InvoiceOpenItemAdmission";
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    public async Task<FinanceOpenItemAdmissionResult> AdmitAsync(
        FinanceContext context,
        CommandMetadata metadata,
        FinanceInvoiceOpenItemAdmissionV1 invoice,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(invoice);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context))
        {
            return Refused(context, CommandOutcome.Denied);
        }
        if (!ValidRequestIdentity(context, metadata))
        {
            return Refused(context, CommandOutcome.ValidationFailed, new ValidationIssue("request", "identity_required"));
        }

        var issues = Validate(invoice, out var kind, out var direction);
        if (issues.Count > 0)
        {
            return Refused(context, CommandOutcome.ValidationFailed, [.. issues]);
        }

        if (await FenceAsync(context, invoice, cancellationToken) is { } fenced)
        {
            return fenced;
        }

        var payloadHash = PayloadHash(invoice);
        if (await PriorAsync(context, metadata, payloadHash, cancellationToken) is { } prior)
        {
            return prior;
        }

        var partyRead = await parties.GetAuthorizedAsync(context, invoice.PartyId, invoice.CompanyAccountId,
            cancellationToken);
        if (!SameContext(context))
        {
            return Refused(context, CommandOutcome.Denied);
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
        if (party.CompanyId != context.CompanyId || party.PartyId != invoice.PartyId
            || party.CompanyAccountId != invoice.CompanyAccountId || !party.IsActive || !party.IsPartyActive)
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                new ValidationIssue("companyAccountId", "party_account_ineligible"));
        }

        var invoiceId = invoice.InvoiceId;
        var existing = await dbContext.OpenItems
            .Where(row => row.SourceKind == kind && row.SourceId == invoiceId)
            .ToListAsync(cancellationToken);
        var reused = existing.Count > 0;
        if (reused && !SameRows(invoice, kind, direction, existing))
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                new ValidationIssue("invoiceId", AlreadyAdmittedCode));
        }

        var now = timeProvider.GetUtcNow();
        if (!reused)
        {
            foreach (var installment in invoice.Installments)
            {
                dbContext.OpenItems.Add(new FinanceOpenItemRow
                {
                    Id = installment.OpenItemId,
                    CompanyId = context.CompanyId,
                    SourceKind = kind,
                    Direction = direction,
                    SourceId = invoice.InvoiceId,
                    SourceLineId = installment.InstallmentId,
                    SourceVersion = invoice.InvoiceVersion,
                    SourceBatchId = invoice.AdmissionId,
                    SourceBatchVersion = invoice.InvoiceVersion,
                    SourceBatchChecksum = payloadHash,
                    PartyId = invoice.PartyId,
                    CompanyAccountId = invoice.CompanyAccountId,
                    CurrencyId = invoice.CurrencyId,
                    OriginalAmountText = installment.Amount,
                    OutstandingAmountText = installment.Amount,
                    ExchangeRateText = invoice.ExchangeRate,
                    FunctionalAmountText = installment.FunctionalAmount,
                    DueOn = installment.DueOn,
                    Status = FinanceOpenItemStatuses.Open,
                    Version = 1,
                    CreatedBy = context.ActorId,
                    CreatedAtUtc = now,
                    UpdatedBy = context.ActorId,
                    UpdatedAtUtc = now,
                });
            }
        }

        var openItemIds = reused
            ? invoice.Installments.Select(installment =>
                existing.Single(row => row.SourceLineId == installment.InstallmentId).Id).ToArray()
            : invoice.Installments.Select(installment => installment.OpenItemId).ToArray();
        var result = new FinanceOpenItemAdmissionResult(CommandOutcome.Succeeded, openItemIds, reused,
            context.CorrelationId, []);
        var receipt = new FinanceOpenItemSourceBatchRow
        {
            Id = invoice.AdmissionId,
            CompanyId = context.CompanyId,
            SourceKind = kind,
            SourceVersion = invoice.InvoiceVersion,
            SourceChecksum = payloadHash,
            IdempotencyKeyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey),
            PayloadHash = payloadHash,
            ActorId = context.ActorId,
            ResultJson = JsonSerializer.Serialize(result, Json),
            CorrelationId = context.CorrelationId,
            CausationId = context.CausationId,
            AdmittedAtUtc = now,
        };
        dbContext.OpenItemSourceBatches.Add(receipt);
        Audit(context, metadata, invoice, kind, reused);
        // The authority is measured again right before the save: a grant withdrawn meanwhile writes nothing.
        if (await FenceAsync(context, invoice, cancellationToken) is { } late)
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
            return await PriorAsync(context, metadata, payloadHash, cancellationToken)
                ?? Refused(context, CommandOutcome.VersionConflict, new ValidationIssue("request", "concurrent_change"));
        }
    }

    /// <summary>The pure shape rules; <paramref name="kind"/> and <paramref name="direction"/> follow the producer.</summary>
    internal static IReadOnlyList<ValidationIssue> Validate(FinanceInvoiceOpenItemAdmissionV1 invoice, out string kind,
        out int direction)
    {
        var issues = new List<ValidationIssue>();
        (kind, direction) = invoice.Producer switch
        {
            FinanceInvoiceProducerV1.Sales => (FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment,
                FinanceOpenItemDirectionCodes.Receivable),
            FinanceInvoiceProducerV1.Purchasing => (FinanceOpenItemSourceKinds.ApprovedOfficialIncomingInvoiceInstallment,
                FinanceOpenItemDirectionCodes.Payable),
            _ => (string.Empty, 0),
        };
        if (kind.Length == 0 || FinanceOpenItemDirectionCodes.ForSourceKind(kind) != direction)
        {
            issues.Add(new ValidationIssue("producer", "finance_invoice_producer_invalid"));
        }
        if (invoice.AdmissionId == Guid.Empty || invoice.InvoiceId == Guid.Empty || invoice.PartyId == Guid.Empty
            || invoice.CompanyAccountId == Guid.Empty || invoice.CurrencyId == Guid.Empty)
        {
            issues.Add(new ValidationIssue("invoice", "identity_required"));
        }
        if (!IsToken(invoice.InvoiceVersion))
        {
            issues.Add(new ValidationIssue("invoiceVersion", "finance_invoice_version_invalid"));
        }

        var installments = invoice.Installments;
        if (installments is null || installments.Count == 0)
        {
            issues.Add(new ValidationIssue("installments", "finance_invoice_installments_required"));
            return issues;
        }

        var foreign = invoice.ExchangeRate is not null;
        if (foreign && (invoice.ExchangeRate!.Length > FinanceOpenItemExchange.RateMaximumLength
            || !FinanceExactDecimalText.TryParsePositive(invoice.ExchangeRate, out _)))
        {
            issues.Add(new ValidationIssue("exchangeRate", "finance_invoice_exchange_rate_invalid"));
        }

        var itemIds = new HashSet<Guid>();
        var installmentIds = new HashSet<Guid>();
        for (var index = 0; index < installments.Count; index++)
        {
            var installment = installments[index];
            var field = $"installments[{index}]";
            if (installment is null)
            {
                issues.Add(new ValidationIssue(field, "identity_required"));
                continue;
            }
            if (installment.OpenItemId == Guid.Empty || installment.InstallmentId == Guid.Empty)
            {
                issues.Add(new ValidationIssue(field, "identity_required"));
            }
            if (!itemIds.Add(installment.OpenItemId) || !installmentIds.Add(installment.InstallmentId))
            {
                issues.Add(new ValidationIssue(field, "finance_invoice_installment_duplicate"));
            }
            // SAT-D86 / ALS-D24: installments are a numbered plan, 1…n, in order.
            if (installment.Sequence != index + 1)
            {
                issues.Add(new ValidationIssue(field + ".sequence", "finance_invoice_installment_sequence_invalid"));
            }
            if (installment.DueOn == default)
            {
                issues.Add(new ValidationIssue(field + ".dueOn", "finance_invoice_installment_due_required"));
            }
            if (!FinanceExactDecimalText.TryParsePositive(installment.Amount, out _))
            {
                issues.Add(new ValidationIssue(field + ".amount", "finance_open_item_amount_invalid"));
            }
            if (foreign != (installment.FunctionalAmount is not null)
                || (foreign && !FinanceExactDecimalText.TryParsePositive(installment.FunctionalAmount, out _)))
            {
                issues.Add(new ValidationIssue(field + ".functionalAmount", "finance_invoice_functional_amount_invalid"));
            }
        }
        return issues;
    }

    private static bool SameRows(FinanceInvoiceOpenItemAdmissionV1 invoice, string kind, int direction,
        IReadOnlyList<FinanceOpenItemRow> existing) =>
        existing.Count == invoice.Installments.Count
        && invoice.Installments.All(installment =>
            existing.SingleOrDefault(row => row.SourceLineId == installment.InstallmentId) is { } row
            && row.SourceKind == kind
            && row.Direction == direction
            && row.SourceVersion == invoice.InvoiceVersion
            && row.PartyId == invoice.PartyId
            && row.CompanyAccountId == invoice.CompanyAccountId
            && row.CurrencyId == invoice.CurrencyId
            && FinanceExactDecimalText.Equal(row.OriginalAmountText, installment.Amount)
            && row.DueOn == installment.DueOn
            && SameOptionalAmount(row.ExchangeRateText, invoice.ExchangeRate)
            && SameOptionalAmount(row.FunctionalAmountText, installment.FunctionalAmount));

    /// <summary>Exact decimal equality ("38.45" = "38.450"); both absent is equal, one absent is not.</summary>
    private static bool SameOptionalAmount(string? stored, string? requested) =>
        stored is null || requested is null
            ? stored is null && requested is null
            : FinanceExactDecimalText.Equal(stored, requested);

    private async Task<FinanceOpenItemAdmissionResult?> FenceAsync(FinanceContext context,
        FinanceInvoiceOpenItemAdmissionV1 invoice, CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return Refused(context, CommandOutcome.Denied);
        }

        var decision = await authority.CheckAsync(context, invoice.Producer, invoice.InvoiceId, cancellationToken);
        if (decision != PermissionDecision.Allowed)
        {
            return decision == PermissionDecision.Denied
                ? Refused(context, CommandOutcome.Denied)
                : Refused(context, CommandOutcome.Unavailable,
                    new ValidationIssue("permission", "finance_invoice_admission_authority_unavailable"));
        }

        return SameContext(context) ? null : Refused(context, CommandOutcome.Denied);
    }

    private async Task<FinanceOpenItemAdmissionResult?> PriorAsync(FinanceContext context, CommandMetadata metadata,
        string payloadHash, CancellationToken cancellationToken)
    {
        var keyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var prior = await dbContext.OpenItemSourceBatches.AsNoTracking()
            .SingleOrDefaultAsync(row => row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (prior is null)
        {
            return null;
        }
        if (prior.PayloadHash != payloadHash || prior.ActorId != context.ActorId)
        {
            return Refused(context, CommandOutcome.IdempotencyConflict);
        }
        return (JsonSerializer.Deserialize<FinanceOpenItemAdmissionResult>(prior.ResultJson, Json)
                ?? throw new InvalidOperationException("Stored invoice admission result cannot be read."))
            with { IsReplay = true };
    }

    private void Audit(FinanceContext context, CommandMetadata metadata, FinanceInvoiceOpenItemAdmissionV1 invoice,
        string kind, bool reused) =>
        auditEvents.Enqueue(new AuditEventDraft(
            context.CompanyId, "finance.invoice-open-items.admitted.v1", EntityType,
            invoice.AdmissionId.ToString("D"), "Admit", null, 1, AuditActorTypes.User, context.ActorId,
            envelope.PermissionCode, envelope.RoleCodes, "{}",
            JsonSerializer.Serialize(new
            {
                Producer = invoice.Producer.ToString(),
                SourceKind = kind,
                invoice.InvoiceId,
                invoice.InvoiceVersion,
                InstallmentCount = invoice.Installments.Count,
                ReusedExistingIdentity = reused,
            }, Json),
            reused ? "Invoice already admitted with the same installments; no second open item created"
                : "Finalised invoice admitted as open items",
            ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null,
            envelope.SourceChannel, envelope.ClientIpAddress, envelope.UserAgent,
            context.CorrelationId, context.CausationId, metadata.IdempotencyKey));

    private static string PayloadHash(FinanceInvoiceOpenItemAdmissionV1 invoice) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            CommandName + "|" + JsonSerializer.Serialize(invoice, Json)))).ToLowerInvariant();

    private static FinanceOpenItemAdmissionResult Refused(FinanceContext context, CommandOutcome outcome,
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
