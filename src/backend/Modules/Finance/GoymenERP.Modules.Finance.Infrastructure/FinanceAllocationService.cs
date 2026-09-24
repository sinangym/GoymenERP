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

/// <summary>F-03 atomic source-to-open-item allocation and explicit release.</summary>
public sealed class FinanceAllocationService(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinancePermissionPort permissions,
    IAuditEventCollector auditEvents,
    FinanceAuditEnvelope envelope,
    TimeProvider timeProvider) : IFinanceAllocationService
{
    internal const string CreateCommandName = "finance.allocation.create.v1";
    internal const string ReleaseCommandName = "finance.allocation.release.v1";
    private const int IdentityMaximumLength = 128;
    private const string EntityType = "Finance.Allocation";
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    public async Task<FinanceAllocationCommandResult> CreateAsync(
        FinanceContext context,
        CommandMetadata metadata,
        CreateFinanceAllocationRequest request,
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

        var source = await dbContext.ReceiptDrafts.SingleOrDefaultAsync(row => row.Id == request.SourceId,
            cancellationToken);
        var openItem = await dbContext.OpenItems.SingleOrDefaultAsync(row => row.Id == request.OpenItemId,
            cancellationToken);
        var sourceState = await dbContext.SourceAllocationStates.SingleOrDefaultAsync(
            row => row.SourceId == request.SourceId, cancellationToken);
        if (source is null || openItem is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue(source is null ? "sourceId" : "openItemId", "finance_allocation_reference_not_found"));
        }
        if (await dbContext.ReversalProposals.AsNoTracking().AnyAsync(
                row => row.OriginalSourceId == source.Id && row.Status == ReversalProposalStatuses.Approved,
                cancellationToken))
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("sourceId", "finance_source_already_reversed"));
        }

        FinanceAllocationMutation mutation;
        try
        {
            mutation = FinanceAllocationPolicy.Create(source, openItem, sourceState, request);
        }
        catch (FinanceAllocationPolicyException exception)
        {
            return PolicyRefusal(context, exception);
        }

        var now = timeProvider.GetUtcNow();
        if (sourceState is null)
        {
            sourceState = new FinanceSourceAllocationStateRow
            {
                CompanyId = context.CompanyId,
                SourceId = source.Id,
                CurrencyId = source.CurrencyId,
                OriginalAmountText = source.AmountText,
                UnallocatedAmountText = mutation.SourceUnallocated,
                Version = 1,
                UpdatedBy = context.ActorId,
                UpdatedAtUtc = now,
            };
            dbContext.SourceAllocationStates.Add(sourceState);
        }
        else
        {
            sourceState.UnallocatedAmountText = mutation.SourceUnallocated;
            sourceState.Version = checked(sourceState.Version + 1);
            sourceState.UpdatedBy = context.ActorId;
            sourceState.UpdatedAtUtc = now;
        }
        openItem.OutstandingAmountText = mutation.OpenItemOutstanding;
        openItem.Status = mutation.OpenItemStatus;
        openItem.Version = checked(openItem.Version + 1);
        openItem.UpdatedBy = context.ActorId;
        openItem.UpdatedAtUtc = now;

        var allocation = new FinanceAllocationRow
        {
            Id = request.AllocationId,
            CompanyId = context.CompanyId,
            SourceId = source.Id,
            OpenItemId = openItem.Id,
            CurrencyId = source.CurrencyId,
            AmountText = request.Amount,
            Reason = request.Reason,
            Status = FinanceAllocationStatuses.Active,
            Version = 1,
            CreatedBy = context.ActorId,
            CreatedAtUtc = now,
        };
        dbContext.Allocations.Add(allocation);
        var result = Succeeded(context, allocation);
        AddCommand(context, metadata, CreateCommandName, payloadHash, allocation.Id, result, now);
        Audit(context, metadata, allocation, "finance.allocation.created.v1", "Create", null, 1,
            "{}", AuditJson(allocation), "Existing Finance source allocated; no money event created");
        return await SaveAsync(context, metadata, CreateCommandName, payloadHash, result, cancellationToken);
    }

    public async Task<FinanceAllocationCommandResult> ReleaseAsync(
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

        var allocation = await dbContext.Allocations.SingleOrDefaultAsync(row => row.Id == request.AllocationId,
            cancellationToken);
        if (allocation is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("allocationId", "finance_allocation_not_found"));
        }
        var openItem = await dbContext.OpenItems.SingleOrDefaultAsync(row => row.Id == allocation.OpenItemId,
            cancellationToken) ?? throw new InvalidOperationException("Stored allocation open item is missing.");
        var sourceState = await dbContext.SourceAllocationStates.SingleOrDefaultAsync(
            row => row.SourceId == allocation.SourceId, cancellationToken)
            ?? throw new InvalidOperationException("Stored allocation source state is missing.");

        FinanceAllocationMutation mutation;
        try
        {
            mutation = FinanceAllocationPolicy.Release(allocation, openItem, sourceState, request);
        }
        catch (FinanceAllocationPolicyException exception)
        {
            return PolicyRefusal(context, exception);
        }

        var now = timeProvider.GetUtcNow();
        var before = AuditJson(allocation);
        allocation.Status = FinanceAllocationStatuses.Released;
        allocation.Version = checked(allocation.Version + 1);
        allocation.ReleasedBy = context.ActorId;
        allocation.ReleasedAtUtc = now;
        allocation.ReleaseReason = request.Reason;
        sourceState.UnallocatedAmountText = mutation.SourceUnallocated;
        sourceState.Version = checked(sourceState.Version + 1);
        sourceState.UpdatedBy = context.ActorId;
        sourceState.UpdatedAtUtc = now;
        openItem.OutstandingAmountText = mutation.OpenItemOutstanding;
        openItem.Status = mutation.OpenItemStatus;
        openItem.Version = checked(openItem.Version + 1);
        openItem.UpdatedBy = context.ActorId;
        openItem.UpdatedAtUtc = now;

        var result = Succeeded(context, allocation);
        AddCommand(context, metadata, ReleaseCommandName, payloadHash, allocation.Id, result, now);
        Audit(context, metadata, allocation, "finance.allocation.released.v1", "Release", 1, allocation.Version,
            before, AuditJson(allocation), request.Reason);
        return await SaveAsync(context, metadata, ReleaseCommandName, payloadHash, result, cancellationToken);
    }

    private async Task<FinanceAllocationCommandResult?> StartAsync(
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

    private async Task<FinanceAllocationCommandResult?> PriorAsync(
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
        if (await FenceAsync(context, prior.FinanceAllocationId, cancellationToken) is { } fenced)
        {
            return fenced;
        }
        var stored = JsonSerializer.Deserialize<FinanceAllocationCommandResult>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Stored allocation command result cannot be read.");
        return stored with { IsReplay = true };
    }

    private async Task<FinanceAllocationCommandResult?> FenceAsync(
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

    private async Task<FinanceAllocationCommandResult> SaveAsync(
        FinanceContext context, CommandMetadata metadata, string command, string payloadHash,
        FinanceAllocationCommandResult result, CancellationToken cancellationToken)
    {
        if (await FenceAsync(context, result.Resource?.Id, cancellationToken) is { } fenced)
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
            return await PriorAsync(context, metadata, command, payloadHash, cancellationToken)
                ?? Refused(context, CommandOutcome.VersionConflict, Issue("request", "concurrent_change"));
        }
        return result;
    }

    private void AddCommand(
        FinanceContext context, CommandMetadata metadata, string command, string payloadHash,
        Guid allocationId, FinanceAllocationCommandResult result, DateTimeOffset now) =>
        dbContext.Commands.Add(new FinanceCommandRow
        {
            CompanyId = context.CompanyId,
            CommandName = command,
            IdempotencyKeyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey),
            ActorId = context.ActorId,
            PayloadHash = payloadHash,
            FinanceAllocationId = allocationId,
            ResultJson = JsonSerializer.Serialize(result, Json),
            CorrelationId = context.CorrelationId,
            RecordedAtUtc = now,
        });

    private void Audit(
        FinanceContext context, CommandMetadata metadata, FinanceAllocationRow allocation,
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

    private static FinanceAllocationCommandResult PolicyRefusal(
        FinanceContext context, FinanceAllocationPolicyException exception) =>
        Refused(context,
            exception.Code == "version_conflict" ? CommandOutcome.VersionConflict : CommandOutcome.ValidationFailed,
            Issue(exception.Field, exception.Code));

    private static FinanceAllocationCommandResult Succeeded(FinanceContext context, FinanceAllocationRow row) =>
        new FinanceAllocationCommandResult(CommandOutcome.Succeeded, View(row), false, context.CorrelationId, []);

    private static FinanceAllocationView View(FinanceAllocationRow row) => new FinanceAllocationView(
        row.Id, row.CompanyId, row.SourceId, row.OpenItemId, row.CurrencyId, row.AmountText,
        (FinanceAllocationStatus)row.Status, row.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
        row.Reason, row.CreatedBy, row.CreatedAtUtc, row.ReleasedBy, row.ReleasedAtUtc, row.ReleaseReason);

    private static string AuditJson(FinanceAllocationRow row) => JsonSerializer.Serialize(View(row), Json);

    private static ValidationIssue Issue(string field, string code) => new ValidationIssue(field, code);

    private static FinanceAllocationCommandResult Refused(
        FinanceContext context, CommandOutcome outcome, params ValidationIssue[] issues) =>
        new FinanceAllocationCommandResult(outcome, null, false, context.CorrelationId, issues);
}
