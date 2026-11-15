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
/// F-019 / API-11 v2 (SAT-D288, SAT-D291, SAT-D294): accepts a finalised customer return invoice. One save writes the
/// reversing credit with its receipt (key hash, payload hash, actor, answer), one reduction row per original open item it
/// reduced, those items' new outstanding amounts, the invoice's returned-total aggregate and the audit event, all or
/// nothing. The original items keep their identity, source facts and original amount; nothing is deleted. The credit
/// reduces the outstanding first; the remainder is the customer's advance, recorded on the credit (SAT-D294 (1)-(3)).
/// When two or more items still carry an outstanding amount and the return covers only part of them, the order of the
/// reduction is not decided: the acceptance is refused and nothing is written.
/// </summary>
public sealed class FinanceInvoiceAcceptanceService(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinanceInvoiceAcceptanceAuthorityV2 authority,
    IAuditEventCollector auditEvents,
    FinanceAuditEnvelope envelope,
    TimeProvider timeProvider) : IFinanceInvoiceAcceptancePortV2
{
    internal const string CommandName = "finance.invoice-acceptances.accept.v2";
    internal const string KindUnsupportedCode = "finance_invoice_acceptance_kind_unsupported";
    internal const string NotAdmittedCode = "finance_invoice_not_admitted";
    internal const string VersionMismatchCode = "finance_invoice_version_mismatch";
    internal const string InvoiceCancelledCode = "finance_customer_return_invoice_cancelled";
    internal const string PartyMismatchCode = "finance_customer_return_party_mismatch";
    internal const string AccountMismatchCode = "finance_customer_return_account_mismatch";
    internal const string CurrencyMismatchCode = "finance_customer_return_currency_mismatch";
    internal const string FunctionalAmountMismatchCode = "finance_customer_return_functional_amount_mismatch";
    internal const string ExceedsInvoiceCode = "finance_customer_return_exceeds_invoice";
    internal const string AlreadyAcceptedCode = "finance_customer_return_already_accepted";
    internal const string AcceptanceIdentityConflictCode = "finance_customer_return_identity_conflict";
    /// <summary>
    /// The partial-return installment order is not decided: two or more items of the original invoice are still
    /// outstanding and the return covers only part of them. Nothing is written until the order is decided.
    /// </summary>
    internal const string InstallmentOrderNotDecidedCode = "finance_customer_return_installment_order_not_decided";
    /// <summary>Stored amounts or item state the acceptance cannot read back safely: nothing is written.</summary>
    internal const string StoredStateNotUnderstoodCode = "finance_customer_return_state_not_understood";
    private const int TokenMaximumLength = 128;
    private const string EntityType = "Finance.CustomerReturnCredit";
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    public async Task<FinanceInvoiceAcceptanceResultV2> AcceptAsync(
        FinanceContext context,
        CommandMetadata metadata,
        FinanceInvoiceAcceptanceV2 acceptance,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(acceptance);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context))
        {
            return Refused(context, CommandOutcome.Denied);
        }
        if (!ValidRequestIdentity(context, metadata))
        {
            return Refused(context, CommandOutcome.ValidationFailed, new ValidationIssue("request", "identity_required"));
        }

        var issues = Validate(acceptance);
        if (issues.Count > 0)
        {
            return Refused(context, CommandOutcome.ValidationFailed, [.. issues]);
        }

        if (await FenceAsync(context, acceptance, cancellationToken) is { } fenced)
        {
            return fenced;
        }

        var payloadHash = PayloadHash(acceptance);
        if (await PriorAsync(context, metadata, acceptance.AcceptanceId, payloadHash, cancellationToken) is { } prior)
        {
            return prior;
        }

        // The return document was accepted already, by another acceptance id: one return is credited once.
        var documentId = acceptance.DocumentId;
        if (await dbContext.CustomerReturnCredits.AsNoTracking()
            .AnyAsync(row => row.SourceKind == FinanceCustomerReturnCreditRules.SourceKind && row.DocumentId == documentId,
                cancellationToken))
        {
            return Refused(context, CommandOutcome.ValidationFailed, new ValidationIssue("documentId", AlreadyAcceptedCode));
        }

        var invoiceId = acceptance.OriginalInvoiceId;
        var items = await dbContext.OpenItems
            .Where(row => row.SourceKind == FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment
                && row.SourceId == invoiceId)
            .OrderBy(row => row.DueOn).ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
        if (items.Count == 0)
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                new ValidationIssue("originalInvoiceId", NotAdmittedCode));
        }
        if (items.Any(row => row.Status == FinanceOpenItemStatuses.Cancelled))
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                new ValidationIssue("originalInvoiceId", InvoiceCancelledCode));
        }
        if (items.Any(row => row.SourceVersion != acceptance.OriginalInvoiceVersion))
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                new ValidationIssue("originalInvoiceVersion", VersionMismatchCode));
        }
        if (Mismatch(items, acceptance) is { } mismatch)
        {
            return Refused(context, CommandOutcome.ValidationFailed, mismatch);
        }

        var plan = Plan(items, acceptance.Amount);
        if (plan.Issue is { } planIssue)
        {
            return Refused(context, CommandOutcome.ValidationFailed, planIssue);
        }

        // The returned total of the invoice stays within its original total; the aggregate's version serialises returns.
        var state = await dbContext.CustomerReturnInvoiceStates
            .SingleOrDefaultAsync(row => row.OriginalInvoiceId == invoiceId, cancellationToken);
        var returnedBefore = default(FinanceExactDecimalText);
        if (state is not null && !FinanceExactDecimalText.TryParseNonNegative(state.ReturnedAmountText, out returnedBefore))
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                new ValidationIssue("originalInvoiceId", StoredStateNotUnderstoodCode));
        }
        var returnedAfter = returnedBefore.Add(plan.Amount);
        if (returnedAfter.CompareTo(plan.InvoiceTotal) > 0)
        {
            return Refused(context, CommandOutcome.ValidationFailed, new ValidationIssue("amount", ExceedsInvoiceCode));
        }

        var now = timeProvider.GetUtcNow();
        foreach (var (item, after, reduced) in plan.Reductions)
        {
            dbContext.CustomerReturnCreditItems.Add(new FinanceCustomerReturnCreditItemRow
            {
                CompanyId = context.CompanyId,
                CreditId = acceptance.AcceptanceId,
                OpenItemId = item.Id,
                ReducedAmountText = reduced.Text,
            });
            item.OutstandingAmountText = after.Text;
            item.Status = after.IsZero ? FinanceOpenItemStatuses.Closed : FinanceOpenItemStatuses.Open;
            item.Version = checked(item.Version + 1);
            item.UpdatedBy = context.ActorId;
            item.UpdatedAtUtc = now;
        }

        if (state is null)
        {
            dbContext.CustomerReturnInvoiceStates.Add(new FinanceCustomerReturnInvoiceStateRow
            {
                CompanyId = context.CompanyId,
                OriginalInvoiceId = invoiceId,
                ReturnedAmountText = returnedAfter.Text,
                Version = 1,
                UpdatedBy = context.ActorId,
                UpdatedAtUtc = now,
            });
        }
        else
        {
            state.ReturnedAmountText = returnedAfter.Text;
            state.Version = checked(state.Version + 1);
            state.UpdatedBy = context.ActorId;
            state.UpdatedAtUtc = now;
        }

        var reductions = plan.Reductions
            .Select(row => new FinanceInvoiceAcceptanceReductionV2(row.Item.Id, row.Reduced.Text))
            .ToArray();
        var result = new FinanceInvoiceAcceptanceResultV2(CommandOutcome.Succeeded, acceptance.AcceptanceId, reductions,
            plan.Advance.Text, false, context.CorrelationId, []);
        dbContext.CustomerReturnCredits.Add(new FinanceCustomerReturnCreditRow
        {
            Id = acceptance.AcceptanceId,
            CompanyId = context.CompanyId,
            SourceKind = FinanceCustomerReturnCreditRules.SourceKind,
            DocumentId = acceptance.DocumentId,
            DocumentVersion = acceptance.DocumentVersion,
            OriginalInvoiceId = invoiceId,
            OriginalInvoiceVersion = acceptance.OriginalInvoiceVersion,
            PartyId = acceptance.PartyId,
            CompanyAccountId = acceptance.CompanyAccountId,
            CurrencyId = acceptance.CurrencyId,
            AmountText = plan.Amount.Text,
            AppliedAmountText = plan.Applied.Text,
            AdvanceAmountText = plan.Advance.Text,
            FunctionalAmountText = acceptance.FunctionalAmount,
            BusinessDate = acceptance.BusinessDate,
            IdempotencyKeyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey),
            PayloadHash = payloadHash,
            ActorId = context.ActorId,
            ResultJson = JsonSerializer.Serialize(result, Json),
            CorrelationId = context.CorrelationId,
            CausationId = context.CausationId,
            AcceptedAtUtc = now,
        });
        // The authority is measured again right before the save: a grant withdrawn meanwhile writes nothing
        // (the audit draft is queued only after the fence, so a refusal leaves none behind).
        if (await FenceAsync(context, acceptance, cancellationToken) is { } late)
        {
            dbContext.ChangeTracker.Clear();
            return late;
        }
        Audit(context, metadata, acceptance, plan, reductions);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            dbContext.ChangeTracker.Clear();
            return await PriorAsync(context, metadata, acceptance.AcceptanceId, payloadHash, cancellationToken)
                ?? Refused(context, CommandOutcome.VersionConflict, new ValidationIssue("request", "concurrent_change"));
        }
    }

    /// <summary>The pure shape rules: only a Sales customer return is accepted; identities, tokens and exact amounts.</summary>
    internal static IReadOnlyList<ValidationIssue> Validate(FinanceInvoiceAcceptanceV2 acceptance)
    {
        var issues = new List<ValidationIssue>();
        if (acceptance.Producer is not (FinanceInvoiceProducerV1.Sales or FinanceInvoiceProducerV1.Purchasing))
        {
            issues.Add(new ValidationIssue("producer", "finance_invoice_producer_invalid"));
        }
        else if (acceptance.Producer != FinanceInvoiceProducerV1.Sales
            || acceptance.Kind != FinanceInvoiceAcceptanceKindV2.CustomerReturn)
        {
            // Our own returns to suppliers live in Purchasing (DEC-FIN-003 EK57); version 2 knows only the customer's.
            issues.Add(new ValidationIssue("kind", KindUnsupportedCode));
        }
        if (acceptance.AcceptanceId == Guid.Empty || acceptance.DocumentId == Guid.Empty
            || acceptance.OriginalInvoiceId == Guid.Empty || acceptance.DocumentId == acceptance.OriginalInvoiceId)
        {
            issues.Add(new ValidationIssue("acceptance", "identity_required"));
        }
        if (acceptance.PartyId == Guid.Empty || acceptance.CompanyAccountId == Guid.Empty
            || acceptance.CurrencyId == Guid.Empty)
        {
            issues.Add(new ValidationIssue("party", "identity_required"));
        }
        if (!IsToken(acceptance.DocumentVersion))
        {
            issues.Add(new ValidationIssue("documentVersion", "finance_invoice_version_invalid"));
        }
        if (!IsToken(acceptance.OriginalInvoiceVersion))
        {
            issues.Add(new ValidationIssue("originalInvoiceVersion", "finance_invoice_version_invalid"));
        }
        if (!FinanceExactDecimalText.TryParsePositive(acceptance.Amount, out _))
        {
            issues.Add(new ValidationIssue("amount", "finance_customer_return_amount_invalid"));
        }
        if (acceptance.FunctionalAmount is not null
            && !FinanceExactDecimalText.TryParsePositive(acceptance.FunctionalAmount, out _))
        {
            issues.Add(new ValidationIssue("functionalAmount", "finance_invoice_functional_amount_invalid"));
        }
        if (acceptance.BusinessDate == default || acceptance.BusinessDate == DateOnly.MaxValue)
        {
            issues.Add(new ValidationIssue("businessDate", "finance_customer_return_business_date_invalid"));
        }
        return issues;
    }

    /// <summary>
    /// The split (SAT-D294): the return reduces the items' outstanding first, the rest is the advance. The return covers
    /// every outstanding item in full, or exactly one item is still outstanding; any other partial return is refused
    /// because the installment order is not decided. Stored amounts that do not read back are refused, not guessed.
    /// </summary>
    internal static FinanceCustomerReturnPlan Plan(IReadOnlyList<FinanceOpenItemRow> items, string amountText)
    {
        if (!FinanceExactDecimalText.TryParsePositive(amountText, out var amount))
        {
            return FinanceCustomerReturnPlan.Refused(new ValidationIssue("amount", "finance_customer_return_amount_invalid"));
        }

        var total = default(FinanceExactDecimalText);
        var outstandingTotal = default(FinanceExactDecimalText);
        var outstanding = new List<(FinanceOpenItemRow Item, FinanceExactDecimalText Outstanding)>();
        foreach (var item in items)
        {
            if (item.Direction != FinanceOpenItemDirectionCodes.Receivable
                || item.Status is not (FinanceOpenItemStatuses.Open or FinanceOpenItemStatuses.Closed)
                || !FinanceExactDecimalText.TryParsePositive(item.OriginalAmountText, out var original)
                || !FinanceExactDecimalText.TryParseNonNegative(item.OutstandingAmountText, out var left)
                || left.CompareTo(original) > 0
                || (item.Status == FinanceOpenItemStatuses.Closed) != left.IsZero)
            {
                return FinanceCustomerReturnPlan.Refused(
                    new ValidationIssue("originalInvoiceId", StoredStateNotUnderstoodCode));
            }
            total = total.Add(original);
            outstandingTotal = outstandingTotal.Add(left);
            if (!left.IsZero)
            {
                outstanding.Add((item, left));
            }
        }

        if (amount.CompareTo(outstandingTotal) >= 0)
        {
            var reductions = outstanding
                .Select(row => (row.Item, After: default(FinanceExactDecimalText), Reduced: row.Outstanding))
                .ToArray();
            return new FinanceCustomerReturnPlan(null, amount, total, outstandingTotal,
                amount.Subtract(outstandingTotal), reductions);
        }
        if (outstanding.Count == 1)
        {
            var (item, left) = outstanding[0];
            return new FinanceCustomerReturnPlan(null, amount, total, amount, default,
                [(item, left.Subtract(amount), amount)]);
        }

        return FinanceCustomerReturnPlan.Refused(new ValidationIssue("amount", InstallmentOrderNotDecidedCode));
    }

    /// <summary>The report must name the invoice's own party, account and currency, and its TL amount exactly when foreign.</summary>
    internal static ValidationIssue? Mismatch(IReadOnlyList<FinanceOpenItemRow> items, FinanceInvoiceAcceptanceV2 acceptance)
    {
        if (items.Any(row => row.PartyId != acceptance.PartyId))
        {
            return new ValidationIssue("partyId", PartyMismatchCode);
        }
        if (items.Any(row => row.CompanyAccountId != acceptance.CompanyAccountId))
        {
            return new ValidationIssue("companyAccountId", AccountMismatchCode);
        }
        if (items.Any(row => row.CurrencyId != acceptance.CurrencyId))
        {
            return new ValidationIssue("currencyId", CurrencyMismatchCode);
        }
        var foreign = items.Select(row => row.ExchangeRateText is not null).Distinct().ToArray();
        if (foreign.Length != 1)
        {
            return new ValidationIssue("originalInvoiceId", StoredStateNotUnderstoodCode);
        }
        return foreign[0] != (acceptance.FunctionalAmount is not null)
            ? new ValidationIssue("functionalAmount", FunctionalAmountMismatchCode)
            : null;
    }

    private async Task<FinanceInvoiceAcceptanceResultV2?> FenceAsync(FinanceContext context,
        FinanceInvoiceAcceptanceV2 acceptance, CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return Refused(context, CommandOutcome.Denied);
        }

        var decision = await authority.CheckAsync(context, acceptance.Producer, acceptance.Kind, acceptance.DocumentId,
            cancellationToken);
        if (decision != PermissionDecision.Allowed)
        {
            return decision == PermissionDecision.Denied
                ? Refused(context, CommandOutcome.Denied)
                : Refused(context, CommandOutcome.Unavailable,
                    new ValidationIssue("permission", "finance_invoice_acceptance_authority_unavailable"));
        }

        return SameContext(context) ? null : Refused(context, CommandOutcome.Denied);
    }

    /// <summary>
    /// The stored answer of this acceptance, found by key (ADR-011: same key, payload and actor replay; otherwise a
    /// conflict) or by <see cref="FinanceInvoiceAcceptanceV2.AcceptanceId"/>: a resend under a new key, e.g. by another
    /// authorised user (SAT-D208), replays when its payload is the same and is refused when it is not.
    /// </summary>
    private async Task<FinanceInvoiceAcceptanceResultV2?> PriorAsync(FinanceContext context, CommandMetadata metadata,
        Guid acceptanceId, string payloadHash, CancellationToken cancellationToken)
    {
        var keyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var prior = await dbContext.CustomerReturnCredits.AsNoTracking()
            .SingleOrDefaultAsync(row => row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (prior is not null)
        {
            return prior.PayloadHash != payloadHash || prior.ActorId != context.ActorId
                ? Refused(context, CommandOutcome.IdempotencyConflict)
                : Stored(prior);
        }

        prior = await dbContext.CustomerReturnCredits.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == acceptanceId, cancellationToken);
        if (prior is null)
        {
            return null;
        }

        return prior.PayloadHash != payloadHash
            ? Refused(context, CommandOutcome.ValidationFailed,
                new ValidationIssue("acceptanceId", AcceptanceIdentityConflictCode))
            : Stored(prior);
    }

    private static FinanceInvoiceAcceptanceResultV2 Stored(FinanceCustomerReturnCreditRow prior) =>
        (JsonSerializer.Deserialize<FinanceInvoiceAcceptanceResultV2>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Stored customer return acceptance result cannot be read."))
        with { IsReplay = true };

    private void Audit(FinanceContext context, CommandMetadata metadata, FinanceInvoiceAcceptanceV2 acceptance,
        FinanceCustomerReturnPlan plan, FinanceInvoiceAcceptanceReductionV2[] reductions) =>
        auditEvents.Enqueue(new AuditEventDraft(
            context.CompanyId, "finance.customer-return-credits.accepted.v2", EntityType,
            acceptance.AcceptanceId.ToString("D"), "Accept", null, 1, AuditActorTypes.User, context.ActorId,
            envelope.PermissionCode, envelope.RoleCodes, "{}",
            JsonSerializer.Serialize(new
            {
                Producer = acceptance.Producer.ToString(),
                Kind = acceptance.Kind.ToString(),
                acceptance.DocumentId,
                acceptance.DocumentVersion,
                acceptance.OriginalInvoiceId,
                acceptance.OriginalInvoiceVersion,
                acceptance.CurrencyId,
                Amount = plan.Amount.Text,
                AppliedAmount = plan.Applied.Text,
                AdvanceAmount = plan.Advance.Text,
                acceptance.FunctionalAmount,
                acceptance.BusinessDate,
                Reductions = reductions,
            }, Json),
            "Finalised customer return invoice accepted as a reversing credit",
            ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null,
            envelope.SourceChannel, envelope.ClientIpAddress, envelope.UserAgent,
            context.CorrelationId, context.CausationId, metadata.IdempotencyKey));

    private static string PayloadHash(FinanceInvoiceAcceptanceV2 acceptance) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            CommandName + "|" + JsonSerializer.Serialize(acceptance, Json)))).ToLowerInvariant();

    private static FinanceInvoiceAcceptanceResultV2 Refused(FinanceContext context, CommandOutcome outcome,
        params ValidationIssue[] issues) =>
        new(outcome, null, [], null, false, context.CorrelationId, issues);

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

/// <summary>
/// The computed split of one return: <see cref="Applied"/> reduced the listed items, <see cref="Advance"/> is the rest;
/// <see cref="InvoiceTotal"/> is the invoice's original total. When <see cref="Issue"/> is set nothing is written.
/// </summary>
internal sealed record FinanceCustomerReturnPlan(
    ValidationIssue? Issue,
    FinanceExactDecimalText Amount,
    FinanceExactDecimalText InvoiceTotal,
    FinanceExactDecimalText Applied,
    FinanceExactDecimalText Advance,
    IReadOnlyList<(FinanceOpenItemRow Item, FinanceExactDecimalText After, FinanceExactDecimalText Reduced)> Reductions)
{
    public static FinanceCustomerReturnPlan Refused(ValidationIssue issue) =>
        new(issue, default, default, default, default, []);
}
