using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class FinanceCreditMovementServiceV1 : IFinanceCreditMovementDecisionReadV1
{
    public static string DecisionPayloadHash(CreditMovementDecisionV1 request) => FinanceCreditAccountService.PayloadHash(DecideCommand, request);
    public static string SourceFactsDigest(CreditMovementViewV1 view, string proposedBy) => FinanceCreditAccountService.PayloadHash(
        "finance.credit-movement.recorded-facts.v1", new { view.Id, view.CompanyId, view.Source, view.CurrencyId,
            view.MoneyProfile, view.OpeningPeriodVersion, ProposedBy = proposedBy, view.ProposedAtUtc });
    public static string DecisionPreviewDigest(FinanceCreditMovementDecisionDetailV1 detail) =>
        FinanceCreditAccountService.PayloadHash("finance.credit-movement.decision-preview.v1", detail);
    public static string OriginalReceiptDigest(CreditMovementResultV1 result) => FinanceCreditAccountService.PayloadHash(
        "finance.credit-movement.original-receipt.v1", result with { IsReplay = false, DecisionReceipt = null });

    public async Task<ReadResult<FinanceCreditMovementDecisionDetailV1>> ReadDecisionAsync(FinanceContext context, Guid movementId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(freshConsumerFence);
        if (await FreshReadFence() is { } before) return before;
        if (movementId == Guid.Empty) return new(ReadOutcome.NotFound, null);
        var row = await db.CreditMovements.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == context.CompanyId && x.Id == movementId, cancellationToken);
        if (await FreshReadFence() is { } afterRow) return afterRow;
        if (row is null) return new(ReadOutcome.NotFound, null);
        if (row.ProposedBy == context.ActorId) return new(ReadOutcome.Denied, null);
        CreditMovementViewV1 view;
        try { view = View(row, context.ActorId); }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return new(ReadOutcome.Unavailable, null); }
        if (!ValidDecisionMovement(view, row.ProposedBy) || !ValidDecisionSourceRow(row, view)) return new(ReadOutcome.Unavailable, null);
        // Only the existing inbox's bank caption is needed, never the bank card, IBAN, balance or another account's movements.
        string? bankName = null;
        if (row.BankAccountId is { } bankId)
        {
            bankName = await db.BankAccounts.AsNoTracking().Where(x => x.CompanyId == context.CompanyId && x.Id == bankId)
                .Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
            if (await FreshReadFence() is { } afterBank) return afterBank;
        }
        var final = await db.CreditMovements.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == context.CompanyId && x.Id == movementId, cancellationToken);
        if (await FreshReadFence() is { } afterFinal) return afterFinal;
        // Bind the full exact row, including retained raw source/profile and decision projection; never mix two revisions.
        if (final is null || JsonSerializer.Serialize(final, Json) != JsonSerializer.Serialize(row, Json)) return new(ReadOutcome.Unavailable, null);
        return new(ReadOutcome.Found, new(view, row.ProposedBy, bankName, SourceFactsDigest(view, row.ProposedBy)));

        async Task<ReadResult<FinanceCreditMovementDecisionDetailV1>?> FreshReadFence()
        {
            cancellationToken.ThrowIfCancellationRequested();
            await freshConsumerFence(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var refusal = await Fence(context, FinanceAction.ApproveCredit, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await freshConsumerFence(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!Same(context)) return new(ReadOutcome.Denied, null);
            // A normal-completing consumer may revoke authority. Do not release an earlier Allowed result.
            if (refusal is null) refusal = await Fence(context, FinanceAction.ApproveCredit, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return refusal is null ? null : new(ReadFailure(refusal), null);
        }
    }

    internal static bool ValidDecisionMovement(CreditMovementViewV1 view, string proposedBy)
    {
        if (view.Id == Guid.Empty || view.CompanyId == Guid.Empty || view.CurrencyId == Guid.Empty || !DecisionActor(proposedBy)
            || view.ProposedAtUtc.Offset != TimeSpan.Zero || view.Source is null || FinanceCreditMovementPolicyV1.Validate(view.Source).Count != 0
            || (view.Source.Kind == CreditMovementKindV1.OpeningBalance ? !FinanceCreditMovementPolicyV1.Text(view.OpeningPeriodVersion, 64) : view.OpeningPeriodVersion is not null)
            || view.MoneyProfile is not { ProfileId: var profileId, ProfileVersion: > 0, CurrencyScale: >= 0 and <= 28 } || profileId == Guid.Empty
            || !FinanceExactDecimalText.TryParsePositive(view.MoneyProfile.SmallestCurrencyUnit, out _)) return false;
        return view.Status switch
        {
            CreditMovementStatusV1.Proposed => view.Version == "1" && view.DecidedAtUtc is null && view.RejectionReason is null && view.ReversedByMovementId is null,
            CreditMovementStatusV1.Approved => (view.Version == "2" && view.ReversedByMovementId is null
                || view.Version == "3" && view.Source.Kind == CreditMovementKindV1.OpeningBalance && view.ReversedByMovementId is { } reversed && reversed != Guid.Empty)
                && view.DecidedAtUtc is { Offset: var offset } && offset == TimeSpan.Zero && view.RejectionReason is null,
            CreditMovementStatusV1.Rejected => view.Version == "2" && view.ReversedByMovementId is null
                && view.DecidedAtUtc is { Offset: var offset } && offset == TimeSpan.Zero && FinanceCreditMovementPolicyV1.Text(view.RejectionReason, 1000),
            _ => false,
        };
    }
    private static bool ValidDecisionSourceRow(FinanceCreditMovementRow row, CreditMovementViewV1 view) =>
        row.CreditAccountId == view.Source.CreditAccountId && row.Kind == (int)view.Source.Kind
        && row.SourceIdentityHash == SourceHash(view.Source) && row.BankAccountId == view.Source.BankEffect?.BankAccountId
        && row.BankAmountText == view.Source.BankEffect?.Amount && row.ReplacesOpeningMovementId == view.Source.ReplacesOpeningMovementId
        && (view.Status == CreditMovementStatusV1.Proposed ? row.DecidedBy is null : DecisionActor(row.DecidedBy) && row.DecidedBy != row.ProposedBy);
    private static bool DecisionActor(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128;
}
