using GoymenERP.Api.Ai;
using GoymenERP.Api.Ai.Commands;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// WP-C1 approved write pipeline over a synthetic test command and the Fake provider (no database, no network, no
/// command family opened). Pins AI-D34 a (approval bound to the seen command; a changed digest, record version or
/// authority invalidates it; permissions and version re-checked at execution), AI-D25 closed list, decision 156
/// (15-minute preview, second approval + step-up), idempotent resend and company isolation.
/// </summary>
public sealed class AiCommandPipelineTests
{
    private const string ToolId = "test.item-rename.v1";
    private const string ToolVersion = "1";
    private const string ToolPermission = "test.items.write";
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid OtherCompany = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
    private static readonly Guid Actor = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherActor = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Target = Guid.Parse("cccccccc-0000-4000-8000-000000000003");
    private static readonly DateTimeOffset Start = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Preview_approval_and_execution_run_the_owner_command_once_with_the_frozen_request()
    {
        var harness = new Harness();

        var preview = await harness.Service().ProposeAsync(Proposal());
        Assert.Equal(AiCommandStatusV1.Previewed, preview.Status);
        var view = preview.Preview!;
        Assert.Equal(Target, view.TargetId);
        Assert.Equal("rv-1", view.TargetVersion);
        Assert.Equal("{\"itemId\":\"cccccccc-0000-4000-8000-000000000003\",\"name\":\"SENTETIK yeni ad\"}", view.CanonicalCommandJson);
        Assert.True(view.RequiresSecondApproval);
        Assert.True(view.RequiresStepUp);
        Assert.Equal(64, view.PreviewHash.Length);
        Assert.Empty(harness.Tool.Executions);

        var approval = await harness.Service().ApproveAsync(view.PreviewId, view.PreviewHash);
        Assert.Equal(AiCommandStatusV1.Approved, approval.Status);
        Assert.Empty(harness.Tool.Executions);

        var executed = await harness.Service().ExecuteAsync(view.PreviewId, view.PreviewHash, approval.ApprovalId!.Value);

        Assert.Equal(AiCommandStatusV1.Executed, executed.Status);
        Assert.Equal("rv-2", executed.ResultVersion);
        Assert.False(executed.IsReplay);
        var request = Assert.Single(harness.Tool.Executions);
        Assert.Equal("ai:" + view.PreviewId.ToString("D"), request.CommandContext.IdempotencyKey);
        Assert.Equal(Company, request.ServerContext.CompanyId);
        Assert.Equal(view.PreviewHash, request.CommandContext.Audit.PreviewHash);
        Assert.Equal(approval.ApprovalId, request.CommandContext.Audit.ApprovalId);
        Assert.Equal(
            [AiCommandEventKindV1.Proposed, AiCommandEventKindV1.Previewed, AiCommandEventKindV1.Approved,
                AiCommandEventKindV1.SecondApproved, AiCommandEventKindV1.Executed],
            harness.Ledger.Events.Select(item => item.Kind));
        Assert.All(harness.Ledger.Events, item => Assert.Equal(Company, item.CompanyId));
        Assert.Equal(
            [SecurityEventPhasesV1.Started, SecurityEventPhasesV1.Completed],
            harness.Writer.Events.Select(item => item.Phase));
        Assert.All(harness.Writer.Events, item => Assert.Equal(AiToolRiskClassV1.Write, item.AiRiskClass));
        harness.AssertEveryEventIsValid();
    }

    [Fact]
    public async Task The_preview_lasts_at_most_the_decision_156_lifetime()
    {
        var harness = new Harness();
        harness.Tool.OwnerPreviewLifetime = TimeSpan.FromHours(2);

        var view = (await harness.Service().ProposeAsync(Proposal())).Preview!;

        Assert.Equal(Start + TimeSpan.FromMinutes(15), view.ExpiresAtUtc);
    }

    [Fact]
    public async Task A_changed_preview_hash_is_refused_at_approval_and_at_execution()
    {
        var harness = new Harness();
        var view = (await harness.Service().ProposeAsync(Proposal())).Preview!;
        var tampered = view.PreviewHash[..^1] + (view.PreviewHash[^1] == '0' ? '1' : '0');

        var refusedApproval = await harness.Service().ApproveAsync(view.PreviewId, tampered);
        var approval = await harness.Service().ApproveAsync(view.PreviewId, view.PreviewHash);
        var refusedExecution = await harness.Service().ExecuteAsync(view.PreviewId, tampered, approval.ApprovalId!.Value);

        Assert.Equal(AiCommandStatusV1.Refused, refusedApproval.Status);
        Assert.Equal(AiCommandReasonCodesV1.PreviewHashMismatch, refusedApproval.ReasonCode);
        Assert.Equal(AiCommandStatusV1.Refused, refusedExecution.Status);
        Assert.Equal(AiCommandReasonCodesV1.PreviewHashMismatch, refusedExecution.ReasonCode);
        Assert.Empty(harness.Tool.Executions);
        Assert.Contains(harness.Writer.Events, item => item.ReasonCode == SecurityEventReasonCodesV1.AiApprovalMissing);
        harness.AssertEveryEventIsValid();
    }

    [Fact]
    public async Task A_record_version_change_after_approval_invalidates_the_approval_and_the_owner_never_runs()
    {
        var harness = new Harness();
        var view = (await harness.Service().ProposeAsync(Proposal())).Preview!;
        var approval = await harness.Service().ApproveAsync(view.PreviewId, view.PreviewHash);

        harness.Tool.TargetVersion = "rv-9";
        var stale = await harness.Service().ExecuteAsync(view.PreviewId, view.PreviewHash, approval.ApprovalId!.Value);
        harness.Tool.TargetVersion = "rv-1";
        var again = await harness.Service().ExecuteAsync(view.PreviewId, view.PreviewHash, approval.ApprovalId.Value);

        Assert.Equal(AiCommandStatusV1.Invalidated, stale.Status);
        Assert.Equal(AiCommandReasonCodesV1.PreviewStale, stale.ReasonCode);
        Assert.Equal(AiCommandStatusV1.Invalidated, again.Status);
        Assert.Empty(harness.Tool.Executions);
        Assert.Contains(harness.Ledger.Events, item => item.Kind == AiCommandEventKindV1.Invalidated);
    }

    [Fact]
    public async Task A_changed_input_effect_before_approval_invalidates_the_preview()
    {
        var harness = new Harness();
        var view = (await harness.Service().ProposeAsync(Proposal())).Preview!;

        harness.Tool.CurrentName = "SENTETIK baska ad";
        var approval = await harness.Service().ApproveAsync(view.PreviewId, view.PreviewHash);

        Assert.Equal(AiCommandStatusV1.Invalidated, approval.Status);
        Assert.Equal(AiCommandReasonCodesV1.PreviewStale, approval.ReasonCode);
        Assert.Null(approval.ApprovalId);
    }

    [Fact]
    public async Task A_revoked_tool_permission_refuses_execution_and_the_owner_never_runs()
    {
        var harness = new Harness();
        var view = (await harness.Service().ProposeAsync(Proposal())).Preview!;
        var approval = await harness.Service().ApproveAsync(view.PreviewId, view.PreviewHash);

        harness.Authority.Revoke(ToolPermission);
        var result = await harness.Service().ExecuteAsync(view.PreviewId, view.PreviewHash, approval.ApprovalId!.Value);

        Assert.Equal(AiCommandStatusV1.Refused, result.Status);
        Assert.Equal(AiCommandReasonCodesV1.PermissionMissing, result.ReasonCode);
        Assert.Empty(harness.Tool.Executions);
        var denied = Assert.Single(harness.Writer.Events);
        Assert.Equal(SecurityEventTypesV1.AiAuthorizationDenied, denied.EventType);
        Assert.Equal(ToolPermission, denied.PermissionCode);
        harness.AssertEveryEventIsValid();
    }

    [Fact]
    public async Task A_revoked_panel_capability_refuses_approval()
    {
        var harness = new Harness();
        var view = (await harness.Service().ProposeAsync(Proposal())).Preview!;

        harness.Authority.Revoke(ErpPermissions.AiPanelUse);
        var result = await harness.Service().ApproveAsync(view.PreviewId, view.PreviewHash);

        Assert.Equal(AiCommandStatusV1.Refused, result.Status);
        Assert.Equal(AiCommandReasonCodesV1.PermissionMissing, result.ReasonCode);
    }

    [Fact]
    public async Task A_changed_authority_revision_invalidates_the_approval()
    {
        var harness = new Harness();
        var view = (await harness.Service().ProposeAsync(Proposal())).Preview!;
        var approval = await harness.Service().ApproveAsync(view.PreviewId, view.PreviewHash);

        harness.Authority.RoleCode = "ROLE-SENTETIK-2";
        var result = await harness.Service().ExecuteAsync(view.PreviewId, view.PreviewHash, approval.ApprovalId!.Value);

        Assert.Equal(AiCommandStatusV1.Invalidated, result.Status);
        Assert.Equal(AiCommandReasonCodesV1.AuthorityChanged, result.ReasonCode);
        Assert.Empty(harness.Tool.Executions);
    }

    [Fact]
    public async Task A_resent_execution_replays_the_result_without_a_second_owner_call()
    {
        var harness = new Harness();
        var view = (await harness.Service().ProposeAsync(Proposal())).Preview!;
        var approval = await harness.Service().ApproveAsync(view.PreviewId, view.PreviewHash);

        var first = await harness.Service().ExecuteAsync(view.PreviewId, view.PreviewHash, approval.ApprovalId!.Value);
        var second = await harness.Service().ExecuteAsync(view.PreviewId, view.PreviewHash, approval.ApprovalId.Value);

        Assert.Equal(AiCommandStatusV1.Executed, first.Status);
        Assert.Equal(AiCommandStatusV1.Executed, second.Status);
        Assert.True(second.IsReplay);
        Assert.Equal(first.ResultVersion, second.ResultVersion);
        Assert.Single(harness.Tool.Executions);
        Assert.Single(harness.Ledger.Events, item => item.Kind == AiCommandEventKindV1.Executed);
    }

    [Fact]
    public async Task A_resent_approval_returns_the_recorded_approval()
    {
        var harness = new Harness();
        var view = (await harness.Service().ProposeAsync(Proposal())).Preview!;

        var first = await harness.Service().ApproveAsync(view.PreviewId, view.PreviewHash);
        var second = await harness.Service().ApproveAsync(view.PreviewId, view.PreviewHash);

        Assert.Equal(first.ApprovalId, second.ApprovalId);
        Assert.True(second.IsReplay);
        Assert.Single(harness.Ledger.Events, item => item.Kind == AiCommandEventKindV1.Approved);
    }

    [Fact]
    public async Task An_unknown_owner_outcome_is_only_looked_up_on_resend_never_executed_again()
    {
        var harness = new Harness();
        var view = (await harness.Service().ProposeAsync(Proposal())).Preview!;
        var approval = await harness.Service().ApproveAsync(view.PreviewId, view.PreviewHash);
        harness.Tool.ThrowAfterApplying = true;

        var unknown = await harness.Service().ExecuteAsync(view.PreviewId, view.PreviewHash, approval.ApprovalId!.Value);
        harness.Tool.ThrowAfterApplying = false;
        var resolved = await harness.Service().ExecuteAsync(view.PreviewId, view.PreviewHash, approval.ApprovalId.Value);

        Assert.Equal(AiCommandStatusV1.Unknown, unknown.Status);
        Assert.Equal(AiCommandStatusV1.Executed, resolved.Status);
        Assert.True(resolved.IsReplay);
        Assert.Single(harness.Tool.Executions);
        var lookup = Assert.Single(harness.Tool.Lookups);
        Assert.Equal(harness.Tool.Executions[0].RequestIdentityJson, lookup.RequestIdentityJson);
    }

    [Theory]
    [InlineData(AiCommandEffectV1.Approval)]
    [InlineData(AiCommandEffectV1.Rejection)]
    [InlineData(AiCommandEffectV1.Payment)]
    [InlineData(AiCommandEffectV1.PeriodClose)]
    [InlineData(AiCommandEffectV1.RoleOrPermissionAssignment)]
    [InlineData(AiCommandEffectV1.PolicyPublication)]
    public async Task A_closed_list_effect_is_refused_even_when_allowlisted(AiCommandEffectV1 effect)
    {
        var harness = new Harness(effects: [AiCommandEffectV1.RecordChange, effect]);

        var result = await harness.Service().ProposeAsync(Proposal());

        Assert.Equal(AiCommandStatusV1.Refused, result.Status);
        Assert.Equal(AiCommandReasonCodesV1.ClosedCommand, result.ReasonCode);
        Assert.Empty(harness.Tool.Previews);
        Assert.Contains(harness.Ledger.Events, item => item.Kind == AiCommandEventKindV1.Refused
            && item.ReasonCode == AiCommandReasonCodesV1.ClosedCommand);
        Assert.Equal(SecurityEventReasonCodesV1.AiToolNotAllowed, Assert.Single(harness.Writer.Events).ReasonCode);
        harness.AssertEveryEventIsValid();
    }

    [Theory]
    [InlineData("test.invoice-approve.v1", "RenameItem", ToolPermission)]
    [InlineData("test.item-rename.v1", "RejectRequest", ToolPermission)]
    [InlineData("test.item-rename.v1", "RenameItem", "finance.payments.write")]
    [InlineData("test.period-close.v1", "RenameItem", ToolPermission)]
    [InlineData("test.item-rename.v1", "AssignRole", ToolPermission)]
    [InlineData("test.item-rename.v1", "PublishPolicy", ToolPermission)]
    public async Task A_closed_family_name_is_refused_even_when_the_declared_effect_is_a_record_change(
        string toolId, string commandName, string permission)
    {
        var harness = new Harness(toolId: toolId, commandName: commandName, permission: permission);

        var result = await harness.Service().ProposeAsync(Proposal(toolId));

        Assert.Equal(AiCommandStatusV1.Refused, result.Status);
        Assert.Equal(AiCommandReasonCodesV1.ClosedCommand, result.ReasonCode);
        Assert.Empty(harness.Tool.Previews);
    }

    [Fact]
    public async Task A_command_tool_without_an_effect_declaration_is_refused()
    {
        var harness = new Harness(declareEffects: false);

        var result = await harness.Service().ProposeAsync(Proposal());

        Assert.Equal(AiCommandReasonCodesV1.ClosedCommand, result.ReasonCode);
        Assert.Empty(harness.Tool.Previews);
    }

    [Theory]
    [InlineData("sales.order-approve.v1", true)]
    [InlineData("purchasing.requests.reject", true)]
    [InlineData("ApproveOrder", true)]
    [InlineData("finance.payment-create.v1", true)]
    [InlineData("accounting.period-close.v1", true)]
    [InlineData("identity.roles.assign", true)]
    [InlineData("settings.policy-publish.v1", true)]
    [InlineData("work.tasks.create", false)]
    [InlineData("RenameItem", false)]
    [InlineData("test.item-rename.v1", false)]
    public void The_closed_list_reads_words_of_ids_command_names_and_permissions(string name, bool closed) =>
        Assert.Equal(closed, AiCommandClosedListV1.NamesClosedFamily(name));

    [Fact]
    public async Task Another_company_cannot_approve_or_execute_the_preview()
    {
        var harness = new Harness();
        var view = (await harness.Service().ProposeAsync(Proposal())).Preview!;
        var approval = await harness.Service().ApproveAsync(view.PreviewId, view.PreviewHash);

        var other = new CompanyContext();
        other.Set(OtherCompany);
        harness.CompanyScope = other;
        harness.Authority.GrantedCompany = OtherCompany;
        var foreignApproval = await harness.Service().ApproveAsync(view.PreviewId, view.PreviewHash);
        var foreignExecution = await harness.Service().ExecuteAsync(view.PreviewId, view.PreviewHash, approval.ApprovalId!.Value);

        Assert.Equal(AiCommandStatusV1.NotFound, foreignApproval.Status);
        Assert.Equal(AiCommandStatusV1.NotFound, foreignExecution.Status);
        Assert.Empty(harness.Tool.Executions);
        Assert.Equal(2, harness.Ledger.Events.Count(item => item.CompanyId == OtherCompany && item.Kind == AiCommandEventKindV1.Refused));
    }

    [Fact]
    public async Task Another_actor_in_the_same_company_cannot_approve_the_preview()
    {
        var harness = new Harness();
        var view = (await harness.Service().ProposeAsync(Proposal())).Preview!;

        harness.User = new TestUser(OtherActor, Company);
        var result = await harness.Service().ApproveAsync(view.PreviewId, view.PreviewHash);

        Assert.Equal(AiCommandStatusV1.NotFound, result.Status);
        Assert.Equal(AiCommandReasonCodesV1.NotActor, result.ReasonCode);
    }

    [Fact]
    public async Task Without_a_verified_step_up_the_second_approval_is_refused()
    {
        var harness = new Harness();
        harness.StepUp = new UnconfiguredAiStepUpVerifier();
        var view = (await harness.Service().ProposeAsync(Proposal())).Preview!;
        var approval = await harness.Service().ApproveAsync(view.PreviewId, view.PreviewHash);

        var result = await harness.Service().ExecuteAsync(view.PreviewId, view.PreviewHash, approval.ApprovalId!.Value);

        Assert.Equal(AiCommandStatusV1.Refused, result.Status);
        Assert.Equal(AiCommandReasonCodesV1.StepUpRequired, result.ReasonCode);
        Assert.Empty(harness.Tool.Executions);
        Assert.DoesNotContain(harness.Ledger.Events, item => item.Kind == AiCommandEventKindV1.SecondApproved);
    }

    [Fact]
    public async Task Execution_without_approval_or_with_another_approval_id_is_refused()
    {
        var harness = new Harness();
        var view = (await harness.Service().ProposeAsync(Proposal())).Preview!;

        var unapproved = await harness.Service().ExecuteAsync(view.PreviewId, view.PreviewHash, Guid.NewGuid());
        await harness.Service().ApproveAsync(view.PreviewId, view.PreviewHash);
        var mismatched = await harness.Service().ExecuteAsync(view.PreviewId, view.PreviewHash, Guid.NewGuid());

        Assert.Equal(AiCommandReasonCodesV1.ApprovalRequired, unapproved.ReasonCode);
        Assert.Equal(AiCommandReasonCodesV1.ApprovalMismatch, mismatched.ReasonCode);
        Assert.Empty(harness.Tool.Executions);
    }

    [Fact]
    public async Task An_expired_preview_cannot_be_approved()
    {
        var harness = new Harness();
        var view = (await harness.Service().ProposeAsync(Proposal())).Preview!;

        harness.Time.Advance(TimeSpan.FromMinutes(15));
        var result = await harness.Service().ApproveAsync(view.PreviewId, view.PreviewHash);

        Assert.Equal(AiCommandStatusV1.Invalidated, result.Status);
        Assert.Equal(AiCommandReasonCodesV1.PreviewExpired, result.ReasonCode);
    }

    [Fact]
    public async Task A_rejected_preview_is_recorded_and_cannot_be_executed()
    {
        var harness = new Harness();
        var view = (await harness.Service().ProposeAsync(Proposal())).Preview!;
        var approval = await harness.Service().ApproveAsync(view.PreviewId, view.PreviewHash);

        var rejected = await harness.Service().RejectAsync(view.PreviewId, view.PreviewHash);
        var execution = await harness.Service().ExecuteAsync(view.PreviewId, view.PreviewHash, approval.ApprovalId!.Value);

        Assert.Equal(AiCommandStatusV1.Rejected, rejected.Status);
        Assert.Equal(AiCommandStatusV1.Rejected, execution.Status);
        Assert.Empty(harness.Tool.Executions);
        Assert.Single(harness.Ledger.Events, item => item.Kind == AiCommandEventKindV1.Rejected);
    }

    [Fact]
    public async Task Proposal_arguments_naming_a_company_or_an_unlisted_field_are_refused()
    {
        var harness = new Harness();

        var company = await harness.Service().ProposeAsync(Proposal(arguments: $"{{\"itemId\":\"{Target:D}\",\"companyId\":\"{OtherCompany:D}\"}}"));
        var unlisted = await harness.Service().ProposeAsync(Proposal(arguments: $"{{\"itemId\":\"{Target:D}\",\"price\":1}}"));

        Assert.Equal(AiCommandStatusV1.Invalid, company.Status);
        Assert.Equal(AiCommandStatusV1.Invalid, unlisted.Status);
        Assert.Empty(harness.Tool.Previews);
    }

    [Fact]
    public async Task A_tool_missing_from_the_command_allowlist_is_refused()
    {
        var harness = new Harness(allowlisted: false);

        var result = await harness.Service().ProposeAsync(Proposal());

        Assert.Equal(AiCommandReasonCodesV1.ToolNotAllowed, result.ReasonCode);
        Assert.Empty(harness.Tool.Previews);
    }

    [Theory]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("not-a-pin")]
    public async Task A_command_tool_whose_card_no_longer_matches_its_approved_pin_is_refused(string stalePin)
    {
        // WP-A6: an allowlisted id+version is not enough; the card the model sees must be the approved one.
        var harness = new Harness(configureCommands: commands => commands.AllowedTools.ForEach(entry => entry.DescriptionSha256 = stalePin));

        var result = await harness.Service().ProposeAsync(Proposal());

        Assert.Equal(AiCommandReasonCodesV1.ToolNotAllowed, result.ReasonCode);
        Assert.Empty(harness.Tool.Previews);
    }

    [Fact]
    public async Task The_default_unconfigured_ledger_keeps_the_pipeline_unavailable()
    {
        var harness = new Harness();
        harness.LedgerOverride = new UnconfiguredAiCommandLedger();

        var result = await harness.Service().ProposeAsync(Proposal());

        Assert.Equal(AiCommandStatusV1.Unavailable, result.Status);
        Assert.Equal(0, harness.Provider.Calls);
        Assert.Empty(harness.Tool.Previews);
    }

    [Fact]
    public async Task A_disabled_pipeline_never_calls_the_provider()
    {
        var harness = new Harness(configureCommands: options => options.Enabled = false);

        var result = await harness.Service().ProposeAsync(Proposal());

        Assert.Equal(AiCommandStatusV1.Disabled, result.Status);
        Assert.Equal(0, harness.Provider.Calls);
    }

    [Fact]
    public void The_shipped_configuration_keeps_the_pipeline_closed_with_a_15_minute_preview()
    {
        var path = Path.Combine(FindRepositoryRoot(), "src", "backend", "GoymenERP.Api", "appsettings.json");
        var shipped = new ConfigurationBuilder().AddJsonFile(path, optional: false, reloadOnChange: false).Build()
            .GetSection(AiCommandOptions.SectionName).Get<AiCommandOptions>();

        Assert.NotNull(shipped);
        Assert.False(shipped.Enabled);
        Assert.Equal(TimeSpan.FromMinutes(15), shipped.PreviewLifetime);
        Assert.Empty(shipped.AllowedTools);
    }

    private static AiCommandProposalRequestV1 Proposal(string toolId = ToolId, string? arguments = null) =>
        new("SENTETIK kalemin adini degistir", toolId, ToolVersion,
            arguments ?? $"{{\"itemId\":\"{Target:D}\",\"name\":\"SENTETIK yeni ad\"}}");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GoymenERP.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class Harness
    {
        private readonly AiRuntimeOptions runtime;
        private readonly AiCommandOptions commands;

        public Harness(
            IReadOnlyCollection<AiCommandEffectV1>? effects = null,
            string toolId = ToolId,
            string commandName = "RenameItem",
            string permission = ToolPermission,
            bool declareEffects = true,
            bool allowlisted = true,
            Action<AiCommandOptions>? configureCommands = null)
        {
            runtime = new AiRuntimeOptions
            {
                Enabled = true,
                Provider = AiProviderKindsV1.Fake,
                PolicyRef = "TEST-AI-POLICY",
                PolicyVersion = 3,
                MaxQuestionLength = 200,
                MaxArgumentBytes = 512,
                MaxToolCallsPerRequest = 1,
                MaxDataClass = AiDataClassV1.Internal,
            };
            commands = new AiCommandOptions
            {
                Enabled = true,
                PreviewLifetime = TimeSpan.FromMinutes(15),
                AllowedTools = allowlisted ? [new AiAllowedToolOptions { ToolId = toolId, Version = ToolVersion }] : [],
            };
            configureCommands?.Invoke(commands);
            var descriptor = new AiToolDescriptorV1(toolId, ToolVersion, "Test", AiToolRiskClassV1.Write, AiDataClassV1.Internal,
                [permission], ["itemId", "name"], commandName,
                new AiContractRevisionV1("in-1", "out-1", "field-1", "limit-1", "catalogue-1", "authz-1", "source-1", "redact-1"));
            Tool = declareEffects
                ? new DeclaredCommandTool(descriptor, Time, effects ?? [AiCommandEffectV1.RecordChange])
                : new UndeclaredCommandTool(descriptor, Time);
            // WP-A6: the harness approves the pin of the card it registers, unless a test configured its own pin.
            foreach (var entry in commands.AllowedTools.Where(entry => entry.ToolId == toolId && entry.Version == ToolVersion
                         && entry.DescriptionSha256 is null))
            {
                entry.DescriptionSha256 = AiToolDescriptionPinV1.Compute(descriptor);
            }
            Authority = new FakeAuthority(ErpPermissions.AiPanelUse, permission);
            CompanyScope.Set(Company);
            Correlation.Set("corr-ai-komut-sentetik", null, SourceChannels.Web, null, null);
        }

        public ManualTime Time { get; } = new(Start);
        public CountingProvider Provider { get; } = new();
        public TestCommandTool Tool { get; }
        public InMemoryLedger Ledger { get; } = new();
        public IAiCommandLedgerV1? LedgerOverride { get; set; }
        public IAiStepUpVerifierV1 StepUp { get; set; } = new VerifiedStepUp();
        public RecordingWriter Writer { get; } = new();
        public FakeAuthority Authority { get; }
        public ICurrentUser User { get; set; } = new TestUser(Actor, Company, OtherCompany);
        public ICompanyContext CompanyScope { get; set; } = new CompanyContext();
        public CorrelationContext Correlation { get; } = new();

        public AiCommandPipeline Service() => new(
            Options.Create(runtime),
            Options.Create(commands),
            new TestHostEnvironment("Development"),
            Provider,
            new AiCommandToolCatalog([Tool], Options.Create(commands)),
            LedgerOverride ?? Ledger,
            StepUp,
            User,
            CompanyScope,
            Authority,
            new FixedSessionResolver(),
            Correlation,
            Writer,
            Time);

        public void AssertEveryEventIsValid()
        {
            foreach (var securityEvent in Writer.Events)
            {
                Assert.Empty(SecurityEventV1Validator.Validate(securityEvent));
                Assert.Equal(SourceChannels.AiAssisted, securityEvent.SourceChannel);
            }
        }
    }

    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;

        public void Advance(TimeSpan by) => now += by;

        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CountingProvider : IAiModelProviderV1
    {
        private readonly FakeAiModelProvider inner = new();

        public int Calls { get; private set; }

        public string ProviderKind => inner.ProviderKind;

        public Task<AiModelPlanV1> PlanAsync(AiModelRequestV1 request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return inner.PlanAsync(request, cancellationToken);
        }
    }

    /// <summary>Synthetic owner command: renames one item; the owner's own expected version and idempotency key are honoured.</summary>
    private class TestCommandTool(AiToolDescriptorV1 descriptor, TimeProvider time) : IAiOwnerCommandToolV1
    {
        private readonly Dictionary<string, AiOwnerRecordedResultV1> applied = new(StringComparer.Ordinal);

        public AiToolDescriptorV1 Descriptor { get; } = descriptor;
        public string TargetVersion { get; set; } = "rv-1";
        public string? CurrentName { get; set; }
        public TimeSpan OwnerPreviewLifetime { get; set; } = TimeSpan.FromMinutes(30);
        public bool ThrowAfterApplying { get; set; }
        public List<AiServerContextV1> Previews { get; } = [];
        public List<AiOwnerCommandRequestV1> Executions { get; } = [];
        public List<AiOwnerCommandRequestV1> Lookups { get; } = [];

        public Task<AiPreviewResultV1> PreviewAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
        {
            Previews.Add(context);
            var now = time.GetUtcNow();
            var difference = $"{{\"name\":{{\"from\":\"{CurrentName ?? "SENTETIK eski ad"}\",\"to\":\"SENTETIK yeni ad\"}}}}";
            return Task.FromResult(new AiPreviewResultV1(AiToolOutcomeV1.Succeeded, new AiPreviewV1(
                Guid.NewGuid(), "item", Target, TargetVersion, input.ArgumentsJson, difference, [], now, now + OwnerPreviewLifetime)));
        }

        public Task<AiOwnerRecordedResultV1> ExecuteAsync(AiOwnerCommandRequestV1 request, CancellationToken cancellationToken = default)
        {
            var key = request.CommandContext.IdempotencyKey;
            if (applied.TryGetValue(key, out var existing))
            {
                return Task.FromResult(existing);
            }

            Executions.Add(request);
            var result = Recorded(request, isReplay: false);
            applied[key] = Recorded(request, isReplay: true);
            if (ThrowAfterApplying)
            {
                throw new TimeoutException("Synthetic lost response after the owner applied the command.");
            }

            return Task.FromResult(result);
        }

        public Task<AiOwnerLookupResultV1> FindResultAsync(AiOwnerCommandRequestV1 request, CancellationToken cancellationToken = default)
        {
            Lookups.Add(request);
            return Task.FromResult(applied.TryGetValue(request.CommandContext.IdempotencyKey, out var recorded)
                ? new AiOwnerLookupResultV1(request, recordedResult: recorded)
                : new AiOwnerLookupResultV1(request));
        }

        private AiOwnerRecordedResultV1 Recorded(AiOwnerCommandRequestV1 request, bool isReplay) => new(
            request,
            new AiOwnerEvidenceBindingV1(request.RequestIdentityJson, request.CommandContext.IdempotencyKey, Descriptor.OwnerModule,
                Descriptor.OwnerCommandName!, Descriptor.Contract.SourceContract, "owner-evidence-sentetik", time.GetUtcNow()),
            new AiCommandResultV1(AiCommandOutcomeV1.Applied, "rv-2", isReplay: isReplay));
    }

    private sealed class DeclaredCommandTool(AiToolDescriptorV1 descriptor, TimeProvider time, IReadOnlyCollection<AiCommandEffectV1> effects)
        : TestCommandTool(descriptor, time), IAiOwnerCommandEffectsV1
    {
        public IReadOnlyCollection<AiCommandEffectV1> Effects { get; } = effects;
    }

    private sealed class UndeclaredCommandTool(AiToolDescriptorV1 descriptor, TimeProvider time) : TestCommandTool(descriptor, time);

    private sealed class InMemoryLedger : IAiCommandLedgerV1
    {
        private readonly Dictionary<(Guid CompanyId, Guid PreviewId), AiCommandRecordV1> records = [];

        public List<AiCommandLedgerEventV1> Events { get; } = [];

        public bool IsAvailable => true;

        public Task<bool> TryAddAsync(AiCommandRecordV1 record, IReadOnlyList<AiCommandLedgerEventV1> events, CancellationToken cancellationToken = default)
        {
            if (!records.TryAdd((record.CompanyId, record.PreviewId), record))
            {
                return Task.FromResult(false);
            }

            Events.AddRange(events);
            return Task.FromResult(true);
        }

        public Task<AiCommandRecordV1?> GetAsync(Guid companyId, Guid previewId, CancellationToken cancellationToken = default) =>
            Task.FromResult(records.TryGetValue((companyId, previewId), out var record) ? record : null);

        public Task<bool> TryReplaceAsync(AiCommandRecordV1 next, long expectedVersion, AiCommandLedgerEventV1 auditEvent, CancellationToken cancellationToken = default)
        {
            var key = (next.CompanyId, next.PreviewId);
            if (!records.TryGetValue(key, out var current) || current.Version != expectedVersion || next.Version != expectedVersion + 1)
            {
                return Task.FromResult(false);
            }

            records[key] = next;
            Events.Add(auditEvent);
            return Task.FromResult(true);
        }

        public Task<bool> AppendAsync(AiCommandLedgerEventV1 auditEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(auditEvent);
            return Task.FromResult(true);
        }
    }

    private sealed class VerifiedStepUp : IAiStepUpVerifierV1
    {
        public Task<bool> IsVerifiedAsync(AiStepUpChallengeV1 challenge, CancellationToken cancellationToken = default) =>
            Task.FromResult(challenge.CompanyId == Company && challenge.PreviewHash.Length == 64);
    }

    private sealed class FakeAuthority(params string[] granted) : IErpAuthorityRevalidationService
    {
        private readonly HashSet<string> grants = new(granted, StringComparer.Ordinal);

        public string RoleCode { get; set; } = "ROLE-SENTETIK";

        public Guid GrantedCompany { get; set; } = Company;

        public void Revoke(string permission) => grants.Remove(permission);

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(companyId == GrantedCompany && grants.Contains(permission)
                ? new PermissionScope(true, true, new HashSet<string>(StringComparer.Ordinal) { RoleCode }, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
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

    private sealed class TestUser(Guid userId, params Guid[] companies) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = companies.ToHashSet();
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class FixedSessionResolver : IActorSessionBindingResolver
    {
        public Task<string?> ResolveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("v1.session-sentetik");
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "GoymenERP.Api.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
