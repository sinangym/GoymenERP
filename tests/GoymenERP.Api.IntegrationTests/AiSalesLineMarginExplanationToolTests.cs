using System.Reflection;
using System.Text.Json;
using GoymenERP.Api.Ai;
using GoymenERP.Api.Ai.Tools;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Controlled owner port boundary, not native Sales/SQL acceptance or a configured production tool.</summary>
public sealed class AiSalesLineMarginExplanationToolTests
{
    [Fact]
    public async Task Copies_the_owner_margin_and_trace_exactly_without_source_free_text_or_new_arithmetic()
    {
        var f = new Fixture();
        var answer = await f.Tool.ReadAsync(new(f.Arguments), f.Context);
        Assert.Equal(AiToolOutcomeV1.Succeeded, answer.Outcome);
        Assert.Equal(AiDataClassV1.Confidential, f.Tool.Descriptor.DataClass);
        Assert.Equal(AiDataClassV1.Confidential, answer.Source!.DataClass);
        Assert.Equal((f.Draft, f.Line), f.Owner.LastIds);
        var content = JsonSerializer.Deserialize<SalesLineMarginExplanationAiContentV1>(answer.ContentJson!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(f.Owner.Answer.Margin, content.Margin);
        Assert.Equal(JsonSerializer.Serialize(f.Owner.Answer.Steps), JsonSerializer.Serialize(content.Steps));
        Assert.Equal("20.00", content.Margin.Amount);
        Assert.Equal("100.0000", content.Steps[0].Value);
        Assert.DoesNotContain("SENTETIK-PRIVATE", answer.ContentJson);
        Assert.DoesNotContain("987654321.99", answer.ContentJson);
        Assert.Equal(SalesLineMarginExplanationAiToolV1.TargetKind, answer.Source.TargetKind);
        Assert.Equal(f.Draft, answer.Source.TargetId);
    }

    [Theory]
    [InlineData("companyId")]
    [InlineData("measure")]
    [InlineData("missing-draft")]
    [InlineData("missing-line")]
    [InlineData("malformed-line")]
    [InlineData("duplicate")]
    public async Task Undefined_measure_company_argument_or_invalid_identity_never_calls_the_owner(string fault)
    {
        var f = new Fixture();
        var json = fault switch
        {
            "companyId" => f.Arguments.Insert(1, "\"companyId\":\"" + f.Company.CompanyId + "\","),
            "measure" => f.Arguments.Insert(1, "\"measure\":\"invented-total\","),
            "missing-draft" => JsonSerializer.Serialize(new { lineId = f.Line }),
            "missing-line" => JsonSerializer.Serialize(new { draftId = f.Draft }),
            "malformed-line" => JsonSerializer.Serialize(new { draftId = f.Draft, lineId = "not-a-guid" }),
            _ => f.Arguments.Insert(1, "\"draftId\":\"" + f.Draft + "\","),
        };
        Assert.Equal(AiToolOutcomeV1.Invalid, (await f.Tool.ReadAsync(new(json), f.Context)).Outcome);
        Assert.Equal(0, f.Owner.Calls);
    }

    [Theory]
    [InlineData(SalesPermissionManifestV1.DraftsRead)]
    [InlineData(SalesPermissionManifestV1.OrdersPriceRead)]
    [InlineData(SalesPermissionManifestV1.OrdersMarginRead)]
    public async Task Every_existing_native_disclosure_permission_is_required_before_read(string permission)
    {
        var f = new Fixture(); f.Authority.Revoke(permission);
        Assert.Equal(AiToolOutcomeV1.Denied, (await f.Tool.ReadAsync(new(f.Arguments), f.Context)).Outcome);
        Assert.Equal(0, f.Owner.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Foreign_company_or_actor_context_never_reaches_the_owner(bool foreignCompany)
    {
        var f = new Fixture();
        var context = foreignCompany ? f.Context with { CompanyId = Guid.NewGuid() } : f.Context with { ActorId = "SENTETIK-other-actor" };
        Assert.Equal(AiToolOutcomeV1.Denied, (await f.Tool.ReadAsync(new(f.Arguments), context)).Outcome);
        Assert.Equal(0, f.Owner.Calls);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("company")]
    [InlineData("price")]
    [InlineData("margin")]
    public async Task Changed_actor_company_or_disclosure_after_owner_read_releases_no_trace(string fault)
    {
        var f = new Fixture();
        f.Owner.AfterRead = () =>
        {
            if (fault == "actor") f.User.Actor = Guid.NewGuid().ToString("D");
            if (fault == "company") f.Company.Set(Guid.NewGuid());
            if (fault == "price") f.Authority.Revoke(SalesPermissionManifestV1.OrdersPriceRead);
            if (fault == "margin") f.Authority.Revoke(SalesPermissionManifestV1.OrdersMarginRead);
        };
        var result = await f.Tool.ReadAsync(new(f.Arguments), f.Context);
        Assert.Equal(AiToolOutcomeV1.Denied, result.Outcome); Assert.Null(result.ContentJson); Assert.Null(result.Source);
        Assert.Equal(1, f.Owner.Calls);
    }

    [Theory]
    [InlineData(403, AiToolOutcomeV1.Denied)]
    [InlineData(404, AiToolOutcomeV1.NotFound)]
    [InlineData(409, AiToolOutcomeV1.Conflict)]
    [InlineData(503, AiToolOutcomeV1.Unavailable)]
    public async Task Native_owner_refusals_never_become_an_answer(int status, AiToolOutcomeV1 expected)
    {
        var f = new Fixture(); f.Owner.Failure = new SalesDraftServiceException(status, "SENTETIK-native");
        var result = await f.Tool.ReadAsync(new(f.Arguments), f.Context);
        Assert.Equal(expected, result.Outcome); Assert.Null(result.ContentJson); Assert.Null(result.Source);
    }

    [Theory]
    [InlineData("contract")]
    [InlineData("draft")]
    [InlineData("line")]
    [InlineData("version")]
    [InlineData("steps")]
    [InlineData("duplicate")]
    [InlineData("withheld")]
    [InlineData("amount")]
    public async Task Malformed_or_wrong_native_projection_is_unavailable(string fault)
    {
        var f = new Fixture(); var original = f.Owner.Answer;
        f.Owner.Answer = fault switch
        {
            "contract" => original with { ContractVersion = 2 },
            "draft" => original with { DraftId = Guid.NewGuid() },
            "line" => original with { Margin = original.Margin with { LineId = Guid.NewGuid() } },
            "version" => original with { Version = "" },
            "steps" => original with { Steps = [original.Steps[0] with { Value = "invented-value" }] },
            "withheld" => original with { Margin = original.Margin with { State = "Withheld" } },
            "amount" => original with { Margin = original.Margin with { Amount = "not-a-number" } },
            _ => original with { Steps = [original.Steps[0], original.Steps[0]] },
        };
        Assert.Equal(AiToolOutcomeV1.Unavailable, (await f.Tool.ReadAsync(new(f.Arguments), f.Context)).Outcome);
    }

    [Fact]
    public async Task An_unavailable_margin_stays_null_and_source_digest_changes_with_the_owners_exact_version()
    {
        var f = new Fixture();
        f.Owner.Answer = f.Owner.Answer with { Margin = f.Owner.Answer.Margin with { State = "Unavailable", Amount = null,
            Percent = null, CurrencyId = null, UnavailableReason = "CostSourceMissing" }, Steps = [] };
        var first = await f.Tool.ReadAsync(new(f.Arguments), f.Context);
        using var json = JsonDocument.Parse(first.ContentJson!);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("margin").GetProperty("amount").ValueKind);
        Assert.Equal("CostSourceMissing", json.RootElement.GetProperty("margin").GetProperty("unavailableReason").GetString());
        f.Owner.Answer = f.Owner.Answer with { Version = "8" };
        var second = await f.Tool.ReadAsync(new(f.Arguments), f.Context);
        Assert.NotEqual(first.Source!.SourceVersion, second.Source!.SourceVersion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Registration_without_an_approved_pin_never_admits_the_new_tool(bool allowlisted)
    {
        var f = new Fixture();
        var options = new AiRuntimeOptions { AllowedTools = allowlisted
            ? [new() { ToolId = SalesLineMarginExplanationAiToolV1.ToolId, Version = "1" }] : [] };
        Assert.Null(new AiToolCatalog([f.Tool], Options.Create(options)).Find(SalesLineMarginExplanationAiToolV1.ToolId, "1"));
        Assert.Equal(0, f.Owner.Calls);
    }

    [Fact]
    public async Task A_missing_owner_or_server_dependency_is_unavailable_without_a_fabricated_trace()
    {
        var f = new Fixture();
        var tool = new SalesLineMarginExplanationAiToolV1(null, f.Company, f.User, f.Authority, TimeProvider.System);
        var answer = await tool.ReadAsync(new(f.Arguments), f.Context);
        Assert.Equal(AiToolOutcomeV1.Unavailable, answer.Outcome);
        Assert.Null(answer.ContentJson); Assert.Null(answer.Source); Assert.Equal(0, f.Owner.Calls);
    }

    internal sealed class Fixture
    {
        public Guid Draft { get; } = Guid.NewGuid();
        public Guid Line { get; } = Guid.NewGuid();
        public MarginCompany Company { get; } = new();
        public MarginUser User { get; }
        public MarginAuthority Authority { get; } = new();
        public MarginOwnerProxy Owner { get; }
        public ISalesDraftService Service { get; }
        public SalesLineMarginExplanationAiToolV1 Tool { get; }
        public AiServerContextV1 Context => new(Company.CompanyId, User.Actor, "SENTETIK-session", "scope-1", "authority-1", "SENTETIK-corr", null, null, null);
        public string Arguments => JsonSerializer.Serialize(new { draftId = Draft, lineId = Line });
        public Fixture()
        {
            Company.Set(TestCompanyIds.CompanyA); User = new(Company.CompanyId);
            Service = DispatchProxy.Create<ISalesDraftService, MarginOwnerProxy>(); Owner = (MarginOwnerProxy)Service;
            Owner.Answer = Explanation(Draft, Line);
            Tool = new(Service, Company, User, Authority, TimeProvider.System);
        }
    }

    internal static SalesDraftLineMarginExplanationV1 Explanation(Guid draft, Guid line) => new(1, draft, "7", "CommercialGrossMargin",
        SalesMarginTraceRulesV1.CalculatorVersion, new(line, 1, "Computed", null, "20.00", Guid.NewGuid(), "20.00", null),
        [new("net-sale", "input", [], "100.0000", null, null, null), new("margin-amount", "owner-result", ["net-sale"], "20.00", null, null, 2)],
        [new("margin-cost-snapshot", Guid.NewGuid(), 3, "SENTETIK-PRIVATE-REFERENCE", DateTimeOffset.UnixEpoch,
            "SENTETIK-PRIVATE-PROOF-987654321.99")], [SalesMarginTraceRulesV1.BaseGolden]);

    public class MarginOwnerProxy : DispatchProxy
    {
        public SalesDraftLineMarginExplanationV1 Answer { get; set; } = null!;
        public Exception? Failure { get; set; }
        public Action? AfterRead { get; set; }
        public int Calls { get; private set; }
        public (Guid Draft, Guid Line) LastIds { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(ISalesDraftService.ExplainDraftLineMarginAsync)) throw new NotSupportedException("Only the existing owner explanation read is allowed.");
            Calls++; LastIds = ((Guid)args![0]!, (Guid)args[1]!);
            if (Failure is { } failure) return Task.FromException<SalesDraftLineMarginExplanationV1>(failure);
            AfterRead?.Invoke(); return Task.FromResult(Answer);
        }
    }
    internal sealed class MarginCompany : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; }
        public void Set(Guid id) => CompanyId = id;
    }
    internal sealed class MarginUser(Guid company) : ICurrentUser
    {
        public string Actor { get; set; } = Guid.NewGuid().ToString("D");
        public bool IsAuthenticated => true;
        public string? UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid id) => AllowedCompanyIds.Contains(id);
    }
    internal sealed class MarginAuthority : IErpAuthorityRevalidationService
    {
        private readonly HashSet<string> denied = [];
        public void Revoke(string permission) => denied.Add(permission);
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(denied.Contains(permission) ? PermissionScope.Denied : new PermissionScope(true, true,
                new HashSet<string> { "SENTETIK" }, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
    }
}
