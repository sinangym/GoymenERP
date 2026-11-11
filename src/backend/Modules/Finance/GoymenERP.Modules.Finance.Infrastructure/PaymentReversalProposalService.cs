using System.Globalization;
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
/// API-23 authorized, idempotent maker-checker reversal of one recorded manual bank payment. The payment and its
/// outgoing money event never change; approval appends one <see cref="OutgoingMoneyEventReversalRow"/> and closes
/// the payment's allocation state, in one transaction with the command record and the audit event.
/// </summary>
public sealed class PaymentReversalProposalService(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinancePermissionPort permissions,
    IFinanceRecordPeriodRule periodRule,
    IAuditEventCollector auditEvents,
    FinancePaymentReversalEnvelopes envelopes,
    TimeProvider timeProvider) : IPaymentReversalProposalService
{
    public const string ProposeCommand = "finance.payment-reversal.propose";
    public const string ApproveCommand = "finance.payment-reversal.approve";
    public const string RejectCommand = "finance.payment-reversal.reject";
    private const string EntityType = "Finance.PaymentReversalProposal";
    private const int IdentityMaximumLength = 128;
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();
    private static readonly JsonSerializerOptions AuditJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task<PaymentReversalProposalCommandResult> ProposeAsync(
        FinanceContext context, CommandMetadata metadata, ProposePaymentReversalRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        if (await StartAsync(context, metadata, FinanceAction.ProposePaymentReversal, request.PaymentId,
                cancellationToken) is { } refused)
        {
            return refused;
        }

        var payloadHash = PayloadHash(ProposeCommand, request);
        if (await PriorAsync(context, metadata, FinanceAction.ProposePaymentReversal, ProposeCommand, payloadHash,
                cancellationToken) is { } prior)
        {
            return prior;
        }

        var payment = await dbContext.ManualBankPayments.SingleOrDefaultAsync(row => row.Id == request.PaymentId,
            cancellationToken);
        if (payment is null)
        {
            return await TerminalAsync(context, metadata, FinanceAction.ProposePaymentReversal, ProposeCommand,
                payloadHash, paymentId: null,
                Refused(context, CommandOutcome.ValidationFailed, Issue("paymentId", "finance_payment_not_found")),
                cancellationToken);
        }

        PaymentReversalProposalRow proposal;
        try
        {
            proposal = PaymentReversalProposalPolicy.Propose(payment, request, Guid.NewGuid(), context.ActorId,
                timeProvider.GetUtcNow());
        }
        catch (ReversalPolicyException exception)
        {
            var result = PolicyRefusal(context, exception);
            return exception.Failure == ReversalPolicyFailure.VersionConflict
                ? await TerminalAsync(context, metadata, FinanceAction.ProposePaymentReversal, ProposeCommand,
                    payloadHash, payment.Id, result, cancellationToken)
                : result;
        }

        if (await CurrentRefusalAsync(context, payment.Id, cancellationToken) is { } current)
        {
            return current;
        }

        dbContext.PaymentReversalProposals.Add(proposal);
        var succeeded = Succeeded(context, proposal);
        AddCommand(context, metadata, ProposeCommand, payloadHash, payment.Id, succeeded, proposal.ProposedAtUtc);
        Audit(context, metadata, envelopes.Propose, proposal, "finance.payment-reversal.proposed.v1", "Propose",
            null, proposal.Version, "{}", ProposalAudit(proposal), null,
            "Payment reversal proposal; no money event yet");
        return await SaveAsync(context, metadata, FinanceAction.ProposePaymentReversal, ProposeCommand, payloadHash,
            succeeded, cancellationToken);
    }

    public async Task<PaymentReversalProposalCommandResult> ApproveAsync(
        FinanceContext context, CommandMetadata metadata, ApprovePaymentReversalRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        if (await StartAsync(context, metadata, FinanceAction.ApprovePaymentReversal, request.ProposalId,
                cancellationToken) is { } refused)
        {
            return refused;
        }

        var payloadHash = PayloadHash(ApproveCommand, request);
        if (await PriorAsync(context, metadata, FinanceAction.ApprovePaymentReversal, ApproveCommand, payloadHash,
                cancellationToken) is { } prior)
        {
            return prior;
        }

        var proposal = await dbContext.PaymentReversalProposals.SingleOrDefaultAsync(
            row => row.Id == request.ProposalId, cancellationToken);
        if (proposal is null)
        {
            return await TerminalAsync(context, metadata, FinanceAction.ApprovePaymentReversal, ApproveCommand,
                payloadHash, null, Refused(context, CommandOutcome.ValidationFailed,
                    Issue("proposalId", "finance_reversal_proposal_not_found")), cancellationToken);
        }

        var payment = await dbContext.ManualBankPayments.SingleOrDefaultAsync(row => row.Id == proposal.PaymentId,
            cancellationToken) ?? throw new InvalidOperationException("Payment reversal proposal payment is missing.");
        if (!string.Equals(proposal.Version.ToString(CultureInfo.InvariantCulture), request.ExpectedVersion,
                StringComparison.Ordinal))
        {
            return await TerminalAsync(context, metadata, FinanceAction.ApprovePaymentReversal, ApproveCommand,
                payloadHash, payment.Id, Refused(context, CommandOutcome.VersionConflict,
                    Issue("expectedVersion", "version_conflict")), cancellationToken);
        }
        if (string.Equals(proposal.ProposedBy, context.ActorId, StringComparison.Ordinal))
        {
            return Refused(context, CommandOutcome.Denied, Issue("approval", "finance_approver_is_proposer"));
        }

        // DEC-FIN-006: a payment applied to a payable open item is reversed only after every allocation is released.
        var hasActiveAllocation = await dbContext.PaymentAllocations.AsNoTracking()
            .AnyAsync(row => row.PaymentId == payment.Id && row.Status == FinanceAllocationStatuses.Active,
                cancellationToken);
        if (hasActiveAllocation)
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("paymentId", "finance_reversal_has_allocations"));
        }

        var now = timeProvider.GetUtcNow();
        var period = await periodRule.CheckAsync(context, proposal.OccurredOn, null, now, cancellationToken);
        if (await FenceAsync(context, FinanceAction.ApprovePaymentReversal, proposal.Id, cancellationToken)
            is { } afterPeriod)
        {
            return afterPeriod;
        }
        if (period.Outcome != RecordPeriodOutcome.Open)
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("occurredOn", period.Code ?? "finance_period_rule_unavailable"));
        }

        var original = await dbContext.OutgoingMoneyEvents.AsNoTracking()
            .SingleOrDefaultAsync(row => row.PaymentId == payment.Id, cancellationToken);
        if (original is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("paymentId", "finance_reversal_source_not_original_payment"));
        }

        var allocationState = await dbContext.PaymentAllocationStates.SingleOrDefaultAsync(
            row => row.PaymentId == payment.Id, cancellationToken);
        FinancePaymentReversalBarrier barrier;
        try
        {
            barrier = PaymentReversalProposalPolicy.PrepareBarrier(payment, allocationState);
        }
        catch (FinanceAllocationPolicyException exception)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue(exception.Field, exception.Code));
        }

        var before = ProposalAudit(proposal);
        var versionBefore = proposal.Version;
        OutgoingMoneyEventReversalRow reversal;
        try
        {
            reversal = PaymentReversalProposalPolicy.Approve(payment, original, proposal, request, Guid.NewGuid(),
                context.ActorId, context.CorrelationId, now);
        }
        catch (ReversalPolicyException exception)
        {
            return PolicyRefusal(context, exception);
        }

        if (allocationState is null)
        {
            dbContext.PaymentAllocationStates.Add(new FinancePaymentAllocationStateRow
            {
                CompanyId = context.CompanyId,
                PaymentId = payment.Id,
                CurrencyId = payment.CurrencyId,
                OriginalAmountText = barrier.OriginalAmount,
                UnallocatedAmountText = barrier.UnallocatedAmount,
                Version = barrier.Version,
                UpdatedBy = context.ActorId,
                UpdatedAtUtc = now,
            });
        }
        else
        {
            allocationState.UnallocatedAmountText = barrier.UnallocatedAmount;
            allocationState.Version = barrier.Version;
            allocationState.UpdatedBy = context.ActorId;
            allocationState.UpdatedAtUtc = now;
        }

        dbContext.OutgoingMoneyEventReversals.Add(reversal);
        var succeeded = Succeeded(context, proposal);
        AddCommand(context, metadata, ApproveCommand, payloadHash, payment.Id, succeeded, now);
        var approval = JsonSerializer.Serialize(new
        {
            rule = "maker-checker",
            proposedBy = proposal.ProposedBy,
            approvedBy = context.ActorId,
            paymentId = payment.Id,
            reversalEventId = reversal.Id,
        }, AuditJson);
        Audit(context, metadata, envelopes.Approve, proposal, "finance.payment-reversal.approved.v1", "Approve",
            versionBefore, proposal.Version, before, ProposalAudit(proposal), approval,
            "Approved payment reversal appends one immutable counter-event");
        return await SaveAsync(context, metadata, FinanceAction.ApprovePaymentReversal, ApproveCommand, payloadHash,
            succeeded, cancellationToken);
    }

    public async Task<PaymentReversalProposalCommandResult> RejectAsync(
        FinanceContext context, CommandMetadata metadata, RejectPaymentReversalRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        if (await StartAsync(context, metadata, FinanceAction.ApprovePaymentReversal, request.ProposalId,
                cancellationToken) is { } refused)
        {
            return refused;
        }

        var payloadHash = PayloadHash(RejectCommand, request);
        if (await PriorAsync(context, metadata, FinanceAction.ApprovePaymentReversal, RejectCommand, payloadHash,
                cancellationToken) is { } prior)
        {
            return prior;
        }

        var proposal = await dbContext.PaymentReversalProposals.SingleOrDefaultAsync(
            row => row.Id == request.ProposalId, cancellationToken);
        if (proposal is null)
        {
            return await TerminalAsync(context, metadata, FinanceAction.ApprovePaymentReversal, RejectCommand,
                payloadHash, null, Refused(context, CommandOutcome.ValidationFailed,
                    Issue("proposalId", "finance_reversal_proposal_not_found")), cancellationToken);
        }

        var payment = await dbContext.ManualBankPayments.SingleOrDefaultAsync(row => row.Id == proposal.PaymentId,
            cancellationToken) ?? throw new InvalidOperationException("Payment reversal proposal payment is missing.");
        var before = ProposalAudit(proposal);
        var versionBefore = proposal.Version;
        try
        {
            PaymentReversalProposalPolicy.Reject(payment, proposal, request, context.ActorId,
                timeProvider.GetUtcNow());
        }
        catch (ReversalPolicyException exception)
        {
            return PolicyRefusal(context, exception);
        }

        var succeeded = Succeeded(context, proposal);
        AddCommand(context, metadata, RejectCommand, payloadHash, payment.Id, succeeded,
            proposal.DecidedAtUtc!.Value);
        var approval = JsonSerializer.Serialize(new
        {
            rule = "maker-checker",
            proposedBy = proposal.ProposedBy,
            rejectedBy = context.ActorId,
        }, AuditJson);
        Audit(context, metadata, envelopes.Approve, proposal, "finance.payment-reversal.rejected.v1", "Reject",
            versionBefore, proposal.Version, before, ProposalAudit(proposal), approval,
            "Payment reversal proposal rejected; no money event created");
        return await SaveAsync(context, metadata, FinanceAction.ApprovePaymentReversal, RejectCommand, payloadHash,
            succeeded, cancellationToken);
    }

    public async Task<ReadResult<PaymentReversalProposalView>> DetailAuthorizedAsync(
        FinanceContext context, Guid proposalId, CancellationToken cancellationToken)
    {
        if (await ReadFenceAsync(context, proposalId, cancellationToken) is { } refused)
        {
            return new ReadResult<PaymentReversalProposalView>(refused, null);
        }
        var row = await dbContext.PaymentReversalProposals.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == proposalId, cancellationToken);
        if (await ReadFenceAsync(context, proposalId, cancellationToken) is { } again)
        {
            return new ReadResult<PaymentReversalProposalView>(again, null);
        }
        return row is null
            ? new ReadResult<PaymentReversalProposalView>(ReadOutcome.NotFound, null)
            : new ReadResult<PaymentReversalProposalView>(ReadOutcome.Found, View(row));
    }

    public async Task<ReadResult<PaymentReversalProposalPage>> PendingAuthorizedAsync(
        FinanceContext context, CancellationToken cancellationToken)
    {
        if (await ReadFenceAsync(context, null, cancellationToken) is { } refused)
        {
            return new ReadResult<PaymentReversalProposalPage>(refused, null);
        }
        var rows = await dbContext.PaymentReversalProposals.AsNoTracking()
            .Where(row => row.Status == ReversalProposalStatuses.Pending)
            .OrderBy(row => row.ProposedAtUtc).ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
        if (await ReadFenceAsync(context, null, cancellationToken) is { } again)
        {
            return new ReadResult<PaymentReversalProposalPage>(again, null);
        }
        return new ReadResult<PaymentReversalProposalPage>(ReadOutcome.Found,
            new PaymentReversalProposalPage(rows.Select(View).ToArray()));
    }

    internal static string PayloadHash<T>(string command, T request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command + "|" + JsonSerializer.Serialize(request, Json))))
            .ToLowerInvariant();

    /// <summary>
    /// Clear refusals for the payment's current state, ahead of the unique indexes that still decide a race:
    /// a payment already reversed, a proposal already pending, or an allocation still applied.
    /// </summary>
    private async Task<PaymentReversalProposalCommandResult?> CurrentRefusalAsync(
        FinanceContext context, Guid paymentId, CancellationToken cancellationToken)
    {
        var openStatuses = await dbContext.PaymentReversalProposals.AsNoTracking()
            .Where(row => row.PaymentId == paymentId && row.Status != ReversalProposalStatuses.Rejected)
            .Select(row => row.Status)
            .ToArrayAsync(cancellationToken);
        if (openStatuses.Contains(ReversalProposalStatuses.Approved))
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("paymentId", "finance_source_already_reversed"));
        }
        if (openStatuses.Contains(ReversalProposalStatuses.Pending))
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("paymentId", "finance_reversal_proposal_already_pending"));
        }

        var hasActiveAllocation = await dbContext.PaymentAllocations.AsNoTracking()
            .AnyAsync(row => row.PaymentId == paymentId && row.Status == FinanceAllocationStatuses.Active,
                cancellationToken);
        return hasActiveAllocation
            ? Refused(context, CommandOutcome.ValidationFailed, Issue("paymentId", "finance_reversal_has_allocations"))
            : null;
    }

    private async Task<PaymentReversalProposalCommandResult?> StartAsync(
        FinanceContext context, CommandMetadata metadata, FinanceAction action, Guid targetId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (await FenceAsync(context, action, targetId, cancellationToken) is { } denied)
        {
            return denied;
        }
        if (targetId == Guid.Empty || !Canonical(metadata.IdempotencyKey)
            || metadata.IdempotencyKey.Length > IdentityMaximumLength)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("request", "identity_required"));
        }
        return context.ActorId.Length > IdentityMaximumLength || context.CorrelationId.Length > IdentityMaximumLength
            ? Refused(context, CommandOutcome.ValidationFailed, Issue("context", "finance_value_does_not_fit"))
            : null;
    }

    private async Task<PaymentReversalProposalCommandResult?> PriorAsync(
        FinanceContext context, CommandMetadata metadata, FinanceAction action, string command, string payloadHash,
        CancellationToken cancellationToken)
    {
        var keyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var prior = await dbContext.Commands.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == command && row.IdempotencyKeyHash == keyHash,
                cancellationToken);
        if (prior is null)
        {
            return null;
        }
        if (prior.PayloadHash != payloadHash || !string.Equals(prior.ActorId, context.ActorId, StringComparison.Ordinal))
        {
            return Refused(context, CommandOutcome.IdempotencyConflict);
        }
        if (await FenceAsync(context, action, prior.ManualBankPaymentId, cancellationToken) is { } fenced)
        {
            return fenced;
        }
        var stored = JsonSerializer.Deserialize<PaymentReversalProposalCommandResult>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Stored payment reversal command result cannot be read.");
        return stored with { IsReplay = true };
    }

    private async Task<PaymentReversalProposalCommandResult?> FenceAsync(
        FinanceContext context, FinanceAction action, Guid? targetId, CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return Refused(context, CommandOutcome.Denied);
        }
        var decision = await permissions.CheckAsync(context, action, targetId, cancellationToken);
        if (decision != PermissionDecision.Allowed)
        {
            return decision == PermissionDecision.Denied
                ? Refused(context, CommandOutcome.Denied)
                : Refused(context, CommandOutcome.Unavailable,
                    Issue("permission", "finance_permission_mapping_unavailable"));
        }
        return SameContext(context) ? null : Refused(context, CommandOutcome.Denied);
    }

    private async Task<ReadOutcome?> ReadFenceAsync(
        FinanceContext context, Guid? targetId, CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return ReadOutcome.Denied;
        }
        var propose = await permissions.CheckAsync(context, FinanceAction.ProposePaymentReversal, targetId,
            cancellationToken);
        var approve = await permissions.CheckAsync(context, FinanceAction.ApprovePaymentReversal, targetId,
            cancellationToken);
        if (!SameContext(context))
        {
            return ReadOutcome.Denied;
        }
        if (propose == PermissionDecision.Allowed || approve == PermissionDecision.Allowed)
        {
            return null;
        }
        return propose == PermissionDecision.Unavailable || approve == PermissionDecision.Unavailable
            ? ReadOutcome.Unavailable
            : ReadOutcome.Denied;
    }

    private bool SameContext(FinanceContext context) =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
        && companyContext.CompanyId == context.CompanyId && currentUser.CanAccessCompany(context.CompanyId);

    private void AddCommand(
        FinanceContext context, CommandMetadata metadata, string command, string payloadHash,
        Guid? paymentId, PaymentReversalProposalCommandResult result, DateTimeOffset now) =>
        dbContext.Commands.Add(new FinanceCommandRow
        {
            CompanyId = context.CompanyId,
            CommandName = command,
            IdempotencyKeyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey),
            ActorId = context.ActorId,
            PayloadHash = payloadHash,
            // The payment is the command's target; a "proposal not found" refusal stores none (ValidationFailed).
            ManualBankPaymentId = paymentId,
            ResultJson = JsonSerializer.Serialize(result, Json),
            CorrelationId = context.CorrelationId,
            RecordedAtUtc = now,
        });

    private async Task<PaymentReversalProposalCommandResult> SaveAsync(
        FinanceContext context, CommandMetadata metadata, FinanceAction action, string command, string payloadHash,
        PaymentReversalProposalCommandResult result, CancellationToken cancellationToken)
    {
        // Measure the action again immediately before the write; a revoked permission must not persist the
        // business rows, the idempotency answer or the audit sidecar prepared by a stale decision.
        if (await FenceAsync(context, action, result.Resource?.Id, cancellationToken) is { } fenced)
        {
            dbContext.ChangeTracker.Clear();
            return fenced;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            dbContext.ChangeTracker.Clear();
            return await PriorAsync(context, metadata, action, command, payloadHash, cancellationToken)
                ?? Refused(context, CommandOutcome.VersionConflict, Issue("request", "concurrent_change"));
        }
        return result;
    }

    private Task<PaymentReversalProposalCommandResult> TerminalAsync(
        FinanceContext context, CommandMetadata metadata, FinanceAction action, string command, string payloadHash,
        Guid? paymentId, PaymentReversalProposalCommandResult refusal, CancellationToken cancellationToken)
    {
        AddCommand(context, metadata, command, payloadHash, paymentId, refusal, timeProvider.GetUtcNow());
        return SaveAsync(context, metadata, action, command, payloadHash, refusal, cancellationToken);
    }

    private void Audit(
        FinanceContext context, CommandMetadata metadata, FinanceAuditEnvelope envelope,
        PaymentReversalProposalRow proposal, string eventType, string operation, long? versionBefore,
        long? versionAfter, string before, string after, string? approvalContextJson, string reason) =>
        auditEvents.Enqueue(new AuditEventDraft(
            context.CompanyId, eventType, EntityType, proposal.Id.ToString("D"), operation,
            versionBefore, versionAfter, AuditActorTypes.User, context.ActorId,
            envelope.PermissionCode, envelope.RoleCodes, before, after, reason, approvalContextJson,
            AuditAiInvolvement.None, AiContextJson: null, envelope.SourceChannel, envelope.ClientIpAddress,
            envelope.UserAgent, context.CorrelationId, context.CausationId, metadata.IdempotencyKey));

    private static PaymentReversalProposalView View(PaymentReversalProposalRow row) => new PaymentReversalProposalView(
        row.Id, row.PaymentId, row.OccurredOn, row.Reason,
        (ReversalProposalStatus)row.Status, row.ProposedBy, row.ProposedAtUtc,
        row.DecidedBy, row.DecidedAtUtc, row.DecisionReason, row.ReversalEventId,
        row.Version.ToString(CultureInfo.InvariantCulture));

    private static string ProposalAudit(PaymentReversalProposalRow row) => JsonSerializer.Serialize(new
    {
        row.Id,
        row.PaymentId,
        row.OccurredOn,
        row.Status,
        row.ProposedBy,
        row.DecidedBy,
        row.ReversalEventId,
        version = row.Version.ToString(CultureInfo.InvariantCulture),
    }, AuditJson);

    private static PaymentReversalProposalCommandResult Succeeded(
        FinanceContext context, PaymentReversalProposalRow row) =>
        new PaymentReversalProposalCommandResult(CommandOutcome.Succeeded, View(row), false, context.CorrelationId, []);

    private static PaymentReversalProposalCommandResult PolicyRefusal(
        FinanceContext context, ReversalPolicyException exception) => exception.Failure switch
        {
            ReversalPolicyFailure.VersionConflict => Refused(context, CommandOutcome.VersionConflict,
                Issue("expectedVersion", "version_conflict")),
            ReversalPolicyFailure.ApproverIsProposer => Refused(context, CommandOutcome.Denied,
                Issue("approval", "finance_approver_is_proposer")),
            ReversalPolicyFailure.SourceNotRecorded => Refused(context, CommandOutcome.ValidationFailed,
                Issue("paymentId", "finance_payment_not_recorded")),
            ReversalPolicyFailure.ProposalNotPending => Refused(context, CommandOutcome.ValidationFailed,
                Issue("proposalId", "finance_reversal_proposal_not_pending")),
            _ => Refused(context, CommandOutcome.ValidationFailed, Issue("request", "invalid_request")),
        };

    private static bool Canonical(string? value) =>
        !string.IsNullOrWhiteSpace(value) && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static ValidationIssue Issue(string field, string code) => new ValidationIssue(field, code);

    private static PaymentReversalProposalCommandResult Refused(
        FinanceContext context, CommandOutcome outcome, params ValidationIssue[] issues) =>
        new PaymentReversalProposalCommandResult(outcome, null, false, context.CorrelationId, issues);
}

public sealed record FinancePaymentReversalEnvelopes(FinanceAuditEnvelope Propose, FinanceAuditEnvelope Approve);
