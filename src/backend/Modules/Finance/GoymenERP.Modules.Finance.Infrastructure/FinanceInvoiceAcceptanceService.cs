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
/// A partial return reduces exactly the installments the user selected (FNS-D44), validated to the last digit against the
/// items' open amounts and the return amount; without a selection it is refused unless the invoice has exactly one
/// outstanding installment (FNS-D81: the user chooses, no automatic order). Both splits and the screen's prefill (<see cref="IFinanceCustomerReturnInstallmentReadPortV2"/>) come from
/// <see cref="FinanceCustomerReturnInstallmentCalculator"/>; the submitted selection is never replaced by the prefill.
/// </summary>
public sealed class FinanceInvoiceAcceptanceService(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinanceInvoiceAcceptanceAuthorityV2 authority,
    IAuditEventCollector auditEvents,
    FinanceAuditEnvelope envelope,
    TimeProvider timeProvider) : IFinanceInvoiceAcceptancePortV2, IFinanceCustomerReturnInstallmentReadPortV2
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
    /// <summary>A selection's shape is not readable: an empty list, a blank id, a repeated item or an amount that is not
    /// positive exact text at the return amount's scale (FNS-D44).</summary>
    internal const string AllocationInvalidCode = "finance_customer_return_allocation_invalid";
    /// <summary>A selected item is not an outstanding item of the original invoice (FNS-D44).</summary>
    internal const string AllocationItemUnknownCode = "finance_customer_return_allocation_item_unknown";
    /// <summary>A selected part is above its installment's open amount (FNS-D44).</summary>
    internal const string AllocationExceedsOpenAmountCode = "finance_customer_return_allocation_exceeds_open_amount";
    /// <summary>The selection does not total the return amount exactly (FNS-D44).</summary>
    internal const string AllocationTotalMismatchCode = "finance_customer_return_allocation_total_mismatch";
    /// <summary>The return covers the whole outstanding amount: every outstanding item closes and no selection applies.</summary>
    internal const string AllocationNotApplicableCode = "finance_customer_return_allocation_not_applicable";
    /// <summary>
    /// A partial return over several outstanding installments arrived without the user's selection (FNS-D81): the user
    /// chooses the installment; no automatic order is applied.
    /// </summary>
    internal const string AllocationRequiredCode = "finance_customer_return_allocation_required";
    /// <summary>Stored amounts or item state the acceptance cannot read back safely: nothing is written.</summary>
    internal const string StoredStateNotUnderstoodCode = "finance_customer_return_state_not_understood";
    /// <summary>
    /// FNS-D115 reduces the original account's remaining part first and then the successor's assignment (KLN-D71). An
    /// item whose balance is held by more than one successor assignment has no decided order between them and is
    /// refused. Nothing is written.
    /// </summary>
    internal const string TransferredBalanceSplitCode = "finance_customer_return_transferred_balance_split";
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
        var items = await InvoiceItemsAsync(invoiceId, tracked: true, cancellationToken);
        if (Admission(items, acceptance.OriginalInvoiceVersion) is { } admission)
        {
            return Refused(context, CommandOutcome.ValidationFailed, admission);
        }
        if (Mismatch(items, acceptance) is { } mismatch)
        {
            return Refused(context, CommandOutcome.ValidationFailed, mismatch);
        }

        var plan = Plan(items, acceptance.Amount, acceptance.Allocations);
        if (plan.Issue is { } planIssue)
        {
            return Refused(context, CommandOutcome.ValidationFailed, planIssue);
        }

        // The returned total of the invoice stays within its original total; the aggregate's version serialises returns.
        var state = await dbContext.CustomerReturnInvoiceStates
            .SingleOrDefaultAsync(row => row.OriginalInvoiceId == invoiceId, cancellationToken);
        if (!TryReturnedBefore(state, out var returnedBefore))
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                new ValidationIssue("originalInvoiceId", StoredStateNotUnderstoodCode));
        }
        var returnedAfter = returnedBefore.Add(plan.Amount);
        if (returnedAfter.CompareTo(plan.InvoiceTotal) > 0)
        {
            return Refused(context, CommandOutcome.ValidationFailed, new ValidationIssue("amount", ExceedsInvoiceCode));
        }

        var transferred = await TransferredReductionsAsync(plan, cancellationToken);
        if (transferred.Issue is { } transferIssue)
        {
            return Refused(context, CommandOutcome.ValidationFailed, transferIssue);
        }

        var now = timeProvider.GetUtcNow();
        foreach (var (assignment, reduced) in transferred.Reductions)
        {
            assignment.AvailableAmountText = FinanceAllocationPositions.Amount(assignment.AvailableAmountText)
                .Subtract(reduced).Text;
            assignment.Version = checked(assignment.Version + 1);
        }

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

    /// <summary>
    /// FNS-D115: the return's reduction of an item whose balance was partly transferred (KLN-D71) takes the original
    /// account's remaining part first and only the rest from the successor's assignment, so the item's outstanding amount
    /// never falls below the part assigned to a successor (the allocation positions keep reading). The advance stays with
    /// the original party on the credit itself. Both parts change in the acceptance's single save; no second movement is
    /// written. An item with more than one live successor assignment, when the rest is not zero, is refused
    /// (<see cref="TransferredBalanceSplitCode"/>); stored assignments that do not read back are refused as not understood.
    /// </summary>
    private async Task<(IReadOnlyList<(FinancePartyBalanceAssignmentRow Assignment, FinanceExactDecimalText Reduced)>
        Reductions, ValidationIssue? Issue)> TransferredReductionsAsync(FinanceCustomerReturnPlan plan,
        CancellationToken cancellationToken)
    {
        var ids = plan.Reductions.Select(row => row.Item.Id).ToArray();
        var assignments = await dbContext.PartyBalanceAssignments
            .Where(row => row.Kind == FinanceBalanceAssignmentKinds.OpenItem && ids.Contains(row.SourceId))
            .ToListAsync(cancellationToken);
        return TransferredReductions(plan, assignments);
    }

    internal static (IReadOnlyList<(FinancePartyBalanceAssignmentRow Assignment, FinanceExactDecimalText Reduced)>
        Reductions, ValidationIssue? Issue) TransferredReductions(FinanceCustomerReturnPlan plan,
        IReadOnlyList<FinancePartyBalanceAssignmentRow> assignments)
    {
        var result = new List<(FinancePartyBalanceAssignmentRow, FinanceExactDecimalText)>();
        var bySource = assignments.ToLookup(row => row.SourceId);
        foreach (var (item, _, reduced) in plan.Reductions)
        {
            var rows = bySource[item.Id].ToArray();
            if (rows.Length == 0)
            {
                continue;
            }

            IReadOnlyList<FinanceAllocationPosition> positions;
            try
            {
                positions = new FinanceAllocationPositions(item.PartyId, item.CompanyAccountId, item.CurrencyId,
                    item.OutstandingAmountText, rows).All();
            }
            catch (InvalidOperationException)
            {
                return ([], new ValidationIssue("originalInvoiceId", StoredStateNotUnderstoodCode));
            }

            var live = rows.Where(row => !FinanceAllocationPositions.Amount(row.AvailableAmountText).IsZero).ToArray();
            if (live.Length == 0)
            {
                continue;
            }
            var originalAvailable = FinanceAllocationPositions.Amount(positions[0].AvailableAmount);
            if (reduced.CompareTo(originalAvailable) <= 0)
            {
                continue;
            }
            var rest = reduced.Subtract(originalAvailable);
            if (live.Length != 1)
            {
                return ([], new ValidationIssue("allocations", TransferredBalanceSplitCode));
            }
            result.Add((live[0], rest));
        }
        return (result, null);
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
        if (acceptance.Allocations is { } allocations
            && (allocations.Count == 0
                || allocations.Any(row => row is null || row.OpenItemId == Guid.Empty
                    || !FinanceExactDecimalText.TryParsePositive(row.Amount, out _))
                || allocations.Select(row => row?.OpenItemId).Distinct().Count() != allocations.Count))
        {
            issues.Add(new ValidationIssue("allocations", AllocationInvalidCode));
        }
        return issues;
    }

    /// <summary>
    /// FNS-D44 prefill read: the outstanding installments of the admitted original invoice, nearest due first, with the
    /// calculator's part of <see cref="FinanceCustomerReturnInstallmentQueryV2.Amount"/> on each. Writes nothing; fenced like
    /// the acceptance; the same refusal codes as the acceptance (not admitted, cancelled, another version, stored state not
    /// understood, a total of returns above the invoice).
    /// </summary>
    public async Task<FinanceCustomerReturnInstallmentPrefillV2> ReadAsync(
        FinanceContext context,
        FinanceCustomerReturnInstallmentQueryV2 query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameContext(context))
        {
            return PrefillRefused(CommandOutcome.Denied);
        }

        var issues = ValidateQuery(query);
        if (issues.Count > 0)
        {
            return PrefillRefused(CommandOutcome.ValidationFailed, [.. issues]);
        }

        if (await FenceCoreAsync(context, query.Producer, query.Kind, query.DocumentId, cancellationToken) is { } fenced)
        {
            return PrefillRefused(fenced.Outcome, [.. fenced.Issues]);
        }

        var invoiceId = query.OriginalInvoiceId;
        var items = await InvoiceItemsAsync(invoiceId, tracked: false, cancellationToken);
        if (Admission(items, query.OriginalInvoiceVersion) is { } admission)
        {
            return PrefillRefused(CommandOutcome.ValidationFailed, admission);
        }

        var prefill = FinanceCustomerReturnInstallmentCalculator.Prefill(items, query.Amount);
        if (prefill.Issue is { } prefillIssue)
        {
            return PrefillRefused(CommandOutcome.ValidationFailed, prefillIssue);
        }

        var state = await dbContext.CustomerReturnInvoiceStates.AsNoTracking()
            .SingleOrDefaultAsync(row => row.OriginalInvoiceId == invoiceId, cancellationToken);
        if (!TryReturnedBefore(state, out var returnedBefore))
        {
            return PrefillRefused(CommandOutcome.ValidationFailed,
                new ValidationIssue("originalInvoiceId", StoredStateNotUnderstoodCode));
        }
        if (returnedBefore.Add(prefill.Amount).CompareTo(prefill.InvoiceTotal) > 0)
        {
            return PrefillRefused(CommandOutcome.ValidationFailed, new ValidationIssue("amount", ExceedsInvoiceCode));
        }

        var installments = prefill.Lines
            .Select(line => new FinanceCustomerReturnInstallmentV2(line.Item.Id, line.DueOn, line.Original.Text,
                line.Outstanding.Text, line.Prefill.Text))
            .ToArray();
        return new FinanceCustomerReturnInstallmentPrefillV2(CommandOutcome.Succeeded, installments,
            prefill.SelectionAllowed, prefill.Applied.Text, prefill.Advance.Text, []);
    }

    /// <summary>The prefill question's shape: the same producer, kind, identity, version and amount rules as an acceptance.</summary>
    internal static IReadOnlyList<ValidationIssue> ValidateQuery(FinanceCustomerReturnInstallmentQueryV2 query)
    {
        var issues = new List<ValidationIssue>();
        if (query.Producer is not (FinanceInvoiceProducerV1.Sales or FinanceInvoiceProducerV1.Purchasing))
        {
            issues.Add(new ValidationIssue("producer", "finance_invoice_producer_invalid"));
        }
        else if (query.Producer != FinanceInvoiceProducerV1.Sales || query.Kind != FinanceInvoiceAcceptanceKindV2.CustomerReturn)
        {
            issues.Add(new ValidationIssue("kind", KindUnsupportedCode));
        }
        if (query.DocumentId == Guid.Empty || query.OriginalInvoiceId == Guid.Empty
            || query.DocumentId == query.OriginalInvoiceId)
        {
            issues.Add(new ValidationIssue("acceptance", "identity_required"));
        }
        if (!IsToken(query.OriginalInvoiceVersion))
        {
            issues.Add(new ValidationIssue("originalInvoiceVersion", "finance_invoice_version_invalid"));
        }
        if (!FinanceExactDecimalText.TryParsePositive(query.Amount, out _))
        {
            issues.Add(new ValidationIssue("amount", "finance_customer_return_amount_invalid"));
        }
        return issues;
    }

    /// <summary>
    /// The split (SAT-D294, FNS-D44, FNS-D81): the return reduces the items' outstanding first, the rest is the advance; a
    /// partial return follows the user's selection exactly, and without one only the single outstanding installment. See
    /// <see cref="FinanceCustomerReturnInstallmentCalculator"/>.
    /// </summary>
    internal static FinanceCustomerReturnPlan Plan(IReadOnlyList<FinanceOpenItemRow> items, string amountText,
        IReadOnlyList<FinanceInvoiceAcceptanceAllocationV2>? allocations = null) =>
        FinanceCustomerReturnInstallmentCalculator.Plan(items, amountText, allocations);

    /// <summary>The original invoice must be admitted, not cancelled, and at the expected version.</summary>
    internal static ValidationIssue? Admission(IReadOnlyList<FinanceOpenItemRow> items, string expectedVersion)
    {
        if (items.Count == 0)
        {
            return new ValidationIssue("originalInvoiceId", NotAdmittedCode);
        }
        if (items.Any(row => row.Status == FinanceOpenItemStatuses.Cancelled))
        {
            return new ValidationIssue("originalInvoiceId", InvoiceCancelledCode);
        }
        return items.Any(row => row.SourceVersion != expectedVersion)
            ? new ValidationIssue("originalInvoiceVersion", VersionMismatchCode)
            : null;
    }

    private async Task<List<FinanceOpenItemRow>> InvoiceItemsAsync(Guid invoiceId, bool tracked,
        CancellationToken cancellationToken)
    {
        var query = dbContext.OpenItems
            .Where(row => row.SourceKind == FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment
                && row.SourceId == invoiceId);
        return await (tracked ? query : query.AsNoTracking())
            .OrderBy(row => row.DueOn).ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
    }

    private static bool TryReturnedBefore(FinanceCustomerReturnInvoiceStateRow? state,
        out FinanceExactDecimalText returnedBefore)
    {
        returnedBefore = default;
        return state is null || FinanceExactDecimalText.TryParseNonNegative(state.ReturnedAmountText, out returnedBefore);
    }

    private static FinanceCustomerReturnInstallmentPrefillV2 PrefillRefused(CommandOutcome outcome,
        params ValidationIssue[] issues) =>
        new(outcome, [], false, null, null, issues);

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
        FinanceInvoiceAcceptanceV2 acceptance, CancellationToken cancellationToken) =>
        await FenceCoreAsync(context, acceptance.Producer, acceptance.Kind, acceptance.DocumentId, cancellationToken)
            is { } fenced
            ? Refused(context, fenced.Outcome, [.. fenced.Issues])
            : null;

    /// <summary>The host-owned authority measured for one return document; null when allowed in the same context.</summary>
    private async Task<FinanceFenceRefusal?> FenceCoreAsync(FinanceContext context, FinanceInvoiceProducerV1 producer,
        FinanceInvoiceAcceptanceKindV2 kind, Guid documentId, CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return new FinanceFenceRefusal(CommandOutcome.Denied, []);
        }

        var decision = await authority.CheckAsync(context, producer, kind, documentId, cancellationToken);
        if (decision != PermissionDecision.Allowed)
        {
            return decision == PermissionDecision.Denied
                ? new FinanceFenceRefusal(CommandOutcome.Denied, [])
                : new FinanceFenceRefusal(CommandOutcome.Unavailable,
                    [new ValidationIssue("permission", "finance_invoice_acceptance_authority_unavailable")]);
        }

        return SameContext(context) ? null : new FinanceFenceRefusal(CommandOutcome.Denied, []);
    }

    private sealed record FinanceFenceRefusal(CommandOutcome Outcome, IReadOnlyList<ValidationIssue> Issues);

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
                Basis = plan.Basis.ToString(),
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
