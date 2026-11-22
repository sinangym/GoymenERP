using System.Text.Json;
using GoymenERP.Api.Ai;
using GoymenERP.Api.Shell;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.Api.WorkRuntime.Ai;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Application.Ai;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Ai;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Work.Domain;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// WP-A3c: the Work READ tools over the real Work inbox read query (SENTETIK owner sources: maker ≠ checker and the
/// owners' approve permissions apply as they are) and the W1-C "my tasks" read; the catalog contract of all nine WP-A3c
/// tools; default-deny admission with an empty <c>Ai:AllowedTools</c>; and the ask path's permission and data-class fences
/// for a Finance (Confidential) tool. No model, network or database.
/// </summary>
public sealed class AiWpA3cReadToolsTests
{
    private const string CatalogPath = "contracts/ai/tool-catalog-v1.json";
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000c1");
    private static readonly Guid OtherCompany = Guid.Parse("bbbbbbbb-0000-4000-8000-0000000000c2");
    private static readonly Guid Actor = Guid.Parse("11111111-1111-4111-8111-1111111111c1");
    private static readonly Guid Someone = Guid.Parse("22222222-2222-4222-8222-2222222222c2");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);
    private const string PaymentKind = "finance.manual-bank-payment.v1";
    private const string PaymentApprove = "finance.manual-bank-payment.approve";
    private const string ExcessKind = "logistics.plan-excess.v1";
    private const string ExcessApprove = "logistics.plan-excess.approve";

    // ---- work.inbox-awaiting-me.v1 / work.overdue-approvals.v1 --------------------------------------

    [Fact]
    public async Task The_inbox_tool_counts_what_waits_for_me_hides_my_own_records_and_releases_no_rule_values()
    {
        var payment = InboxSource("finance", PaymentKind, PaymentApprove, WorkApprovalDurationClassV1.Hours,
            [Item("finance", PaymentKind, Now.AddHours(-10), Someone), Item("finance", PaymentKind, Now.AddHours(-1), Someone),
                Item("finance", PaymentKind, Now.AddHours(-20), Actor)]);
        var inbox = Inbox([payment]);

        var result = await new WorkInboxAwaitingMeAiTool(inbox.Query).ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        Assert.True(root.GetProperty("complete").GetBoolean());
        Assert.Equal(2, root.GetProperty("count").GetInt32());
        Assert.Equal(1, root.GetProperty("overdueCount").GetInt32());
        var kind = Assert.Single(root.GetProperty("byKind").EnumerateArray());
        Assert.Equal((PaymentKind, 2), (kind.GetProperty("kind").GetString(), kind.GetProperty("count").GetInt32()));
        Assert.DoesNotContain("12.5", result.ContentJson, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTETIK Cari", result.ContentJson, StringComparison.Ordinal);
        Assert.Equal((Company, Actor.ToString("D")), (payment.Context!.CompanyId, payment.Context.UserId));
    }

    [Fact]
    public async Task A_source_whose_approve_permission_I_lack_is_not_asked_and_without_any_the_answer_is_denied()
    {
        var payment = InboxSource("finance", PaymentKind, PaymentApprove, WorkApprovalDurationClassV1.Hours,
            [Item("finance", PaymentKind, Now.AddHours(-1), Someone)]);
        var excess = InboxSource("logistics", ExcessKind, ExcessApprove, WorkApprovalDurationClassV1.Hours,
            [Item("logistics", ExcessKind, Now.AddHours(-2), Someone)]);
        var inbox = Inbox([payment, excess]);
        inbox.Authority.Deny(ExcessApprove);

        var partly = await new WorkInboxAwaitingMeAiTool(inbox.Query).ReadAsync(new AiToolInputV1("{}"), Context());

        using (var content = JsonDocument.Parse(partly.ContentJson!))
        {
            Assert.Equal(1, content.RootElement.GetProperty("count").GetInt32());
            Assert.Equal("finance", content.RootElement.GetProperty("items")[0].GetProperty("module").GetString());
        }

        Assert.Equal(0, excess.Calls);
        inbox.Authority.Deny(PaymentApprove);
        var none = await new WorkInboxAwaitingMeAiTool(inbox.Query).ReadAsync(new AiToolInputV1("{}"), Context());
        Assert.Equal(AiToolOutcomeV1.Denied, none.Outcome);
    }

    [Fact]
    public async Task A_source_that_cannot_answer_or_holds_more_makes_the_inbox_counts_incomplete_and_names_the_gap()
    {
        var payment = InboxSource("finance", PaymentKind, PaymentApprove, WorkApprovalDurationClassV1.Hours,
            [Item("finance", PaymentKind, Now.AddHours(-1), Someone)], hasMore: true);
        var excess = new FakeInboxSource("logistics", ExcessKind, ExcessApprove, WorkApprovalDurationClassV1.Hours,
            _ => WorkInboxSourceResultV1.Unavailable());
        var inbox = Inbox([payment, excess]);

        var result = await new WorkInboxAwaitingMeAiTool(inbox.Query).ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var content = JsonDocument.Parse(result.ContentJson!);
        Assert.False(content.RootElement.GetProperty("complete").GetBoolean());
        Assert.Equal(JsonValueKind.Null, content.RootElement.GetProperty("count").ValueKind);
        Assert.Equal(2, content.RootElement.GetProperty("sourceGaps").GetArrayLength());
        Assert.Equal(1, content.RootElement.GetProperty("listedCount").GetInt32());
    }

    [Fact]
    public async Task Another_company_in_the_ambient_scope_releases_nothing_from_the_inbox()
    {
        var payment = InboxSource("finance", PaymentKind, PaymentApprove, WorkApprovalDurationClassV1.Hours,
            [Item("finance", PaymentKind, Now.AddHours(-1), Someone)]);
        var inbox = Inbox([payment], ambientCompany: OtherCompany);

        var result = await new WorkInboxAwaitingMeAiTool(inbox.Query).ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(AiToolOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.ContentJson);
    }

    [Fact]
    public async Task The_overdue_tool_lists_only_items_past_the_settings_duration_and_counts_undefined_durations()
    {
        var hours = InboxSource("finance", PaymentKind, PaymentApprove, WorkApprovalDurationClassV1.Hours,
            [Item("finance", PaymentKind, Now.AddHours(-10), Someone), Item("finance", PaymentKind, Now.AddHours(-1), Someone)]);
        var inbox = Inbox([hours]);

        var result = await new WorkOverdueApprovalsAiTool(inbox.Query).ReadAsync(new AiToolInputV1("{}"), Context());

        using (var content = JsonDocument.Parse(result.ContentJson!))
        {
            Assert.Equal(1, content.RootElement.GetProperty("count").GetInt32());
            var item = Assert.Single(content.RootElement.GetProperty("items").EnumerateArray());
            Assert.True(item.GetProperty("isOverdue").GetBoolean());
            Assert.Equal(0, content.RootElement.GetProperty("durationUndefinedCount").GetInt32());
        }

        var undefined = Inbox([InboxSource("finance", PaymentKind, PaymentApprove, WorkApprovalDurationClassV1.Hours,
            [Item("finance", PaymentKind, Now.AddHours(-10), Someone)])], withPolicy: false);
        var withoutPolicy = await new WorkOverdueApprovalsAiTool(undefined.Query).ReadAsync(new AiToolInputV1("{}"), Context());
        using (var content = JsonDocument.Parse(withoutPolicy.ContentJson!))
        {
            Assert.Equal(0, content.RootElement.GetProperty("count").GetInt32());
            Assert.Equal(1, content.RootElement.GetProperty("durationUndefinedCount").GetInt32());
        }
    }

    [Theory]
    [InlineData("{\"companyId\":\"aaaaaaaa-0000-4000-8000-0000000000c1\"}")]
    [InlineData("{\"userId\":\"someone\"}")]
    [InlineData("{\"limit\":51}")]
    public async Task Inbox_tool_arguments_naming_a_company_or_person_or_out_of_range_are_invalid_and_no_source_is_asked(string arguments)
    {
        var payment = InboxSource("finance", PaymentKind, PaymentApprove, WorkApprovalDurationClassV1.Hours, []);
        var inbox = Inbox([payment]);

        var result = await new WorkInboxAwaitingMeAiTool(inbox.Query).ReadAsync(new AiToolInputV1(arguments), Context());

        Assert.Equal(AiToolOutcomeV1.Invalid, result.Outcome);
        Assert.Equal(0, payment.Calls);
    }

    // ---- work.my-tasks.v1 ----------------------------------------------------------------------

    [Fact]
    public async Task My_tasks_are_counted_as_overdue_today_or_later_without_details_or_names()
    {
        var tasks = new Tasks();
        tasks.Add(Now.AddHours(-2));
        tasks.Add(Now.AddHours(5));
        tasks.Add(Now.AddDays(3), assigner: Actor);

        var result = await new WorkMyTasksAiTool(tasks, new FixedTime(Now)).ReadAsync(new AiToolInputV1("{}"), Context());

        Assert.Equal(AiToolOutcomeV1.Succeeded, result.Outcome);
        using var content = JsonDocument.Parse(result.ContentJson!);
        var root = content.RootElement;
        Assert.Equal(3, root.GetProperty("openCount").GetInt32());
        Assert.Equal(1, root.GetProperty("overdueCount").GetInt32());
        Assert.Equal(1, root.GetProperty("dueTodayCount").GetInt32());
        Assert.Equal(1, root.GetProperty("upcomingCount").GetInt32());
        Assert.True(root.GetProperty("items")[2].GetProperty("assignedBySelf").GetBoolean());
        Assert.DoesNotContain("SENTETIK ayrinti", result.ContentJson, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTETIK Kisi", result.ContentJson, StringComparison.Ordinal);
        Assert.False(tasks.IncludeClosed);
    }

    [Fact]
    public async Task My_tasks_refuse_a_non_member_release_nothing_for_another_company_or_person_and_withhold_counts_beyond_a_page()
    {
        var forbidden = new Tasks { Failure = new WorkCollaborationException(403, "forbidden") };
        Assert.Equal(AiToolOutcomeV1.Denied, (await new WorkMyTasksAiTool(forbidden, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);

        var foreign = new Tasks();
        foreign.Add(Now.AddHours(1), company: OtherCompany);
        Assert.Equal(AiToolOutcomeV1.Unavailable, (await new WorkMyTasksAiTool(foreign, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);

        var someoneElses = new Tasks();
        someoneElses.Add(Now.AddHours(1), assignee: Someone);
        Assert.Equal(AiToolOutcomeV1.Unavailable, (await new WorkMyTasksAiTool(someoneElses, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context())).Outcome);

        var more = new Tasks { HasMore = true };
        more.Add(Now.AddHours(1));
        using var content = JsonDocument.Parse((await new WorkMyTasksAiTool(more, new FixedTime(Now))
            .ReadAsync(new AiToolInputV1("{}"), Context())).ContentJson!);
        Assert.False(content.RootElement.GetProperty("complete").GetBoolean());
        Assert.Equal(JsonValueKind.Null, content.RootElement.GetProperty("openCount").ValueKind);
    }

    // ---- catalog, admission, ask-path fences -----------------------------------------------------

    [Fact]
    public void The_tool_catalog_contract_matches_every_wpa3c_descriptor()
    {
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRepositoryRoot(), CatalogPath)));
        var entries = catalog.RootElement.GetProperty("tools").EnumerateArray().ToArray();
        var descriptors = CatalogDescriptors();
        Assert.Equal(9, descriptors.Length);
        foreach (var descriptor in descriptors)
        {
            var entry = Assert.Single(entries, e => e.GetProperty("toolId").GetString() == descriptor.ToolId);
            Assert.Equal(descriptor.Version, entry.GetProperty("version").GetString());
            Assert.Equal(descriptor.OwnerModule, entry.GetProperty("ownerModule").GetString());
            Assert.Equal(descriptor.Risk.ToString(), entry.GetProperty("risk").GetString());
            Assert.Equal(descriptor.DataClass.ToString(), entry.GetProperty("dataClass").GetString());
            Assert.Equal(descriptor.RequiredPermissions,
                entry.GetProperty("requiredPermissions").EnumerateArray().Select(value => value.GetString()!).Order(StringComparer.Ordinal));
            Assert.Equal(descriptor.FieldAllowlist,
                entry.GetProperty("fieldAllowlist").EnumerateArray().Select(value => value.GetString()!).Order(StringComparer.Ordinal));
            Assert.Equal(descriptor.FieldAllowlist,
                entry.GetProperty("arguments").EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal));
            Assert.Equal(AiToolRiskClassV1.Read, descriptor.Risk);
            Assert.Null(descriptor.OwnerCommandName);
            Assert.Equal(CatalogPath, descriptor.Contract.Catalogue);
        }

        Assert.All(descriptors.Where(descriptor => descriptor.OwnerModule == "Finance" && descriptor.ToolId.Contains("due", StringComparison.Ordinal)),
            descriptor => Assert.Equal(AiDataClassV1.Confidential, descriptor.DataClass));
    }

    [Fact]
    public void With_an_empty_allowlist_the_catalog_admits_none_of_the_wpa3c_tools_and_an_exact_entry_admits_one()
    {
        var tools = CatalogTools();
        var empty = new AiRuntimeOptions { AllowedTools = [] };
        Assert.Empty(new AiToolCatalog(tools, Options.Create(empty)).Descriptors);

        var one = new AiRuntimeOptions
        {
            AllowedTools =
            [
                new AiAllowedToolOptions
                {
                    ToolId = AccountingPeriodStatusAiTool.ToolId, Version = AccountingPeriodStatusAiTool.ToolVersion,
                    DescriptionSha256 = AiToolDescriptionPinV1.Compute(
                        tools.Single(tool => tool.Descriptor.ToolId == AccountingPeriodStatusAiTool.ToolId).Descriptor),
                },
            ],
        };
        var admitted = Assert.Single(new AiToolCatalog(tools, Options.Create(one)).Descriptors);
        Assert.Equal(AccountingPeriodStatusAiTool.ToolId, admitted.ToolId);
    }

    [Fact]
    public async Task Without_the_open_item_amount_grant_the_finance_tool_is_refused_before_the_owner_is_read()
    {
        var owner = new RecordingOpenItems();
        var harness = new AskHarness(owner, AiDataClassV1.Confidential, ErpPermissions.AiPanelUse, FinancePermissionNames.OpenItemRead);

        var result = await harness.Service().AskAsync(Ask(FinanceDueOpenItemsAiTool.ToolId, "{}"));

        Assert.Equal(AiAskStatusV1.Refused, result.Status);
        var denied = Assert.Single(harness.Writer.Events);
        Assert.Equal(SecurityEventTypesV1.AiAuthorizationDenied, denied.EventType);
        Assert.Equal(FinancePermissionNames.OpenItemAmountRead, denied.PermissionCode);
        Assert.Equal(0, owner.Calls);
    }

    [Fact]
    public async Task A_confidential_finance_tool_is_refused_when_the_runtime_admits_only_internal_data()
    {
        var owner = new RecordingOpenItems();
        var harness = new AskHarness(owner, AiDataClassV1.Internal, ErpPermissions.AiPanelUse, FinancePermissionNames.OpenItemRead,
            FinancePermissionNames.OpenItemAmountRead);

        var result = await harness.Service().AskAsync(Ask(FinanceDueOpenItemsAiTool.ToolId, "{}"));

        Assert.Equal(AiAskStatusV1.Refused, result.Status);
        Assert.Contains(harness.Writer.Events, e => e.ReasonCode == SecurityEventReasonCodesV1.AiDataClassNotAllowed);
        Assert.Equal(0, owner.Calls);
    }

    [Fact]
    public async Task With_both_grants_and_confidential_data_admitted_the_finance_tool_answers_through_the_owner()
    {
        var owner = new RecordingOpenItems();
        var harness = new AskHarness(owner, AiDataClassV1.Confidential, ErpPermissions.AiPanelUse, FinancePermissionNames.OpenItemRead,
            FinancePermissionNames.OpenItemAmountRead);

        var result = await harness.Service().AskAsync(Ask(FinanceDueOpenItemsAiTool.ToolId, "{\"withinDays\":0}"));

        Assert.Equal(AiAskStatusV1.Answered, result.Status);
        var toolResult = Assert.Single(result.Response!.ToolResults);
        Assert.Equal(nameof(AiDataClassV1.Confidential), toolResult.Source!.DataClass);
        Assert.Equal(1, owner.Calls);
        Assert.Equal(Company, owner.Context!.CompanyId);
        Assert.Equal(Actor.ToString("D"), owner.Context.ActorId);
    }

    /// <summary>The nine WP-A3c descriptors, built from the tools themselves (also used by the Y-A1 catalog count check).</summary>
    internal static AiToolDescriptorV1[] CatalogDescriptors() => [.. CatalogTools().Select(tool => tool.Descriptor)];

    private static IAiOwnerToolV1[] CatalogTools()
    {
        var time = new FixedTime(Now);
        var company = new CompanyContext();
        var inbox = Inbox([]).Query;
        return
        [
            new FinanceDueOpenItemsAiTool(new RecordingOpenItems(), time),
            new FinanceDueCustomerInstrumentsAiTool(new UnusedFinance(), time),
            new FinanceTreasuryAccountsAiTool(new UnusedFinance(), new UnusedFinance(), time),
            new AccountingPeriodStatusAiTool(new UnusedAccounting(), company, time),
            new AccountingPostingQueuesAiTool(new UnusedAccounting(), company, time),
            new AccountingPeriodJournalsAiTool(new UnusedAccounting(), company, time),
            new WorkInboxAwaitingMeAiTool(inbox),
            new WorkOverdueApprovalsAiTool(inbox),
            new WorkMyTasksAiTool(new Tasks(), time),
        ];
    }

    // ---- fixtures -------------------------------------------------------------------------------

    private static AiServerContextV1 Context() => new(Company, Actor.ToString("D"), "v1.session-sentetik",
        $"company:{Company:D}", "roles:sentetik", "corr-wpa3c-sentetik", null, null, null);

    private static AiAskRequestV1 Ask(string toolId, string arguments)
    {
        using var document = JsonDocument.Parse(arguments);
        return new AiAskRequestV1("SENTETIK soru", toolId, "1", document.RootElement.Clone());
    }

    private static WorkInboxItemV1 Item(string module, string kind, DateTimeOffset since, Guid preparedBy)
    {
        var id = Guid.NewGuid();
        return new(module, kind, id, "SENTETIK-" + id.ToString("N")[..6], since, $"/{module}/{id:D}", preparedBy.ToString("D"),
            [new WorkApprovalRuleResultV1(module + ".rule", "10", "12.5", "2.5", "%")],
            [new WorkApprovalReferenceV1("party", "SENTETIK Cari", Guid.NewGuid(), null)]);
    }

    private static FakeInboxSource InboxSource(string module, string kind, string permission, WorkApprovalDurationClassV1 durationClass,
        IReadOnlyList<WorkInboxItemV1> items, bool hasMore = false) =>
        new(module, kind, permission, durationClass, context => WorkInboxSourceResultV1.Found(context.CompanyId, items, hasMore));

    /// <summary>The real inbox query over SENTETIK sources. <paramref name="withPolicy"/> false: Settings has no duration policy.</summary>
    private static InboxFixture Inbox(IReadOnlyList<IWorkInboxSourceV1> sources, Guid? ambientCompany = null, bool withPolicy = true)
    {
        var company = new CompanyContext();
        company.Set(ambientCompany ?? Company);
        var authority = new SourceAuthority();
        var query = new WorkInboxReadQueryV1(company, new TestUser(Actor, Company, OtherCompany), new FixedCorrelation(), authority,
            new Durations(withPolicy ? new SettingsWorkApprovalDurationPolicyV1(Company, 4, 1, 2, 1) : null), new Holidays(),
            new Measurements(), new SourceRules(), new IstanbulZone(), new FixedTime(Now), NullLogger<WorkInboxReadQueryV1>.Instance, sources);
        return new(query, authority);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, CatalogPath)))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("GöymenERP repository kökü bulunamadı.");
    }

    private sealed record InboxFixture(WorkInboxReadQueryV1 Query, SourceAuthority Authority);

    /// <summary>WP1: no recorded rule; each SENTETIK source keeps its own duration class as the default rule.</summary>
    private sealed class SourceRules : GoymenERP.Modules.Work.Infrastructure.ApprovalRules.IWorkApprovalRuleReaderV1
    {
        public Task<IReadOnlyDictionary<string, GoymenERP.Modules.Work.Infrastructure.ApprovalRules.WorkApprovalRuleResolutionV1>?> ResolveAsync(
            Guid companyId, IReadOnlyList<IWorkInboxSourceV1> sources, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, GoymenERP.Modules.Work.Infrastructure.ApprovalRules.WorkApprovalRuleResolutionV1>?>(
                sources.ToDictionary(source => source.Kind,
                    source => new GoymenERP.Modules.Work.Infrastructure.ApprovalRules.WorkApprovalRuleResolutionV1(source.Kind,
                        source is FakeInboxSource fake ? fake.DurationClass : WorkApprovalDurationClassV1.BusinessDays, null, 0, true, true),
                    StringComparer.Ordinal));
    }

    private sealed class IstanbulZone : GoymenERP.Modules.Work.Infrastructure.ApprovalRules.IWorkBusinessTimeZoneSourceV1
    {
        public TimeZoneInfo? BusinessTimeZone { get; } = TimeZoneInfo.FindSystemTimeZoneById(
            GoymenERP.Modules.Work.Infrastructure.ApprovalRules.SystemWorkBusinessTimeZoneSourceV1.TimeZoneId);
    }

    private sealed class FakeInboxSource(string module, string kind, string permission, WorkApprovalDurationClassV1 durationClass,
        Func<WorkInboxSourceContextV1, WorkInboxSourceResultV1> answer) : IWorkInboxSourceV1
    {
        public string Module => module;
        public string Kind => kind;
        public string ApproverPermission => permission;
        public WorkApprovalDurationClassV1 DurationClass => durationClass;
        public WorkInboxSourceContextV1? Context { get; private set; }
        public int Calls { get; private set; }

        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int maxCount,
            CancellationToken cancellationToken)
        {
            Calls++;
            Context = context;
            return Task.FromResult(answer(context));
        }
    }

    private sealed class SourceAuthority : IErpAuthorityRevalidationService
    {
        private readonly HashSet<string> denied = new(StringComparer.Ordinal);

        public void Deny(string permission) => denied.Add(permission);

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) =>
            // Any company the user may access is granted here, so the TOOL's own company fence is what a foreign scope meets.
            Task.FromResult(denied.Contains(permission)
                ? PermissionScope.Denied
                : new PermissionScope(true, true, new HashSet<string>(StringComparer.Ordinal), new HashSet<Guid>(), new HashSet<Guid>(),
                    new HashSet<Guid>()));
    }

    private sealed class Durations(SettingsWorkApprovalDurationPolicyV1? policy) : ISettingsWorkApprovalDurationPolicyProviderV1
    {
        public Task<SettingsWorkApprovalDurationPolicyV1> GetWorkApprovalDurationAsync(Guid companyId,
            CancellationToken cancellationToken = default) =>
            policy is null ? throw new InvalidOperationException("SENTETIK: Settings unavailable.") : Task.FromResult(policy);
    }

    private sealed class Holidays : ISettingsWorkHolidayCalendarProviderV1
    {
        public Task<SettingsWorkHolidayCalendarsV1> GetWorkHolidayCalendarsAsync(Guid companyId, int fromYear, int toYear,
            CancellationToken cancellationToken = default) => Task.FromResult(new SettingsWorkHolidayCalendarsV1(companyId, []));
    }

    private sealed class Measurements : IWorkApprovalInboxMeasurementsV1
    {
        public Task ObserveAsync(Guid companyId, string kind, IReadOnlyList<WorkApprovalInboxCycleV1> pending, bool complete,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<WorkApprovalInboxInteractionOutcomeV1> RecordInteractionAsync(Guid companyId,
            WorkApprovalInboxInteractionV1 interaction, CancellationToken cancellationToken = default) =>
            Task.FromResult(WorkApprovalInboxInteractionOutcomeV1.Recorded);
    }

    private sealed class FixedCorrelation : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => "corr-wpa3c-sentetik";
        public string? CausationId => null;
        public string SourceChannel => "api";
        public string? ClientIpAddress => null;
        public string? UserAgent => null;
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent) =>
            throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestUser(Guid userId, params Guid[] companies) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = companies.ToHashSet();
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    /// <summary>A SENTETIK W1-C "my tasks" read: the caller's tasks only, as the owner answers them.</summary>
    private sealed class Tasks : IWorkCollaborationServiceV1
    {
        private readonly List<WorkTaskViewV1> rows = [];

        public Exception? Failure { get; init; }
        public bool HasMore { get; init; }
        public bool? IncludeClosed { get; private set; }

        public void Add(DateTimeOffset dueAtUtc, Guid? assigner = null, Guid? assignee = null, Guid? company = null) =>
            rows.Add(new WorkTaskViewV1(Guid.NewGuid(), company ?? Company, null, null, "SENTETIK gorev " + rows.Count,
                "SENTETIK ayrinti", assigner ?? Someone, "SENTETIK Kisi A", assignee ?? Actor, "SENTETIK Kisi B", dueAtUtc,
                WorkTaskV1.Wire(WorkTaskStatusV1.Open), "1", Now.AddDays(-1), null, null, null));

        public Task<WorkTaskPageV1> ListMyTasksAsync(bool includeClosed, CancellationToken cancellationToken = default)
        {
            IncludeClosed = includeClosed;
            return Failure is not null ? Task.FromException<WorkTaskPageV1>(Failure) : Task.FromResult(new WorkTaskPageV1(Company, rows, HasMore));
        }

        public Task<WorkNotePageV1> ListNotesAsync(string recordType, Guid recordId, bool includePassive, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkNoteResponseV1> AddNoteAsync(string recordType, Guid recordId, AddWorkNoteRequestV1 request, WorkCommandMetadataV1 metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkNoteResponseV1> PassivateNoteAsync(Guid noteId, PassivateWorkNoteRequestV1 request, WorkCommandMetadataV1 metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkNoteReadResponseV1> MarkNoteReadAsync(Guid noteId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkTaskPageV1> ListRecordTasksAsync(string recordType, Guid recordId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkTaskResponseV1> CreateTaskAsync(CreateWorkTaskRequestV1 request, WorkCommandMetadataV1 metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkTaskResponseV1> CompleteTaskAsync(Guid taskId, CompleteWorkTaskRequestV1 request, WorkCommandMetadataV1 metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkTaskResponseV1> CancelTaskAsync(Guid taskId, CancelWorkTaskRequestV1 request, WorkCommandMetadataV1 metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkInboxNotificationPageV1> ListMyInboxAsync(string? notificationClass, bool unreadOnly, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkInboxNotificationReadResponseV1> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkPeoplePageV1> SearchPeopleAsync(string? query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>A SENTETIK FI04 read with one open receivable due today.</summary>
    private sealed class RecordingOpenItems : IFinanceOpenItemReadServiceV1
    {
        public int Calls { get; private set; }
        public FinanceContext? Context { get; private set; }

        public Task<ReadResult<FinanceOpenItemPageV1>> ListAuthorizedAsync(FinanceContext context, SourceListRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            Context = context;
            var amount = new AmountView(AmountDisclosure.Visible, new MoneyText("42.00", SettingsCurrencyIdentitiesV1.TurkishLira));
            var item = new FinanceOpenItemViewV1(Guid.NewGuid(), context.CompanyId, "SENTETIK-kaynak", Guid.NewGuid(), Guid.NewGuid(), "1",
                Guid.NewGuid(), Guid.NewGuid(), SettingsCurrencyIdentitiesV1.TurkishLira, "1", new DateOnly(2026, 11, 22),
                FinanceOpenItemStatusV1.Open, Now.AddDays(-1), amount, amount, FinanceOpenItemDirectionV1.Receivable);
            return Task.FromResult(new ReadResult<FinanceOpenItemPageV1>(ReadOutcome.Found,
                new FinanceOpenItemPageV1(context.CompanyId, [item], null)));
        }

        public Task<ReadResult<FinanceOpenItemViewV1>> DetailAuthorizedAsync(FinanceContext context, Guid openItemId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>Owner reads a descriptor-only test never calls.</summary>
    private sealed class UnusedFinance : ICustomerInstrumentService, IFinanceBankAccountReader, IFinanceCashAccountMaster
    {
        public Task<CustomerInstrumentCommandResult> ReceiveAsync(FinanceContext c, CommandMetadata m, CustomerInstrumentReceiveRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<CustomerInstrumentCommandResult> ChangeStatusAsync(FinanceContext c, CommandMetadata m, CustomerInstrumentStatusChangeRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<CustomerInstrumentCommandResult> CollectAsync(FinanceContext c, CommandMetadata m, CustomerInstrumentCollectRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<CustomerInstrumentPage>> ListAuthorizedAsync(FinanceContext c, string? cursor, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<CustomerInstrumentDetail>> DetailAuthorizedAsync(FinanceContext c, Guid id, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<CustomerInstrumentRiskView>> RiskAuthorizedAsync(FinanceContext c, CustomerInstrumentRiskRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<BankAccountPage>> ListAuthorizedAsync(FinanceContext c, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<BankAccountView>> GetAuthorizedAsync(FinanceContext c, Guid id, CancellationToken t) => throw new NotSupportedException();
        public Task<CashAccountCommandResult> ProposeAsync(FinanceContext c, CommandMetadata m, CashAccountProposeRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<CashAccountCommandResult> ApproveAsync(FinanceContext c, CommandMetadata m, CashAccountApproveRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<CashAccountCommandResult> RejectAsync(FinanceContext c, CommandMetadata m, CashAccountRejectRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<CashAccountCommandResult> ProposeStateChangeAsync(FinanceContext c, CommandMetadata m, CashAccountStateChange s, CashAccountStateChangeRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<CashAccountCommandResult> ApproveStateChangeAsync(FinanceContext c, CommandMetadata m, CashAccountStateChange s, CashAccountStateChangeRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<CashAccountCommandResult> RejectStateChangeAsync(FinanceContext c, CommandMetadata m, CashAccountStateChange s, CashAccountStateChangeRejectRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<CashAccountMasterPage>> ListAsync(FinanceContext c, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<CashAccountView>> GetAsync(FinanceContext c, Guid id, CancellationToken t) => throw new NotSupportedException();
        public Task<ReadResult<CashAccountCurrencyCandidates>> CurrencyCandidatesAsync(FinanceContext c, CancellationToken t) => throw new NotSupportedException();
    }

    private sealed class UnusedAccounting : IAccountingOwnerReadV1
    {
        public Task<AccountingApiResult<PeriodPageV1>> ListPeriodsAsync(int? pageSize, string? cursor, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AccountingApiResult<JournalPageV1>> ListJournalsAsync(Guid? periodId, int? pageSize, string? cursor, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AccountingApiResult<PostingQueuesV1>> GetPostingQueuesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class AskHarness
    {
        private readonly AiRuntimeOptions options;
        private readonly IFinanceOpenItemReadServiceV1 owner;

        public AskHarness(IFinanceOpenItemReadServiceV1 owner, AiDataClassV1 maxDataClass, params string[] granted)
        {
            this.owner = owner;
            options = new AiRuntimeOptions
            {
                Enabled = true,
                Provider = AiProviderKindsV1.Fake,
                PolicyRef = "TEST-AI-POLICY",
                PolicyVersion = 1,
                MaxQuestionLength = 200,
                MaxArgumentBytes = 256,
                MaxToolCallsPerRequest = 1,
                MaxDataClass = maxDataClass,
                AllowedTools =
                [
                    new AiAllowedToolOptions
                    {
                        ToolId = FinanceDueOpenItemsAiTool.ToolId, Version = FinanceDueOpenItemsAiTool.ToolVersion,
                        DescriptionSha256 = AiToolDescriptionPinV1.Compute(new FinanceDueOpenItemsAiTool(owner, new FixedTime(Now)).Descriptor),
                    },
                ],
            };
            Authority = new AskAuthority(granted);
            CompanyScope.Set(Company);
            Correlation.Set("corr-wpa3c-sentetik", null, SourceChannels.Web, null, null);
        }

        public AskAuthority Authority { get; }
        public RecordingWriter Writer { get; } = new();
        public CompanyContext CompanyScope { get; } = new();
        public CorrelationContext Correlation { get; } = new();

        public AiAskService Service()
        {
            IAiOwnerToolV1[] tools = [new FinanceDueOpenItemsAiTool(owner, new FixedTime(Now))];
            return new AiAskService(Options.Create(options), new TestHostEnvironment("Development"), new FakeAiModelProvider(),
                new AiToolCatalog(tools, Options.Create(options)), new TestUser(Actor, Company), CompanyScope, Authority,
                new FixedSessionResolver(), Correlation, Writer, new FixedTime(Now), AiGatewayTestDoubles.Gateway(options, GatewayStore));
        }

        public InMemoryAiGatewayStore GatewayStore { get; } = new(Company);
    }

    private sealed class AskAuthority(params string[] granted) : IErpAuthorityRevalidationService, IErpAuthorizationService
    {
        private readonly HashSet<string> grants = new(granted, StringComparer.Ordinal);

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ScopeOf(permission, companyId));

        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ScopeOf(permission, companyId));

        public Task<bool> HasPermissionAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ScopeOf(permission, companyId).IsGranted);

        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The AI runtime measures scopes, not snapshots.");

        private PermissionScope ScopeOf(string permission, Guid companyId) =>
            companyId == Company && grants.Contains(permission)
                ? new PermissionScope(true, true, new HashSet<string>(StringComparer.Ordinal) { "ROLE-SENTETIK" }, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied;
    }

    private sealed class RecordingWriter : ISecurityEventWriterV1
    {
        public List<SecurityEventV1> Events { get; } = [];

        public Task<SecurityEventWriteResultV1> WriteAsync(SecurityEventV1 securityEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(securityEvent);
            return Task.FromResult(SecurityEventWriteResultV1.Written(
                SecurityEventWriteFailureModesV1.Resolve(securityEvent.EventType, securityEvent.Phase)));
        }
    }

    private sealed class FixedSessionResolver : IActorSessionBindingResolver
    {
        public Task<string?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("v1.session-sentetik");
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "GoymenERP.Api.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
