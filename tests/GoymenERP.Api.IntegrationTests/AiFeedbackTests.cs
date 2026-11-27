using System.Text.Json;
using GoymenERP.Api.Ai;
using GoymenERP.Api.Ai.Feedback;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.AiKnowledge.Domain;
using GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.IntegrationTests;

public sealed class AiFeedbackTests
{
    private static readonly Guid Company = TestCompanyIds.CompanyA;
    private static readonly Guid Actor = Guid.Parse("aaaaaaaa-1000-4000-8000-000000000001");
    private static readonly Guid Answer = Guid.Parse("aaaaaaaa-1000-4000-8000-000000000002");
    private static readonly DateTimeOffset At = new(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);
    private static AiFeedbackAppendV1 Command() => new(Company, Answer, Actor, 0, AiFeedbackKindV1.Accepted, null, "SENTETIK-key", "SENTETIK-correlation");

    [Theory]
    [InlineData(AiFeedbackKindV1.Accepted, true)] [InlineData(AiFeedbackKindV1.Corrected, false)]
    [InlineData(AiFeedbackKindV1.Rejected, false)] [InlineData(AiFeedbackKindV1.Reverted, false)]
    public void Reasons_are_the_approved_closed_list_with_no_free_text(AiFeedbackKindV1 kind, bool nullAllowed)
    {
        Assert.Equal(nullAllowed, AiFeedbackReasonsV1.Valid(kind, null));
        foreach (var code in new[] { AiFeedbackReasonsV1.IncorrectInformation, AiFeedbackReasonsV1.IncorrectOrMissingSource,
            AiFeedbackReasonsV1.IncompleteAnswer, AiFeedbackReasonsV1.OutdatedInformation, AiFeedbackReasonsV1.Other })
            Assert.True(AiFeedbackReasonsV1.Valid(kind, code));
        foreach (var code in new[] { "", "SENTETIK explanation", "other ", "Other", "123.45", "other\n" })
            Assert.False(AiFeedbackReasonsV1.Valid(kind, code));
        Assert.False(AiFeedbackReasonsV1.Valid((AiFeedbackKindV1)0, AiFeedbackReasonsV1.Other));
    }
    [Fact]
    public void Fingerprints_bind_the_command_while_retries_keep_original_correlation_and_keys_are_not_plaintext_storage()
    {
        var command = Command(); var hash = AiFeedbackRulesV1.Fingerprint(command);
        Assert.Equal(hash, AiFeedbackRulesV1.Fingerprint(command with { CorrelationId = "SENTETIK-new-correlation" }));
        Assert.NotEqual(hash, AiFeedbackRulesV1.Fingerprint(command with { ExpectedVersion = 1 }));
        Assert.NotEqual(hash, AiFeedbackRulesV1.Fingerprint(command with { ReasonCode = AiFeedbackReasonsV1.Other }));
        Assert.NotEqual(hash, AiFeedbackRulesV1.Fingerprint(command with { AnswerId = Guid.NewGuid() }));
        Assert.True(AiAnswerReceiptRulesV1.Hash(AiFeedbackRulesV1.KeyHash(command.IdempotencyKey)));
        Assert.NotEqual(command.IdempotencyKey, AiFeedbackRulesV1.KeyHash(command.IdempotencyKey));
        Assert.False(AiFeedbackRulesV1.Valid(command with { ExpectedVersion = long.MaxValue }));
        Assert.False(AiFeedbackRulesV1.Valid(command with { ActorId = Guid.Empty }));
    }
    [Fact]
    public void The_owner_model_is_an_immutable_company_bound_version_chain_with_separate_identity_and_no_content()
    {
        using var db = new AiKnowledgeDbContext(new DbContextOptionsBuilder<AiKnowledgeDbContext>()
            .UseSqlServer("Server=unused;Database=unused").Options, new CompanyContext());
        var model = db.GetService<IDesignTimeModel>().Model;
        var events = model.FindEntityType(typeof(AiFeedbackEventRow))!; var identities = model.FindEntityType(typeof(AiFeedbackIdentityRow))!;
        Assert.Equal(new[] { "CompanyId", "AnswerId", "Version" }, events.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.NotEmpty(events.GetDeclaredQueryFilters()); Assert.NotEmpty(identities.GetDeclaredQueryFilters());
        Assert.Equal(AiKnowledgeDbContext.FeedbackEventCheck, Assert.Single(events.GetCheckConstraints()).Sql);
        Assert.Equal(AiKnowledgeDbContext.FeedbackIdentityCheck, Assert.Single(identities.GetCheckConstraints()).Sql);
        Assert.Contains(events.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(AiAnswerReceiptRow));
        Assert.Contains(events.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(AiFeedbackEventRow));
        Assert.Contains(identities.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(["CompanyId", "ActorId", "IdempotencyKeySha256"]));
        foreach (var name in new[] { "ActorId", "CorrelationId", "Text", "Question", "AnswerText", "CorrectionText", "IdempotencyKey", "Amount", "Name" })
            Assert.Null(events.FindProperty(name));
        Assert.NotNull(identities.FindProperty("ActorId")); Assert.Null(identities.FindProperty("Text"));
    }
    [Theory]
    [InlineData("0", "Accepted", null, true)] [InlineData("0", "Rejected", "other", true)]
    [InlineData("00", "Accepted", null, false)] [InlineData("-1", "Accepted", null, false)]
    [InlineData("9223372036854775807", "Accepted", null, false)] [InlineData("0", "1", null, false)]
    [InlineData("0", "Rejected", null, false)] [InlineData("0", "Corrected", "free text", false)]
    public async Task The_api_has_only_typed_feedback_and_canonical_versions(string version, string kind, string? reason, bool valid)
    {
        var rig = new Rig();
        var result = await rig.Service().RecordAsync(Answer, new(version, kind, reason), "SENTETIK-key");
        Assert.Equal(valid ? AiFeedbackApiStatusV1.Succeeded : AiFeedbackApiStatusV1.Invalid, result.Status);
        Assert.Equal(valid ? 1 : 0, rig.Store.Calls);
        if (valid) Assert.Equal("1", result.Receipt!.Version);
    }
    [Fact]
    public void Feedback_wire_rejects_prompt_correction_text_company_or_actor_fields()
    {
        foreach (var field in new[] { "question", "answer", "correctionText", "companyId", "actorId" })
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AiFeedbackRequestV1>(
                "{\"expectedVersion\":\"0\",\"kind\":\"Accepted\",\"reasonCode\":null,\"" + field + "\":\"SENTETIK\"}", new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
    [Fact]
    public async Task A_late_authority_loss_after_a_committed_feedback_never_releases_a_success_receipt()
    {
        var rig = new Rig(); rig.Store.AfterCommit = () => rig.Authority.Allowed = false;
        var result = await rig.Service().RecordAsync(Answer, new("0", "Accepted", null), "SENTETIK-key");
        Assert.True(rig.Store.Committed); Assert.Equal(AiFeedbackApiStatusV1.Refused, result.Status); Assert.Null(result.Receipt);
    }
    [Theory]
    [InlineData("disabled")] [InlineData("owner")] [InlineData("permission")] [InlineData("company")] [InlineData("session")]
    public async Task No_missing_runtime_owner_company_authority_or_session_is_treated_as_a_recorded_observation(string fault)
    {
        var rig = new Rig();
        if (fault == "disabled") rig.Options.Enabled = false;
        if (fault == "permission") rig.Authority.Allowed = false;
        if (fault == "company") rig.Company.Set(TestCompanyIds.CompanyB);
        if (fault == "session") rig.Session.Value = null;
        var result = await rig.Service(fault != "owner").RecordAsync(Answer, new("0", "Accepted", null), "SENTETIK-key");
        Assert.Equal(fault == "disabled" ? AiFeedbackApiStatusV1.Disabled : fault == "owner" ? AiFeedbackApiStatusV1.Unavailable : AiFeedbackApiStatusV1.Refused, result.Status);
        Assert.Equal(0, rig.Store.Calls); Assert.Null(result.Receipt);
    }
    [Fact]
    public void Monthly_windows_reuse_the_native_budget_timezone_and_do_not_treat_old_or_future_cohorts_as_zero()
    {
        var window = AiFeedbackAcceptanceCountSourceV1.Window(202611, new(2026, 12, 2, 9, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 21, 0, 0, TimeSpan.Zero), window.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 11, 30, 21, 0, 0, TimeSpan.Zero), window.ExclusiveEndUtc);
        Assert.Equal(window.ExclusiveEndUtc, window.AsOfUtc);
        var now = new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);
        Assert.Equal(now, AiFeedbackAcceptanceCountSourceV1.Window(202611, now).AsOfUtc);
        foreach (var invalid in new[] { 202600, 202613, 202612, 202601 })
            Assert.Throws<InvalidOperationException>(() => AiFeedbackAcceptanceCountSourceV1.Window(invalid, now));
    }
    [Fact]
    public async Task The_count_adapter_uses_management_authority_and_keeps_real_unreviewed_separate_from_absent_measurement()
    {
        var rig = new Rig(); rig.Authority.Permission = GoymenERP.Modules.Settings.Application.Contracts.V1.SettingsAiGatewayPermissionsV1.Manage;
        var counts = await new AiFeedbackAcceptanceCountSourceV1(rig.Store, rig.Company, rig.User, rig.Authority, rig.Session, new Clock())
            .GetMonthAsync(Company, 202611);
        var line = Assert.Single(counts); Assert.Equal(1, line.Accepted); Assert.Equal(2, line.Unreviewed);
        Assert.NotNull(rig.Store.LastWindow); Assert.Equal(At, rig.Store.LastWindow!.AsOfUtc);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AiFeedbackAcceptanceCountSourceV1(null, rig.Company, rig.User,
            rig.Authority, rig.Session, new Clock()).GetMonthAsync(Company, 202611));
        rig.Authority.Allowed = false;
        await Assert.ThrowsAsync<AiFeedbackAuthorityLostException>(() => new AiFeedbackAcceptanceCountSourceV1(rig.Store, rig.Company, rig.User,
            rig.Authority, rig.Session, new Clock()).GetMonthAsync(Company, 202611));
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => At; }
    private sealed class Rig
    {
        public AiRuntimeOptions Options { get; } = new() { Enabled = true };
        public MutableCompany Company { get; } = new(); public User User { get; } = new();
        public Authority Authority { get; } = new(); public Session Session { get; } = new(); public Store Store { get; } = new();
        public AiFeedbackServiceV1 Service(bool owner = true)
        {
            var correlation = new CorrelationContext(); correlation.Set("SENTETIK-correlation", null, "web", null, null);
            return new(Microsoft.Extensions.Options.Options.Create(Options), owner ? Store : null, Company, User, Authority, Session, correlation);
        }
    }
    private sealed class MutableCompany : ICompanyContext
    { public bool IsResolved => true; public Guid CompanyId { get; private set; } = Company; public void Set(Guid id) => CompanyId = id; }
    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true; public string? UserId => Actor.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([Company]); public bool CanAccessCompany(Guid id) => id == Company;
    }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public bool Allowed = true; public string Permission = ErpPermissions.AiPanelUse;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken token = default) =>
            Task.FromResult(Allowed && companyId == Company && permission == Permission
                ? new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) : PermissionScope.Denied);
    }
    private sealed class Session : IActorSessionBindingResolver
    { public string? Value = "SENTETIK-session"; public Task<string?> ResolveAsync(CancellationToken token = default) => Task.FromResult(Value); }
    private sealed class Store : IAiFeedbackPersistenceV1
    {
        public int Calls; public bool Committed; public Action? AfterCommit;
        public async Task<AiFeedbackWriteResultV1> AppendAsync(AiFeedbackAppendV1 request, Func<CancellationToken, Task> fence, CancellationToken token = default)
        {
            Calls++; await fence(token); Committed = true; AfterCommit?.Invoke();
            return new(AiFeedbackWriteStatusV1.Recorded, new(request.CompanyId, request.AnswerId, request.ExpectedVersion + 1,
                request.ExpectedVersion == 0 ? null : request.ExpectedVersion, request.Kind, request.ReasonCode, At, At.AddDays(90)), request.CorrelationId);
        }
        public Task<AiFeedbackCurrentV1?> GetOwnCurrentAsync(Guid companyId, Guid answerId, Guid actorId, Func<CancellationToken, Task> fence, CancellationToken token = default) =>
            Task.FromResult<AiFeedbackCurrentV1?>(new(answerId, 0, null, null));
        public AiFeedbackCountWindowV1? LastWindow;
        public async Task<IReadOnlyList<AiFeedbackMonthCountV1>> GetMonthCountsAsync(Guid companyId, Guid actorId, AiFeedbackCountWindowV1 window, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { await fence(token); LastWindow = window; return [new("Panel", 1, 0, 0, 0, 2, window.AsOfUtc)]; }
    }
}
