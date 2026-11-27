using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Idempotency.Domain.Executions;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private static bool ExpensePaymentVersion(string? value, out long version, bool increment = false) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out version) && version > 0
        && (!increment || version < long.MaxValue) && version.ToString(CultureInfo.InvariantCulture) == value;

    private static bool ValidExpensePaymentProof(string kind, WorkApprovalDecisionSnapshotV1 snapshot)
    {
        if (kind != ExpensePayment) return snapshot.ExpensePaymentProof is null;
        return snapshot.ExpensePaymentProof is { } p && p.CompanyId == snapshot.CompanyId && p.PaymentId == snapshot.Item.ItemId
            && p.PaymentId != Guid.Empty && p.PreparedBy == snapshot.Item.PreparedBy && !string.IsNullOrWhiteSpace(p.PreparedBy)
            && p.PreparedAtUtc.Offset == TimeSpan.Zero && p.PreparedAtUtc == snapshot.Item.WaitingSinceUtc
            && OfficialSalesDigest(p.DraftFactsDigest) && OfficialSalesDigest(p.PreviewDigest)
            && ExpensePaymentVersion(snapshot.SourceVersion, out _)
            && (snapshot.Pending ? p.Status == "Draft" : p.Status is "Approved" or "Recorded")
            && snapshot.Item.Scope is null
            && (snapshot.Item.ExcludedDeciders is null || snapshot.Item.ExcludedDeciders.Count <= WorkInboxRulesV1.MaxExcludedDecidersPerItem
                && snapshot.Item.ExcludedDeciders.All(x => !string.IsNullOrWhiteSpace(x))
                && snapshot.Item.ExcludedDeciders.Distinct(StringComparer.Ordinal).Count() == snapshot.Item.ExcludedDeciders.Count)
            && snapshot.Item.Rules.Count == 1 && snapshot.Item.Rules[0].RuleCode == "finance.expense-payment-approval-required"
            && snapshot.Item.Rules[0].Actual is not null && snapshot.Item.Rules[0].Threshold is null
            && snapshot.Item.Rules[0].Deviation is null && snapshot.Item.Rules[0].Unit is null
            && snapshot.Item.References.Count == 3
            && snapshot.Item.References.Count(x => (x.Role is "bank-account" or "cash-account") && x.RecordId is { } treasury && treasury != Guid.Empty) == 1
            && new[] { "expense-item", "currency" }.All(role => snapshot.Item.References.Count(x => x.Role == role && x.RecordId is { } id && id != Guid.Empty) == 1);
    }

    private async Task<ExpensePaymentDecisionDetailV1> ExpensePaymentHumanPreviewAsync(WorkApprovalDecisionSnapshotV1 snapshot,
        string actor, CancellationToken token)
    {
        if (expensePayments is not IExpensePaymentDecisionReadV1 owner) throw new WorkDecisionException(503);
        ReadResult<ExpensePaymentDecisionDetailV1> result;
        try { result = await owner.ReadDecisionAsync(new(snapshot.CompanyId, actor, correlation.CorrelationId, correlation.CausationId), snapshot.Item.ItemId, Fence, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { await Fence(token); throw new WorkDecisionException(503); }
        await Fence(token);
        if (result is null) throw new WorkDecisionException(503);
        if (result.Outcome == ReadOutcome.Denied && result.Resource is null) throw new WorkDecisionException(403);
        var p = snapshot.ExpensePaymentProof!;
        if (result.Outcome != ReadOutcome.Found || result.Resource is not { } detail || detail.Payment is not { } payment
            || payment.CompanyId != snapshot.CompanyId || payment.Id != p.PaymentId || payment.Version != snapshot.SourceVersion
            || payment.PreparedBy != p.PreparedBy || payment.PreparedAtUtc != p.PreparedAtUtc || payment.Status.ToString() != p.Status
            || detail.DraftFactsDigest != p.DraftFactsDigest || ExpensePaymentService.DraftFactsDigest(payment) != p.DraftFactsDigest
            || ExpensePaymentService.DecisionPreviewDigest(detail) != p.PreviewDigest
            || !ExpensePaymentService.MatchesDecisionItem(snapshot.Item, detail)) throw new WorkDecisionException(503);
        return detail;
        Task Fence(CancellationToken ct) => RequireDecisionAuthorityAsync(ExpensePayment, FinancePermissionNames.ExpensePaymentApprove, snapshot.CompanyId, actor, ct);
    }

    private async Task<WorkDecisionResultV1> DecideExpensePaymentAsync(Guid id, WorkDecisionRequestV1 request, string key,
        WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (expensePayments is null) throw new WorkDecisionException(503);
        ExpensePaymentCommandResult result;
        try
        {
            result = await expensePayments.ApproveAsync(new(snapshot.CompanyId, actor, correlation.CorrelationId, correlation.CausationId),
                new(key), new(id, request.ExpectedVersion), token);
        }
        catch (Exception error) when (error is InvalidOperationException or JsonException) { throw new WorkDecisionException(503); }
        await RequireDecisionAuthorityAsync(ExpensePayment, FinancePermissionNames.ExpensePaymentApprove, snapshot.CompanyId, actor, token);
        if (result is null || !Enum.IsDefined(result.Outcome) || result.Issues is null) throw new WorkDecisionException(503);
        if (result.Outcome != CommandOutcome.Succeeded)
            throw new WorkDecisionException(result.Outcome switch { CommandOutcome.Denied => 403, CommandOutcome.Unavailable => 503,
                CommandOutcome.IdempotencyConflict or CommandOutcome.VersionConflict => 409, _ => 422 },
                result.Issues.FirstOrDefault()?.Code, result.IsReplay, result.OriginalCorrelationId);
        var p = snapshot.ExpensePaymentProof!;
        if (!ExpensePaymentVersion(request.ExpectedVersion, out var expected, increment: true)
            || !ExpensePaymentVersion(snapshot.SourceVersion, out var current)) throw new WorkDecisionException(503);
        var approvedVersion = (expected + 1).ToString(CultureInfo.InvariantCulture);
        // Current SourceVersion may describe a later Record. Only the original immutable Approve answer is released.
        // POST does not request the second human preview or remap a current recorded row into an approval receipt.
        if (result.Resource is not { } r || result.DecisionReceipt is not { } receipt || result.Issues.Count != 0
            || r.Id != id || r.CompanyId != snapshot.CompanyId || r.PreparedBy != p.PreparedBy || r.PreparedAtUtc != p.PreparedAtUtc
            || r.Status != ExpensePaymentStatus.Approved || r.Version != approvedVersion || r.ApprovedBy != actor || r.ApprovedBy == r.PreparedBy
            || r.ApprovedAtUtc is null || r.ApprovedAtUtc.Value.Offset != TimeSpan.Zero || r.RecordedBy is not null || r.RecordedAtUtc is not null || r.PaymentConfirmed
            || (snapshot.Pending ? current != expected : current < expected + 1)
            || receipt.CompanyId != snapshot.CompanyId || receipt.PaymentId != id || receipt.CommandName != ExpensePaymentService.ApproveCommand
            || receipt.IdempotencyKeyHash != IdempotencyHash.ComputeKey(key) || receipt.PayloadHash != ExpensePaymentService.ApprovePayloadHash(new(id, request.ExpectedVersion))
            || receipt.ActorId != actor || receipt.ExpectedVersion != request.ExpectedVersion || receipt.ResultVersion != approvedVersion
            || receipt.RecordedAtUtc != r.ApprovedAtUtc || receipt.DraftFactsDigest != p.DraftFactsDigest
            || ExpensePaymentService.DraftFactsDigest(r) != p.DraftFactsDigest
            || receipt.OriginalReceiptDigest != ExpensePaymentService.OriginalReceiptDigest(result)
            || string.IsNullOrWhiteSpace(result.OriginalCorrelationId) || receipt.OriginalCorrelationId != result.OriginalCorrelationId
            || !result.IsReplay && (!snapshot.Pending || snapshot.SourceVersion != request.ExpectedVersion || result.OriginalCorrelationId != correlation.CorrelationId))
            throw new WorkDecisionException(503);
        return new(snapshot.CompanyId, ExpensePayment, id, "Approved", result.IsReplay, result.OriginalCorrelationId);
    }
}
