using System.Reflection;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class NativeApprovalReadQueryV1Tests
{
    private static readonly Guid Company = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherCompany = Guid.Parse("a1000000-0000-0000-0000-000000000002");
    private static readonly Guid Batch = Guid.Parse("a2000000-0000-0000-0000-000000000001");
    private static readonly string Checksum = new('a', 64);

    [Fact]
    public async Task Finance_adapter_projects_exact_owner_page_and_detail_without_rewriting_opaque_facts()
    {
        var batch = PendingBatch();
        var preview = Preview(batch);
        var owner = new Owner
        {
            ListResult = new(ReadOutcome.Found,
                new FinanceOfficialInvoiceImportPendingBatchPageV1(Company, [batch], "opaque+/=cursor")),
            GetResult = new(ReadOutcome.Found, new FinanceOfficialInvoiceImportPendingBatchDetailV1(batch, preview)),
        };
        var adapter = new FinanceInvoiceImportPendingApprovalAdapterV1(owner);
        var context = Context();

        var page = await adapter.ListAsync(context, "opaque+/=request", default);
        var detail = await adapter.GetAsync(context, NativeApprovalKindsV1.FinanceOfficialInvoiceImport, Batch, default);

        var item = Assert.Single(page.Resource!.Items);
        Assert.Equal(ReadOutcome.Found, page.Outcome);
        Assert.Equal(("opaque+/=request", "opaque+/=cursor", 1),
            (owner.ObservedCursor, page.Resource.NextCursor, owner.ListCalls));
        Assert.Equal((Batch, Company, "1", "source-version-7", Checksum,
                FinanceOfficialInvoiceImportTemplateV1.Version, "maker", 3),
            (item.ApprovalId, item.CompanyId, item.Version, item.BatchVersion, item.Checksum,
                item.TemplateVersion, item.SubmittedBy, item.ItemCount));
        Assert.Equal((ReadOutcome.Found, Batch, Checksum, 1),
            (detail.Outcome, detail.Resource!.Item.ApprovalId, detail.Resource.Preview.SourceSha256,
                owner.GetCalls));
        Assert.Same(preview, detail.Resource.Preview);
    }

    [Fact]
    public async Task Query_captures_server_context_calls_owner_once_and_preserves_true_empty()
    {
        var adapter = new Adapter();
        var fixture = Fixture(adapter);

        var result = await fixture.Query.ListAsync("opaque-cursor", default);

        Assert.Equal(ReadOutcome.Found, result.Outcome);
        Assert.Empty(result.Resource!.Items);
        Assert.Equal((Company, "checker", "corr-1", "cause-1", "opaque-cursor", 1),
            (adapter.Context!.CompanyId, adapter.Context.ActorId, adapter.Context.CorrelationId,
                adapter.Context.CausationId, adapter.Cursor, adapter.ListCalls));
    }

    [Fact]
    public async Task Query_get_returns_the_exact_found_detail_after_one_owner_call()
    {
        var expected = NativeDetail();
        var adapter = new Adapter
        {
            OnGet = (_, kind, approvalId, _) => Task.FromResult(
                string.Equals(kind, NativeApprovalKindsV1.FinanceOfficialInvoiceImport, StringComparison.Ordinal)
                && approvalId == Batch
                    ? new ReadResult<NativeApprovalReadDetailV1>(ReadOutcome.Found, expected)
                    : new ReadResult<NativeApprovalReadDetailV1>(ReadOutcome.NotFound, null)),
        };
        var fixture = Fixture(adapter);

        var result = await fixture.Query.GetAsync(
            NativeApprovalKindsV1.FinanceOfficialInvoiceImport, Batch, default);

        Assert.Equal(ReadOutcome.Found, result.Outcome);
        Assert.Same(expected, result.Resource);
        Assert.Equal((Company, "checker", "corr-1", Batch, 1),
            (adapter.Context!.CompanyId, adapter.Context.ActorId, adapter.Context.CorrelationId,
                result.Resource!.Item.ApprovalId, adapter.GetCalls));
    }

    [Theory]
    [InlineData("company")]
    [InlineData("actor")]
    [InlineData("access")]
    [InlineData("authority")]
    public async Task Query_get_refuses_late_context_or_owner_authority_change_after_one_await(string change)
    {
        FixtureState? fixture = null;
        var adapter = new Adapter
        {
            OnGet = (_, _, _, _) =>
            {
                if (change == "company") fixture!.Company.Set(OtherCompany);
                if (change == "actor") fixture!.User.ActorId = "other-checker";
                if (change == "access") fixture!.User.RemoveAccess(Company);
                return Task.FromResult(change == "authority"
                    ? new ReadResult<NativeApprovalReadDetailV1>(ReadOutcome.Denied, null)
                    : new ReadResult<NativeApprovalReadDetailV1>(ReadOutcome.Found, NativeDetail()));
            },
        };
        fixture = Fixture(adapter);

        var result = await fixture.Query.GetAsync(
            NativeApprovalKindsV1.FinanceOfficialInvoiceImport, Batch, default);

        Assert.Equal(ReadOutcome.Denied, result.Outcome);
        Assert.Null(result.Resource);
        Assert.Equal(1, adapter.GetCalls);
    }

    [Theory]
    [InlineData("company-unresolved")]
    [InlineData("company-access-denied")]
    [InlineData("actor-unauthenticated")]
    [InlineData("actor-empty")]
    public async Task Query_rejects_invalid_initial_company_or_actor_without_calling_the_adapter(string refusal)
    {
        var adapter = new Adapter();
        var fixture = Fixture(adapter);
        if (refusal == "company-unresolved") fixture.Company.Set(Guid.Empty);
        if (refusal == "company-access-denied") fixture.User.RemoveAccess(Company);
        if (refusal == "actor-unauthenticated") fixture.User.IsAuthenticated = false;
        if (refusal == "actor-empty") fixture.User.ActorId = string.Empty;

        var page = await fixture.Query.ListAsync(null, default);
        var detail = await fixture.Query.GetAsync(
            NativeApprovalKindsV1.FinanceOfficialInvoiceImport, Batch, default);

        Assert.Equal(ReadOutcome.Denied, page.Outcome);
        Assert.Null(page.Resource);
        Assert.Equal(ReadOutcome.Denied, detail.Outcome);
        Assert.Null(detail.Resource);
        Assert.Equal((0, 0), (adapter.ListCalls, adapter.GetCalls));
    }

    [Theory]
    [InlineData("company")]
    [InlineData("actor")]
    [InlineData("access")]
    [InlineData("correlation")]
    public async Task Query_refuses_context_drift_after_the_single_owner_await(string drift)
    {
        FixtureState? fixture = null;
        var adapter = new Adapter
        {
            OnList = (context, _, _) =>
            {
                if (drift == "company") fixture!.Company.Set(OtherCompany);
                if (drift == "actor") fixture!.User.ActorId = "other-checker";
                if (drift == "access") fixture!.User.RemoveAccess(Company);
                if (drift == "correlation") fixture!.Correlation.Set("corr-2", "cause-1", "test", null, null);
                return Task.FromResult(EmptyPage(context.CompanyId));
            },
        };
        fixture = Fixture(adapter);

        var result = await fixture.Query.ListAsync(null, default);

        Assert.Equal(ReadOutcome.Denied, result.Outcome);
        Assert.Null(result.Resource);
        Assert.Equal(1, adapter.ListCalls);
    }

    [Theory]
    [InlineData(ReadOutcome.Denied)]
    [InlineData(ReadOutcome.Unavailable)]
    [InlineData(ReadOutcome.NotFound)]
    public async Task Owner_refusals_are_never_converted_to_successful_empty(ReadOutcome outcome)
    {
        var owner = new Owner { ListResult = new(outcome, null), GetResult = new(outcome, null) };
        var adapter = new FinanceInvoiceImportPendingApprovalAdapterV1(owner);

        var page = await adapter.ListAsync(Context(), "foreign-opaque", default);
        var detail = await adapter.GetAsync(Context(), NativeApprovalKindsV1.FinanceOfficialInvoiceImport, Batch, default);

        Assert.Equal(outcome, page.Outcome);
        Assert.Null(page.Resource);
        Assert.Equal(outcome, detail.Outcome);
        Assert.Null(detail.Resource);
        Assert.Equal("foreign-opaque", owner.ObservedCursor);
    }

    [Fact]
    public async Task Malformed_found_owner_replies_fail_closed_as_unavailable()
    {
        var owner = new Owner
        {
            ListResult = new(ReadOutcome.Found,
                new FinanceOfficialInvoiceImportPendingBatchPageV1(OtherCompany, [], null)),
            GetResult = new(ReadOutcome.Found, null),
        };
        var adapter = new FinanceInvoiceImportPendingApprovalAdapterV1(owner);

        var page = await adapter.ListAsync(Context(), null, default);
        var detail = await adapter.GetAsync(Context(), NativeApprovalKindsV1.FinanceOfficialInvoiceImport, Batch, default);

        Assert.Equal(ReadOutcome.Unavailable, page.Outcome);
        Assert.Null(page.Resource);
        Assert.Equal(ReadOutcome.Unavailable, detail.Outcome);
        Assert.Null(detail.Resource);
    }

    [Fact]
    public async Task Uploader_or_non_pending_owner_rows_fail_closed()
    {
        var uploaderRow = PendingBatch() with { UploadedBy = "checker" };
        var approvedRow = PendingBatch() with
        {
            Status = FinanceOfficialInvoiceImportApprovalStatusV1.Approved,
            ApprovedBy = "approver",
            ApprovedAtUtc = DateTimeOffset.Parse("2026-10-05T08:00:00Z"),
        };
        var owner = new Owner
        {
            ListResult = new(ReadOutcome.Found,
                new FinanceOfficialInvoiceImportPendingBatchPageV1(Company, [uploaderRow, approvedRow], null)),
        };

        var result = await new FinanceInvoiceImportPendingApprovalAdapterV1(owner)
            .ListAsync(Context(), null, default);

        Assert.Equal(ReadOutcome.Unavailable, result.Outcome);
        Assert.Null(result.Resource);
    }

    [Fact]
    public async Task Unknown_kind_and_empty_id_are_not_sent_to_the_owner()
    {
        var adapter = new Adapter();
        var fixture = Fixture(adapter);

        var unknown = await fixture.Query.GetAsync("unknown", Batch, default);
        var empty = await fixture.Query.GetAsync(NativeApprovalKindsV1.FinanceOfficialInvoiceImport, Guid.Empty, default);

        Assert.Equal(ReadOutcome.NotFound, unknown.Outcome);
        Assert.Equal(ReadOutcome.NotFound, empty.Outcome);
        Assert.Equal(0, adapter.GetCalls);
    }

    [Fact]
    public async Task Cancellation_and_owner_exceptions_are_not_converted_to_empty_results()
    {
        var cancelled = new CancellationToken(canceled: true);
        var fixture = Fixture(new Adapter());
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Query.ListAsync(null, cancelled));

        var throwing = Fixture(new Adapter
        {
            OnList = (_, _, _) => throw new InvalidOperationException("owner read failed"),
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => throwing.Query.ListAsync(null, default));
    }

    [Fact]
    public void Runtime_dependencies_are_read_only_and_contain_no_db_or_write_port()
    {
        var queryParameters = Constructor(typeof(NativeApprovalReadQueryV1));
        var adapterParameters = Constructor(typeof(FinanceInvoiceImportPendingApprovalAdapterV1));

        Assert.Equal(
            [typeof(ICompanyContext), typeof(ICurrentUser), typeof(ICorrelationContext), typeof(INativeApprovalReadAdapterV1)],
            queryParameters);
        Assert.Equal([typeof(IFinanceOfficialInvoiceImportPendingApprovalReadServiceV1)], adapterParameters);
        Assert.DoesNotContain(queryParameters.Concat(adapterParameters), type =>
            type.Name.Contains("DbContext", StringComparison.Ordinal)
            || type.Name.Contains("Write", StringComparison.Ordinal)
            || type.Name.Contains("Command", StringComparison.Ordinal));
    }

    private static Type[] Constructor(Type type) => type
        .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .Single()
        .GetParameters()
        .Select(parameter => parameter.ParameterType)
        .ToArray();

    private static FixtureState Fixture(Adapter adapter)
    {
        var company = new MutableCompanyContext(Company);
        var user = new MutableUser("checker", Company);
        var correlation = new MutableCorrelation();
        correlation.Set("corr-1", "cause-1", "test", null, null);
        return new(new NativeApprovalReadQueryV1(company, user, correlation, adapter), company, user, correlation);
    }

    private static ReadResult<NativeApprovalReadPageV1> EmptyPage(Guid company) =>
        new(ReadOutcome.Found, new NativeApprovalReadPageV1(company, [], null));

    private static NativeApprovalReadDetailV1 NativeDetail()
    {
        var batch = PendingBatch();
        return new(
            new NativeApprovalReadItemV1(
                NativeApprovalKindsV1.FinanceOfficialInvoiceImport,
                batch.BatchId,
                batch.CompanyId,
                batch.Status,
                batch.Version,
                batch.BatchVersion,
                batch.BatchChecksum,
                batch.TemplateVersion,
                batch.RowCount,
                batch.UploadedBy,
                batch.UploadedAtUtc),
            Preview(batch));
    }

    private static FinanceContext Context() => new(Company, "checker", "corr-1", "cause-1");

    private static FinanceOfficialInvoiceImportBatchViewV1 PendingBatch() => new(
        Batch,
        Company,
        "source-version-7",
        Checksum,
        FinanceOfficialInvoiceImportTemplateV1.Version,
        FinanceOfficialInvoiceImportApprovalStatusV1.Pending,
        "1",
        3,
        "maker",
        DateTimeOffset.Parse("2026-10-05T07:00:00Z"),
        null,
        null);

    private static FinanceOfficialInvoiceImportPreviewV1 Preview(
        FinanceOfficialInvoiceImportBatchViewV1 batch) => new(
        batch.TemplateVersion,
        batch.BatchChecksum,
        new ApprovedFinanceOpenItemSourceBatch(
            batch.BatchId,
            batch.CompanyId,
            batch.BatchVersion,
            batch.BatchChecksum,
            Enumerable.Range(1, batch.RowCount)
                .Select(index => new ApprovedFinanceOpenItemSourceRow(
                    Guid.Parse($"b1000000-0000-0000-0000-{index:D12}"),
                    Guid.Parse($"b2000000-0000-0000-0000-{index:D12}"),
                    Guid.Parse($"b3000000-0000-0000-0000-{index:D12}"),
                    $"invoice-{index}",
                    FinanceOpenItemSourceKinds.OfficialInvoiceInstallmentDocument,
                    Guid.Parse($"b4000000-0000-0000-0000-{index:D12}"),
                    Guid.Parse($"b5000000-0000-0000-0000-{index:D12}"),
                    Guid.Parse($"b6000000-0000-0000-0000-{index:D12}"),
                    "1250.50",
                    new DateOnly(2026, 10, index)))
                .ToArray()),
        []);

    private sealed class Owner : IFinanceOfficialInvoiceImportPendingApprovalReadServiceV1
    {
        public ReadResult<FinanceOfficialInvoiceImportPendingBatchPageV1> ListResult { get; set; } =
            new(ReadOutcome.Found, new FinanceOfficialInvoiceImportPendingBatchPageV1(Company, [], null));
        public ReadResult<FinanceOfficialInvoiceImportPendingBatchDetailV1> GetResult { get; set; } =
            new(ReadOutcome.NotFound, null);
        public int ListCalls { get; private set; }
        public int GetCalls { get; private set; }
        public string? ObservedCursor { get; private set; }

        public Task<ReadResult<FinanceOfficialInvoiceImportPendingBatchPageV1>> ListAsync(
            FinanceContext context, string? cursor, CancellationToken cancellationToken)
        {
            ListCalls++;
            ObservedCursor = cursor;
            return Task.FromResult(ListResult);
        }

        public Task<ReadResult<FinanceOfficialInvoiceImportPendingBatchDetailV1>> GetAsync(
            FinanceContext context, Guid batchId, CancellationToken cancellationToken)
        {
            GetCalls++;
            return Task.FromResult(GetResult);
        }
    }

    private sealed class Adapter : INativeApprovalReadAdapterV1
    {
        public Func<FinanceContext, string?, CancellationToken, Task<ReadResult<NativeApprovalReadPageV1>>>? OnList { get; init; }
        public Func<FinanceContext, string, Guid, CancellationToken, Task<ReadResult<NativeApprovalReadDetailV1>>>? OnGet { get; init; }
        public FinanceContext? Context { get; private set; }
        public string? Cursor { get; private set; }
        public int ListCalls { get; private set; }
        public int GetCalls { get; private set; }

        public Task<ReadResult<NativeApprovalReadPageV1>> ListAsync(
            FinanceContext context, string? cursor, CancellationToken cancellationToken)
        {
            ListCalls++;
            Context = context;
            Cursor = cursor;
            return OnList?.Invoke(context, cursor, cancellationToken) ?? Task.FromResult(EmptyPage(context.CompanyId));
        }

        public Task<ReadResult<NativeApprovalReadDetailV1>> GetAsync(
            FinanceContext context, string kind, Guid approvalId, CancellationToken cancellationToken)
        {
            GetCalls++;
            Context = context;
            return OnGet?.Invoke(context, kind, approvalId, cancellationToken)
                ?? Task.FromResult(new ReadResult<NativeApprovalReadDetailV1>(ReadOutcome.NotFound, null));
        }
    }

    private sealed class MutableCompanyContext(Guid companyId) : ICompanyContext
    {
        public bool IsResolved { get; private set; } = true;
        public Guid CompanyId { get; private set; } = companyId;
        public void Set(Guid value) { CompanyId = value; IsResolved = value != Guid.Empty; }
    }

    private sealed class MutableUser(string actorId, Guid companyId) : ICurrentUser
    {
        private readonly HashSet<Guid> allowedCompanyIds = [companyId, OtherCompany];

        public bool IsAuthenticated { get; set; } = true;
        public string? UserId => ActorId;
        public string ActorId { get; set; } = actorId;
        public IReadOnlySet<Guid> AllowedCompanyIds => allowedCompanyIds;
        public bool CanAccessCompany(Guid value) => AllowedCompanyIds.Contains(value);
        public void RemoveAccess(Guid value) => allowedCompanyIds.Remove(value);
    }

    private sealed class MutableCorrelation : ICorrelationContext
    {
        public bool IsResolved { get; private set; }
        public string CorrelationId { get; private set; } = string.Empty;
        public string? CausationId { get; private set; }
        public string SourceChannel { get; private set; } = string.Empty;
        public string? ClientIpAddress { get; private set; }
        public string? UserAgent { get; private set; }

        public void Set(string correlationId, string? causationId, string sourceChannel,
            string? clientIpAddress, string? userAgent)
        {
            IsResolved = true;
            CorrelationId = correlationId;
            CausationId = causationId;
            SourceChannel = sourceChannel;
            ClientIpAddress = clientIpAddress;
            UserAgent = userAgent;
        }
    }

    private sealed record FixtureState(
        NativeApprovalReadQueryV1 Query,
        MutableCompanyContext Company,
        MutableUser User,
        MutableCorrelation Correlation);
}
