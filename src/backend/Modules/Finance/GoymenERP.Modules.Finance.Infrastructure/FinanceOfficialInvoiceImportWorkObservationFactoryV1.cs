using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Modules.Finance.Infrastructure;

internal static class FinanceOfficialInvoiceImportWorkObservationFactoryV1
{
    internal const string SourceKey = "finance.official-invoice-import.v1";
    internal const int PayloadSchemaVersion = 1;

    internal static FinanceOfficialInvoiceImportWorkObservationV1 CreateUpload(
        FinanceOfficialInvoiceImportCommandRow command,
        FinanceOfficialInvoiceImportBatchRow batch) =>
        Create(command, batch, approval: null, 1, FinanceOfficialInvoiceImportWorkStateV1.NeedsAction, batch.Id);

    internal static FinanceOfficialInvoiceImportWorkObservationV1 CreateApproval(
        FinanceOfficialInvoiceImportCommandRow command,
        FinanceOfficialInvoiceImportBatchRow batch,
        FinanceOfficialInvoiceImportApprovalRow approval) =>
        Create(command, batch, approval, 2, FinanceOfficialInvoiceImportWorkStateV1.Satisfied, approval.Id);

    internal static FinanceOfficialInvoiceImportWorkDeliveryRow ToRow(
        FinanceOfficialInvoiceImportWorkObservationV1 observation) => new()
        {
            DeliveryId = observation.DeliveryId,
            CompanyId = observation.CompanyId,
            CommandReceiptId = observation.DeliveryId,
            BatchId = observation.BatchId,
            SourceKey = observation.SourceKey,
            ScopeKey = observation.ScopeKey,
            ObservationOrdinal = observation.ObservationOrdinal,
            DesiredState = (int)observation.DesiredState,
            EvidenceId = observation.EvidenceId,
            PayloadSchemaVersion = observation.PayloadSchemaVersion,
            PayloadUtf8 = observation.PayloadUtf8.ToArray(),
            PayloadSha256 = observation.PayloadSha256,
            CorrelationId = observation.CorrelationId,
            CausationId = observation.CausationId,
            OccurredAtUtc = observation.OccurredAtUtc,
        };

    internal static bool TryRestore(
        FinanceOfficialInvoiceImportWorkDeliveryRow row,
        FinanceOfficialInvoiceImportCommandRow command,
        FinanceOfficialInvoiceImportBatchRow batch,
        FinanceOfficialInvoiceImportApprovalRow? approval,
        out FinanceOfficialInvoiceImportWorkObservationV1 observation)
    {
        observation = null!;
        FinanceOfficialInvoiceImportWorkObservationV1 expected;
        try
        {
            expected = row.ObservationOrdinal switch
            {
                1 when approval is null => CreateUpload(command, batch),
                2 when approval is not null => CreateApproval(command, batch, approval),
                _ => null!,
            };
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        if (expected is null
            || row.DeliveryId != expected.DeliveryId
            || row.CommandReceiptId != expected.DeliveryId
            || row.CompanyId != expected.CompanyId
            || row.BatchId != expected.BatchId
            || !string.Equals(row.SourceKey, expected.SourceKey, StringComparison.Ordinal)
            || !string.Equals(row.ScopeKey, expected.ScopeKey, StringComparison.Ordinal)
            || row.ObservationOrdinal != expected.ObservationOrdinal
            || row.DesiredState != (int)expected.DesiredState
            || row.EvidenceId != expected.EvidenceId
            || row.PayloadSchemaVersion != expected.PayloadSchemaVersion
            || !row.PayloadUtf8.AsSpan().SequenceEqual(expected.PayloadUtf8.Span)
            || !string.Equals(row.PayloadSha256, expected.PayloadSha256, StringComparison.Ordinal)
            || !string.Equals(row.CorrelationId, expected.CorrelationId, StringComparison.Ordinal)
            || !string.Equals(row.CausationId, expected.CausationId, StringComparison.Ordinal)
            || row.OccurredAtUtc != expected.OccurredAtUtc)
        {
            return false;
        }

        observation = expected with { PayloadUtf8 = expected.PayloadUtf8.ToArray() };
        return true;
    }

    internal static string ScopeKey(Guid batchId) => $"batch:{batchId:D}";

    private static FinanceOfficialInvoiceImportWorkObservationV1 Create(
        FinanceOfficialInvoiceImportCommandRow command,
        FinanceOfficialInvoiceImportBatchRow batch,
        FinanceOfficialInvoiceImportApprovalRow? approval,
        long ordinal,
        FinanceOfficialInvoiceImportWorkStateV1 state,
        Guid evidenceId)
    {
        if (command.Id == Guid.Empty || command.CompanyId == Guid.Empty || command.BatchId == Guid.Empty
            || command.ProducerSchemaVersion != PayloadSchemaVersion
            || batch.Id == Guid.Empty || batch.CompanyId != command.CompanyId || batch.Id != command.BatchId
            || evidenceId == Guid.Empty
            || (ordinal == 1 && !string.Equals(command.CommandName,
                FinanceOfficialInvoiceImportApprovalServiceV1.UploadCommand, StringComparison.Ordinal))
            || (ordinal == 2 && (!string.Equals(command.CommandName,
                    FinanceOfficialInvoiceImportApprovalServiceV1.ApproveCommand, StringComparison.Ordinal)
                || approval is null || approval.CompanyId != batch.CompanyId || approval.BatchId != batch.Id)))
        {
            throw new InvalidOperationException("Finance Work gözlem kaynağı tutarsızdır.");
        }

        var scopeKey = ScopeKey(batch.Id);
        var payload = Payload(batch, approval, scopeKey, ordinal, state, evidenceId);
        var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        return new FinanceOfficialInvoiceImportWorkObservationV1(
            command.Id,
            batch.CompanyId,
            batch.Id,
            SourceKey,
            scopeKey,
            ordinal,
            state,
            evidenceId,
            PayloadSchemaVersion,
            payload,
            hash,
            command.CorrelationId,
            ordinal == 1 ? batch.CausationId : approval!.CausationId,
            ordinal == 1 ? batch.UploadedAtUtc : approval!.ApprovedAtUtc);
    }

    private static byte[] Payload(
        FinanceOfficialInvoiceImportBatchRow batch,
        FinanceOfficialInvoiceImportApprovalRow? approval,
        string scopeKey,
        long ordinal,
        FinanceOfficialInvoiceImportWorkStateV1 state,
        Guid evidenceId)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", PayloadSchemaVersion);
            writer.WriteString("kind", SourceKey);
            writer.WriteString("companyId", batch.CompanyId.ToString("D"));
            writer.WriteString("batchId", batch.Id.ToString("D"));
            writer.WriteString("sourceKey", SourceKey);
            writer.WriteString("scopeKey", scopeKey);
            writer.WriteNumber("observationOrdinal", ordinal);
            writer.WriteString("desiredState", state.ToString());
            writer.WriteString("evidenceId", evidenceId.ToString("D"));
            writer.WriteString("batchVersion", batch.BatchVersion);
            writer.WriteString("batchChecksum", batch.BatchChecksum);
            writer.WriteString("templateVersion", batch.TemplateVersion);
            writer.WriteNumber("rowCount", batch.RowCount);
            writer.WriteString("uploadedBy", batch.UploadedBy);
            writer.WriteString("uploadedAtUtc", Utc(batch.UploadedAtUtc));
            if (approval is null)
            {
                writer.WriteNull("approvedBy");
                writer.WriteNull("approvedAtUtc");
            }
            else
            {
                writer.WriteString("approvedBy", approval.ApprovedBy);
                writer.WriteString("approvedAtUtc", Utc(approval.ApprovedAtUtc));
            }
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static string Utc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
