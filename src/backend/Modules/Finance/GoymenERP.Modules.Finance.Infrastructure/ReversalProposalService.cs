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

/// <summary>FIN-T06/B1-B2 authorized, idempotent maker-checker reversal workflow.</summary>
public sealed class ReversalProposalService(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinancePermissionPort permissions,
    IFinanceRecordPeriodRule periodRule,
    IFinanceReversalAllocationGate allocationGate,
    IAuditEventCollector auditEvents,
    FinanceReversalEnvelopes envelopes,
    TimeProvider timeProvider) : IReversalProposalService
{
    public const string ProposeCommand = "finance.receipt-reversal.propose";
    public const string ApproveCommand = "finance.receipt-reversal.approve";
    public const string RejectCommand = "finance.receipt-reversal.reject";
    private const string EntityType = "Finance.ReversalProposal";
    private const int IdentityMaximumLength = 128;
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    public async Task<ReversalProposalCommandResult> ProposeAsync(
        FinanceContext context, CommandMetadata metadata, ProposeReversalRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        if (await StartAsync(context, metadata, FinanceAction.ProposeReversal, request.SourceId, cancellationToken)
            is { } refused)
        {
            return refused;
        }

        var payloadHash = PayloadHash(ProposeCommand, request);
        if (await PriorAsync(context, metadata, FinanceAction.ProposeReversal, ProposeCommand, payloadHash,
                cancellationToken) is { } prior)
        {
            return prior;
        }

        var original = await dbContext.ReceiptDrafts.SingleOrDefaultAsync(row => row.Id == request.SourceId,
            cancellationToken);
        if (original is null)
        {
            return await TerminalAsync(context, metadata, FinanceAction.ProposeReversal, ProposeCommand, payloadHash,
                draftId: null, proposalId: null,
                Refused(context, CommandOutcome.ValidationFailed, Issue("sourceId", "finance_source_not_found")),
                cancellationToken);
        }

        ReversalProposalRow proposal;
        try
        {
            proposal = ReversalProposalPolicy.Propose(original, request, Guid.NewGuid(), context.ActorId,
                timeProvider.GetUtcNow());
        }
        catch (ReversalPolicyException exception)
        {
            var result = PolicyRefusal(context, exception);
            return exception.Failure == ReversalPolicyFailure.VersionConflict
                ? await TerminalAsync(context, metadata, FinanceAction.ProposeReversal, ProposeCommand, payloadHash,
                    original.Id, null, result, cancellationToken)
                : result;
        }

        dbContext.ReversalProposals.Add(proposal);
        var succeeded = Succeeded(context, proposal);
        AddCommand(context, metadata, ProposeCommand, payloadHash, null, proposal.Id, succeeded, proposal.ProposedAtUtc);
        Audit(context, metadata, envelopes.Propose, proposal, "finance.receipt-reversal.proposed.v1", "Propose",
            null, proposal.Version, "{}", ProposalAudit(proposal), null,
            "Receipt reversal proposal; no money event yet");
        return await SaveAsync(context, metadata, FinanceAction.ProposeReversal, ProposeCommand, payloadHash, succeeded,
            cancellationToken);
    }

    public async Task<ReversalProposalCommandResult> ApproveAsync(
        FinanceContext context, CommandMetadata metadata, ApproveReversalRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        if (await StartAsync(context, metadata, FinanceAction.ApproveReversal, request.ProposalId, cancellationToken)
            is { } refused)
        {
            return refused;
        }

        var payloadHash = PayloadHash(ApproveCommand, request);
        if (await PriorAsync(context, metadata, FinanceAction.ApproveReversal, ApproveCommand, payloadHash,
                cancellationToken) is { } prior)
        {
            return prior;
        }

        var proposal = await dbContext.ReversalProposals.SingleOrDefaultAsync(row => row.Id == request.ProposalId,
            cancellationToken);
        if (proposal is null)
        {
            return await TerminalAsync(context, metadata, FinanceAction.ApproveReversal, ApproveCommand, payloadHash,
                null, null, Refused(context, CommandOutcome.ValidationFailed,
                    Issue("proposalId", "finance_reversal_proposal_not_found")), cancellationToken);
        }

        var original = await dbContext.ReceiptDrafts.SingleOrDefaultAsync(row => row.Id == proposal.OriginalSourceId,
            cancellationToken) ?? throw new InvalidOperationException("Reversal proposal source is missing.");
        if (!string.Equals(proposal.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                request.ExpectedVersion, StringComparison.Ordinal))
        {
            return await TerminalAsync(context, metadata, FinanceAction.ApproveReversal, ApproveCommand, payloadHash,
                null, proposal.Id, Refused(context, CommandOutcome.VersionConflict,
                    Issue("expectedVersion", "version_conflict")), cancellationToken);
        }
        if (string.Equals(proposal.ProposedBy, context.ActorId, StringComparison.Ordinal))
        {
            return Refused(context, CommandOutcome.Denied, Issue("approval", "finance_approver_is_proposer"));
        }

        var allocation = await allocationGate.CheckAsync(context, original.Id, cancellationToken);
        if (await FenceAsync(context, FinanceAction.ApproveReversal, proposal.Id, cancellationToken) is { } afterAllocation)
        {
            return afterAllocation;
        }
        if (allocation.Outcome != ReversalAllocationOutcome.NoAllocation)
        {
            return allocation.Outcome switch
            {
                ReversalAllocationOutcome.HasAllocation => Refused(context, CommandOutcome.ValidationFailed,
                    Issue("sourceId", allocation.Code ?? "finance_reversal_has_allocations")),
                ReversalAllocationOutcome.Denied => Refused(context, CommandOutcome.Denied),
                _ => Refused(context, CommandOutcome.Unavailable,
                    Issue("sourceId", allocation.Code ?? "finance_allocation_rule_unavailable")),
            };
        }

        var now = timeProvider.GetUtcNow();
        var period = await periodRule.CheckAsync(context, proposal.OccurredOn, null, now, cancellationToken);
        if (await FenceAsync(context, FinanceAction.ApproveReversal, proposal.Id, cancellationToken) is { } afterPeriod)
        {
            return afterPeriod;
        }
        if (period.Outcome != RecordPeriodOutcome.Open)
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("occurredOn", period.Code ?? "finance_period_rule_unavailable"));
        }

        var before = ProposalAudit(proposal);
        var versionBefore = proposal.Version;
        ApprovedReversalRows rows;
        try
        {
            rows = ReversalProposalPolicy.Approve(original, proposal, request, Guid.NewGuid(), Guid.NewGuid(),
                context.ActorId, context.CorrelationId, now);
        }
        catch (ReversalPolicyException exception)
        {
            return PolicyRefusal(context, exception);
        }

        dbContext.ReceiptDrafts.Add(rows.Source);
        dbContext.MoneyEvents.Add(rows.Event);
        var succeeded = Succeeded(context, proposal);
        AddCommand(context, metadata, ApproveCommand, payloadHash, null, proposal.Id, succeeded, now);
        var approval = JsonSerializer.Serialize(new
        {
            rule = "maker-checker",
            proposedBy = proposal.ProposedBy,
            approvedBy = context.ActorId,
            originalSourceId = original.Id,
            reversalSourceId = rows.Source.Id,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Audit(context, metadata, envelopes.Approve, proposal, "finance.receipt-reversal.approved.v1", "Approve",
            versionBefore, proposal.Version, before, ProposalAudit(proposal), approval,
            "Approved reversal creates one immutable counter-event");
        return await SaveAsync(context, metadata, FinanceAction.ApproveReversal, ApproveCommand, payloadHash, succeeded,
            cancellationToken);
    }

    public async Task<ReversalProposalCommandResult> RejectAsync(
        FinanceContext context, CommandMetadata metadata, RejectReversalRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        if (await StartAsync(context, metadata, FinanceAction.ApproveReversal, request.ProposalId, cancellationToken)
            is { } refused)
        {
            return refused;
        }

        var payloadHash = PayloadHash(RejectCommand, request);
        if (await PriorAsync(context, metadata, FinanceAction.ApproveReversal, RejectCommand, payloadHash,
                cancellationToken) is { } prior)
        {
            return prior;
        }
        var proposal = await dbContext.ReversalProposals.SingleOrDefaultAsync(row => row.Id == request.ProposalId,
            cancellationToken);
        if (proposal is null)
        {
            return await TerminalAsync(context, metadata, FinanceAction.ApproveReversal, RejectCommand, payloadHash,
                null, null, Refused(context, CommandOutcome.ValidationFailed,
                    Issue("proposalId", "finance_reversal_proposal_not_found")), cancellationToken);
        }
        var original = await dbContext.ReceiptDrafts.SingleOrDefaultAsync(row => row.Id == proposal.OriginalSourceId,
            cancellationToken) ?? throw new InvalidOperationException("Reversal proposal source is missing.");
        var before = ProposalAudit(proposal);
        var versionBefore = proposal.Version;
        try
        {
            ReversalProposalPolicy.Reject(original, proposal, request, context.ActorId, timeProvider.GetUtcNow());
        }
        catch (ReversalPolicyException exception)
        {
            return PolicyRefusal(context, exception);
        }

        var succeeded = Succeeded(context, proposal);
        AddCommand(context, metadata, RejectCommand, payloadHash, null, proposal.Id, succeeded,
            proposal.DecidedAtUtc!.Value);
        var approval = JsonSerializer.Serialize(new
        {
            rule = "maker-checker",
            proposedBy = proposal.ProposedBy,
            rejectedBy = context.ActorId,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Audit(context, metadata, envelopes.Approve, proposal, "finance.receipt-reversal.rejected.v1", "Reject",
            versionBefore, proposal.Version, before, ProposalAudit(proposal), approval,
            "Reversal proposal rejected; no money event created");
        return await SaveAsync(context, metadata, FinanceAction.ApproveReversal, RejectCommand, payloadHash, succeeded,
            cancellationToken);
    }

    public async Task<ReadResult<ReversalProposalView>> DetailAuthorizedAsync(
        FinanceContext context, Guid proposalId, CancellationToken cancellationToken)
    {
        if (await ReadFenceAsync(context, proposalId, cancellationToken) is { } refused)
        {
            return new ReadResult<ReversalProposalView>(refused, null);
        }
        var row = await dbContext.ReversalProposals.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == proposalId, cancellationToken);
        if (await ReadFenceAsync(context, proposalId, cancellationToken) is { } again)
        {
            return new ReadResult<ReversalProposalView>(again, null);
        }
        return row is null
            ? new ReadResult<ReversalProposalView>(ReadOutcome.NotFound, null)
            : new ReadResult<ReversalProposalView>(ReadOutcome.Found, View(row));
    }

    public async Task<ReadResult<ReversalProposalPage>> PendingAuthorizedAsync(
        FinanceContext context, CancellationToken cancellationToken)
    {
        if (await ReadFenceAsync(context, null, cancellationToken) is { } refused)
        {
            return new ReadResult<ReversalProposalPage>(refused, null);
        }
        var rows = await dbContext.ReversalProposals.AsNoTracking()
            .Where(row => row.Status == ReversalProposalStatuses.Pending)
            .OrderBy(row => row.ProposedAtUtc).ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
        if (await ReadFenceAsync(context, null, cancellationToken) is { } again)
        {
            return new ReadResult<ReversalProposalPage>(again, null);
        }
        return new ReadResult<ReversalProposalPage>(ReadOutcome.Found,
            new ReversalProposalPage(rows.Select(View).ToArray()));
    }

    internal static string PayloadHash<T>(string command, T request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command + "|" + JsonSerializer.Serialize(request, Json))))
            .ToLowerInvariant();

    private async Task<ReversalProposalCommandResult?> StartAsync(
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

    private async Task<ReversalProposalCommandResult?> PriorAsync(
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
        if (await FenceAsync(context, action, prior.ReversalProposalId ?? prior.DraftId, cancellationToken) is { } fenced)
        {
            return fenced;
        }
        var stored = JsonSerializer.Deserialize<ReversalProposalCommandResult>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Stored reversal command result cannot be read.");
        return stored with { IsReplay = true };
    }

    private async Task<ReversalProposalCommandResult?> FenceAsync(
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
        var propose = await permissions.CheckAsync(context, FinanceAction.ProposeReversal, targetId, cancellationToken);
        var approve = await permissions.CheckAsync(context, FinanceAction.ApproveReversal, targetId, cancellationToken);
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
        Guid? draftId, Guid? proposalId, ReversalProposalCommandResult result, DateTimeOffset now) =>
        dbContext.Commands.Add(new FinanceCommandRow
        {
            CompanyId = context.CompanyId,
            CommandName = command,
            IdempotencyKeyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey),
            ActorId = context.ActorId,
            PayloadHash = payloadHash,
            DraftId = draftId,
            ReversalProposalId = proposalId,
            ResultJson = JsonSerializer.Serialize(result, Json),
            CorrelationId = context.CorrelationId,
            RecordedAtUtc = now,
        });

    private async Task<ReversalProposalCommandResult> SaveAsync(
        FinanceContext context, CommandMetadata metadata, FinanceAction action, string command, string payloadHash,
        ReversalProposalCommandResult result, CancellationToken cancellationToken)
    {
        // The command may have awaited proposal/source/owner reads since StartAsync. Measure the action
        // again immediately before the write; a revoked permission must not persist the business row,
        // idempotency answer or audit sidecar prepared by the stale authorization decision.
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

    private Task<ReversalProposalCommandResult> TerminalAsync(
        FinanceContext context, CommandMetadata metadata, FinanceAction action, string command, string payloadHash,
        Guid? draftId, Guid? proposalId, ReversalProposalCommandResult refusal,
        CancellationToken cancellationToken)
    {
        AddCommand(context, metadata, command, payloadHash, draftId, proposalId, refusal, timeProvider.GetUtcNow());
        return SaveAsync(context, metadata, action, command, payloadHash, refusal, cancellationToken);
    }

    private void Audit(
        FinanceContext context, CommandMetadata metadata, FinanceAuditEnvelope envelope, ReversalProposalRow proposal,
        string eventType, string operation, long? versionBefore, long? versionAfter, string before, string after,
        string? approvalContextJson, string reason) =>
        auditEvents.Enqueue(new AuditEventDraft(
            context.CompanyId, eventType, EntityType, proposal.Id.ToString("D"), operation,
            versionBefore, versionAfter, AuditActorTypes.User, context.ActorId,
            envelope.PermissionCode, envelope.RoleCodes, before, after, reason, approvalContextJson,
            AuditAiInvolvement.None, AiContextJson: null, envelope.SourceChannel, envelope.ClientIpAddress,
            envelope.UserAgent, context.CorrelationId, context.CausationId, metadata.IdempotencyKey));

    private static ReversalProposalView View(ReversalProposalRow row) => new ReversalProposalView(
        row.Id, row.OriginalSourceId, row.OccurredOn, row.Reason,
        (ReversalProposalStatus)row.Status, row.ProposedBy, row.ProposedAtUtc,
        row.DecidedBy, row.DecidedAtUtc, row.DecisionReason, row.ReversalSourceId,
        row.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static string ProposalAudit(ReversalProposalRow row) => JsonSerializer.Serialize(new
    {
        row.Id,
        row.OriginalSourceId,
        row.OccurredOn,
        row.Status,
        row.ProposedBy,
        row.DecidedBy,
        row.ReversalSourceId,
        version = row.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static ReversalProposalCommandResult Succeeded(FinanceContext context, ReversalProposalRow row) =>
        new ReversalProposalCommandResult(CommandOutcome.Succeeded, View(row), false, context.CorrelationId, []);

    private static ReversalProposalCommandResult PolicyRefusal(
        FinanceContext context, ReversalPolicyException exception) => exception.Failure switch
        {
            ReversalPolicyFailure.VersionConflict => Refused(context, CommandOutcome.VersionConflict,
                Issue("expectedVersion", "version_conflict")),
            ReversalPolicyFailure.ApproverIsProposer => Refused(context, CommandOutcome.Denied,
                Issue("approval", "finance_approver_is_proposer")),
            ReversalPolicyFailure.SourceNotRecorded => Refused(context, CommandOutcome.ValidationFailed,
                Issue("sourceId", "finance_source_not_recorded")),
            ReversalPolicyFailure.ProposalNotPending => Refused(context, CommandOutcome.ValidationFailed,
                Issue("proposalId", "finance_reversal_proposal_not_pending")),
            _ => Refused(context, CommandOutcome.ValidationFailed, Issue("request", "invalid_request")),
        };

    private static bool Canonical(string? value) =>
        !string.IsNullOrWhiteSpace(value) && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static ValidationIssue Issue(string field, string code) => new ValidationIssue(field, code);

    private static ReversalProposalCommandResult Refused(
        FinanceContext context, CommandOutcome outcome, params ValidationIssue[] issues) =>
        new ReversalProposalCommandResult(outcome, null, false, context.CorrelationId, issues);
}

public sealed record FinanceReversalEnvelopes(FinanceAuditEnvelope Propose, FinanceAuditEnvelope Approve);
