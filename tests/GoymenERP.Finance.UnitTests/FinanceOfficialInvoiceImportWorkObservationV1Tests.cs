using System.Security.Cryptography;
using System.Text;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinanceOfficialInvoiceImportWorkObservationV1Tests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Batch = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid UploadReceipt = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid ApprovalReceipt = Guid.Parse("40000000-0000-0000-0000-000000000004");
    private static readonly Guid Approval = Guid.Parse("50000000-0000-0000-0000-000000000005");
    private static readonly DateTimeOffset UploadedAt = DateTimeOffset.Parse("2026-10-05T18:00:00+00:00");
    private static readonly DateTimeOffset ApprovedAt = DateTimeOffset.Parse("2026-10-05T18:05:00+00:00");

    [Fact]
    public void Upload_payload_is_canonical_stable_and_owned_by_the_pending_fact()
    {
        var first = FinanceOfficialInvoiceImportWorkObservationFactoryV1.CreateUpload(
            Command(UploadReceipt, FinanceOfficialInvoiceImportApprovalServiceV1.UploadCommand), BatchRow());
        var second = FinanceOfficialInvoiceImportWorkObservationFactoryV1.CreateUpload(
            Command(UploadReceipt, FinanceOfficialInvoiceImportApprovalServiceV1.UploadCommand), BatchRow());

        Assert.Equal(UploadReceipt, first.DeliveryId);
        Assert.Equal(Batch, first.EvidenceId);
        Assert.Equal(1, first.ObservationOrdinal);
        Assert.Equal(FinanceOfficialInvoiceImportWorkStateV1.NeedsAction, first.DesiredState);
        Assert.Equal("finance.official-invoice-import.v1", first.SourceKey);
        Assert.Equal($"batch:{Batch:D}", first.ScopeKey);
        Assert.Equal(first.PayloadUtf8.ToArray(), second.PayloadUtf8.ToArray());
        Assert.Equal(first.PayloadSha256, second.PayloadSha256);
        Assert.Equal(Hash(first.PayloadUtf8.Span), first.PayloadSha256);
        Assert.Contains("\"approvedBy\":null", Encoding.UTF8.GetString(first.PayloadUtf8.Span));
    }

    [Fact]
    public void Approval_payload_is_ordinal_two_and_uses_the_approval_fact_as_evidence()
    {
        var upload = FinanceOfficialInvoiceImportWorkObservationFactoryV1.CreateUpload(
            Command(UploadReceipt, FinanceOfficialInvoiceImportApprovalServiceV1.UploadCommand), BatchRow());
        var approved = FinanceOfficialInvoiceImportWorkObservationFactoryV1.CreateApproval(
            Command(ApprovalReceipt, FinanceOfficialInvoiceImportApprovalServiceV1.ApproveCommand),
            BatchRow(), ApprovalRow());

        Assert.Equal(ApprovalReceipt, approved.DeliveryId);
        Assert.Equal(Approval, approved.EvidenceId);
        Assert.Equal(2, approved.ObservationOrdinal);
        Assert.Equal(FinanceOfficialInvoiceImportWorkStateV1.Satisfied, approved.DesiredState);
        Assert.NotEqual(upload.PayloadSha256, approved.PayloadSha256);
        Assert.Contains("\"approvedBy\":\"checker\"", Encoding.UTF8.GetString(approved.PayloadUtf8.Span));
    }

    [Fact]
    public void Restore_refuses_any_stored_payload_or_hash_drift_and_returns_owned_bytes()
    {
        var command = Command(UploadReceipt, FinanceOfficialInvoiceImportApprovalServiceV1.UploadCommand);
        var batch = BatchRow();
        var expected = FinanceOfficialInvoiceImportWorkObservationFactoryV1.CreateUpload(command, batch);
        var row = FinanceOfficialInvoiceImportWorkObservationFactoryV1.ToRow(expected);

        Assert.True(FinanceOfficialInvoiceImportWorkObservationFactoryV1.TryRestore(
            row, command, batch, null, out var restored));
        row.PayloadUtf8[0] ^= 1;

        Assert.False(FinanceOfficialInvoiceImportWorkObservationFactoryV1.TryRestore(
            row, command, batch, null, out _));
        Assert.NotEqual(row.PayloadUtf8, restored.PayloadUtf8.ToArray());
    }

    private static FinanceOfficialInvoiceImportCommandRow Command(Guid id, string name) => new()
    {
        Id = id, CompanyId = Company, BatchId = Batch, CommandName = name, CorrelationId = "correlation",
    };

    private static FinanceOfficialInvoiceImportBatchRow BatchRow() => new()
    {
        Id = Batch, CompanyId = Company, BatchVersion = "batch-v1", BatchChecksum = new string('a', 64),
        TemplateVersion = "template-v1", RowCount = 1, UploadedBy = "maker", UploadedAtUtc = UploadedAt,
        CorrelationId = "correlation", CausationId = "cause-upload",
    };

    private static FinanceOfficialInvoiceImportApprovalRow ApprovalRow() => new()
    {
        Id = Approval, CompanyId = Company, BatchId = Batch, UploadedBy = "maker", ApprovedBy = "checker",
        ApprovedAtUtc = ApprovedAt, CorrelationId = "correlation", CausationId = "cause-approval",
    };

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
