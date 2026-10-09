using System.Reflection;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class NativeApprovalReadQueryV1Tests
{
    private static readonly Guid Company = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherCompany = Guid.Parse("a1000000-0000-0000-0000-000000000002");
    private static readonly Guid Batch = Guid.Parse("a2000000-0000-0000-0000-000000000001");
    private static readonly Guid Draft = Guid.Parse("a3000000-0000-0000-0000-000000000001");
    private static readonly string Checksum = new('a', 64);

    [Fact]
    public async Task Finance_adapter_preserves_owner_cursor_and_projects_the_legacy_wire()
    {
        var batch = PendingBatch();
        var preview = Preview(batch);
        var owner = new Owner
        {
            ListResult = new(ReadOutcome.Found,
                new FinanceOfficialInvoiceImportPendingBatchPageV1(Company, [batch], "opaque+/=cursor")),
            GetResult = new(ReadOutcome.Found,
                new FinanceOfficialInvoiceImportPendingBatchDetailV1(batch, preview)),
        };
        var adapter = new FinanceInvoiceImportPendingApprovalAdapterV1(owner);
        var context = Context();

        var page = await adapter.ListAsync(context, "opaque+/=request", default);
        var detail = await adapter.GetAsync(context, Batch, default);

        var item = Assert.IsType<FinanceInvoiceImportNativeApprovalReadItemV1>(
            Assert.Single(page.Resource!.Items));
        Assert.Equal(NativeApprovalReadOutcomeV1.Found, page.Outcome);
        Assert.Equal(("opaque+/=request", "opaque+/=cursor", 1),
            (owner.ObservedCursor, page.Resource.NextCursor, owner.ListCalls));
        Assert.Equal((NativeApprovalKindsV1.FinanceOfficialInvoiceImport, Batch, Company, "Pending",
                "1", "source-version-7", Checksum, FinanceOfficialInvoiceImportTemplateV1.Version, "maker", 3),
            (item.Kind, item.ApprovalId, item.CompanyId, item.Status, item.Version, item.BatchVersion,
                item.Checksum, item.TemplateVersion, item.SubmittedBy, item.ItemCount));
        var financeDetail = Assert.IsType<FinanceInvoiceImportNativeApprovalReadDetailV1>(detail.Resource);
        Assert.Equal((NativeApprovalReadOutcomeV1.Found, Batch, Checksum, 1),
            (detail.Outcome, financeDetail.Item.ApprovalId, financeDetail.Preview.SourceSha256,
                owner.GetCalls));
        Assert.Same(preview, financeDetail.Preview);
    }

    [Fact]
    public async Task Sales_adapter_preserves_owner_cursor_and_projects_pending_draft_without_a_creator_ban()
    {
        var source = SalesItem(isOwnDraft: true);
        var owner = new SalesOwner
        {
            ListResult = new(SalesOrderDraftPendingApprovalReadOutcomeV1.Found,
                new SalesOrderDraftPendingApprovalPageV1(Company, [source], "sales+/=cursor")),
            GetResult = new(SalesOrderDraftPendingApprovalReadOutcomeV1.Found,
                new SalesOrderDraftPendingApprovalDetailV1(source)),
        };
        var adapter = new SalesOrderDraftPendingApprovalAdapterV1(owner);
        var context = Context();

        var page = await adapter.ListAsync(context, "sales+/=request", default);
        var detail = await adapter.GetAsync(context, Draft, default);

        var item = Assert.IsType<SalesOrderDraftNativeApprovalReadItemV1>(
            Assert.Single(page.Resource!.Items));
        Assert.Equal(NativeApprovalReadOutcomeV1.Found, page.Outcome);
        Assert.Equal(("sales+/=request", "sales+/=cursor", 1),
            (owner.ObservedCursor, page.Resource.NextCursor, owner.ListCalls));
        Assert.Equal((NativeApprovalKindsV1.SalesOrderDraft, Draft, Company, "PendingApproval",
                "7", "3", "SO-2026-001", true, "sales-maker"),
            (item.Kind, item.ApprovalId, item.CompanyId, item.Status, item.Version,
                item.Revision, item.DocumentNo, item.IsOwnDraft, item.SubmittedBy));
        var salesDetail = Assert.IsType<SalesOrderDraftNativeApprovalReadDetailV1>(detail.Resource);
        Assert.Equal(item, salesDetail.Item);
        Assert.Equal((NativeApprovalReadOutcomeV1.Found, Draft, 1),
            (detail.Outcome, salesDetail.Item.ApprovalId, owner.GetCalls));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("blank-document")]
    [InlineData("company")]
    [InlineData("found-null")]
    [InlineData("failure-resource")]
    public async Task Sales_adapter_malformed_owner_results_fail_closed(string defect)
    {
        var source = SalesItem(
            companyId: defect == "company" ? OtherCompany : Company,
            documentNo: defect == "blank-document" ? " " : "SO-2026-001");
        var page = new SalesOrderDraftPendingApprovalPageV1(
            Company,
            defect == "duplicate" ? [source, source] : [source],
            null);
        var owner = new SalesOwner
        {
            ListResult = defect switch
            {
                "found-null" => new(SalesOrderDraftPendingApprovalReadOutcomeV1.Found, null),
                "failure-resource" => new(SalesOrderDraftPendingApprovalReadOutcomeV1.Denied, page),
                _ => new(SalesOrderDraftPendingApprovalReadOutcomeV1.Found, page),
            },
        };

        var result = await new SalesOrderDraftPendingApprovalAdapterV1(owner)
            .ListAsync(Context(), null, default);

        Assert.Equal(NativeApprovalReadOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.Resource);
    }

    [Theory]
    [InlineData("Denied")]
    [InlineData("NotFound")]
    [InlineData("Unavailable")]
    public async Task Sales_adapter_preserves_strict_owner_refusals(
        string outcomeName)
    {
        var ownerOutcome = Enum.Parse<SalesOrderDraftPendingApprovalReadOutcomeV1>(outcomeName);
        var expected = Enum.Parse<NativeApprovalReadOutcomeV1>(outcomeName);
        var owner = new SalesOwner
        {
            ListResult = new(ownerOutcome, null),
            GetResult = new(ownerOutcome, null),
        };
        var adapter = new SalesOrderDraftPendingApprovalAdapterV1(owner);

        var page = await adapter.ListAsync(Context(), null, default);
        var detail = await adapter.GetAsync(Context(), Draft, default);

        Assert.Equal(expected, page.Outcome);
        Assert.Equal(expected, detail.Outcome);
        Assert.Null(page.Resource);
        Assert.Null(detail.Resource);
    }

    [Fact]
    public async Task Null_kind_defaults_to_finance_and_passes_cursor_byte_for_byte_once()
    {
        var adapter = new Adapter();
        var fixture = Fixture([adapter]);

        var result = await fixture.Query.ListAsync(null, "opaque+/=cursor", default);

        Assert.Equal(NativeApprovalReadOutcomeV1.Found, result.Outcome);
        Assert.Empty(result.Resource!.Items);
        Assert.Equal((NativeApprovalKindsV1.FinanceOfficialInvoiceImport, "opaque+/=cursor", 1),
            (adapter.Kind, adapter.Cursor, adapter.ListCalls));
        Assert.Equal((Company, "checker", "corr-1", "cause-1"),
            (adapter.Context!.CompanyId, adapter.Context.ActorId,
                adapter.Context.CorrelationId, adapter.Context.CausationId));
    }

    [Fact]
    public async Task Exact_known_kind_selects_one_adapter_and_returns_the_exact_detail()
    {
        var expected = NativeDetail();
        var adapter = new Adapter
        {
            OnGet = (_, approvalId, _) => Task.FromResult(
                approvalId == Batch
                    ? new NativeApprovalReadResultV1<NativeApprovalReadDetailV1>(
                        NativeApprovalReadOutcomeV1.Found, expected)
                    : new NativeApprovalReadResultV1<NativeApprovalReadDetailV1>(
                        NativeApprovalReadOutcomeV1.NotFound, null)),
        };
        var fixture = Fixture([adapter]);

        var result = await fixture.Query.GetAsync(
            NativeApprovalKindsV1.FinanceOfficialInvoiceImport, Batch, default);

        Assert.Equal(NativeApprovalReadOutcomeV1.Found, result.Outcome);
        Assert.Same(expected, result.Resource);
        Assert.Equal(1, adapter.GetCalls);
    }

    [Fact]
    public async Task Unknown_kind_is_not_found_but_known_unregistered_is_unavailable()
    {
        var fixture = Fixture([]);

        var unknown = await fixture.Query.ListAsync("unknown", "foreign", default);
        var known = await fixture.Query.ListAsync(
            NativeApprovalKindsV1.FinanceOfficialInvoiceImport, "owned", default);
        var detail = await fixture.Query.GetAsync(
            NativeApprovalKindsV1.FinanceOfficialInvoiceImport, Batch, default);

        Assert.Equal(NativeApprovalReadOutcomeV1.NotFound, unknown.Outcome);
        Assert.Equal(NativeApprovalReadOutcomeV1.Unavailable, known.Outcome);
        Assert.Equal(NativeApprovalReadOutcomeV1.Unavailable, detail.Outcome);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    public async Task Invalid_adapter_registration_fails_closed_as_unavailable(string defect)
    {
        var first = new Adapter
        {
            Kind = defect == "empty" ? string.Empty
                : defect == "unknown" ? "unknown"
                : NativeApprovalKindsV1.FinanceOfficialInvoiceImport,
        };
        var adapters = defect == "duplicate"
            ? new INativeApprovalReadAdapterV1[] { first, new Adapter() }
            : [first];
        var fixture = Fixture(adapters);

        var result = await fixture.Query.ListAsync(null, null, default);

        Assert.Equal(NativeApprovalReadOutcomeV1.Unavailable, result.Outcome);
        Assert.Equal(0, adapters.Sum(adapter => ((Adapter)adapter).ListCalls));
    }

    [Theory]
    [InlineData("company")]
    [InlineData("actor")]
    [InlineData("access")]
    [InlineData("correlation")]
    public async Task Query_refuses_context_drift_after_the_single_adapter_await(string drift)
    {
        FixtureState? fixture = null;
        var adapter = new Adapter
        {
            OnList = (context, _, _) =>
            {
                if (drift == "company") fixture!.Company.Set(OtherCompany);
                if (drift == "actor") fixture!.User.ActorId = "other-checker";
                if (drift == "access") fixture!.User.RemoveAccess(Company);
                if (drift == "correlation")
                {
                    fixture!.Correlation.Set("corr-2", "cause-1", "test", null, null);
                }
                return Task.FromResult(EmptyPage(context.CompanyId));
            },
        };
        fixture = Fixture([adapter]);

        var result = await fixture.Query.ListAsync(null, null, default);

        Assert.Equal(NativeApprovalReadOutcomeV1.Denied, result.Outcome);
        Assert.Null(result.Resource);
        Assert.Equal(1, adapter.ListCalls);
    }

    [Theory]
    [InlineData("page-company")]
    [InlineData("item-company")]
    [InlineData("item-id")]
    [InlineData("item-kind")]
    [InlineData("empty-cursor")]
    [InlineData("failure-resource")]
    public async Task Malformed_adapter_replies_fail_closed_as_unavailable(string defect)
    {
        NativeApprovalReadItemV1 item = defect == "item-kind"
            ? new ForeignItem(Batch, Company, "Pending", "1", "maker", DateTimeOffset.UtcNow)
            : Item(
                companyId: defect == "item-company" ? OtherCompany : Company,
                approvalId: defect == "item-id" ? Guid.Empty : Batch);
        var page = new NativeApprovalReadPageV1(
            defect == "page-company" ? OtherCompany : Company,
            [item],
            defect == "empty-cursor" ? string.Empty : null);
        var adapter = new Adapter
        {
            OnList = (_, _, _) => Task.FromResult(
                defect == "failure-resource"
                    ? new NativeApprovalReadResultV1<NativeApprovalReadPageV1>(
                        NativeApprovalReadOutcomeV1.Denied, page)
                    : new NativeApprovalReadResultV1<NativeApprovalReadPageV1>(
                        NativeApprovalReadOutcomeV1.Found, page)),
        };

        var result = await Fixture([adapter]).Query.ListAsync(null, null, default);

        Assert.Equal(NativeApprovalReadOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.Resource);
    }

    [Fact]
    public async Task Finance_owner_refusals_and_malformed_found_results_are_never_empty_success()
    {
        var owner = new Owner
        {
            ListResult = new(ReadOutcome.Denied, null),
            GetResult = new(ReadOutcome.Found, null),
        };
        var adapter = new FinanceInvoiceImportPendingApprovalAdapterV1(owner);

        var page = await adapter.ListAsync(Context(), "foreign-opaque", default);
        var detail = await adapter.GetAsync(Context(), Batch, default);

        Assert.Equal(NativeApprovalReadOutcomeV1.Denied, page.Outcome);
        Assert.Equal(NativeApprovalReadOutcomeV1.Unavailable, detail.Outcome);
        Assert.Null(page.Resource);
        Assert.Null(detail.Resource);
        Assert.Equal("foreign-opaque", owner.ObservedCursor);
    }

    [Fact]
    public void Runtime_dependencies_are_read_only_and_module_neutral()
    {
        var queryParameters = Constructor(typeof(NativeApprovalReadQueryV1));
        var financeAdapterParameters = Constructor(typeof(FinanceInvoiceImportPendingApprovalAdapterV1));
        var salesAdapterParameters = Constructor(typeof(SalesOrderDraftPendingApprovalAdapterV1));

        Assert.Equal(
            [typeof(ICompanyContext), typeof(ICurrentUser), typeof(ICorrelationContext),
                typeof(IEnumerable<INativeApprovalReadAdapterV1>)],
            queryParameters);
        Assert.Equal([typeof(IFinanceOfficialInvoiceImportPendingApprovalReadServiceV1)],
            financeAdapterParameters);
        Assert.Equal([typeof(ISalesOrderDraftPendingApprovalReadServiceV1)], salesAdapterParameters);
        Assert.DoesNotContain(queryParameters, type => type.Namespace?.Contains(".Finance.", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(queryParameters.Concat(financeAdapterParameters).Concat(salesAdapterParameters), type =>
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

    private static FixtureState Fixture(IEnumerable<INativeApprovalReadAdapterV1> adapters)
    {
        var company = new MutableCompanyContext(Company);
        var user = new MutableUser("checker", Company);
        var correlation = new MutableCorrelation();
        correlation.Set("corr-1", "cause-1", "test", null, null);
        return new(new NativeApprovalReadQueryV1(company, user, correlation, adapters),
            company, user, correlation);
    }

    private static NativeApprovalReadResultV1<NativeApprovalReadPageV1> EmptyPage(Guid company) =>
        new(NativeApprovalReadOutcomeV1.Found, new NativeApprovalReadPageV1(company, [], null));

    private static FinanceInvoiceImportNativeApprovalReadDetailV1 NativeDetail()
    {
        var batch = PendingBatch();
        return new(Item(), Preview(batch));
    }

    private static FinanceInvoiceImportNativeApprovalReadItemV1 Item(
        Guid? companyId = null,
        Guid? approvalId = null) => new(
        approvalId ?? Batch,
        companyId ?? Company,
        "Pending",
        "1",
        "source-version-7",
        Checksum,
        FinanceOfficialInvoiceImportTemplateV1.Version,
        3,
        "maker",
        DateTimeOffset.Parse("2026-10-05T07:00:00Z"));

    private static NativeApprovalReadContextV1 Context() =>
        new(Company, "checker", "corr-1", "cause-1");

    private static SalesOrderDraftPendingApprovalItemV1 SalesItem(
        Guid? companyId = null,
        string documentNo = "SO-2026-001",
        bool isOwnDraft = false) => new(
        Draft,
        companyId ?? Company,
        documentNo,
        "7",
        "3",
        "sales-maker",
        DateTimeOffset.Parse("2026-10-09T07:00:00Z"),
        isOwnDraft);

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
        public ReadResult<FinanceOfficialInvoiceImportPendingBatchPageV1> ListResult { get; init; } =
            new(ReadOutcome.Found, new FinanceOfficialInvoiceImportPendingBatchPageV1(Company, [], null));
        public ReadResult<FinanceOfficialInvoiceImportPendingBatchDetailV1> GetResult { get; init; } =
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

    private sealed class SalesOwner : ISalesOrderDraftPendingApprovalReadServiceV1
    {
        public SalesOrderDraftPendingApprovalReadResultV1<SalesOrderDraftPendingApprovalPageV1>
            ListResult { get; init; } = new(SalesOrderDraftPendingApprovalReadOutcomeV1.Found,
                new SalesOrderDraftPendingApprovalPageV1(Company, [], null));
        public SalesOrderDraftPendingApprovalReadResultV1<SalesOrderDraftPendingApprovalDetailV1>
            GetResult { get; init; } = new(SalesOrderDraftPendingApprovalReadOutcomeV1.NotFound, null);
        public string? ObservedCursor { get; private set; }
        public int ListCalls { get; private set; }
        public int GetCalls { get; private set; }

        public Task<SalesOrderDraftPendingApprovalReadResultV1<SalesOrderDraftPendingApprovalPageV1>>
            ListAsync(SalesOrderDraftPendingApprovalReadContextV1 context, string? cursor,
                CancellationToken cancellationToken)
        {
            ListCalls++;
            ObservedCursor = cursor;
            return Task.FromResult(ListResult);
        }

        public Task<SalesOrderDraftPendingApprovalReadResultV1<SalesOrderDraftPendingApprovalDetailV1>>
            GetAsync(SalesOrderDraftPendingApprovalReadContextV1 context, Guid draftId,
                CancellationToken cancellationToken)
        {
            GetCalls++;
            return Task.FromResult(GetResult);
        }
    }

    private sealed class Adapter : INativeApprovalReadAdapterV1
    {
        public string Kind { get; init; } = NativeApprovalKindsV1.FinanceOfficialInvoiceImport;
        public Func<NativeApprovalReadContextV1, string?, CancellationToken,
            Task<NativeApprovalReadResultV1<NativeApprovalReadPageV1>>>? OnList { get; init; }
        public Func<NativeApprovalReadContextV1, Guid, CancellationToken,
            Task<NativeApprovalReadResultV1<NativeApprovalReadDetailV1>>>? OnGet { get; init; }
        public NativeApprovalReadContextV1? Context { get; private set; }
        public string? Cursor { get; private set; }
        public int ListCalls { get; private set; }
        public int GetCalls { get; private set; }

        public Task<NativeApprovalReadResultV1<NativeApprovalReadPageV1>> ListAsync(
            NativeApprovalReadContextV1 context,
            string? cursor,
            CancellationToken cancellationToken)
        {
            ListCalls++;
            Context = context;
            Cursor = cursor;
            return OnList?.Invoke(context, cursor, cancellationToken)
                ?? Task.FromResult(EmptyPage(context.CompanyId));
        }

        public Task<NativeApprovalReadResultV1<NativeApprovalReadDetailV1>> GetAsync(
            NativeApprovalReadContextV1 context,
            Guid approvalId,
            CancellationToken cancellationToken)
        {
            GetCalls++;
            Context = context;
            return OnGet?.Invoke(context, approvalId, cancellationToken)
                ?? Task.FromResult(new NativeApprovalReadResultV1<NativeApprovalReadDetailV1>(
                    NativeApprovalReadOutcomeV1.NotFound, null));
        }
    }

    private sealed record ForeignItem(
        Guid ApprovalId,
        Guid CompanyId,
        string Status,
        string Version,
        string SubmittedBy,
        DateTimeOffset SubmittedAtUtc)
        : NativeApprovalReadItemV1(
            ApprovalId, CompanyId, Status, Version, SubmittedBy, SubmittedAtUtc)
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public override string Kind => "foreign";
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
