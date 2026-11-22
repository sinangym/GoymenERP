using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Ai.External;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.Ai.Commands;

/// <summary>
/// WP-C1 approved write pipeline (AI-D25, AI-D34 a, DEC-AI-002/003, decision 156). It writes no business rule: the
/// owner module's existing command runs through its <see cref="IAiOwnerCommandToolV1"/> port with the owner's own
/// authorization, expected version and idempotency. The pipeline adds, in order:
/// <list type="number">
/// <item>Preview: the model proposes one command (untrusted); the owner previews it under the server context; the
/// complete canonical command, its difference and target version are stored with the canonical preview identity and
/// its SHA-256 digest.</item>
/// <item>Approval: bound to that digest and to the frozen context (actor, company, session, authority revision). The
/// record is previewed again; when the target version, command or difference changed, the approval is invalidated.</item>
/// <item>Second approval (decision 156, YOL-D27): a different authorized person than the preparer and the first
/// approver approves the same digest and approval id; the record is previewed again under their authority and their
/// password is typed again (LocalAccount verification with the login lockout). The same person is refused.</item>
/// <item>Execution by the preparer: the digest and approval id are sent again, session, permissions and authority are
/// re-measured, the record is previewed again, then the frozen owner request runs with idempotency key
/// <c>ai:&lt;previewId&gt;</c>. A resend never re-executes: an executed record replays its result, an executing or
/// unknown one only looks the same frozen request up.</item>
/// <item>Closed list (AI-D25): approval/rejection, payment, period close, role/permission assignment and policy
/// publication are refused at proposal and at every later step.</item>
/// <item>Audit: proposal, preview, approval, second approval, execution, rejection, invalidation and refusal are
/// separate content-free ledger events; refusals and the owner call are also Group E security events
/// (<c>ai.tool.called</c> Started is fail-closed).</item>
/// </list>
/// Closed by default: <c>Ai:Enabled</c> and <c>Ai:Commands:Enabled</c> are false, no command tool is allowlisted, no
/// durable ledger is configured.
/// </summary>
public sealed class AiCommandPipeline(
    IOptions<AiRuntimeOptions> runtimeOptions,
    IOptions<AiCommandOptions> commandOptions,
    IHostEnvironment environment,
    IAiModelProviderV1 provider,
    AiCommandToolCatalog catalog,
    IAiCommandLedgerV1 ledger,
    IAiStepUpVerifierV1 stepUpVerifier,
    ICurrentUser currentUser,
    ICompanyContext companyContext,
    IErpAuthorityRevalidationService authority,
    IActorSessionBindingResolver sessionBindingResolver,
    ICorrelationContext correlationContext,
    ISecurityEventWriterV1 securityEvents,
    TimeProvider timeProvider)
{
    private const string CompanyArgumentName = "companyId";

    public async Task<AiCommandPipelineResultV1> ProposeAsync(AiCommandProposalRequestV1? request, CancellationToken cancellationToken = default)
    {
        var (caller, failure) = await GateAsync(cancellationToken);
        if (caller is null)
        {
            return failure!;
        }

        var question = request?.Question?.Trim();
        if (string.IsNullOrEmpty(question) || question.Length > caller.Limits.MaxQuestionLength
            || !TryReadHint(request!, out var hint))
        {
            return await RefuseAsync(caller, null, null, AiCommandStatusV1.Invalid, AiCommandReasonCodesV1.InvalidProposal, null, cancellationToken);
        }

        AiModelPlanV1 plan;
        try
        {
            var available = catalog.Descriptors
                .Where(descriptor => descriptor.DataClass <= caller.Limits.MaxDataClass)
                .Select(descriptor => new AiModelToolV1(descriptor.ToolId, descriptor.Version, descriptor.DataClass, descriptor.FieldAllowlist))
                .ToArray();
            plan = await provider.PlanAsync(new AiModelRequestV1(question, available, hint), cancellationToken);
        }
        catch (AiExternalEgressRefusedException)
        {
            return await RefuseAsync(caller, null, null, AiCommandStatusV1.Refused, AiCommandReasonCodesV1.InvalidProposal, null, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return await RefuseAsync(caller, null, null, AiCommandStatusV1.Unavailable, AiCommandReasonCodesV1.ProviderFailed, null, cancellationToken);
        }

        // Model output is untrusted: exactly one planned command, nothing else.
        if (plan?.ToolCalls is not { Count: 1 } calls || calls[0] is not { } call)
        {
            return await RefuseAsync(caller, null, null, AiCommandStatusV1.Refused, AiCommandReasonCodesV1.InvalidProposal, null, cancellationToken);
        }

        var proposedToolId = AiToolDescriptorV1.IsWellFormedToolId(call.ToolId) ? call.ToolId : null;
        var proposedVersion = proposedToolId is not null && IsBoundedToken(call.ToolVersion) ? call.ToolVersion : null;
        if (!await ledger.AppendAsync(LedgerEvent(caller, AiCommandEventKindV1.Proposed, null,
                proposedVersion is null ? null : proposedToolId, proposedVersion), cancellationToken))
        {
            return AiCommandPipelineResultV1.Of(AiCommandStatusV1.Unavailable, AiCommandReasonCodesV1.AuditUnavailable);
        }

        if (catalog.IsClosed(call.ToolId))
        {
            return await RefuseAsync(caller, null, null, AiCommandStatusV1.Refused, AiCommandReasonCodesV1.ClosedCommand, null, cancellationToken);
        }

        var tool = catalog.Resolve(call.ToolId, call.ToolVersion);
        if (tool is null)
        {
            return await RefuseAsync(caller, null, null, AiCommandStatusV1.Refused, AiCommandReasonCodesV1.ToolNotAllowed, null, cancellationToken);
        }

        var descriptor = tool.Descriptor;
        if (descriptor.DataClass > caller.Limits.MaxDataClass)
        {
            return await RefuseAsync(caller, null, descriptor, AiCommandStatusV1.Refused, AiCommandReasonCodesV1.DataClassNotAllowed, null, cancellationToken);
        }

        var (authorityRevision, missingPermission) = await MeasureAuthorityAsync(caller, descriptor, cancellationToken);
        if (authorityRevision is null)
        {
            return await RefuseAsync(caller, null, descriptor, AiCommandStatusV1.Refused, AiCommandReasonCodesV1.PermissionMissing, missingPermission, cancellationToken);
        }

        if (!TryCanonicalizeArguments(call.ArgumentsJson, descriptor, caller.Limits, out var argumentsJson))
        {
            return await RefuseAsync(caller, null, descriptor, AiCommandStatusV1.Invalid, AiCommandReasonCodesV1.InvalidProposal, null, cancellationToken);
        }

        var context = ServerContext(caller, authorityRevision);
        AiPreviewResultV1? previewResult;
        try
        {
            previewResult = await tool.PreviewAsync(new AiToolInputV1(argumentsJson), context, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            previewResult = null;
        }

        var now = Now();
        if (previewResult is not { Outcome: AiToolOutcomeV1.Succeeded, Preview: { } preview })
        {
            var (status, reason) = previewResult?.Outcome switch
            {
                AiToolOutcomeV1.NotFound => (AiCommandStatusV1.NotFound, AiCommandReasonCodesV1.OwnerPreviewRefused),
                AiToolOutcomeV1.Denied => (AiCommandStatusV1.Refused, AiCommandReasonCodesV1.PermissionMissing),
                AiToolOutcomeV1.Invalid => (AiCommandStatusV1.Invalid, AiCommandReasonCodesV1.OwnerPreviewRefused),
                AiToolOutcomeV1.Conflict => (AiCommandStatusV1.Refused, AiCommandReasonCodesV1.PreviewStale),
                null => (AiCommandStatusV1.Unavailable, AiCommandReasonCodesV1.OwnerFailed),
                _ => (AiCommandStatusV1.Unavailable, AiCommandReasonCodesV1.OwnerPreviewRefused),
            };
            return await RefuseAsync(caller, null, descriptor, status, reason, null, cancellationToken);
        }

        if (preview.CreatedAtUtc > now || preview.ExpiresAtUtc <= now
            || preview.Sources.Any(source => source.DataClass > descriptor.DataClass || source.DataClass > caller.Limits.MaxDataClass))
        {
            return await RefuseAsync(caller, null, descriptor, AiCommandStatusV1.Refused, AiCommandReasonCodesV1.OwnerPreviewRefused, null, cancellationToken);
        }

        string identity;
        string hash;
        try
        {
            identity = AiCanonicalJson.PreviewIdentity(context, descriptor, preview);
            hash = AiCanonicalJson.PreviewHash(context, descriptor, preview);
        }
        catch (ArgumentException)
        {
            return await RefuseAsync(caller, null, descriptor, AiCommandStatusV1.Refused, AiCommandReasonCodesV1.OwnerPreviewRefused, null, cancellationToken);
        }

        var lifetimeEnd = now + caller.PreviewLifetime;
        var record = new AiCommandRecordV1(
            preview.Id,
            context,
            descriptor.ToolId,
            descriptor.Version,
            argumentsJson,
            preview,
            identity,
            hash,
            preview.ExpiresAtUtc < lifetimeEnd ? preview.ExpiresAtUtc : lifetimeEnd,
            AiCommandStateV1.Previewed,
            Version: 1);
        if (!await ledger.TryAddAsync(record, [LedgerEvent(caller, AiCommandEventKindV1.Previewed, record)], cancellationToken))
        {
            return AiCommandPipelineResultV1.Of(AiCommandStatusV1.Unavailable, AiCommandReasonCodesV1.AuditUnavailable);
        }

        return new AiCommandPipelineResultV1(AiCommandStatusV1.Previewed, Preview: View(record));
    }

    public async Task<AiCommandPipelineResultV1> ApproveAsync(Guid previewId, string? previewHash, CancellationToken cancellationToken = default)
    {
        var (caller, failure) = await GateAsync(cancellationToken);
        if (caller is null)
        {
            return failure!;
        }

        var (record, missing) = await LoadOwnRecordAsync(caller, previewId, cancellationToken);
        if (record is null)
        {
            return missing!;
        }

        if (!HashMatches(record, previewHash))
        {
            return await RefuseAsync(caller, record, null, AiCommandStatusV1.Refused, AiCommandReasonCodesV1.PreviewHashMismatch, null, cancellationToken);
        }

        // A resent approval of the same digest is idempotent: it returns the approval already recorded.
        if (record.State == AiCommandStateV1.Approved)
        {
            return new AiCommandPipelineResultV1(AiCommandStatusV1.Approved, Preview: View(record), ApprovalId: record.ApprovalId, IsReplay: true);
        }

        if (record.State != AiCommandStateV1.Previewed)
        {
            return await TerminalAsync(caller, record, cancellationToken);
        }

        var (tool, bindingFailure) = await CheckBindingAsync(caller, record, bindToPreparer: true, cancellationToken);
        if (tool is null)
        {
            return bindingFailure!;
        }

        var approvalId = Guid.NewGuid();
        var approvedAt = Now();
        try
        {
            _ = new AiAuditContextV1(record.Context, tool.Descriptor, record.Preview, approvalId, caller.ActorId, approvedAt);
        }
        catch (ArgumentException)
        {
            return await InvalidateAsync(caller, record, tool.Descriptor, AiCommandReasonCodesV1.PreviewExpired, cancellationToken);
        }

        var approved = record with
        {
            State = AiCommandStateV1.Approved,
            Version = record.Version + 1,
            ApprovalId = approvalId,
            ApprovedBy = caller.ActorId,
            ApprovedAtUtc = approvedAt,
        };
        if (!await ledger.TryReplaceAsync(approved, record.Version,
                LedgerEvent(caller, AiCommandEventKindV1.Approved, approved, approvalId: approvalId), cancellationToken))
        {
            var current = await ledger.GetAsync(caller.CompanyId, previewId, cancellationToken);
            return current is { State: AiCommandStateV1.Approved } && HashMatches(current, previewHash)
                ? new AiCommandPipelineResultV1(AiCommandStatusV1.Approved, Preview: View(current), ApprovalId: current.ApprovalId, IsReplay: true)
                : await RefuseAsync(caller, record, tool.Descriptor, AiCommandStatusV1.Refused, AiCommandReasonCodesV1.Concurrent, null, cancellationToken);
        }

        return new AiCommandPipelineResultV1(AiCommandStatusV1.Approved, Preview: View(approved), ApprovalId: approvalId);
    }

    public async Task<AiCommandPipelineResultV1> RejectAsync(Guid previewId, string? previewHash, CancellationToken cancellationToken = default)
    {
        var (caller, failure) = await GateAsync(cancellationToken);
        if (caller is null)
        {
            return failure!;
        }

        var (record, missing) = await LoadOwnRecordAsync(caller, previewId, cancellationToken);
        if (record is null)
        {
            return missing!;
        }

        if (!HashMatches(record, previewHash))
        {
            return await RefuseAsync(caller, record, null, AiCommandStatusV1.Refused, AiCommandReasonCodesV1.PreviewHashMismatch, null, cancellationToken);
        }

        if (record.State == AiCommandStateV1.Rejected)
        {
            return new AiCommandPipelineResultV1(AiCommandStatusV1.Rejected, IsReplay: true);
        }

        if (record.State is not (AiCommandStateV1.Previewed or AiCommandStateV1.Approved))
        {
            return await TerminalAsync(caller, record, cancellationToken);
        }

        var rejected = record with { State = AiCommandStateV1.Rejected, Version = record.Version + 1 };
        if (!await ledger.TryReplaceAsync(rejected, record.Version,
                LedgerEvent(caller, AiCommandEventKindV1.Rejected, rejected, approvalId: record.ApprovalId), cancellationToken))
        {
            return await RefuseAsync(caller, record, null, AiCommandStatusV1.Refused, AiCommandReasonCodesV1.Concurrent, null, cancellationToken);
        }

        return AiCommandPipelineResultV1.Of(AiCommandStatusV1.Rejected);
    }

    public async Task<AiCommandPipelineResultV1> ExecuteAsync(Guid previewId, string? previewHash, Guid approvalId, CancellationToken cancellationToken = default)
    {
        var (caller, failure) = await GateAsync(cancellationToken);
        if (caller is null)
        {
            return failure!;
        }

        var (record, missing) = await LoadOwnRecordAsync(caller, previewId, cancellationToken);
        if (record is null)
        {
            return missing!;
        }

        if (!HashMatches(record, previewHash))
        {
            return await RefuseAsync(caller, record, null, AiCommandStatusV1.Refused, AiCommandReasonCodesV1.PreviewHashMismatch, null, cancellationToken);
        }

        if (record.State == AiCommandStateV1.Previewed)
        {
            return await RefuseAsync(caller, record, null, AiCommandStatusV1.Refused, AiCommandReasonCodesV1.ApprovalRequired, null, cancellationToken);
        }

        if (record.ApprovalId is not { } recordedApproval || recordedApproval != approvalId)
        {
            return await RefuseAsync(caller, record, null, AiCommandStatusV1.Refused, AiCommandReasonCodesV1.ApprovalMismatch, null, cancellationToken);
        }

        switch (record.State)
        {
            case AiCommandStateV1.Executed:
                return Replay(record);
            case AiCommandStateV1.Executing or AiCommandStateV1.Unknown:
                return await LookupAsync(caller, record, cancellationToken);
            case AiCommandStateV1.Approved:
                // YOL-D27: no execution before a different authorized person's second approval.
                return await RefuseAsync(caller, record, null, AiCommandStatusV1.Refused, AiCommandReasonCodesV1.SecondApprovalRequired, null, cancellationToken);
            case AiCommandStateV1.SecondApproved:
                break;
            default:
                return await TerminalAsync(caller, record, cancellationToken);
        }

        var (tool, bindingFailure) = await CheckBindingAsync(caller, record, bindToPreparer: true, cancellationToken);
        if (tool is null)
        {
            return bindingFailure!;
        }

        var descriptor = tool.Descriptor;
        if (!TryBuildOwnerRequest(record, descriptor, out var ownerRequest))
        {
            return await InvalidateAsync(caller, record, descriptor, AiCommandReasonCodesV1.ApprovalMismatch, cancellationToken);
        }

        // Group E: the owner call is recorded before it runs; if it cannot be recorded, nothing runs.
        var started = await securityEvents.WriteAsync(
            NewEvent(SecurityEventTypesV1.AiToolCalled, SecurityEventPhasesV1.Started, SecurityEventOutcomesV1.Allowed, null,
                SecurityEventSeveritiesV1.Info, caller.CompanyId, caller.ActorId, null, descriptor),
            cancellationToken);
        if (!started.Recorded || !started.CallerMayContinue)
        {
            return AiCommandPipelineResultV1.Of(AiCommandStatusV1.Unavailable, AiCommandReasonCodesV1.AuditUnavailable);
        }

        var executing = record with { State = AiCommandStateV1.Executing, Version = record.Version + 1 };
        if (!await ledger.TryReplaceAsync(executing, record.Version,
                LedgerEvent(caller, AiCommandEventKindV1.ExecutionStarted, executing, approvalId: approvalId), cancellationToken))
        {
            await WriteCompletedAsync(caller, descriptor, released: false, cancellationToken);
            var current = await ledger.GetAsync(caller.CompanyId, previewId, cancellationToken);
            return current?.State switch
            {
                AiCommandStateV1.Executed => Replay(current),
                AiCommandStateV1.Executing or AiCommandStateV1.Unknown => await LookupAsync(caller, current, cancellationToken),
                _ => await RefuseAsync(caller, record, descriptor, AiCommandStatusV1.Refused, AiCommandReasonCodesV1.Concurrent, null, cancellationToken),
            };
        }

        AiOwnerRecordedResultViewV1 view;
        try
        {
            var recorded = await tool.ExecuteAsync(ownerRequest, cancellationToken);
            view = recorded.ReadFor(ownerRequest);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The owner may or may not have applied it: the outcome stays unknown and a resend only looks it up.
            await WriteCompletedAsync(caller, descriptor, released: false, cancellationToken);
            return await SettleAsync(caller, executing, descriptor, AiCommandOutcomeV1.Unknown, null, null, isReplay: false, cancellationToken);
        }

        await WriteCompletedAsync(caller, descriptor, released: view.Result.Outcome == AiCommandOutcomeV1.Applied, cancellationToken);
        return await SettleAsync(caller, executing, descriptor, view.Result.Outcome, view.Result.ResultVersion,
            view.Result.RefusalCode, view.Result.IsReplay, cancellationToken);
    }

    /// <summary>
    /// The preview as an authorized second approver sees it (YOL-D27): same company, the tool's permissions held now,
    /// first approval recorded. The preparer and the first approver get it from their own preview/approval results.
    /// </summary>
    public async Task<AiCommandPipelineResultV1> ReviewAsync(Guid previewId, CancellationToken cancellationToken = default)
    {
        var (caller, failure) = await GateAsync(cancellationToken);
        if (caller is null)
        {
            return failure!;
        }

        var (record, missing) = await LoadCompanyRecordAsync(caller, previewId, cancellationToken);
        if (record is null)
        {
            return missing!;
        }

        var tool = catalog.Resolve(record.ToolId, record.ToolVersion);
        if (tool is null || catalog.IsClosed(record.ToolId))
        {
            return await RefuseAsync(caller, record, null, AiCommandStatusV1.Refused, AiCommandReasonCodesV1.ToolNotAllowed, null, cancellationToken);
        }

        var (authorityRevision, missingPermission) = await MeasureAuthorityAsync(caller, tool.Descriptor, cancellationToken);
        if (authorityRevision is null)
        {
            return await RefuseAsync(caller, record, tool.Descriptor, AiCommandStatusV1.Refused, AiCommandReasonCodesV1.PermissionMissing, missingPermission, cancellationToken);
        }

        return record.State == AiCommandStateV1.Approved
            ? new AiCommandPipelineResultV1(AiCommandStatusV1.Approved, Preview: View(record), ApprovalId: record.ApprovalId)
            : await TerminalAsync(caller, record, cancellationToken);
    }

    /// <summary>
    /// Decision 156 + YOL-D27 second approval: a different authorized person than the preparer and the first approver,
    /// bound to the same digest and approval id, with the record previewed again under their authority and their
    /// password typed again (LocalAccount verification, login lockout). The same person is refused.
    /// </summary>
    public async Task<AiCommandPipelineResultV1> SecondApproveAsync(
        Guid previewId, string? previewHash, Guid approvalId, string? password, CancellationToken cancellationToken = default)
    {
        var (caller, failure) = await GateAsync(cancellationToken);
        if (caller is null)
        {
            return failure!;
        }

        var (record, missing) = await LoadCompanyRecordAsync(caller, previewId, cancellationToken);
        if (record is null)
        {
            return missing!;
        }

        if (string.Equals(caller.ActorId, record.ActorId, StringComparison.Ordinal)
            || string.Equals(caller.ActorId, record.ApprovedBy, StringComparison.Ordinal))
        {
            return await RefuseAsync(caller, record, null, AiCommandStatusV1.Refused, AiCommandReasonCodesV1.SamePerson, null, cancellationToken);
        }

        if (!HashMatches(record, previewHash))
        {
            return await RefuseAsync(caller, record, null, AiCommandStatusV1.Refused, AiCommandReasonCodesV1.PreviewHashMismatch, null, cancellationToken);
        }

        if (record.ApprovalId is not { } recordedApproval || recordedApproval != approvalId)
        {
            return await RefuseAsync(caller, record, null,
                AiCommandStatusV1.Refused,
                record.State == AiCommandStateV1.Previewed ? AiCommandReasonCodesV1.ApprovalRequired : AiCommandReasonCodesV1.ApprovalMismatch,
                null, cancellationToken);
        }

        // A resent second approval by the same second approver is idempotent.
        if (record.SecondApprovedBy is not null)
        {
            return string.Equals(record.SecondApprovedBy, caller.ActorId, StringComparison.Ordinal)
                ? new AiCommandPipelineResultV1(AiCommandStatusV1.SecondApproved, Preview: View(record), ApprovalId: record.ApprovalId, IsReplay: true)
                : await RefuseAsync(caller, record, null, AiCommandStatusV1.Refused, AiCommandReasonCodesV1.Terminal, null, cancellationToken);
        }

        if (record.State != AiCommandStateV1.Approved)
        {
            return await TerminalAsync(caller, record, cancellationToken);
        }

        var (tool, bindingFailure) = await CheckBindingAsync(caller, record, bindToPreparer: false, cancellationToken);
        if (tool is null)
        {
            return bindingFailure!;
        }

        bool verified;
        try
        {
            verified = await stepUpVerifier.IsVerifiedAsync(
                new AiStepUpChallengeV1(caller.CompanyId, caller.ActorId, caller.SessionId, record.PreviewId, record.PreviewHash),
                password,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            verified = false;
        }

        if (!verified)
        {
            return await RefuseAsync(caller, record, tool.Descriptor, AiCommandStatusV1.Refused, AiCommandReasonCodesV1.StepUpRequired, null, cancellationToken);
        }

        var secondApprovalId = Guid.NewGuid();
        var secondApproved = record with
        {
            State = AiCommandStateV1.SecondApproved,
            Version = record.Version + 1,
            SecondApprovalId = secondApprovalId,
            SecondApprovedBy = caller.ActorId,
            SecondApprovedAtUtc = Now(),
        };
        if (!await ledger.TryReplaceAsync(secondApproved, record.Version,
                LedgerEvent(caller, AiCommandEventKindV1.SecondApproved, secondApproved, approvalId: secondApprovalId), cancellationToken))
        {
            return await RefuseAsync(caller, record, tool.Descriptor, AiCommandStatusV1.Refused, AiCommandReasonCodesV1.Concurrent, null, cancellationToken);
        }

        return new AiCommandPipelineResultV1(AiCommandStatusV1.SecondApproved, Preview: View(secondApproved), ApprovalId: record.ApprovalId);
    }

    private async Task<AiCommandPipelineResultV1> LookupAsync(Caller caller, AiCommandRecordV1 record, CancellationToken cancellationToken)
    {
        var tool = catalog.Resolve(record.ToolId, record.ToolVersion);
        if (tool is null)
        {
            return await RefuseAsync(caller, record, null, AiCommandStatusV1.Refused,
                catalog.IsClosed(record.ToolId) ? AiCommandReasonCodesV1.ClosedCommand : AiCommandReasonCodesV1.ToolNotAllowed, null, cancellationToken);
        }

        if (!TryBuildOwnerRequest(record, tool.Descriptor, out var ownerRequest))
        {
            return AiCommandPipelineResultV1.Of(AiCommandStatusV1.Unknown, AiCommandReasonCodesV1.OwnerFailed);
        }

        AiOwnerLookupViewV1 lookup;
        try
        {
            lookup = (await tool.FindResultAsync(ownerRequest, cancellationToken)).ReadFor(ownerRequest);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return AiCommandPipelineResultV1.Of(AiCommandStatusV1.Unknown, AiCommandReasonCodesV1.OwnerFailed);
        }

        var result = lookup.RecordedResult?.Result;
        return lookup.Outcome switch
        {
            AiCommandOutcomeV1.Applied or AiCommandOutcomeV1.Refused => await SettleAsync(caller, record, tool.Descriptor,
                lookup.Outcome, result?.ResultVersion, result?.RefusalCode, isReplay: true, cancellationToken),
            // Definitive owner evidence that the frozen request was not admitted and cannot take effect: the approval
            // is spent, the user prepares a new preview.
            AiCommandOutcomeV1.NotRecorded => await InvalidateAsync(caller, record, tool.Descriptor, AiCommandReasonCodesV1.OwnerFailed, cancellationToken),
            _ => AiCommandPipelineResultV1.Of(AiCommandStatusV1.Unknown),
        };
    }

    private async Task<AiCommandPipelineResultV1> SettleAsync(
        Caller caller,
        AiCommandRecordV1 record,
        AiToolDescriptorV1 descriptor,
        AiCommandOutcomeV1 outcome,
        string? resultVersion,
        string? refusalCode,
        bool isReplay,
        CancellationToken cancellationToken)
    {
        var (state, kind, reason) = outcome switch
        {
            AiCommandOutcomeV1.Applied => (AiCommandStateV1.Executed, AiCommandEventKindV1.Executed, (string?)null),
            AiCommandOutcomeV1.Refused => (AiCommandStateV1.Refused, AiCommandEventKindV1.Refused, AiCommandReasonCodesV1.OwnerRefused),
            _ => (AiCommandStateV1.Unknown, AiCommandEventKindV1.OutcomeUnknown, (string?)null),
        };
        if (state != record.State)
        {
            var settled = record with
            {
                State = state,
                Version = record.Version + 1,
                ResultVersion = resultVersion,
                ReasonCode = state == AiCommandStateV1.Refused ? refusalCode : null,
            };
            // The owner outcome stands whether or not this ledger write wins; a lost write is reconciled by the next lookup.
            _ = await ledger.TryReplaceAsync(settled, record.Version,
                LedgerEvent(caller, kind, settled, approvalId: record.ApprovalId, reasonCode: reason, resultVersion: resultVersion),
                cancellationToken);
        }

        return state switch
        {
            AiCommandStateV1.Executed => new AiCommandPipelineResultV1(AiCommandStatusV1.Executed, Preview: View(record),
                ApprovalId: record.ApprovalId, ResultVersion: resultVersion, IsReplay: isReplay),
            AiCommandStateV1.Refused => await RefuseAsync(caller, null, descriptor, AiCommandStatusV1.Refused,
                refusalCode ?? AiCommandReasonCodesV1.OwnerRefused, null, cancellationToken, recordLedger: false),
            _ => AiCommandPipelineResultV1.Of(AiCommandStatusV1.Unknown),
        };
    }

    /// <summary>
    /// Everything an approval or execution re-checks against what was previewed (AI-D34 a): tool still admitted and not
    /// closed, stored identity intact, not expired, same session, permissions held now, same authority revision, and the
    /// owner's fresh preview of the same input still equals the approved target version, command and difference.
    /// </summary>
    private async Task<(IAiOwnerCommandToolV1? Tool, AiCommandPipelineResultV1? Failure)> CheckBindingAsync(
        Caller caller, AiCommandRecordV1 record, bool bindToPreparer, CancellationToken cancellationToken)
    {
        if (catalog.IsClosed(record.ToolId))
        {
            return (null, await RefuseAsync(caller, record, null, AiCommandStatusV1.Refused, AiCommandReasonCodesV1.ClosedCommand, null, cancellationToken));
        }

        var tool = catalog.Resolve(record.ToolId, record.ToolVersion);
        if (tool is null)
        {
            return (null, await RefuseAsync(caller, record, null, AiCommandStatusV1.Refused, AiCommandReasonCodesV1.ToolNotAllowed, null, cancellationToken));
        }

        var descriptor = tool.Descriptor;
        if (!IdentityIntact(record, descriptor))
        {
            return (null, await InvalidateAsync(caller, record, descriptor, AiCommandReasonCodesV1.PreviewStale, cancellationToken));
        }

        if (Now() >= record.ExpiresAtUtc)
        {
            return (null, await InvalidateAsync(caller, record, descriptor, AiCommandReasonCodesV1.PreviewExpired, cancellationToken));
        }

        // The preparer stays bound to the frozen session and authority; a second approver is measured on their own.
        if (bindToPreparer && !string.Equals(caller.SessionId, record.Context.SessionId, StringComparison.Ordinal))
        {
            return (null, await RefuseAsync(caller, record, descriptor, AiCommandStatusV1.Refused, AiCommandReasonCodesV1.SessionInvalid, null, cancellationToken));
        }

        var (authorityRevision, missingPermission) = await MeasureAuthorityAsync(caller, descriptor, cancellationToken);
        if (authorityRevision is null)
        {
            return (null, await RefuseAsync(caller, record, descriptor, AiCommandStatusV1.Refused, AiCommandReasonCodesV1.PermissionMissing, missingPermission, cancellationToken));
        }

        if (bindToPreparer && !string.Equals(authorityRevision, record.Context.AuthorityRevision, StringComparison.Ordinal))
        {
            return (null, await InvalidateAsync(caller, record, descriptor, AiCommandReasonCodesV1.AuthorityChanged, cancellationToken));
        }

        AiPreviewResultV1? fresh;
        try
        {
            fresh = await tool.PreviewAsync(new AiToolInputV1(record.CanonicalArgumentsJson), ServerContext(caller, authorityRevision), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (null, await RefuseAsync(caller, record, descriptor, AiCommandStatusV1.Unavailable, AiCommandReasonCodesV1.OwnerFailed, null, cancellationToken));
        }

        if (fresh is not { Outcome: AiToolOutcomeV1.Succeeded, Preview: { } current } || !SameEffect(record.Preview, current))
        {
            return (null, await InvalidateAsync(caller, record, descriptor, AiCommandReasonCodesV1.PreviewStale, cancellationToken));
        }

        return (tool, null);
    }

    private static bool SameEffect(AiPreviewV1 approved, AiPreviewV1 current) =>
        string.Equals(approved.TargetKind, current.TargetKind, StringComparison.Ordinal)
        && approved.TargetId == current.TargetId
        && string.Equals(approved.TargetVersion, current.TargetVersion, StringComparison.Ordinal)
        && string.Equals(approved.CanonicalCommandJson, current.CanonicalCommandJson, StringComparison.Ordinal)
        && string.Equals(approved.DifferenceJson, current.DifferenceJson, StringComparison.Ordinal);

    private static bool IdentityIntact(AiCommandRecordV1 record, AiToolDescriptorV1 descriptor)
    {
        try
        {
            return record.Preview.Id == record.PreviewId
                && string.Equals(AiCanonicalJson.PreviewIdentity(record.Context, descriptor, record.Preview), record.PreviewIdentityJson, StringComparison.Ordinal)
                && string.Equals(AiCanonicalJson.PreviewHash(record.Context, descriptor, record.Preview), record.PreviewHash, StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>The frozen owner request is rebuilt only from stored values, so execution and lookup carry one identity.</summary>
    private static bool TryBuildOwnerRequest(AiCommandRecordV1 record, AiToolDescriptorV1 descriptor, out AiOwnerCommandRequestV1 request)
    {
        request = null!;
        if (record.ApprovalId is not { } approvalId || record.ApprovedBy is not { } approvedBy || record.ApprovedAtUtc is not { } approvedAt)
        {
            return false;
        }

        try
        {
            var audit = new AiAuditContextV1(record.Context, descriptor, record.Preview, approvalId, approvedBy, approvedAt);
            request = new AiOwnerCommandRequestV1(descriptor, record.Context, record.Preview,
                new AiCommandContextV1(record.PreviewId, audit), record.PreviewIdentityJson);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private async Task<(AiCommandRecordV1? Record, AiCommandPipelineResultV1? Failure)> LoadOwnRecordAsync(
        Caller caller, Guid previewId, CancellationToken cancellationToken)
    {
        // Company-scoped read: another company's preview is absent, and another actor's preview is not disclosed.
        var record = previewId == Guid.Empty ? null : await ledger.GetAsync(caller.CompanyId, previewId, cancellationToken);
        if (record is null || record.CompanyId != caller.CompanyId)
        {
            return (null, await RefuseAsync(caller, null, null, AiCommandStatusV1.NotFound, AiCommandReasonCodesV1.CompanyContextMismatch, null, cancellationToken));
        }

        if (!string.Equals(record.ActorId, caller.ActorId, StringComparison.Ordinal))
        {
            return (null, await RefuseAsync(caller, null, null, AiCommandStatusV1.NotFound, AiCommandReasonCodesV1.NotActor, null, cancellationToken));
        }

        return (record, null);
    }

    /// <summary>Company-scoped read for a second approver: another company's preview is absent.</summary>
    private async Task<(AiCommandRecordV1? Record, AiCommandPipelineResultV1? Failure)> LoadCompanyRecordAsync(
        Caller caller, Guid previewId, CancellationToken cancellationToken)
    {
        var record = previewId == Guid.Empty ? null : await ledger.GetAsync(caller.CompanyId, previewId, cancellationToken);
        return record is null || record.CompanyId != caller.CompanyId
            ? (null, await RefuseAsync(caller, null, null, AiCommandStatusV1.NotFound, AiCommandReasonCodesV1.CompanyContextMismatch, null, cancellationToken))
            : (record, null);
    }

    private async Task<AiCommandPipelineResultV1> TerminalAsync(Caller caller, AiCommandRecordV1 record, CancellationToken cancellationToken) =>
        record.State switch
        {
            AiCommandStateV1.Invalidated => await RefuseAsync(caller, record, null, AiCommandStatusV1.Invalidated,
                record.ReasonCode ?? AiCommandReasonCodesV1.Terminal, null, cancellationToken),
            AiCommandStateV1.Rejected => await RefuseAsync(caller, record, null, AiCommandStatusV1.Rejected,
                AiCommandReasonCodesV1.Terminal, null, cancellationToken),
            _ => await RefuseAsync(caller, record, null, AiCommandStatusV1.Refused,
                record.State == AiCommandStateV1.Refused ? record.ReasonCode ?? AiCommandReasonCodesV1.OwnerRefused : AiCommandReasonCodesV1.Terminal,
                null, cancellationToken),
        };

    private async Task<AiCommandPipelineResultV1> InvalidateAsync(
        Caller caller, AiCommandRecordV1 record, AiToolDescriptorV1? descriptor, string reasonCode, CancellationToken cancellationToken)
    {
        var invalidated = record with { State = AiCommandStateV1.Invalidated, Version = record.Version + 1, ReasonCode = reasonCode };
        _ = await ledger.TryReplaceAsync(invalidated, record.Version,
            LedgerEvent(caller, AiCommandEventKindV1.Invalidated, invalidated, approvalId: record.ApprovalId, reasonCode: reasonCode),
            cancellationToken);
        return await RefuseAsync(caller, null, descriptor, AiCommandStatusV1.Invalidated, reasonCode, null, cancellationToken, recordLedger: false);
    }

    private AiCommandPipelineResultV1 Replay(AiCommandRecordV1 record) =>
        new(AiCommandStatusV1.Executed, Preview: View(record), ApprovalId: record.ApprovalId, ResultVersion: record.ResultVersion, IsReplay: true);

    private async Task<(Caller? Caller, AiCommandPipelineResultV1? Failure)> GateAsync(CancellationToken cancellationToken)
    {
        // Without correlation metadata no security event can be written, so nothing may run.
        if (!correlationContext.IsResolved)
        {
            return (null, AiCommandPipelineResultV1.Of(AiCommandStatusV1.Unavailable, AiCommandReasonCodesV1.PipelineUnavailable));
        }

        var actorId = ResolveActorId();
        var state = AiRuntimeStateEvaluator.Evaluate(runtimeOptions, environment, provider);
        AiCommandOptions? options;
        try
        {
            options = commandOptions.Value;
        }
        catch (InvalidOperationException)
        {
            options = null;
        }

        if (!state.IsReady || options is not { Enabled: true })
        {
            await SecurityRefusalAsync(AiCommandReasonCodesV1.PipelineDisabled, ResolvedCompanyOrNull(actorId), actorId, null, null, cancellationToken);
            return (null, AiCommandPipelineResultV1.Of(
                state.Availability == AiRuntimeAvailability.Disabled || (state.IsReady && options is { Enabled: false })
                    ? AiCommandStatusV1.Disabled
                    : AiCommandStatusV1.Unavailable,
                AiCommandReasonCodesV1.PipelineDisabled));
        }

        if (options.PreviewLifetime is not { } lifetime || lifetime <= TimeSpan.Zero || !ledger.IsAvailable)
        {
            await SecurityRefusalAsync(AiCommandReasonCodesV1.PipelineUnavailable, ResolvedCompanyOrNull(actorId), actorId, null, null, cancellationToken);
            return (null, AiCommandPipelineResultV1.Of(AiCommandStatusV1.Unavailable, AiCommandReasonCodesV1.PipelineUnavailable));
        }

        if (actorId is null)
        {
            await SecurityRefusalAsync(AiCommandReasonCodesV1.SessionInvalid, null, null, null, null, cancellationToken);
            return (null, AiCommandPipelineResultV1.Of(AiCommandStatusV1.Refused, AiCommandReasonCodesV1.SessionInvalid));
        }

        if (ResolvedCompanyOrNull(actorId) is not { } companyId)
        {
            await SecurityRefusalAsync(AiCommandReasonCodesV1.CompanyContextMismatch, null, actorId, null, null, cancellationToken);
            return (null, AiCommandPipelineResultV1.Of(AiCommandStatusV1.Refused, AiCommandReasonCodesV1.CompanyContextMismatch));
        }

        var panelScope = await authority.RevalidatePermissionScopeAsync(ErpPermissions.AiPanelUse, companyId, cancellationToken);
        if (!IsCompanyWideGrant(panelScope))
        {
            await SecurityRefusalAsync(AiCommandReasonCodesV1.PermissionMissing, companyId, actorId, ErpPermissions.AiPanelUse, null, cancellationToken);
            return (null, AiCommandPipelineResultV1.Of(AiCommandStatusV1.Refused, AiCommandReasonCodesV1.PermissionMissing));
        }

        var sessionId = await sessionBindingResolver.ResolveAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            await SecurityRefusalAsync(AiCommandReasonCodesV1.SessionInvalid, companyId, actorId, null, null, cancellationToken);
            return (null, AiCommandPipelineResultV1.Of(AiCommandStatusV1.Refused, AiCommandReasonCodesV1.SessionInvalid));
        }

        return (new Caller(companyId, actorId, sessionId, state.Limits!, lifetime), null);
    }

    private async Task<(string? Revision, string? MissingPermission)> MeasureAuthorityAsync(
        Caller caller, AiToolDescriptorV1 descriptor, CancellationToken cancellationToken)
    {
        var panelScope = await authority.RevalidatePermissionScopeAsync(ErpPermissions.AiPanelUse, caller.CompanyId, cancellationToken);
        if (!IsCompanyWideGrant(panelScope))
        {
            return (null, ErpPermissions.AiPanelUse);
        }

        var roles = new SortedSet<string>(panelScope.RoleCodes, StringComparer.Ordinal);
        foreach (var permission in descriptor.RequiredPermissions)
        {
            var scope = await authority.RevalidatePermissionScopeAsync(permission, caller.CompanyId, cancellationToken);
            if (!IsCompanyWideGrant(scope))
            {
                return (null, permission);
            }

            roles.UnionWith(scope.RoleCodes);
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', roles)));
        return ($"roles:{Convert.ToHexStringLower(digest)}", null);
    }

    private AiServerContextV1 ServerContext(Caller caller, string authorityRevision) => new(
        caller.CompanyId,
        caller.ActorId,
        caller.SessionId,
        ScopeRevision: $"company:{caller.CompanyId:D}",
        AuthorityRevision: authorityRevision,
        correlationContext.CorrelationId,
        correlationContext.CausationId,
        ModelDeployment: null,
        PromptPolicyVersion: null);

    private static AiCommandPreviewViewV1 View(AiCommandRecordV1 record) => new(
        record.PreviewId,
        record.PreviewHash,
        record.ToolId,
        record.ToolVersion,
        record.Preview.TargetKind,
        record.Preview.TargetId,
        record.Preview.TargetVersion,
        record.Preview.CanonicalCommandJson,
        record.Preview.DifferenceJson,
        record.ExpiresAtUtc,
        RequiresSecondApproval: true,
        RequiresStepUp: true);

    private static bool HashMatches(AiCommandRecordV1 record, string? previewHash) =>
        previewHash is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(previewHash), Encoding.UTF8.GetBytes(record.PreviewHash));

    private async Task<AiCommandPipelineResultV1> RefuseAsync(
        Caller caller,
        AiCommandRecordV1? record,
        AiToolDescriptorV1? descriptor,
        AiCommandStatusV1 status,
        string reasonCode,
        string? permissionCode,
        CancellationToken cancellationToken,
        bool recordLedger = true)
    {
        // Refusals are fail-open with alarm: the refusal stands whether or not either event was stored.
        await SecurityRefusalAsync(reasonCode, caller.CompanyId, caller.ActorId, permissionCode, descriptor, cancellationToken);
        if (recordLedger)
        {
            _ = await ledger.AppendAsync(LedgerEvent(caller, AiCommandEventKindV1.Refused, record,
                descriptor?.ToolId, descriptor?.Version, approvalId: record?.ApprovalId, reasonCode: reasonCode), cancellationToken);
        }

        return new AiCommandPipelineResultV1(status, reasonCode);
    }

    private async Task SecurityRefusalAsync(
        string reasonCode, Guid? companyId, string? actorId, string? permissionCode, AiToolDescriptorV1? descriptor, CancellationToken cancellationToken)
    {
        var (eventType, securityReason) = reasonCode switch
        {
            AiCommandReasonCodesV1.PermissionMissing => (SecurityEventTypesV1.AiAuthorizationDenied, SecurityEventReasonCodesV1.PermissionMissing),
            AiCommandReasonCodesV1.SessionInvalid => (SecurityEventTypesV1.AiRequestRefused, SecurityEventReasonCodesV1.SessionInvalid),
            AiCommandReasonCodesV1.CompanyContextMismatch or AiCommandReasonCodesV1.NotActor =>
                (SecurityEventTypesV1.AiRequestRefused, SecurityEventReasonCodesV1.CompanyContextMismatch),
            AiCommandReasonCodesV1.ToolNotAllowed or AiCommandReasonCodesV1.ClosedCommand =>
                (SecurityEventTypesV1.AiRequestRefused, SecurityEventReasonCodesV1.AiToolNotAllowed),
            AiCommandReasonCodesV1.DataClassNotAllowed => (SecurityEventTypesV1.AiRequestRefused, SecurityEventReasonCodesV1.AiDataClassNotAllowed),
            AiCommandReasonCodesV1.ProviderFailed => (SecurityEventTypesV1.AiRequestRefused, SecurityEventReasonCodesV1.AiProviderFailed),
            AiCommandReasonCodesV1.StepUpRequired => (SecurityEventTypesV1.AiRequestRefused, SecurityEventReasonCodesV1.CredentialsInvalid),
            AiCommandReasonCodesV1.ApprovalRequired or AiCommandReasonCodesV1.ApprovalMismatch or AiCommandReasonCodesV1.SamePerson
                or AiCommandReasonCodesV1.SecondApprovalRequired
                or AiCommandReasonCodesV1.PreviewHashMismatch or AiCommandReasonCodesV1.PreviewExpired or AiCommandReasonCodesV1.PreviewStale
                or AiCommandReasonCodesV1.AuthorityChanged =>
                (SecurityEventTypesV1.AiRequestRefused, SecurityEventReasonCodesV1.AiApprovalMissing),
            AiCommandReasonCodesV1.OwnerFailed => (SecurityEventTypesV1.AiRequestRefused, SecurityEventReasonCodesV1.OperationFailed),
            _ => (SecurityEventTypesV1.AiRequestRefused, SecurityEventReasonCodesV1.PolicyRefused),
        };
        await securityEvents.WriteAsync(
            NewEvent(eventType, SecurityEventPhasesV1.Single, SecurityEventOutcomesV1.Denied, securityReason,
                SecurityEventSeveritiesV1.Warning, companyId, actorId, permissionCode, descriptor),
            cancellationToken);
    }

    private async Task WriteCompletedAsync(Caller caller, AiToolDescriptorV1 descriptor, bool released, CancellationToken cancellationToken) =>
        await securityEvents.WriteAsync(
            NewEvent(SecurityEventTypesV1.AiToolCalled, SecurityEventPhasesV1.Completed,
                released ? SecurityEventOutcomesV1.Allowed : SecurityEventOutcomesV1.Failed,
                released ? null : SecurityEventReasonCodesV1.OperationFailed,
                released ? SecurityEventSeveritiesV1.Info : SecurityEventSeveritiesV1.Warning,
                caller.CompanyId, caller.ActorId, null, descriptor),
            cancellationToken);

    private AiCommandLedgerEventV1 LedgerEvent(
        Caller caller,
        AiCommandEventKindV1 kind,
        AiCommandRecordV1? record,
        string? toolId = null,
        string? toolVersion = null,
        Guid? approvalId = null,
        string? reasonCode = null,
        string? resultVersion = null) => new(
        Guid.NewGuid(),
        caller.CompanyId,
        caller.ActorId,
        kind,
        record?.PreviewId,
        record?.ToolId ?? toolId,
        record?.ToolVersion ?? toolVersion,
        record?.PreviewHash,
        approvalId,
        reasonCode,
        resultVersion,
        correlationContext.CorrelationId,
        correlationContext.CausationId,
        Now());

    private SecurityEventV1 NewEvent(
        string eventType,
        string phase,
        string outcome,
        string? reasonCode,
        string severity,
        Guid? companyId,
        string? actorId,
        string? permissionCode,
        AiToolDescriptorV1? tool)
    {
        var policy = AiPolicyReferenceV1.Resolve(AiRuntimeOptionsReader.TryRead(runtimeOptions, out var value) ? value : null);
        return new SecurityEventV1
        {
            EventId = Guid.NewGuid(),
            OccurredAtUtc = Now(),
            ScopeKind = companyId is null ? SecurityEventScopeKindsV1.Platform : SecurityEventScopeKindsV1.Company,
            CompanyId = companyId,
            ActorType = actorId is null ? SecurityEventActorTypesV1.Anonymous : SecurityEventActorTypesV1.User,
            ActorId = actorId,
            EventType = eventType,
            Phase = phase,
            Outcome = outcome,
            ReasonCode = reasonCode,
            Severity = severity,
            PermissionCode = permissionCode,
            PolicyId = policy.PolicyId,
            PolicyVersion = policy.PolicyVersion,
            CorrelationId = correlationContext.CorrelationId,
            CausationId = correlationContext.CausationId,
            SourceChannel = SourceChannels.AiAssisted,
            AiToolId = tool?.ToolId,
            AiToolVersion = tool?.Version,
            AiRiskClass = tool?.Risk,
            AiDataClass = tool?.DataClass,
        };
    }

    private DateTimeOffset Now() => timeProvider.GetUtcNow().ToUniversalTime();

    private static bool IsCompanyWideGrant(PermissionScope? scope) => scope is { IsGranted: true, IsCompanyWide: true };

    private static bool IsBoundedToken(string? value) => value is { Length: > 0 and <= 32 } && value.All(char.IsAsciiLetterOrDigit);

    private static bool TryReadHint(AiCommandProposalRequestV1 request, out AiToolHintV1? hint)
    {
        hint = null;
        if (request.ToolId is null && request.ToolVersion is null && request.ArgumentsJson is null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(request.ToolId) || string.IsNullOrWhiteSpace(request.ToolVersion))
        {
            return false;
        }

        hint = new AiToolHintV1(request.ToolId, request.ToolVersion, request.ArgumentsJson ?? "{}");
        return true;
    }

    /// <summary>
    /// Canonical JSON object within the configured byte budget, with only the owner's allowlisted top-level fields.
    /// A company field is refused outright: the company comes from the server context, never from arguments.
    /// </summary>
    private static bool TryCanonicalizeArguments(string? argumentsJson, AiToolDescriptorV1 descriptor, AiRuntimeLimits limits, out string canonical)
    {
        canonical = string.Empty;
        if (argumentsJson is null || Encoding.UTF8.GetByteCount(argumentsJson) > limits.MaxArgumentBytes)
        {
            return false;
        }

        try
        {
            canonical = AiCanonicalJson.Canonicalize(argumentsJson);
        }
        catch (JsonException)
        {
            return false;
        }

        if (Encoding.UTF8.GetByteCount(canonical) > limits.MaxArgumentBytes)
        {
            return false;
        }

        using var document = JsonDocument.Parse(canonical);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (string.Equals(property.Name, CompanyArgumentName, StringComparison.OrdinalIgnoreCase)
                || !descriptor.FieldAllowlist.Contains(property.Name, StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private string? ResolveActorId() =>
        currentUser.IsAuthenticated && Guid.TryParse(currentUser.UserId, out var userId) && userId != Guid.Empty
            ? userId.ToString("D")
            : null;

    private Guid? ResolvedCompanyOrNull(string? actorId) =>
        actorId is not null
        && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty
        && currentUser.CanAccessCompany(companyContext.CompanyId)
            ? companyContext.CompanyId
            : null;

    private sealed record Caller(
        Guid CompanyId,
        string ActorId,
        string SessionId,
        AiRuntimeLimits Limits,
        TimeSpan PreviewLifetime);
}
