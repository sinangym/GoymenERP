using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinanceOfficialInvoiceImportApplicationServiceV1Tests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Batch = Guid.NewGuid();
    private static readonly Guid OpenItem = Guid.NewGuid();
    private static readonly Guid Document = Guid.NewGuid();
    private static readonly Guid Installment = Guid.NewGuid();
    private static readonly Guid Party = Guid.NewGuid();
    private static readonly Guid Account = Guid.NewGuid();
    private static readonly Guid Currency = Guid.NewGuid();
    private const string Checksum = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task Invalid_preview_never_calls_admission_or_read()
    {
        var preview = ValidPreview() with
        {
            Issues = [new FinanceOfficialInvoiceImportIssueV1(1, "header", "invalid")]
        };
        var admission = new Admission();
        var read = new Reader(Found());
        var service = new FinanceOfficialInvoiceImportApplicationServiceV1(
            new Parser(preview), new ApprovedSource(preview.Batch), admission, read);

        var result = await service.ApplyAsync(Context(), new CommandMetadata("key"), Request(), default);

        Assert.False(result.IsSucceeded);
        Assert.Null(result.Admission);
        Assert.Null(result.ApprovedSource);
        Assert.Null(result.FreshRead);
        Assert.Equal(0, admission.Calls);
        Assert.Equal(0, read.Calls);
    }

    [Fact]
    public async Task Apply_uses_canonical_reference_and_returns_fresh_open_item()
    {
        var admission = new Admission();
        var read = new Reader(Found());
        var service = Service(admission, read);

        var result = await service.ApplyAsync(Context(), new CommandMetadata("same-key"), Request(), default);

        Assert.True(result.IsSucceeded);
        Assert.Equal(ReadOutcome.Found, result.ApprovedSource!.Outcome);
        Assert.Equal(1, admission.Calls);
        Assert.Equal(Batch, admission.Reference!.BatchId);
        Assert.Equal("official-file-v1", admission.Reference.BatchVersion);
        Assert.Equal(Checksum, admission.Reference.BatchChecksum);
        Assert.Equal("same-key", admission.Metadata!.IdempotencyKey);
        Assert.Equal(1, read.Calls);
        Assert.Equal(Batch, read.BatchId);
        var item = Assert.Single(result.FreshRead!.Resource!.OpenItems);
        Assert.Equal(OpenItem, item.Id);
        Assert.Equal("1250.50", item.OutstandingAmount);
        Assert.Equal("1", item.Version);
    }

    [Fact]
    public async Task Successful_replay_still_reopens_current_finance_state()
    {
        var admission = new Admission(isReplay: true);
        var current = Found() with
        {
            Resource = Found().Resource! with
            {
                OpenItems = [Item() with { OutstandingAmount = "250.50", Status = 1, Version = "2" }]
            }
        };
        var service = Service(admission, new Reader(current));

        var result = await service.ApplyAsync(Context(), new CommandMetadata("same-key"), Request(), default);

        Assert.True(result.IsSucceeded);
        Assert.True(result.Admission!.IsReplay);
        Assert.Equal("250.50", Assert.Single(result.FreshRead!.Resource!.OpenItems).OutstandingAmount);
        Assert.Equal("2", Assert.Single(result.FreshRead.Resource.OpenItems).Version);
    }

    [Fact]
    public async Task Refused_admission_never_discloses_a_read()
    {
        var admission = new Admission(CommandOutcome.Denied);
        var read = new Reader(Found());
        var service = Service(admission, read);

        var result = await service.ApplyAsync(Context(), new CommandMetadata("key"), Request(), default);

        Assert.False(result.IsSucceeded);
        Assert.Equal(CommandOutcome.Denied, result.Admission!.Outcome);
        Assert.Null(result.FreshRead);
        Assert.Equal(0, read.Calls);
    }

    [Fact]
    public async Task Source_not_approved_refuses_before_admission_and_read()
    {
        var admission = new Admission();
        var read = new Reader(Found());
        var service = new FinanceOfficialInvoiceImportApplicationServiceV1(
            new Parser(ValidPreview()), new ApprovedSource(null), admission, read);

        var result = await service.ApplyAsync(Context(), new CommandMetadata("key"), Request(), default);

        Assert.False(result.IsSucceeded);
        Assert.Equal(ReadOutcome.NotFound, result.ApprovedSource!.Outcome);
        Assert.Null(result.Admission);
        Assert.Equal(0, admission.Calls);
        Assert.Equal(0, read.Calls);
    }

    [Fact]
    public async Task Approved_source_row_mismatch_refuses_before_admission()
    {
        var preview = ValidPreview();
        var batch = Assert.IsType<ApprovedFinanceOpenItemSourceBatch>(preview.Batch);
        var mismatched = batch with
        {
            Rows = [batch.Rows[0] with { Amount = "1250.51" }]
        };
        var admission = new Admission();
        var service = new FinanceOfficialInvoiceImportApplicationServiceV1(
            new Parser(preview), new ApprovedSource(mismatched), admission, new Reader(Found()));

        var result = await service.ApplyAsync(Context(), new CommandMetadata("key"), Request(), default);

        Assert.False(result.IsSucceeded);
        Assert.Equal(ReadOutcome.Unavailable, result.ApprovedSource!.Outcome);
        Assert.Equal(0, admission.Calls);
    }

    [Fact]
    public async Task Fresh_identity_mismatch_fails_closed()
    {
        var mismatched = Found() with
        {
            Resource = Found().Resource! with
            {
                OpenItems = [Item() with { SourceDocumentId = Guid.NewGuid() }]
            }
        };
        var service = Service(new Admission(), new Reader(mismatched));

        var result = await service.ApplyAsync(Context(), new CommandMetadata("key"), Request(), default);

        Assert.False(result.IsSucceeded);
        Assert.Equal(ReadOutcome.Unavailable, result.FreshRead!.Outcome);
        Assert.Null(result.FreshRead.Resource);
    }

    [Fact]
    public async Task Fresh_noncanonical_source_kind_fails_closed()
    {
        var mismatched = Found() with
        {
            Resource = Found().Resource! with
            {
                OpenItems = [Item() with
                {
                    SourceKind = FinanceOpenItemSourceKinds.OfficialInvoiceInstallmentDocument
                }]
            }
        };
        var service = Service(new Admission(), new Reader(mismatched));

        var result = await service.ApplyAsync(Context(), new CommandMetadata("key"), Request(), default);

        Assert.False(result.IsSucceeded);
        Assert.Equal(ReadOutcome.Unavailable, result.FreshRead!.Outcome);
    }

    [Fact]
    public async Task Duplicate_open_item_from_approved_provider_fails_closed_without_admission()
    {
        var preview = ValidPreview();
        var batch = Assert.IsType<ApprovedFinanceOpenItemSourceBatch>(preview.Batch);
        var first = batch.Rows[0];
        var second = first with
        {
            OpenItemId = Guid.NewGuid(),
            SourceInstallmentId = Guid.NewGuid()
        };
        var expected = batch with { Rows = [first, second] };
        var expectedPreview = preview with { Batch = expected };
        var duplicated = expected with { Rows = [first, first] };
        var admission = new Admission();
        var service = new FinanceOfficialInvoiceImportApplicationServiceV1(
            new Parser(expectedPreview), new ApprovedSource(duplicated), admission, new Reader(Found()));

        var result = await service.ApplyAsync(Context(), new CommandMetadata("key"), Request(), default);

        Assert.False(result.IsSucceeded);
        Assert.Equal(ReadOutcome.Unavailable, result.ApprovedSource!.Outcome);
        Assert.Equal(0, admission.Calls);
    }

    [Fact]
    public async Task Reopen_uses_the_same_fresh_read_port()
    {
        var read = new Reader(Found());
        var service = Service(new Admission(), read);

        var result = await service.ReopenAsync(Context(), Batch, default);

        Assert.Equal(ReadOutcome.Found, result.Outcome);
        Assert.Equal(Batch, result.Resource!.BatchId);
        Assert.Equal(1, read.Calls);
    }

    private static FinanceOfficialInvoiceImportApplicationServiceV1 Service(Admission admission, Reader read) =>
        new(new Parser(ValidPreview()), new ApprovedSource(ValidPreview().Batch), admission, read);

    private static FinanceContext Context() => new(Company, "actor", "correlation", null);

    private static FinanceOfficialInvoiceImportPreviewRequestV1 Request() =>
        new(Batch, "official-file-v1", ReadOnlyMemory<byte>.Empty);

    private static FinanceOfficialInvoiceImportPreviewV1 ValidPreview()
    {
        var row = new ApprovedFinanceOpenItemSourceRow(
            OpenItem, Document, Installment, "invoice-v1",
            FinanceOpenItemSourceKinds.OfficialInvoiceInstallmentDocument,
            Party, Account, Currency, "1250.50", new DateOnly(2026, 10, 31));
        return new("finance-official-invoice-v1", Checksum,
            new ApprovedFinanceOpenItemSourceBatch(Batch, Company, "official-file-v1", Checksum, [row]), []);
    }

    private static ReadResult<FinanceOfficialInvoiceAppliedBatchV1> Found() =>
        new(ReadOutcome.Found,
            new FinanceOfficialInvoiceAppliedBatchV1(Batch, Company, "official-file-v1", Checksum, [Item()]));

    private static FinanceOfficialInvoiceOpenItemV1 Item() =>
        new(OpenItem, Company, FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment,
            Document, Installment, "invoice-v1", Batch, "official-file-v1", Checksum,
            Party, Account, Currency, "1250.50", "1250.50", new DateOnly(2026, 10, 31), 1, "1");

    private sealed class Parser(FinanceOfficialInvoiceImportPreviewV1 preview)
        : IFinanceOfficialInvoiceImportParserV1
    {
        public FinanceOfficialInvoiceImportPreviewV1 Preview(
            FinanceContext context,
            FinanceOfficialInvoiceImportPreviewRequestV1 request) => preview;
    }

    private sealed class Admission(
        CommandOutcome outcome = CommandOutcome.Succeeded,
        bool isReplay = false) : IFinanceOpenItemSourceAdmissionService
    {
        public int Calls { get; private set; }
        public CommandMetadata? Metadata { get; private set; }
        public FinanceOpenItemSourceReference? Reference { get; private set; }

        public Task<FinanceOpenItemAdmissionResult> AdmitAsync(
            FinanceContext context,
            CommandMetadata metadata,
            FinanceOpenItemSourceReference source,
            CancellationToken cancellationToken)
        {
            Calls++;
            Metadata = metadata;
            Reference = source;
            return Task.FromResult(new FinanceOpenItemAdmissionResult(
                outcome,
                outcome == CommandOutcome.Succeeded ? [OpenItem] : [],
                isReplay,
                context.CorrelationId,
                []));
        }
    }

    private sealed class ApprovedSource(ApprovedFinanceOpenItemSourceBatch? batch)
        : IApprovedFinanceOpenItemSourcePort
    {
        public Task<ReadResult<ApprovedFinanceOpenItemSourceBatch>> GetApprovedAsync(
            FinanceContext context,
            FinanceOpenItemSourceReference source,
            CancellationToken cancellationToken) =>
            Task.FromResult(batch is null
                ? new ReadResult<ApprovedFinanceOpenItemSourceBatch>(ReadOutcome.NotFound, null)
                : new ReadResult<ApprovedFinanceOpenItemSourceBatch>(ReadOutcome.Found, batch));
    }

    private sealed class Reader(ReadResult<FinanceOfficialInvoiceAppliedBatchV1> result)
        : IFinanceOfficialInvoiceImportReadPortV1
    {
        public int Calls { get; private set; }
        public Guid BatchId { get; private set; }

        public Task<ReadResult<FinanceOfficialInvoiceAppliedBatchV1>> ReadAsync(
            FinanceContext context,
            Guid batchId,
            CancellationToken cancellationToken)
        {
            Calls++;
            BatchId = batchId;
            return Task.FromResult(result);
        }
    }
}
