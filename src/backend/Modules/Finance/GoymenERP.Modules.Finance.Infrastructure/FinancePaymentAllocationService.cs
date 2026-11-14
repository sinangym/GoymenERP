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
/// F3a / API-20 (DEC-FIN-006): a recorded manual bank payment applied to an open payable item, and its explicit
/// release. The receipt allocation's skeleton: the allocation permission fences before the first read and right before
/// the save, one save writes the allocation, the payment's balance, the item, the command record and the audit, and
/// every state refusal is a recorded terminal answer that the same key replays. The command record names the PAYMENT
/// as its target (<see cref="FinanceCommandRow.ManualBankPaymentId"/>), so <c>finance.Commands</c> keeps its schema and
/// its exactly-one-target rule; the allocation's own id is in the stored result and in the audit.
/// No money event and no ledger row are written: the payment's outgoing money event stays the only fact.
/// </summary>
public sealed class FinancePaymentAllocationService(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinancePermissionPort permissions,
    IAuditEventCollector auditEvents,
    FinanceAuditEnvelope envelope,
    TimeProvider timeProvider) : IFinancePaymentAllocationService
{
    internal const string CreateCommandName = "finance.payment-allocation.create.v1";
    internal const string ReleaseCommandName = "finance.payment-allocation.release.v1";
    internal const string CreatedEventType = "finance.payment-allocation.created.v1";
    internal const string ReleasedEventType = "finance.payment-allocation.released.v1";
    private const int IdentityMaximumLength = 128;
    private const string EntityType = "Finance.PaymentAllocation";
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    public async Task<FinancePaymentAllocationCommandResult> CreateAsync(
        FinanceContext context,
        CommandMetadata metadata,
        CreateFinancePaymentAllocationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await StartAsync(context, metadata, request.AllocationId, cancellationToken) is { } refused)
        {
            return refused;
        }
        var payloadHash = PayloadHash(CreateCommandName, request);
        if (await PriorAsync(context, metadata, CreateCommandName, payloadHash, cancellationToken) is { } prior)
        {
            return prior;
        }
        var normalizedRequest = Normalize(request);

        var payment = await dbContext.ManualBankPayments.SingleOrDefaultAsync(row => row.Id == request.PaymentId,
            cancellationToken);
        var openItem = await dbContext.OpenItems.SingleOrDefaultAsync(row => row.Id == request.OpenItemId,
            cancellationToken);
        if (payment is null || openItem is null)
        {
            var refusal = Refused(context, CommandOutcome.ValidationFailed,
                Issue(payment is null ? "paymentId" : "openItemId", "finance_allocation_reference_not_found"));
            return await TerminalAsync(context, metadata, CreateCommandName, payloadHash,
                payment?.Id, request.AllocationId, refusal, cancellationToken);
        }
        var paymentState = await dbContext.PaymentAllocationStates.SingleOrDefaultAsync(
            row => row.PaymentId == payment.Id, cancellationToken);

        FinanceAllocationMutation mutation;
        try
        {
            // API-23: an approved reversal closes the payment for good (its allocation state is also set to zero);
            // name the reason instead of reporting a bare amount shortfall.
            if (await dbContext.OutgoingMoneyEventReversals.AsNoTracking()
                    .AnyAsync(row => row.PaymentId == payment.Id, cancellationToken))
            {
                throw new FinanceAllocationPolicyException("paymentId", "finance_source_already_reversed");
            }
            mutation = FinanceAllocationPolicy.CreatePayment(payment, openItem, paymentState, normalizedRequest);
            // The recorded payment's one outgoing money event is the fact being applied: it must exist with the
            // payment's own amount, account and currency, or the payment is not a usable source.
            var moneyEvent = await dbContext.OutgoingMoneyEvents.AsNoTracking()
                .SingleOrDefaultAsync(row => row.PaymentId == payment.Id, cancellationToken);
            if (moneyEvent is null || moneyEvent.CompanyAccountId != payment.CompanyAccountId
                || moneyEvent.CurrencyId != payment.CurrencyId
                || !FinanceExactDecimalText.Equal(moneyEvent.AmountText, payment.AmountText))
            {
                throw new FinanceAllocationPolicyException("paymentId", "finance_allocation_reference_invalid");
            }
        }
        catch (FinanceAllocationPolicyException exception)
        {
            return await TerminalAsync(context, metadata, CreateCommandName, payloadHash,
                payment.Id, request.AllocationId, PolicyRefusal(context, exception), cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        if (paymentState is null)
        {
            paymentState = new FinancePaymentAllocationStateRow
            {
                CompanyId = context.CompanyId,
                PaymentId = payment.Id,
                CurrencyId = payment.CurrencyId,
                OriginalAmountText = payment.AmountText,
                UnallocatedAmountText = mutation.SourceUnallocated,
                Version = 1,
                UpdatedBy = context.ActorId,
                UpdatedAtUtc = now,
            };
            dbContext.PaymentAllocationStates.Add(paymentState);
        }
        else
        {
            paymentState.UnallocatedAmountText = mutation.SourceUnallocated;
            paymentState.Version = checked(paymentState.Version + 1);
            paymentState.UpdatedBy = context.ActorId;
            paymentState.UpdatedAtUtc = now;
        }
        openItem.OutstandingAmountText = mutation.OpenItemOutstanding;
        openItem.Status = mutation.OpenItemStatus;
        openItem.Version = checked(openItem.Version + 1);
        openItem.UpdatedBy = context.ActorId;
        openItem.UpdatedAtUtc = now;

        var allocation = new FinancePaymentAllocationRow
        {
            Id = request.AllocationId,
            CompanyId = context.CompanyId,
            PaymentId = payment.Id,
            OpenItemId = openItem.Id,
            CurrencyId = payment.CurrencyId,
            AmountText = normalizedRequest.Amount,
            Reason = normalizedRequest.Reason,
            Status = FinanceAllocationStatuses.Active,
            Version = 1,
            CreatedBy = context.ActorId,
            CreatedAtUtc = now,
        };
        dbContext.PaymentAllocations.Add(allocation);
        var result = Succeeded(context, allocation);
        AddCommand(context, metadata, CreateCommandName, payloadHash, payment.Id, result, now);
        return await SaveAsync(context, metadata, CreateCommandName, payloadHash,
            allocation.Id, result, cancellationToken,
            () => Audit(context, metadata, allocation, CreatedEventType, "Create", null, 1,
            "{}", AuditJson(allocation), "Recorded payment allocated to a payable; no money event created"));
    }

    public async Task<FinancePaymentAllocationCommandResult> ReleaseAsync(
        FinanceContext context,
        CommandMetadata metadata,
        ReleaseFinanceAllocationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await StartAsync(context, metadata, request.AllocationId, cancellationToken) is { } refused)
        {
            return refused;
        }
        var payloadHash = PayloadHash(ReleaseCommandName, request);
        if (await PriorAsync(context, metadata, ReleaseCommandName, payloadHash, cancellationToken) is { } prior)
        {
            return prior;
        }
        var normalizedRequest = Normalize(request);

        var allocation = await dbContext.PaymentAllocations.SingleOrDefaultAsync(
            row => row.Id == request.AllocationId, cancellationToken);
        if (allocation is null)
        {
            var refusal = Refused(context, CommandOutcome.ValidationFailed,
                Issue("allocationId", "finance_allocation_not_found"));
            return await TerminalAsync(context, metadata, ReleaseCommandName, payloadHash,
                null, request.AllocationId, refusal, cancellationToken);
        }
        var openItem = await dbContext.OpenItems.SingleOrDefaultAsync(row => row.Id == allocation.OpenItemId,
            cancellationToken) ?? throw new InvalidOperationException("Stored payment allocation open item is missing.");
        var paymentState = await dbContext.PaymentAllocationStates.SingleOrDefaultAsync(
            row => row.PaymentId == allocation.PaymentId, cancellationToken)
            ?? throw new InvalidOperationException("Stored payment allocation state is missing.");

        FinanceAllocationMutation mutation;
        try
        {
            mutation = FinanceAllocationPolicy.ReleasePayment(allocation, openItem, paymentState, normalizedRequest);
        }
        catch (FinanceAllocationPolicyException exception)
        {
            return await TerminalAsync(context, metadata, ReleaseCommandName, payloadHash,
                allocation.PaymentId, allocation.Id, PolicyRefusal(context, exception), cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        var before = AuditJson(allocation);
        allocation.Status = FinanceAllocationStatuses.Released;
        allocation.Version = checked(allocation.Version + 1);
        allocation.ReleasedBy = context.ActorId;
        allocation.ReleasedAtUtc = now;
        allocation.ReleaseReason = normalizedRequest.Reason;
        paymentState.UnallocatedAmountText = mutation.SourceUnallocated;
        paymentState.Version = checked(paymentState.Version + 1);
        paymentState.UpdatedBy = context.ActorId;
        paymentState.UpdatedAtUtc = now;
        openItem.OutstandingAmountText = mutation.OpenItemOutstanding;
        openItem.Status = mutation.OpenItemStatus;
        openItem.Version = checked(openItem.Version + 1);
        openItem.UpdatedBy = context.ActorId;
        openItem.UpdatedAtUtc = now;

        var result = Succeeded(context, allocation);
        AddCommand(context, metadata, ReleaseCommandName, payloadHash, allocation.PaymentId, result, now);
        return await SaveAsync(context, metadata, ReleaseCommandName, payloadHash,
            allocation.Id, result, cancellationToken,
            () => Audit(context, metadata, allocation, ReleasedEventType, "Release", 1, allocation.Version,
            before, AuditJson(allocation), normalizedRequest.Reason));
    }

    private async Task<FinancePaymentAllocationCommandResult?> StartAsync(
        FinanceContext context, CommandMetadata metadata, Guid targetId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        if (await FenceAsync(context, targetId, cancellationToken) is { } refused)
        {
            return refused;
        }
        if (targetId == Guid.Empty || string.IsNullOrWhiteSpace(metadata.IdempotencyKey)
            || !string.Equals(metadata.IdempotencyKey, metadata.IdempotencyKey.Trim(), StringComparison.Ordinal)
            || metadata.IdempotencyKey.Length > IdentityMaximumLength
            || context.ActorId.Length > IdentityMaximumLength || context.CorrelationId.Length > IdentityMaximumLength)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("request", "identity_required"));
        }
        return null;
    }

    private async Task<FinancePaymentAllocationCommandResult?> PriorAsync(
        FinanceContext context, CommandMetadata metadata, string command, string payloadHash,
        CancellationToken cancellationToken)
    {
        var prior = await dbContext.Commands.AsNoTracking().SingleOrDefaultAsync(
            row => row.CommandName == command
                && row.IdempotencyKeyHash == FinanceCommandKeys.Hash(metadata.IdempotencyKey), cancellationToken);
        if (prior is null)
        {
            return null;
        }
        if (prior.PayloadHash != payloadHash || !string.Equals(prior.ActorId, context.ActorId, StringComparison.Ordinal))
        {
            return Refused(context, CommandOutcome.IdempotencyConflict);
        }
        if (await FenceAsync(context, prior.ManualBankPaymentId, cancellationToken) is { } fenced)
        {
            return fenced;
        }
        var stored = JsonSerializer.Deserialize<FinancePaymentAllocationCommandResult>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Stored payment allocation command result cannot be read.");
        return stored with { IsReplay = true };
    }

    private async Task<FinancePaymentAllocationCommandResult?> FenceAsync(
        FinanceContext context, Guid? targetId, CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return Refused(context, CommandOutcome.Denied);
        }
        var decision = await permissions.CheckAsync(context, FinanceAction.WriteAllocation, targetId, cancellationToken);
        if (decision != PermissionDecision.Allowed)
        {
            return decision == PermissionDecision.Denied
                ? Refused(context, CommandOutcome.Denied)
                : Refused(context, CommandOutcome.Unavailable,
                    Issue("permission", "finance_permission_mapping_unavailable"));
        }
        return SameContext(context) ? null : Refused(context, CommandOutcome.Denied);
    }

    private async Task<FinancePaymentAllocationCommandResult> SaveAsync(
        FinanceContext context, CommandMetadata metadata, string command, string payloadHash,
        Guid? permissionTargetId, FinancePaymentAllocationCommandResult result, CancellationToken cancellationToken,
        Action? audit = null)
    {
        if (await FenceAsync(context, permissionTargetId, cancellationToken) is { } fenced)
        {
            dbContext.ChangeTracker.Clear();
            return fenced;
        }
        audit?.Invoke();
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            dbContext.ChangeTracker.Clear();
            return await PriorAsync(context, metadata, command, payloadHash, cancellationToken)
                ?? Refused(context, CommandOutcome.VersionConflict, Issue("request", "concurrent_change"));
        }
        return result;
    }

    private void AddCommand(
        FinanceContext context, CommandMetadata metadata, string command, string payloadHash,
        Guid? paymentId, FinancePaymentAllocationCommandResult result, DateTimeOffset now) =>
        dbContext.Commands.Add(new FinanceCommandRow
        {
            CompanyId = context.CompanyId,
            CommandName = command,
            IdempotencyKeyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey),
            ActorId = context.ActorId,
            PayloadHash = payloadHash,
            ManualBankPaymentId = paymentId,
            ResultJson = JsonSerializer.Serialize(result, Json),
            CorrelationId = context.CorrelationId,
            RecordedAtUtc = now,
        });

    private Task<FinancePaymentAllocationCommandResult> TerminalAsync(
        FinanceContext context, CommandMetadata metadata, string command, string payloadHash,
        Guid? paymentId, Guid? permissionTargetId,
        FinancePaymentAllocationCommandResult refusal, CancellationToken cancellationToken)
    {
        AddCommand(context, metadata, command, payloadHash, paymentId, refusal, timeProvider.GetUtcNow());
        return SaveAsync(context, metadata, command, payloadHash, permissionTargetId, refusal, cancellationToken);
    }

    private void Audit(
        FinanceContext context, CommandMetadata metadata, FinancePaymentAllocationRow allocation,
        string eventType, string operation, long? beforeVersion, long? afterVersion,
        string before, string after, string reason) =>
        auditEvents.Enqueue(new AuditEventDraft(
            context.CompanyId, eventType, EntityType, allocation.Id.ToString("D"), operation,
            beforeVersion, afterVersion, AuditActorTypes.User, context.ActorId,
            envelope.PermissionCode, envelope.RoleCodes, before, after, reason,
            ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null,
            envelope.SourceChannel, envelope.ClientIpAddress, envelope.UserAgent,
            context.CorrelationId, context.CausationId, metadata.IdempotencyKey));

    private bool SameContext(FinanceContext context) =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
        && companyContext.CompanyId == context.CompanyId && currentUser.CanAccessCompany(context.CompanyId);

    private static string PayloadHash<T>(string command, T request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command + "|" + JsonSerializer.Serialize(request, Json))))
            .ToLowerInvariant();

    private static CreateFinancePaymentAllocationRequest Normalize(CreateFinancePaymentAllocationRequest request) =>
        request with
        {
            Amount = FinanceExactDecimalText.TryParsePositive(request.Amount, out var amount)
                ? amount.Text
                : request.Amount,
            Reason = NormalizeReason(request.Reason),
        };

    private static ReleaseFinanceAllocationRequest Normalize(ReleaseFinanceAllocationRequest request) => request with
    {
        Reason = NormalizeReason(request.Reason),
    };

    private static string NormalizeReason(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 1000 ? value.Trim() : value;

    private static FinancePaymentAllocationCommandResult PolicyRefusal(
        FinanceContext context, FinanceAllocationPolicyException exception) =>
        Refused(context,
            exception.Code == "version_conflict" ? CommandOutcome.VersionConflict : CommandOutcome.ValidationFailed,
            Issue(exception.Field, exception.Code));

    private static FinancePaymentAllocationCommandResult Succeeded(
        FinanceContext context, FinancePaymentAllocationRow row) =>
        new FinancePaymentAllocationCommandResult(CommandOutcome.Succeeded, View(row), false, context.CorrelationId, []);

    internal static FinancePaymentAllocationView View(FinancePaymentAllocationRow row) => new FinancePaymentAllocationView(
        row.Id, row.CompanyId, row.PaymentId, row.OpenItemId, row.CurrencyId, row.AmountText,
        (FinanceAllocationStatus)row.Status, row.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
        row.Reason, row.CreatedBy, row.CreatedAtUtc, row.ReleasedBy, row.ReleasedAtUtc, row.ReleaseReason);

    private static string AuditJson(FinancePaymentAllocationRow row) => JsonSerializer.Serialize(View(row), Json);

    private static ValidationIssue Issue(string field, string code) => new ValidationIssue(field, code);

    private static FinancePaymentAllocationCommandResult Refused(
        FinanceContext context, CommandOutcome outcome, params ValidationIssue[] issues) =>
        new FinancePaymentAllocationCommandResult(outcome, null, false, context.CorrelationId, issues);
}
