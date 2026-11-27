using System.Text.Json;
using GoymenERP.Api.Ai;
using GoymenERP.Api.Ai.Tools;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AiAskServiceTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Sales_context_binds_one_draft_argument_and_owner_source_without_list_fallback(bool expectedDigest)
    {
        var rig = new SalesContextRig();
        var result = await rig.Service().AskAsync(rig.Ask(expectedDigest));
        Assert.Equal(AiAskStatusV1.Answered, result.Status);
        var citation = Assert.Single(Assert.IsType<AiAnswerV1>(result.Response!.Answer).Citations);
        Assert.Equal((SalesDraftSummaryAiToolV1.TargetKind, Target, SalesContextRig.Digest),
            (citation.TargetKind, citation.TargetId, citation.SourceVersion));
        using var args = JsonDocument.Parse(Assert.Single(rig.Tool.Inputs).ArgumentsJson);
        Assert.Equal("draftId", Assert.Single(args.RootElement.EnumerateObject()).Name);
        Assert.Equal(Target.ToString("D"), args.RootElement.GetProperty("draftId").GetString());
        Assert.Equal(Company, Assert.Single(rig.Tool.Contexts).CompanyId);
    }

    [Theory]
    [InlineData("foreign-id")] [InlineData("list-source")] [InlineData("digest")]
    public async Task Sales_context_never_releases_a_company_list_other_draft_or_stale_digest(string fault)
    {
        var rig = new SalesContextRig(); var source = rig.Tool.NextResult!.Source!;
        rig.Tool.NextResult = new(AiToolOutcomeV1.Succeeded, "{\"name\":\"SENTETIK\"}", fault switch
        {
            "foreign-id" => source with { TargetId = Guid.NewGuid() },
            "list-source" => source with { TargetKind = "sales.orders.status", TargetId = Company },
            _ => source with { SourceVersion = "sha256:" + new string('b', 64) },
        });
        var result = await rig.Service().AskAsync(rig.Ask(true));
        Assert.Equal(AiAskStatusV1.ToolUnavailable, result.Status); Assert.Null(result.Response);
        Assert.Empty(rig.Common.GatewayStore.Reservations);
    }

    [Theory]
    [InlineData("foreign-id")] [InlineData("company-argument")] [InlineData("other-tool")]
    public async Task Sales_context_rejects_untrusted_plan_drift_before_owner_read(string fault)
    {
        var rig = new SalesContextRig();
        rig.Common.Provider.Plan = request => new([new(fault == "other-tool" ? "sales.order-status.v1" : request.Hint!.ToolId,
            "1", fault == "company-argument" ? JsonSerializer.Serialize(new { draftId = Target, companyId = Company })
                : JsonSerializer.Serialize(new { draftId = fault == "foreign-id" ? Guid.NewGuid() : Target }))]);
        Assert.Equal(AiAskStatusV1.Refused, (await rig.Service().AskAsync(rig.Ask())).Status);
        Assert.Empty(rig.Tool.Inputs); Assert.Empty(rig.Common.GatewayStore.Reservations);
    }

    [Theory]
    [InlineData("general")] [InlineData("permission")] [InlineData("pin")]
    public async Task Sales_context_does_not_enable_general_ERP_carry_or_create_permission_or_catalogue_access(string fault)
    {
        var rig = new SalesContextRig();
        if (fault == "permission") rig.Authority.Revoke(SalesPermissionManifestV1.DraftsRead);
        var result = await rig.Service(badPin: fault == "pin").AskAsync(rig.Ask() with
            { Mode = fault == "general" ? AiAskModesV1.GeneralChat : null });
        Assert.Equal(fault == "general" ? AiAskStatusV1.Invalid : AiAskStatusV1.Refused, result.Status);
        Assert.Empty(rig.Tool.Inputs); Assert.Empty(rig.Common.GatewayStore.Reservations);
    }

    // Existing shared Ask/gateway/Fake owner seam only. The synthetic pin is not a shipped catalogue approval.
    private sealed class SalesContextRig
    {
        public const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        public Harness Common { get; } = new();
        public FakeReadTool Tool { get; }
        public FakeAuthority Authority { get; } = new(ErpPermissions.AiPanelUse, SalesPermissionManifestV1.DraftsRead);
        public SalesContextRig()
        {
            var descriptor = new SalesDraftSummaryAiToolV1(null, null, null, null, TimeProvider.System).Descriptor;
            Tool = new(descriptor) { NextResult = new(AiToolOutcomeV1.Succeeded, "{\"name\":\"SENTETIK taslak\"}",
                new(SalesDraftSummaryAiToolV1.ToolId, "1", SalesDraftSummaryAiToolV1.TargetKind, Target, "SENTETIK", Digest,
                    DateTimeOffset.UnixEpoch, AiDataClassV1.Internal)) };
            Common.Provider.Plan = request => new([new(request.Hint!.ToolId, request.Hint.ToolVersion, request.Hint.ArgumentsJson)]);
        }
        public AiAskRequestV1 Ask(bool expected = false) => new("SENTETIK taslak özeti?", null, null, null,
            OpenRecordContext: new(SalesDraftSummaryAiToolV1.TargetKind, Target, expected ? Digest : null));
        public AiAskService Service(bool badPin = false)
        {
            var options = Harness.ReadyOptions();
            options.AllowedTools = [new() { ToolId = SalesDraftSummaryAiToolV1.ToolId, Version = "1",
                DescriptionSha256 = badPin ? new string('0', 64) : AiToolDescriptionPinV1.Compute(Tool.Descriptor) }];
            return new(Options.Create(options), new TestHostEnvironment("Development"), Common.Provider,
                new AiToolCatalog([Tool], Options.Create(options)), new TestUser(Actor, Company), Common.CompanyScope, Authority,
                new FixedSessionResolver(), Common.Correlation, Common.Writer, TimeProvider.System,
                AiGatewayTestDoubles.Gateway(options, Common.GatewayStore), Common.ReceiptWriter);
        }
    }
}
