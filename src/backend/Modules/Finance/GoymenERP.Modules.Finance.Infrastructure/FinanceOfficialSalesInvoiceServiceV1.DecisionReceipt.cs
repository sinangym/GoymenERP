using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class FinanceOfficialSalesInvoiceServiceV1
{
    private async Task<FinanceOfficialSalesInvoiceDecisionReceiptV1?> ReadDecisionReceiptAsync(FinanceContext context,
        CommandMetadata metadata, Guid invoiceId, string command, FinanceOfficialSalesInvoiceResultV1 result,
        CancellationToken token)
    {
        await Fence();
        var key = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var row = await dbContext.OfficialSalesInvoiceCommands.AsNoTracking().SingleOrDefaultAsync(item =>
            item.CompanyId == context.CompanyId && item.InvoiceId == invoiceId && item.CommandName == command
            && item.IdempotencyKeyHash == key && item.ActorId == context.ActorId, token);
        await Fence();
        // Legacy native answers still replay unchanged; missing/ambiguous provenance is never Work success proof.
        return row is null ? null : RestoreDecisionReceipt(row, context, metadata, invoiceId, command, result);
        async Task Fence()
        {
            var action = command == ApproveCommand ? FinanceOfficialSalesInvoiceAuthorityActionV1.Approve
                : FinanceOfficialSalesInvoiceAuthorityActionV1.Reject;
            if (await FenceAsync(context, action, invoiceId, token) is { } refusal)
                throw new InvoiceDecisionRefusal(refusal);
        }
    }

    internal static FinanceOfficialSalesInvoiceDecisionReceiptV1? RestoreDecisionReceipt(
        FinanceOfficialSalesInvoiceCommandRow row, FinanceContext context, CommandMetadata metadata, Guid invoiceId,
        string command, FinanceOfficialSalesInvoiceResultV1 result)
    {
        try
        {
            var stored = JsonSerializer.Deserialize<FinanceOfficialSalesInvoiceResultV1>(row.ResultJson, Json);
            if (stored is not { Outcome: CommandOutcome.Succeeded, IsReplay: false, Resource: { } view }
                || stored.Issues is not { Count: 0 } || stored.ExistingOfficialNo is not null
                || command is not (ApproveCommand or RejectCommand)
                || result.Outcome != CommandOutcome.Succeeded || result.Issues is not { Count: 0 }
                || result.ExistingOfficialNo is not null
                || !JsonNode.DeepEquals(JsonNode.Parse(row.ResultJson), JsonSerializer.SerializeToNode(stored, Json))
                || !JsonNode.DeepEquals(JsonSerializer.SerializeToNode(stored, Json),
                    JsonSerializer.SerializeToNode(result with { IsReplay = false, DecisionReceipt = null }, Json))
                || row.CompanyId != context.CompanyId || view.CompanyId != context.CompanyId
                || row.InvoiceId != invoiceId || view.InvoiceId != invoiceId || invoiceId == Guid.Empty
                || row.CommandName != command || row.ActorId != context.ActorId
                || row.IdempotencyKeyHash != FinanceCommandKeys.Hash(metadata.IdempotencyKey)
                || string.IsNullOrWhiteSpace(row.CorrelationId) || row.CorrelationId != stored.OriginalCorrelationId
                || !result.IsReplay && row.CorrelationId != context.CorrelationId
                || row.RecordedAtUtc.Offset != TimeSpan.Zero || view.Version != VersionText(DecidedVersion)
                || string.IsNullOrWhiteSpace(view.RecordedBy) || view.RecordedBy == row.ActorId
                || view.RecordedAtUtc.Offset != TimeSpan.Zero || view.WithdrawnBy is not null || view.WithdrawnAtUtc is not null)
                return null;
            var approved = command == ApproveCommand;
            if (approved
                ? view.Status != FinanceOfficialSalesInvoiceStatusV1.Approved || view.ApprovedBy != row.ActorId
                    || view.ApprovedAtUtc != row.RecordedAtUtc || view.RejectedBy is not null
                    || view.RejectedAtUtc is not null || view.RejectionReason is not null
                : view.Status != FinanceOfficialSalesInvoiceStatusV1.Rejected || view.RejectedBy != row.ActorId
                    || view.RejectedAtUtc != row.RecordedAtUtc || view.ApprovedBy is not null || view.ApprovedAtUtc is not null
                    || string.IsNullOrWhiteSpace(view.RejectionReason) || view.RejectionReason != view.RejectionReason.Trim()
                    || view.RejectionReason.Length > RejectionReasonMaximumLength)
                return null;
            var expected = VersionText(PendingVersion);
            var payload = approved ? JsonSerializer.Serialize(new FinanceOfficialSalesInvoiceApproveRequestV1(invoiceId, expected), Json)
                : JsonSerializer.Serialize(new FinanceOfficialSalesInvoiceRejectRequestV1(invoiceId, expected, view.RejectionReason!), Json);
            if (row.PayloadHash != Hash(payload)) return null;
            return new(context.CompanyId, invoiceId, command, row.IdempotencyKeyHash, row.PayloadHash, row.ActorId,
                expected, view.Version, row.CorrelationId, row.RecordedAtUtc);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or FormatException)
        {
            return null;
        }
    }
}
