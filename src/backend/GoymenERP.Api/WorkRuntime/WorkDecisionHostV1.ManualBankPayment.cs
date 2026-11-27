using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.WorkRuntime;

public sealed partial class WorkDecisionHostV1
{
    private const string ManualBankPaymentRule = "finance.manual-bank-payment-approval-required";

    private static bool ValidManualBankPaymentSnapshot(WorkApprovalDecisionSnapshotV1 snapshot) =>
        BankAccountVersion(snapshot.SourceVersion)
        && snapshot.Item.Rules.Count == 1
        && snapshot.Item.Rules[0] is { RuleCode: ManualBankPaymentRule, Threshold: null, Deviation: null, Unit: null } rule
        && decimal.TryParse(rule.Actual, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
        && amount > 0
        && new[] { "party", "company-account", "bank-account", "currency" }.All(role =>
            snapshot.Item.References.Count(reference => reference.Role == role && reference.RecordId is { } id && id != Guid.Empty) == 1);

    private async Task<WorkDecisionResultV1> DecideManualBankPaymentAsync(Guid id, WorkDecisionRequestV1 request, string key,
        WorkApprovalDecisionSnapshotV1 snapshot, string actor, CancellationToken token)
    {
        if (manualBankPayments is null) throw new WorkDecisionException(503);
        // Approval has no bank/treasury effect. RecordAsync and BankResultConfirmed never enter this Work route.
        var result = await manualBankPayments.ApproveAsync(new(snapshot.CompanyId, actor, correlation.CorrelationId, correlation.CausationId),
            new(key), new(id, request.ExpectedVersion), token);
        if (result.Outcome != CommandOutcome.Succeeded)
            throw new WorkDecisionException(result.Outcome switch
            {
                CommandOutcome.Denied => 403, CommandOutcome.Unavailable => 503,
                CommandOutcome.VersionConflict or CommandOutcome.IdempotencyConflict => 409, _ => 422,
            }, result.Issues.FirstOrDefault()?.Code, result.IsReplay, result.OriginalCorrelationId);
        var payment = result.Resource;
        if (payment is null || payment.CompanyId != snapshot.CompanyId || payment.Id != id
            || payment.Status != ManualBankPaymentStatus.Approved || payment.ApprovedBy != actor
            || payment.ApprovedAtUtc is not { } approvedAt || approvedAt == default || approvedAt.Offset != TimeSpan.Zero
            || payment.PreparedBy != snapshot.Item.PreparedBy || payment.PreparedAtUtc.ToUniversalTime() != snapshot.Item.WaitingSinceUtc
            || approvedAt < payment.PreparedAtUtc || payment.RecordedBy is not null || payment.RecordedAtUtc is not null || payment.BankResultConfirmed
            || payment.Version != (long.Parse(request.ExpectedVersion, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture)
            || payment.Preparation is not { Money: not null } preparation
            || !Matches("party", preparation.PartyId) || !Matches("company-account", preparation.CompanyAccountId)
            || !Matches("bank-account", preparation.TreasuryAccountId) || !Matches("currency", preparation.Money.CurrencyId)
            || !decimal.TryParse(preparation.Money.Amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            || WorkInboxRulesV1.Decimal(amount) != snapshot.Item.Rules[0].Actual
            || string.IsNullOrWhiteSpace(result.OriginalCorrelationId)
            || !result.IsReplay && (!snapshot.Pending || snapshot.SourceVersion != request.ExpectedVersion || result.OriginalCorrelationId != correlation.CorrelationId))
            throw new WorkDecisionException(503);
        return new(snapshot.CompanyId, ManualBankPayment, id, "Approved", result.IsReplay, result.OriginalCorrelationId);

        bool Matches(string role, Guid value) => value != Guid.Empty && snapshot.Item.References.Any(reference => reference.Role == role && reference.RecordId == value);
    }
}
