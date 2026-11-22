using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32, YOL-D18): the prepared book values of currency-exchange cash transfers (FIN-S14) waiting for a
/// decision, as Finance lists them for the Work inbox. A preparation waits while it has no decision of its own and its
/// transfer has no approved book value (exactly what the decide command refuses as <c>cash_exchange_book_value_decided</c>)
/// since it was prepared; a preparation is immutable. It is decided on the book value approval screen by a holder of
/// <c>accounting.normal-journal.draft.approve</c> (the owner's existing Accounting permission, #1189/6960684942) other
/// than the preparer (the decide command refuses only the preparer: <c>cash_exchange_book_value_maker_cannot_approve</c>).
/// The owner answers only a company-wide grant, so scoped approvers are not accepted. Every pending preparation of the
/// company is returned, the caller's own included: Work hides a maker's own item.
/// <para>
/// The rules carry the stored, document-backed TL values of the preparation (source book value; target functional value)
/// as actual values, read from the owner's own stored preparation exactly as the owner reads it; nothing is computed. A
/// legacy preparation without a target functional value is listed with its own rule: the owner refuses to approve it
/// (<c>cash_exchange_target_functional_value_required</c>) but it can still be rejected. The values are TL by the owner's
/// contract; Finance stores no currency code for them, so the unit is left empty.
/// </para>
/// </summary>
internal sealed class FinanceCashExchangeBookValueWorkInboxSourceV1(
    FinanceDbContext dbContext,
    FinanceWorkInboxFence fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "finance";
    public const string KindName = "finance.cash-exchange-book-value.v1";
    public const string ScreenPath = "/finans/doviz-defter-degeri-onaylari";

    /// <summary>The prepared TL book value of the source side (actual value).</summary>
    public const string SourceBookValueRule = "finance.cash-exchange-source-book-value";

    /// <summary>The prepared TL functional value of the target side (actual value).</summary>
    public const string TargetFunctionalValueRule = "finance.cash-exchange-target-functional-value";

    /// <summary>A legacy preparation without a target functional value: it cannot be approved, only rejected.</summary>
    public const string TargetFunctionalValueMissingRule = "finance.cash-exchange-target-functional-value-missing";

    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    public string Module => ModuleName;

    public string Kind => KindName;

    public string ApproverPermission => CashExchangeBookValuePermissionsV1.Approve;

    public Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken) =>
        FinanceWorkInboxRead.ListAsync(fence, context, ApproverPermission, maxCount, ReadAsync, Item, cancellationToken);

    private async Task<(List<CashExchangeBookValueProposalRow> Rows, IReadOnlyDictionary<Guid, string?> Transfers)> ReadAsync(
        Guid companyId, int maxCount, CancellationToken cancellationToken)
    {
        var decisions = dbContext.CashExchangeBookValueDecisions.AsNoTracking()
            .Where(decision => decision.CompanyId == companyId);
        var rows = await dbContext.CashExchangeBookValueProposals.AsNoTracking()
            .Where(row => row.CompanyId == companyId
                && !decisions.Any(decision => decision.ProposalId == row.Id)
                && !decisions.Any(decision => decision.TransferId == row.TransferId && decision.Approved))
            .OrderBy(row => row.PreparedAtUtc).ThenBy(row => row.Id)
            .Take(maxCount + 1)
            .ToListAsync(cancellationToken);
        var transferIds = rows.Take(maxCount).Select(row => row.TransferId).Distinct().ToArray();
        var transfers = await dbContext.CashTransfers.AsNoTracking()
            .Where(row => row.CompanyId == companyId && transferIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.DocumentNumber, cancellationToken);
        return (rows, transfers);
    }

    private static WorkInboxItemV1? Item(CashExchangeBookValueProposalRow row, IReadOnlyDictionary<Guid, string?> transfers)
    {
        if (row.Id == Guid.Empty || row.TransferId == Guid.Empty || string.IsNullOrWhiteSpace(row.PreparedBy)
            || Preparation(row.PreparationJson) is not { } preparation
            || preparation.TransferId != row.TransferId || preparation.Evidence is not { } evidence
            || evidence.DocumentId == Guid.Empty
            || FinanceWorkInboxRead.Amount(preparation.SourceBookValueTl) is not { } source)
        {
            return null;
        }

        var rules = new List<WorkApprovalRuleResultV1>(2) { new(SourceBookValueRule, null, source, null, null) };
        if (preparation.TargetFunctionalValueTl is null)
        {
            rules.Add(new(TargetFunctionalValueMissingRule, null, null, null, null));
        }
        else if (FinanceWorkInboxRead.Amount(preparation.TargetFunctionalValueTl) is { } target)
        {
            rules.Add(new(TargetFunctionalValueRule, null, target, null, null));
        }
        else
        {
            return null;
        }

        var documentNumber = transfers.GetValueOrDefault(row.TransferId);
        return new WorkInboxItemV1(ModuleName, KindName, row.Id, FinanceWorkInboxRead.Label(documentNumber, row.Id),
            row.PreparedAtUtc.ToUniversalTime(), ScreenPath, row.PreparedBy, rules,
            [
                FinanceWorkInboxRead.Reference("cash-transfer", row.TransferId,
                    FinanceWorkInboxRead.Label(documentNumber, row.TransferId)),
                FinanceWorkInboxRead.Reference("evidence-document", evidence.DocumentId),
            ]);
    }

    private static ProposeCashExchangeBookValueV1? Preparation(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ProposeCashExchangeBookValueV1>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
