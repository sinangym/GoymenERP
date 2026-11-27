using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class FinanceOfficialInvoiceImportApprovalServiceV1
{
    /// <summary>Exactly the existing native approval fingerprint; no new command or serialization policy.</summary>
    public static string ApprovalPayloadHash(FinanceOfficialInvoiceImportApproveRequestV1 request) =>
        Hash(JsonSerializer.Serialize(request, Json));

    private async Task<FinanceOfficialInvoiceImportApprovalResultV1> WithDecisionReceiptAsync(FinanceContext context,
        CommandMetadata metadata, FinanceOfficialInvoiceImportApproveRequestV1 request,
        FinanceOfficialInvoiceImportApprovalResultV1 result, CancellationToken token)
    {
        await Fence();
        var keyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var command = await dbContext.OfficialInvoiceImportCommands.AsNoTracking().SingleOrDefaultAsync(row =>
            row.CompanyId == context.CompanyId && row.BatchId == request.BatchId && row.CommandName == ApproveCommand
            && row.IdempotencyKeyHash == keyHash && row.ActorId == context.ActorId, token);
        var delivery = command is null || command.ProducerSchemaVersion is null ? null
            : await dbContext.OfficialInvoiceImportWorkDeliveries.AsNoTracking()
                .SingleOrDefaultAsync(row => row.CompanyId == context.CompanyId && row.CommandReceiptId == command.Id, token);
        var batch = delivery is null ? null : await dbContext.OfficialInvoiceImportBatches.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CompanyId == context.CompanyId && row.Id == request.BatchId, token);
        var approval = delivery is null ? null : await dbContext.OfficialInvoiceImportApprovals.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CompanyId == context.CompanyId && row.BatchId == request.BatchId, token);
        await Fence();
        if (command is null) throw new InvalidOperationException("Import approval command receipt is missing.");
        // Existing marker-null receipts retain native replay compatibility, but cannot claim Work provenance.
        if (command.ProducerSchemaVersion is null) return result with { DecisionReceipt = null };
        if (delivery is null || batch is null || approval is null
            || !FinanceOfficialInvoiceImportWorkObservationFactoryV1.TryRestore(delivery, command, batch, approval, out _))
            throw new InvalidOperationException("Import approval delivery provenance is invalid.");
        using var document = JsonDocument.Parse(command.ResultJson);
        if (!Exact(document.RootElement, "outcome", "resource", "isReplay", "originalCorrelationId", "issues")
            || !document.RootElement.TryGetProperty("resource", out var resourceJson)
            || !Exact(resourceJson, "batchId", "companyId", "batchVersion", "batchChecksum", "templateVersion", "status",
                "version", "rowCount", "uploadedBy", "uploadedAtUtc", "approvedBy", "approvedAtUtc"))
            throw new InvalidOperationException("Import approval receipt shape is invalid.");
        var stored = JsonSerializer.Deserialize<FinanceOfficialInvoiceImportApprovalResultV1>(command.ResultJson, Json);
        if (stored is not { Outcome: CommandOutcome.Succeeded, IsReplay: false, Resource: { } resource }
            || stored.Issues is not { Count: 0 } || result.Outcome != CommandOutcome.Succeeded || result.Issues is not { Count: 0 }
            || result.Resource != resource || resource != View(batch, approval)
            || result.OriginalCorrelationId != stored.OriginalCorrelationId
            || command.CompanyId != context.CompanyId || command.BatchId != request.BatchId
            || command.CommandName != ApproveCommand || command.ActorId != context.ActorId
            || command.IdempotencyKeyHash != keyHash || command.PayloadHash != ApprovalPayloadHash(request)
            || request.ExpectedVersion != "1" || resource.Version != "2"
            || resource.Status != FinanceOfficialInvoiceImportApprovalStatusV1.Approved
            || resource.ApprovedBy != context.ActorId || resource.UploadedBy == context.ActorId
            || resource.ApprovedAtUtc is not { } decided || decided.Offset != TimeSpan.Zero || decided < resource.UploadedAtUtc
            || command.RecordedAtUtc != decided || string.IsNullOrWhiteSpace(command.CorrelationId)
            || command.CorrelationId != stored.OriginalCorrelationId
            || !result.IsReplay && command.CorrelationId != context.CorrelationId)
            throw new InvalidOperationException("Import approval original receipt binding is invalid.");
        return result with { DecisionReceipt = new(context.CompanyId, request.BatchId, approval.Id, command.CommandName,
            command.IdempotencyKeyHash, command.PayloadHash, command.ActorId, request.ExpectedVersion, resource.Version,
            command.CorrelationId, command.RecordedAtUtc) };

        async Task Fence()
        {
            if (await FenceAsync(context, FinanceOfficialInvoiceImportAuthorityActionV1.Approve, request.BatchId, token) is { } refusal)
                throw new ApprovalRefusal(refusal);
        }
        static bool Exact(JsonElement value, params string[] names) => value.ValueKind == JsonValueKind.Object
            && value.EnumerateObject().Count() == names.Length && names.All(name => value.TryGetProperty(name, out _));
    }
}
