using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class NativeApprovalQueueReadQueryV1Tests
{
    private static readonly Guid Company = Guid.Parse("c1000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherCompany = Guid.Parse("c1000000-0000-0000-0000-000000000002");
    private static readonly Guid First = Guid.Parse("c2000000-0000-0000-0000-000000000001");
    private static readonly Guid Second = Guid.Parse("c2000000-0000-0000-0000-000000000002");
    private static readonly Guid Batch = Guid.Parse("c3000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Early = DateTimeOffset.Parse("2026-11-01T07:00:00Z");
    private static readonly DateTimeOffset Late = DateTimeOffset.Parse("2026-11-02T07:00:00Z");

    [Fact]
    public async Task Module_sources_are_merged_oldest_first_with_the_captured_context_and_the_size_bound()
    {
        var purchasing = new Source("purchasing", "purchasing.request.v1")
        {
            Result = ctx => NativeApprovalQueueResultV1.Found(ctx.CompanyId,
                [QueueItem("purchasing", "purchasing.request.v1", Second, Late)], hasMore: true),
        };
        var logistics = new Source("logistics", "logistics.shipment.v1")
        {
            Result = ctx => NativeApprovalQueueResultV1.Found(ctx.CompanyId,
                [QueueItem("logistics", "logistics.shipment.v1", First, Early)], hasMore: false),
        };
        var fixture = Fixture([purchasing, logistics], []);

        var result = await fixture.Query.ListAsync(default);

        Assert.Equal(NativeApprovalReadOutcomeV1.Found, result.Outcome);
        var page = result.Resource!;
        Assert.Equal(Company, page.CompanyId);
        Assert.Equal([First, Second], page.Items.Select(item => item.ItemId).ToArray());
        Assert.Equal(
            [("logistics", "logistics.shipment.v1", NativeApprovalQueueSourceStatesV1.Ready, false),
                ("purchasing", "purchasing.request.v1", NativeApprovalQueueSourceStatesV1.Ready, true)],
            page.Sources.Select(source => (source.Module, source.Kind, source.State, source.HasMore)).ToArray());
        Assert.Equal((Company, "checker", "corr-1", "cause-1", NativeApprovalQueueRulesV1.MaxItemsPerSource),
            (purchasing.Context!.CompanyId, purchasing.Context.UserId, purchasing.Context.CorrelationId,
                purchasing.Context.CausationId, purchasing.MaxCount));
        Assert.Equal((1, 1), (purchasing.Calls, logistics.Calls));
    }

    [Theory]
    [InlineData("company")]
    [InlineData("module")]
    [InlineData("kind")]
    [InlineData("empty-id")]
    [InlineData("duplicate")]
    [InlineData("offset")]
    [InlineData("default-instant")]
    [InlineData("absolute-link")]
    [InlineData("protocol-relative-link")]
    [InlineData("dot-segment-link")]
    [InlineData("encoded-dot-segment-link")]
    [InlineData("null-items")]
    [InlineData("too-many")]
    [InlineData("denied-with-items")]
    public async Task A_malformed_source_answer_marks_only_that_source_unavailable_and_hides_its_items(string defect)
    {
        var broken = new Source("purchasing", "purchasing.request.v1")
        {
            Result = ctx => Malformed(defect, ctx.CompanyId),
        };
        var healthy = new Source("logistics", "logistics.shipment.v1")
        {
            Result = ctx => NativeApprovalQueueResultV1.Found(ctx.CompanyId,
                [QueueItem("logistics", "logistics.shipment.v1", First, Early)], hasMore: false),
        };

        var result = await Fixture([broken, healthy], []).Query.ListAsync(default);

        Assert.Equal(NativeApprovalReadOutcomeV1.Found, result.Outcome);
        Assert.Equal([First], result.Resource!.Items.Select(item => item.ItemId).ToArray());
        Assert.Equal(
            [("logistics", NativeApprovalQueueSourceStatesV1.Ready),
                ("purchasing", NativeApprovalQueueSourceStatesV1.Unavailable)],
            result.Resource.Sources.Select(source => (source.Module, source.State)).ToArray());
    }

    [Fact]
    public async Task A_denied_source_is_omitted_and_all_sources_denied_is_a_denial()
    {
        var denied = new Source("purchasing", "purchasing.request.v1")
        {
            Result = _ => NativeApprovalQueueResultV1.Denied(),
        };
        var allowed = new Source("logistics", "logistics.shipment.v1")
        {
            Result = ctx => NativeApprovalQueueResultV1.Found(ctx.CompanyId, [], hasMore: false),
        };

        var partly = await Fixture([denied, allowed], []).Query.ListAsync(default);
        var wholly = await Fixture([denied], []).Query.ListAsync(default);

        Assert.Equal(NativeApprovalReadOutcomeV1.Found, partly.Outcome);
        Assert.Empty(partly.Resource!.Items);
        Assert.Equal(["logistics"], partly.Resource.Sources.Select(source => source.Module).ToArray());
        Assert.Equal(NativeApprovalReadOutcomeV1.Denied, wholly.Outcome);
        Assert.Null(wholly.Resource);
    }

    [Fact]
    public async Task Every_readable_source_unavailable_is_unavailable_and_no_source_is_an_empty_queue()
    {
        var down = new Source("purchasing", "purchasing.request.v1")
        {
            Result = _ => NativeApprovalQueueResultV1.Unavailable(),
        };

        var unavailable = await Fixture([down], []).Query.ListAsync(default);
        var none = await Fixture([], []).Query.ListAsync(default);

        Assert.Equal(NativeApprovalReadOutcomeV1.Unavailable, unavailable.Outcome);
        Assert.Null(unavailable.Resource);
        Assert.Equal(NativeApprovalReadOutcomeV1.Found, none.Outcome);
        Assert.Empty(none.Resource!.Items);
        Assert.Empty(none.Resource.Sources);
    }

    [Theory]
    [InlineData("duplicate-kind")]
    [InlineData("kind-of-another-module")]
    [InlineData("unversioned-kind")]
    [InlineData("uppercase-module")]
    [InlineData("typed-kind-also-served-by-a-module")]
    public async Task An_inexact_registration_makes_the_whole_queue_unavailable_before_any_source_is_read(string defect)
    {
        var good = new Source("logistics", "logistics.shipment.v1");
        var bad = defect switch
        {
            "duplicate-kind" => new Source("logistics", "logistics.shipment.v1"),
            "kind-of-another-module" => new Source("logistics", "purchasing.request.v1"),
            "unversioned-kind" => new Source("logistics", "logistics.shipment"),
            "uppercase-module" => new Source("Logistics", "Logistics.shipment.v1"),
            _ => new Source("finance", NativeApprovalKindsV1.FinanceOfficialInvoiceImport),
        };

        var result = await Fixture([good, bad], [new TypedAdapter()]).Query.ListAsync(default);

        Assert.Equal(NativeApprovalReadOutcomeV1.Unavailable, result.Outcome);
        Assert.Equal((0, 0), (good.Calls, bad.Calls));
    }

    [Theory]
    [InlineData("company")]
    [InlineData("actor")]
    [InlineData("access")]
    [InlineData("correlation")]
    public async Task Context_drift_after_any_source_denies_the_whole_read(string drift)
    {
        FixtureState? fixture = null;
        var drifting = new Source("logistics", "logistics.shipment.v1")
        {
            Result = ctx =>
            {
                if (drift == "company") fixture!.Company.Set(OtherCompany);
                if (drift == "actor") fixture!.User.ActorId = "other-checker";
                if (drift == "access") fixture!.User.RemoveAccess(Company);
                if (drift == "correlation") fixture!.Correlation.Set("corr-2", "cause-1", "test", null, null);
                return NativeApprovalQueueResultV1.Found(ctx.CompanyId, [], hasMore: false);
            },
        };
        var later = new Source("purchasing", "purchasing.request.v1");
        fixture = Fixture([drifting, later], []);

        var result = await fixture.Query.ListAsync(default);

        Assert.Equal(NativeApprovalReadOutcomeV1.Denied, result.Outcome);
        Assert.Null(result.Resource);
        Assert.Equal(0, later.Calls);
    }

    [Theory]
    [InlineData("unresolved-company")]
    [InlineData("anonymous")]
    [InlineData("no-access")]
    [InlineData("no-correlation")]
    public async Task An_uncapturable_request_reads_no_source(string defect)
    {
        var source = new Source("logistics", "logistics.shipment.v1");
        var fixture = Fixture([source], []);
        if (defect == "unresolved-company") fixture.Company.Set(Guid.Empty);
        if (defect == "anonymous") fixture.User.IsAuthenticated = false;
        if (defect == "no-access") fixture.User.RemoveAccess(Company);
        if (defect == "no-correlation") fixture.Correlation.Clear();

        var result = await fixture.Query.ListAsync(default);

        Assert.Equal(defect == "no-correlation"
            ? NativeApprovalReadOutcomeV1.Unavailable
            : NativeApprovalReadOutcomeV1.Denied, result.Outcome);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task A_typed_source_is_listed_from_its_first_page_with_the_work_detail_link()
    {
        var typed = new TypedAdapter
        {
            Page = ctx => new(NativeApprovalReadOutcomeV1.Found,
                new NativeApprovalReadPageV1(ctx.CompanyId, [FinanceItem(ctx.CompanyId)], "next+/=cursor")),
        };

        var result = await Fixture([], [typed]).Query.ListAsync(default);

        Assert.Equal(NativeApprovalReadOutcomeV1.Found, result.Outcome);
        var item = Assert.Single(result.Resource!.Items);
        Assert.Equal(("finance", Batch, NativeApprovalKindsV1.FinanceOfficialInvoiceImport,
                DateTimeOffset.Parse("2026-10-05T07:00:00Z"),
                "/onaylarim/finance.official-invoice-import.v1/" + Batch.ToString("D")),
            (item.Module, item.ItemId, item.Kind, item.CreatedAtUtc, item.Link));
        Assert.Equal(TimeSpan.Zero, item.CreatedAtUtc.Offset);
        var source = Assert.Single(result.Resource.Sources);
        Assert.Equal(("finance", NativeApprovalQueueSourceStatesV1.Ready, true),
            (source.Module, source.State, source.HasMore));
        Assert.Null(typed.Cursor);
        Assert.Equal(1, typed.Calls);
        Assert.Equal((Company, "checker", "corr-1", "cause-1", false),
            (typed.Context!.CompanyId, typed.Context.ActorId, typed.Context.CorrelationId,
                typed.Context.CausationId, typed.Context.IncludeInventoryLabelCode));
    }

    [Theory]
    [InlineData("Denied", "omitted")]
    [InlineData("NotFound", "Unavailable")]
    [InlineData("Unavailable", "Unavailable")]
    [InlineData("other-company", "Unavailable")]
    public async Task A_typed_source_refusal_or_malformed_page_follows_the_inbox_rules(string answer, string expected)
    {
        var typed = new TypedAdapter
        {
            Page = ctx => answer == "other-company"
                ? new(NativeApprovalReadOutcomeV1.Found,
                    new NativeApprovalReadPageV1(OtherCompany, [FinanceItem(OtherCompany)], null))
                : new(Enum.Parse<NativeApprovalReadOutcomeV1>(answer), null),
        };
        var healthy = new Source("logistics", "logistics.shipment.v1")
        {
            Result = ctx => NativeApprovalQueueResultV1.Found(ctx.CompanyId, [], hasMore: false),
        };

        var result = await Fixture([healthy], [typed]).Query.ListAsync(default);

        Assert.Equal(NativeApprovalReadOutcomeV1.Found, result.Outcome);
        Assert.Empty(result.Resource!.Items);
        var finance = result.Resource.Sources.SingleOrDefault(source => source.Module == "finance");
        Assert.Equal(expected, finance?.State ?? "omitted");
    }

    [Fact]
    public async Task A_typed_page_larger_than_the_bound_is_cut_and_reports_more()
    {
        var items = Enumerable.Range(1, NativeApprovalQueueRulesV1.MaxItemsPerSource + 1)
            .Select(index => (NativeApprovalReadItemV1)FinanceItem(Company,
                Guid.Parse($"c4000000-0000-0000-0000-{index:D12}")))
            .ToArray();
        var typed = new TypedAdapter
        {
            Page = ctx => new(NativeApprovalReadOutcomeV1.Found, new NativeApprovalReadPageV1(ctx.CompanyId, items, null)),
        };

        var result = await Fixture([], [typed]).Query.ListAsync(default);

        Assert.Equal(NativeApprovalQueueRulesV1.MaxItemsPerSource, result.Resource!.Items.Count);
        Assert.True(Assert.Single(result.Resource.Sources).HasMore);
    }

    [Theory]
    [InlineData("/onaylarim/finance.official-invoice-import.v1/c3000000-0000-0000-0000-000000000001", true)]
    [InlineData("/satis/onaylar?onay=c3000000-0000-0000-0000-000000000001", true)]
    [InlineData("/stok/etiket/c3000000-0000-0000-0000-000000000001", true)]
    [InlineData("", false)]
    [InlineData("satis/onaylar", false)]
    [InlineData("//evil.example/x", false)]
    [InlineData("/a//b", false)]
    [InlineData("https://evil.example/x", false)]
    [InlineData("/a\\b", false)]
    [InlineData("/a/../b", false)]
    [InlineData("/a/%2e%2E/b", false)]
    [InlineData("/a/%2fb", false)]
    [InlineData("/a/%5Cb", false)]
    [InlineData("/a#fragment", false)]
    [InlineData("/a b", false)]
    [InlineData("/javascript:alert(1)", false)]
    public void Link_rule_accepts_only_relative_application_paths(string link, bool expected)
    {
        Assert.Equal(expected, NativeApprovalQueueRulesV1.IsValidLink(link));
    }

    [Theory]
    [InlineData("finance", "finance.official-invoice-import.v1", true)]
    [InlineData("work-flow", "work-flow.task.v12", true)]
    [InlineData("finance", "finance.v1", false)]
    [InlineData("finance", "sales.order-draft.v1", false)]
    [InlineData("finance", "finance.batch.v0", false)]
    [InlineData("finance", "finance.Batch.v1", false)]
    [InlineData("Finance", "Finance.batch.v1", false)]
    [InlineData("", ".batch.v1", false)]
    public void Kind_rule_requires_the_module_prefix_and_a_version(string module, string kind, bool expected)
    {
        Assert.Equal(expected, NativeApprovalQueueRulesV1.IsValidKind(module, kind));
    }

    private static NativeApprovalQueueResultV1 Malformed(string defect, Guid company)
    {
        const string module = "purchasing";
        const string kind = "purchasing.request.v1";
        var item = QueueItem(module, kind, Second, Late);
        return defect switch
        {
            "company" => NativeApprovalQueueResultV1.Found(OtherCompany, [item], false),
            "module" => NativeApprovalQueueResultV1.Found(company, [item with { Module = "logistics" }], false),
            "kind" => NativeApprovalQueueResultV1.Found(company, [item with { Kind = "purchasing.other.v1" }], false),
            "empty-id" => NativeApprovalQueueResultV1.Found(company, [item with { ItemId = Guid.Empty }], false),
            "duplicate" => NativeApprovalQueueResultV1.Found(company, [item, item], false),
            "offset" => NativeApprovalQueueResultV1.Found(company,
                [item with { CreatedAtUtc = new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.FromHours(3)) }], false),
            "default-instant" => NativeApprovalQueueResultV1.Found(company, [item with { CreatedAtUtc = default }], false),
            "absolute-link" => NativeApprovalQueueResultV1.Found(company,
                [item with { Link = "https://evil.example/approve" }], false),
            "protocol-relative-link" => NativeApprovalQueueResultV1.Found(company,
                [item with { Link = "//evil.example/approve" }], false),
            "dot-segment-link" => NativeApprovalQueueResultV1.Found(company,
                [item with { Link = "/satin-alma/../yonetim" }], false),
            "encoded-dot-segment-link" => NativeApprovalQueueResultV1.Found(company,
                [item with { Link = "/satin-alma/%2E%2e/yonetim" }], false),
            "null-items" => new(NativeApprovalQueueOutcomeV1.Found, company, null, false),
            "too-many" => NativeApprovalQueueResultV1.Found(company,
                Enumerable.Range(1, NativeApprovalQueueRulesV1.MaxItemsPerSource + 1)
                    .Select(index => item with { ItemId = Guid.Parse($"c5000000-0000-0000-0000-{index:D12}") })
                    .ToArray(), true),
            "denied-with-items" => new(NativeApprovalQueueOutcomeV1.Denied, company, [item], false),
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };
    }

    private static NativeApprovalQueueItemV1 QueueItem(string module, string kind, Guid id, DateTimeOffset at) =>
        new(module, id, kind, at, $"/kaynak/{module}/{id:D}");

    private static FinanceInvoiceImportNativeApprovalReadItemV1 FinanceItem(Guid company, Guid? id = null) => new(
        id ?? Batch,
        company,
        "Pending",
        "1",
        "source-version-7",
        new string('a', 64),
        FinanceOfficialInvoiceImportTemplateV1.Version,
        1,
        "maker",
        DateTimeOffset.Parse("2026-10-05T10:00:00+03:00"));

    private static FixtureState Fixture(
        IEnumerable<INativeApprovalQueueAdapterV1> sources,
        IEnumerable<INativeApprovalReadAdapterV1> typed)
    {
        var company = new MutableCompanyContext(Company);
        var user = new MutableUser("checker", Company);
        var correlation = new MutableCorrelation();
        correlation.Set("corr-1", "cause-1", "test", null, null);
        return new(new NativeApprovalQueueReadQueryV1(company, user, correlation, sources, typed),
            company, user, correlation);
    }

    private sealed class Source(string module, string kind) : INativeApprovalQueueAdapterV1
    {
        public string Module { get; } = module;
        public string Kind { get; } = kind;
        public Func<NativeApprovalQueueContextV1, NativeApprovalQueueResultV1>? Result { get; init; }
        public NativeApprovalQueueContextV1? Context { get; private set; }
        public int MaxCount { get; private set; }
        public int Calls { get; private set; }

        public Task<NativeApprovalQueueResultV1> ListPendingAsync(
            NativeApprovalQueueContextV1 context,
            int maxCount,
            CancellationToken cancellationToken)
        {
            Calls++;
            Context = context;
            MaxCount = maxCount;
            return Task.FromResult(Result?.Invoke(context)
                ?? NativeApprovalQueueResultV1.Found(context.CompanyId, [], hasMore: false));
        }
    }

    private sealed class TypedAdapter : INativeApprovalReadAdapterV1
    {
        public string Kind { get; init; } = NativeApprovalKindsV1.FinanceOfficialInvoiceImport;
        public Func<NativeApprovalReadContextV1, NativeApprovalReadResultV1<NativeApprovalReadPageV1>>? Page
        {
            get;
            init;
        }
        public NativeApprovalReadContextV1? Context { get; private set; }
        public string? Cursor { get; private set; } = "unset";
        public int Calls { get; private set; }

        public Task<NativeApprovalReadResultV1<NativeApprovalReadPageV1>> ListAsync(
            NativeApprovalReadContextV1 context,
            string? cursor,
            CancellationToken cancellationToken)
        {
            Calls++;
            Context = context;
            Cursor = cursor;
            return Task.FromResult(Page?.Invoke(context)
                ?? new NativeApprovalReadResultV1<NativeApprovalReadPageV1>(NativeApprovalReadOutcomeV1.Found,
                    new NativeApprovalReadPageV1(context.CompanyId, [], null)));
        }

        public Task<NativeApprovalReadResultV1<NativeApprovalReadDetailV1>> GetAsync(
            NativeApprovalReadContextV1 context,
            Guid approvalId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The Work queue never reads a typed detail.");
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

        public void Clear()
        {
            IsResolved = false;
            CorrelationId = string.Empty;
            CausationId = null;
        }
    }

    private sealed record FixtureState(
        NativeApprovalQueueReadQueryV1 Query,
        MutableCompanyContext Company,
        MutableUser User,
        MutableCorrelation Correlation);
}
