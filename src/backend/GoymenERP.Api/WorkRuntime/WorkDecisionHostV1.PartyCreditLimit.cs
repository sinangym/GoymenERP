using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Idempotency.Domain.Executions;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool PartyLimitVersion(string? text, bool increment = false) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
        && (!increment || value < long.MaxValue) && value.ToString(CultureInfo.InvariantCulture) == text;
    private static bool PartyLimitRevision(string? text) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= 0
        && value.ToString(CultureInfo.InvariantCulture) == text;
    private static bool ValidPartyCreditLimitRequest(WorkDecisionRequestV1 request) =>
        PartyLimitVersion(request.ExpectedVersion, increment: true) && (request.Approve ? request.Reason is null
            : !string.IsNullOrWhiteSpace(request.Reason) && request.Reason == request.Reason.Trim() && request.Reason.Length <= 500);

    private static bool ValidPartyCreditLimitProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot)
    {
        if (kind != PartyCreditLimit) return snapshot.PartyCreditLimitProof is null;
        if (snapshot.PartyCreditLimitProof is not { Proposal: { } p } proof || proof.CompanyId != snapshot.CompanyId
            || proof.ProposalId != snapshot.Item.ItemId || !PartyLimitVersion(snapshot.SourceVersion)
            || p.PartyId == Guid.Empty || p.CompanyAccountId == Guid.Empty
            || !PartyLimitRevision(p.BaseRevision) || !PartyLimitRevision(p.CurrentRevision)
            || string.IsNullOrWhiteSpace(p.ProposedBy) || p.ProposedBy.Length > 128 || p.ProposedBy != snapshot.Item.PreparedBy
            || p.ProposedAtUtc == default || p.ProposedAtUtc.Offset != TimeSpan.Zero || p.ProposedAtUtc != snapshot.Item.WaitingSinceUtc
            || snapshot.Item.Scope is not null || snapshot.Item.ExcludedDeciders is { Count: > 0 }
            || snapshot.Item.References.Count(r => r.Role == "party" && r.RecordId == p.PartyId) != 1
            || snapshot.Item.References.Count(r => r.Role == "company-account" && r.RecordId == p.CompanyAccountId) != 1) return false;
        return p.Kind switch
        {
            "Set" => p.CurrencyId is { } currency && currency != Guid.Empty && FinancePartyCreditLimitService.IsExactAmount(p.Amount)
                && snapshot.Item.References.Count(r => r.Role == "currency" && r.RecordId == currency) == 1,
            "Remove" => p.CurrencyId is null && p.Amount is null,
            _ => false,
        };
    }

    private async Task<WorkDecisionResultV1> DecidePartyCreditLimitAsync(Guid id, WorkDecisionRequestV1 request,
        string key, WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (partyCreditLimits is null || snapshot.PartyCreditLimitProof is not { Proposal: { } p }) throw new WorkDecisionException(503);
        var context = new FinanceContext(snapshot.CompanyId, actor, correlation.CorrelationId, correlation.CausationId);
        var metadata = new CommandMetadata(key);
        PartyCreditLimitCommandResult result;
        try
        {
            result = request.Approve
                ? await partyCreditLimits.ApproveAsync(context, metadata, new(id, request.ExpectedVersion), token)
                : await partyCreditLimits.RejectAsync(context, metadata, new(id, request.ExpectedVersion, request.Reason!), token);
        }
        catch (Exception error) when (error is InvalidOperationException or JsonException) { throw new WorkDecisionException(503); }
        await RequireDecisionAuthorityAsync(PartyCreditLimit, ErpPermissions.FinanceCreditLimitsApprove, snapshot.CompanyId, actor, token);
        if (result.Outcome != CommandOutcome.Succeeded)
            throw new WorkDecisionException(result.Outcome switch { CommandOutcome.Denied => 403, CommandOutcome.Unavailable => 503,
                CommandOutcome.VersionConflict or CommandOutcome.IdempotencyConflict => 409, _ => 422 },
                result.Issues.FirstOrDefault()?.Code, result.IsReplay, result.OriginalCorrelationId);
        var command = request.Approve ? FinancePartyCreditLimitService.ApproveCommand : FinancePartyCreditLimitService.RejectCommand;
        var hash = request.Approve
            ? FinancePartyCreditLimitService.PayloadHash(command, new PartyCreditLimitApproveRequest(id, request.ExpectedVersion))
            : FinancePartyCreditLimitService.PayloadHash(command, new PartyCreditLimitRejectRequest(id, request.ExpectedVersion, request.Reason!));
        var expectedResultVersion = (long.Parse(request.ExpectedVersion, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);
        var kindValue = p.Kind == "Set" ? PartyCreditLimitKind.Set : PartyCreditLimitKind.Remove;
        var state = request.Approve ? PartyCreditLimitStatus.Approved : PartyCreditLimitStatus.Rejected;
        if (result.Resource is not { } r || result.DecisionReceipt is not { } proof || result.Issues is not { Count: 0 }
            || r.Id != id || r.CompanyId != snapshot.CompanyId || r.PartyId != p.PartyId || r.CompanyAccountId != p.CompanyAccountId
            || r.Kind != kindValue || r.CurrencyId != p.CurrencyId || r.Amount != p.Amount || r.IsDirect || r.Status != state
            || r.ProposedAtUtc != p.ProposedAtUtc || r.Version != expectedResultVersion || r.SupersededAtUtc is not null
            || r.DecidedAtUtc is not { } decided || decided.Offset != TimeSpan.Zero || decided < p.ProposedAtUtc
            || request.Approve && (r.RejectionReason is not null || !PartyLimitRevision(r.Revision) || r.Revision == "0")
            || !request.Approve && (r.RejectionReason != request.Reason || r.Revision is not null)
            || proof.CompanyId != snapshot.CompanyId || proof.ProposalId != id || proof.CommandName != command
            || proof.IdempotencyKeyHash != IdempotencyHash.ComputeKey(key) || proof.PayloadHash != hash || proof.ActorId != actor
            || proof.ExpectedVersion != request.ExpectedVersion || proof.ResultVersion != expectedResultVersion
            || proof.RecordedAtUtc != decided || proof.RecordedAtUtc.Offset != TimeSpan.Zero
            || string.IsNullOrWhiteSpace(result.OriginalCorrelationId) || proof.OriginalCorrelationId != result.OriginalCorrelationId
            || !result.IsReplay && (!snapshot.Pending || snapshot.SourceVersion != request.ExpectedVersion
                || result.OriginalCorrelationId != correlation.CorrelationId)) throw new WorkDecisionException(503);
        // The original immutable result stays Approved/Rejected even if a later direct Set superseded the record.
        return new(snapshot.CompanyId, PartyCreditLimit, id, state.ToString(), result.IsReplay, result.OriginalCorrelationId);
    }
}
