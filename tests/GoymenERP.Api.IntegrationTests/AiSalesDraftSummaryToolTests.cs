using System.Reflection;
using System.Text.Json;
using GoymenERP.Api.Ai;
using GoymenERP.Api.Ai.Tools;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Controlled native-shaped owner answers; not native Sales/SQL acceptance or a shipped catalogue pin.</summary>
public sealed class AiSalesDraftSummaryToolTests
{
    [Fact]
    public async Task Exact_owner_quantities_are_copied_without_prices_people_notes_or_arithmetic()
    {
        var f = new Fixture();
        f.Authority.Revoke(SalesPermissionManifestV1.OrdersPriceRead);
        var answer = await f.Read();
        Assert.Equal(AiToolOutcomeV1.Succeeded, answer.Outcome);
        Assert.Equal(AiDataClassV1.Internal, answer.Source!.DataClass);
        Assert.Equal(f.Id, answer.Source.TargetId);
        Assert.Equal(SalesDraftSummaryAiToolV1.TargetKind, answer.Source.TargetKind);
        var content = JsonSerializer.Deserialize<SalesDraftSummaryAiContentV1>(answer.ContentJson!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("1.2500", Assert.Single(content.Lines).Quantity);
        Assert.Equal(f.Id, content.DraftId);
        Assert.Equal(f.Id, f.Owner.LastId); Assert.Equal(1, f.Owner.Calls);
        Assert.Equal([SalesPermissionManifestV1.DraftsRead], f.Tool.Descriptor.RequiredPermissions);
        Assert.DoesNotContain("PRIVATE", answer.ContentJson!);
        using var json = JsonDocument.Parse(answer.ContentJson!);
        // AiReadResultV1 canonicalizes JSON object keys; verify the exact canonical surface, including nested keys.
        Assert.Equal(["approvalState", "draftId", "lines", "revision", "state", "version"], json.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["lineId", "lineNo", "orderUnitId", "productId", "quantity"],
            json.RootElement.GetProperty("lines")[0].EnumerateObject().Select(p => p.Name));
        f.Owner.Answer = f.Owner.Answer with { Draft = f.Owner.Answer.Draft with { Version = "8" } };
        Assert.NotEqual(answer.Source.SourceVersion, (await f.Read()).Source!.SourceVersion);
    }

    [Theory]
    [InlineData("{}")] [InlineData("{\"draftId\":null}")] [InlineData("{\"draftId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"companyId\":\"x\"}")] [InlineData("[]")]
    public async Task Invalid_arguments_do_not_read_the_owner(string json)
    {
        var f = new Fixture();
        Assert.Equal(AiToolOutcomeV1.Invalid, (await f.Tool.ReadAsync(new(json), f.Context)).Outcome);
        Assert.Equal(0, f.Owner.Calls);
    }

    [Theory]
    [InlineData("companyId")] [InlineData("lineId")] [InlineData("duplicate")]
    public async Task A_valid_target_does_not_admit_extra_or_duplicate_arguments(string fault)
    {
        var f = new Fixture();
        var json = fault == "duplicate" ? "{\"draftId\":\"" + f.Id + "\",\"draftId\":\"" + f.Id + "\"}"
            : JsonSerializer.Serialize(new Dictionary<string, Guid> { ["draftId"] = f.Id, [fault] = Guid.NewGuid() });
        Assert.Equal(AiToolOutcomeV1.Invalid, (await f.Tool.ReadAsync(new(json), f.Context)).Outcome);
        Assert.Equal(0, f.Owner.Calls);
    }

    [Fact]
    public async Task Missing_owner_is_unavailable_without_a_placeholder_summary()
    {
        var f = new Fixture(); var tool = new SalesDraftSummaryAiToolV1(null, f.Company, f.User, f.Authority, TimeProvider.System);
        var result = await tool.ReadAsync(new(JsonSerializer.Serialize(new { draftId = f.Id })), f.Context);
        Assert.Equal(AiToolOutcomeV1.Unavailable, result.Outcome); Assert.Null(result.Source); Assert.Null(result.ContentJson);
        Assert.Equal(0, f.Owner.Calls);
    }

    [Theory]
    [InlineData("foreign-company")] [InlineData("foreign-id")] [InlineData("state")] [InlineData("approval")]
    [InlineData("version")] [InlineData("count")] [InlineData("quantity")] [InlineData("zero")]
    [InlineData("unit")] [InlineData("duplicate")]
    public async Task Malformed_or_unbound_owner_answers_never_release_content(string fault)
    {
        var f = new Fixture(); var d = f.Owner.Answer; var line = d.Lines[0];
        f.Owner.Answer = fault switch
        {
            "foreign-company" => d with { Draft = d.Draft with { CompanyId = Guid.NewGuid() } },
            "foreign-id" => d with { Draft = d.Draft with { Id = Guid.NewGuid() } },
            "state" => d with { Draft = d.Draft with { State = "Unreviewed" } },
            "approval" => d with { Draft = d.Draft with { ApprovalState = "Unreviewed" } },
            "version" => d with { Draft = d.Draft with { Version = "" } },
            "count" => d with { Draft = d.Draft with { LineCount = 2 } },
            "quantity" => d with { Lines = [line with { Quantity = "1,25" }] },
            "zero" => d with { Lines = [line with { Quantity = "0" }] },
            "unit" => d with { Lines = [line with { OrderUnitId = Guid.Empty }] },
            _ => d with { Draft = d.Draft with { LineCount = 2 }, Lines = [line, line] },
        };
        var result = await f.Read();
        Assert.Equal(AiToolOutcomeV1.Unavailable, result.Outcome); Assert.Null(result.Source); Assert.Null(result.ContentJson);
    }

    [Theory]
    [InlineData("before-permission")] [InlineData("after-permission")] [InlineData("after-company")] [InlineData("after-actor")]
    public async Task Fresh_owner_authority_and_context_are_required_on_both_sides_of_the_read(string fault)
    {
        var f = new Fixture();
        if (fault == "before-permission") f.Authority.Revoke(SalesPermissionManifestV1.DraftsRead);
        else f.Owner.AfterRead = () =>
        {
            if (fault == "after-permission") f.Authority.Revoke(SalesPermissionManifestV1.DraftsRead);
            else if (fault == "after-company") f.Company.Set(Guid.NewGuid());
            else f.User.Actor = Guid.NewGuid().ToString("D");
        };
        var result = await f.Read();
        Assert.Equal(AiToolOutcomeV1.Denied, result.Outcome); Assert.Null(result.Source);
        Assert.Equal(fault == "before-permission" ? 0 : 1, f.Owner.Calls);
    }

    [Theory]
    [InlineData(403, AiToolOutcomeV1.Denied)] [InlineData(404, AiToolOutcomeV1.NotFound)]
    [InlineData(409, AiToolOutcomeV1.Conflict)] [InlineData(503, AiToolOutcomeV1.Unavailable)]
    public async Task Native_refusals_are_preserved(int status, AiToolOutcomeV1 expected)
    {
        var f = new Fixture(); f.Owner.Failure = new SalesDraftServiceException(status, "SENTETIK");
        var result = await f.Read(); Assert.Equal(expected, result.Outcome); Assert.Null(result.Source);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void A_tool_without_the_approved_description_pin_remains_nonexecutable(bool listed)
    {
        var f = new Fixture(); var options = new AiRuntimeOptions();
        if (listed) options.AllowedTools = [new() { ToolId = SalesDraftSummaryAiToolV1.ToolId, Version = "1" }];
        Assert.Null(new AiToolCatalog([f.Tool], Options.Create(options)).Find(SalesDraftSummaryAiToolV1.ToolId, "1"));
        Assert.Equal(0, f.Owner.Calls);
    }

    private sealed class Fixture
    {
        public Guid Id { get; } = Guid.NewGuid();
        public AiSalesLineMarginExplanationToolTests.MarginCompany Company { get; } = new();
        public AiSalesLineMarginExplanationToolTests.MarginUser User { get; }
        public AiSalesLineMarginExplanationToolTests.MarginAuthority Authority { get; } = new();
        public SummaryOwnerProxy Owner { get; }
        public SalesDraftSummaryAiToolV1 Tool { get; }
        public AiServerContextV1 Context => new(Company.CompanyId, User.Actor, "SENTETIK-session", "scope", "authority", "SENTETIK-corr", null, null, null);
        public Fixture()
        {
            Company.Set(TestCompanyIds.CompanyA); User = new(Company.CompanyId);
            var service = DispatchProxy.Create<ISalesDraftService, SummaryOwnerProxy>(); Owner = (SummaryOwnerProxy)service;
            var draft = new SalesOrderDraftSummaryView(Id, Company.CompanyId, "PRIVATE-document", "Normal", "Draft", "7", "Rev.0",
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new(2026, 11, 27), DateTimeOffset.UnixEpoch, 1, "Preparing");
            Owner.Answer = new(draft, null, "PRIVATE-note",
                [new(Guid.NewGuid(), 1, Guid.NewGuid(), "PRIVATE-version", "PRIVATE-code", "PRIVATE-name", Guid.NewGuid(), "1.2500",
                    "999999.99", Guid.NewGuid(), null, "PRIVATE-line-note", null, "99", SalesPriceDisclosure.Visible)],
                null, null, null, null, null, null, new("PRIVATE-submitter", null, "PRIVATE-decider", null, "PRIVATE-reason"),
                new(false, null, "PRIVATE-acceptor", null, false), false, false);
            Tool = new(service, Company, User, Authority, TimeProvider.System);
        }
        public Task<AiReadResultV1> Read() => Tool.ReadAsync(new(JsonSerializer.Serialize(new { draftId = Id })), Context);
    }

    public class SummaryOwnerProxy : DispatchProxy
    {
        public SalesOrderDraftDetailView Answer { get; set; } = null!;
        public Exception? Failure { get; set; }
        public Action? AfterRead { get; set; }
        public int Calls { get; private set; }
        public Guid LastId { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(ISalesDraftService.GetDraftDetailAsync)) throw new NotSupportedException();
            Calls++; LastId = (Guid)args![0]!;
            if (Failure is { } error) return Task.FromException<SalesOrderDraftDetailView>(error);
            AfterRead?.Invoke(); return Task.FromResult(Answer);
        }
    }
}
