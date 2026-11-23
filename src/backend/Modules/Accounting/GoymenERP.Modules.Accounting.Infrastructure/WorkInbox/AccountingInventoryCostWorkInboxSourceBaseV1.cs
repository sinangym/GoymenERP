using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;

/// <summary>
/// W1-A (AI-D31, AI-D32): the inventory cost proposals of one kind prepared and waiting for approval, as Accounting lists
/// them for the Work inbox. Every preparation appends a complete version; a proposal waits while its LATEST version has the
/// stored status 2 (Prepared: the approve command refuses any other status) and, for an invoiced shipment, its source
/// invoice has no cancellation receipt (the approve command refuses a cancelled source, <c>inventory_cost_source_cancelled</c>).
/// It waits since that version was recorded. The approve command refuses the preparer
/// (<c>inventory_cost_approval_refused</c>) and no one else, and accepts only a company-wide grant (D11).
/// <para>
/// The owner binds a different approve permission to each kind, so each kind is its own Work inbox source. The rule carries
/// the stored prepared signed TL amount as the actual value; no threshold exists.
/// </para>
/// </summary>
internal abstract class AccountingInventoryCostWorkInboxSourceBaseV1(
    AccountingDbContext dbContext,
    AccountingWorkInboxFence fence) : IWorkInboxSourceV1
{
    public const string ModuleName = "accounting";

    /// <summary>The stored status the approve command accepts (Prepared).</summary>
    internal const int PreparedStatus = 2;

    /// <summary>The unit of the prepared amount: the owner posts it in Turkish lira.</summary>
    internal const string TlUnit = "TRY";

    public string Module => ModuleName;

    public abstract string Kind { get; }

    public abstract string ApproverPermission { get; }

    protected abstract AccountingInventoryCostKindV1 CostKind { get; }

    protected abstract string ScreenLink { get; }

    protected abstract string RuleCode { get; }

    public Task<WorkInboxSourceResultV1> ListPendingAsync(
        WorkInboxSourceContextV1 context,
        int maxCount,
        CancellationToken cancellationToken)
    {
        var kind = (int)CostKind;
        var checksCancellation = CostKind == AccountingInventoryCostKindV1.SalesInvoicedShipment;
        return fence.ListAsync(context, ApproverPermission, maxCount,
            token =>
            {
                var company = context.CompanyId;
                var versions = dbContext.InventoryCostProposals.AsNoTracking().Where(row => row.CompanyId == company);
                var cancellations = dbContext.SourceInvoiceCancellations.AsNoTracking().Where(row => row.CompanyId == company);
                return (
                        from row in versions
                        join source in dbContext.InventoryCostSources.AsNoTracking() on row.EventId equals source.EventId
                        where row.Status == PreparedStatus && source.CompanyId == company && source.Kind == kind
                            && !versions.Any(newer => newer.Id == row.Id && newer.Version > row.Version)
                            && (!checksCancellation
                                || !cancellations.Any(cancellation => cancellation.SourceDocumentId == source.AdmissionId))
                        orderby row.RecordedAtUtc, row.Id
                        select new Pending(row, source.InvoiceId))
                    .Take(maxCount + 1)
                    .ToListAsync(token);
            },
            rows => AccountingWorkInboxFence.Page(rows, maxCount, Item),
            cancellationToken);
    }

    private WorkInboxItemV1? Item(Pending pending)
    {
        var row = pending.Row;
        if (row.Id == Guid.Empty || row.EventId == Guid.Empty || pending.InvoiceId == Guid.Empty
            || row.PreparedBy is not { } preparedBy || preparedBy == Guid.Empty
            || row.PeriodId is not { } period || period == Guid.Empty
            || row.ApprovedBy is not null || row.JournalId is not null
            || !TryView(row, pending.InvoiceId, preparedBy, period, out var invoiceNumber, out var amount))
        {
            return null;
        }

        return new WorkInboxItemV1(ModuleName, Kind, row.Id, AccountingWorkInboxFence.Label(invoiceNumber, row.Id),
            row.RecordedAtUtc.ToUniversalTime(), ScreenLink, AccountingWorkInboxFence.Actor(preparedBy),
            [new WorkApprovalRuleResultV1(RuleCode, null, WorkInboxRulesV1.Decimal(amount), null, TlUnit)],
            [
                new WorkApprovalReferenceV1("invoice", AccountingWorkInboxFence.Label(invoiceNumber, pending.InvoiceId),
                    pending.InvoiceId, null),
                new WorkApprovalReferenceV1("period", AccountingWorkInboxFence.ShortReference(period), period, null),
            ]);
    }

    /// <summary>
    /// The stored version view must name the same proposal, invoice, preparer and period as its row (the owner refuses a
    /// view that does not, <c>inventory_cost_source_unavailable</c>); the label and amount come from it.
    /// </summary>
    private static bool TryView(AccountingInventoryCostProposalRow row, Guid invoiceId, Guid preparedBy, Guid period,
        out string? invoiceNumber, out decimal amount)
    {
        invoiceNumber = null;
        amount = 0;
        try
        {
            using var document = JsonDocument.Parse(row.ViewJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !SameGuid(root, "proposalId", row.Id) || !SameGuid(root, "invoiceId", invoiceId)
                || !root.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String
                || status.GetString() != "Prepared"
                || !root.TryGetProperty("preparation", out var preparation) || preparation.ValueKind != JsonValueKind.Object
                || !SameGuid(preparation, "preparedBy", preparedBy) || !SameGuid(preparation, "periodId", period)
                || !preparation.TryGetProperty("signedAmount", out var signed) || signed.ValueKind != JsonValueKind.String
                || !AccountingWorkInboxFence.TryAmount(signed.GetString(), out amount))
            {
                return false;
            }

            if (root.TryGetProperty("officialInvoiceNumber", out var number))
            {
                if (number.ValueKind == JsonValueKind.String)
                {
                    invoiceNumber = number.GetString();
                }
                else if (number.ValueKind != JsonValueKind.Null)
                {
                    return false;
                }
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool SameGuid(JsonElement element, string name, Guid expected) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && value.TryGetGuid(out var actual) && actual == expected;

    private sealed record Pending(AccountingInventoryCostProposalRow Row, Guid InvoiceId);
}
