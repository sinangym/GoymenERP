using GoymenERP.Api.Ai;
using GoymenERP.Api.Ai.Pilot;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Ai;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.IntegrationTests;

// Admission source tests only: no actual dispatch/egress/account/provider or approved pilot data.
public sealed class AiExternalReadPilotAdmissionTests
{
    [Theory]
    [InlineData("absent")] [InlineData("unapproved")] [InlineData("unreviewed-data")]
    [InlineData("pin")] [InlineData("field-policy")] [InlineData("sensitive")]
    [InlineData("never-egress")] [InlineData("confidential-field")] [InlineData("unregistered")]
    public async Task Missing_approval_pin_or_nonsensitive_field_policy_fails_closed(string fault)
    {
        var h = new Harness(); var policy = h.Policy.Value!; var tool = policy.Tools[0];
        tool = fault switch {
            "unregistered" => tool with { ToolId = "test.unknown.v1" },
            "pin" => tool with { DescriptionSha256 = new string('0', 64) },
            "field-policy" => tool with { FieldPolicyRevision = "other" },
            "unreviewed-data" => tool with { NonSensitiveDataApproved = false },
            "sensitive" => tool with { OutputFields = [new("count", AiDataClassV1.Internal, AiSensitiveDataTypeV1.Price)] },
            "never-egress" => tool with { OutputFields = [new("count", AiDataClassV1.Internal, AiSensitiveDataTypeV1.None, true)] },
            "confidential-field" => tool with { OutputFields = [new("count", AiDataClassV1.Confidential, AiSensitiveDataTypeV1.None)] }, _ => tool };
        h.Policy.Value = fault == "absent" ? null : policy with { Approved = fault != "unapproved", Tools = [tool, policy.Tools[1], policy.Tools[2]] };
        Assert.False(await h.Check()); Assert.Equal(0, h.Audit.Calls); Assert.Equal(0, h.Tool.Reads);
    }

    [Theory]
    [InlineData(AiToolRiskClassV1.Write, AiDataClassV1.Internal)]
    [InlineData(AiToolRiskClassV1.Read, AiDataClassV1.Confidential)]
    public async Task Pilot_policy_cannot_admit_write_or_sensitive_tools(AiToolRiskClassV1 risk, AiDataClassV1 dataClass)
    {
        var h = new Harness(risk, dataClass);
        Assert.False(await h.Check()); Assert.Equal(0, h.Tool.Reads); Assert.Equal(0, h.Audit.Calls);
    }

    [Theory]
    [InlineData("actor")] [InlineData("company")] [InlineData("session")]
    [InlineData("authority")] [InlineData("policy")]
    public async Task Drift_during_audit_cannot_release_admission(string fault)
    {
        var h = new Harness();
        h.Audit.AfterWrite = () => {
            if (fault == "actor") h.User.Actor = "other";
            if (fault == "company") h.Company.Id = Guid.NewGuid();
            if (fault == "session") h.Session.Value = "new-session";
            if (fault == "authority") h.Authority.Allowed = false;
            if (fault == "policy") h.Policy.Value = h.Policy.Value! with { Version = 2 };
        };
        Assert.False(await h.Check(release: true)); Assert.Equal(1, h.Audit.Calls); Assert.Equal(0, h.Tool.Reads);
    }

    [Fact]
    public async Task Audit_failure_closes_even_when_writer_says_fail_open()
    {
        var h = new Harness(); h.Audit.Recorded = false;
        Assert.False(await h.Check()); Assert.Equal(0, h.Tool.Reads);
    }

    [Fact]
    public async Task Explicit_metadata_check_is_audited_without_dispatch_or_reclassification()
    {
        var h = new Harness();
        Assert.True(await h.Check()); Assert.True(await h.Check(release: true));
        Assert.Equal(0, h.Tool.Reads); Assert.Equal(2, h.Audit.Calls);
        Assert.Equal(AiDataClassV1.Internal, h.Audit.Last!.AiDataClass);
        Assert.Empty(SecurityEventV1Validator.Validate(h.Audit.Last));
        Assert.Equal(SecurityEventPhasesV1.Completed, h.Audit.Last.Phase);
    }

    private sealed class Harness
    {
        public Tool Tool { get; }
        public Policy Policy { get; } = new(); public Company Company { get; } = new(); public User User { get; } = new();
        public Session Session { get; } = new(); public Authority Authority { get; } = new(); public Audit Audit { get; } = new();
        private AiExternalReadPilotAdmissionV1 Service { get; }
        private AiServerContextV1 Context { get; }
        public Harness(AiToolRiskClassV1 risk = AiToolRiskClassV1.Read, AiDataClassV1 dataClass = AiDataClassV1.Internal)
        {
            Tool = new(risk, dataClass);
            Tool[] tools = [Tool, new(AiToolRiskClassV1.Read, AiDataClassV1.Internal, "test.other-one.v1"),
                new(AiToolRiskClassV1.Read, AiDataClassV1.Internal, "test.other-two.v1")];
            var options = Options.Create(new AiRuntimeOptions { AllowedTools = tools.Select(x => new AiAllowedToolOptions {
                ToolId = x.Descriptor.ToolId, Version = "1", DescriptionSha256 = AiToolDescriptionPinV1.Compute(x.Descriptor) }).ToList() });
            Policy.Value = new(Company.Id, User.Actor, "SENTETIK-pilot", 1, true,
                tools.Select(x => new AiExternalReadPilotToolV1(x.Descriptor.ToolId, "1", AiToolDescriptionPinV1.Compute(x.Descriptor),
                    x.Descriptor.Contract.FieldPolicy, true, [new("count", AiDataClassV1.Internal, AiSensitiveDataTypeV1.None)])).ToArray());
            Context = new(Company.Id, User.Actor, Session.Value!, "scope", "authority", "SENTETIK-correlation", null, null, null);
            Service = new(new AiToolCatalog(tools, options), Policy, Company, User, Session, Authority, Audit, TimeProvider.System);
        }
        public Task<bool> Check(bool release = false) => Service.CheckAsync(Context, Tool.Descriptor.ToolId, "1", release);
    }
    private sealed class Tool : IAiOwnerReadToolV1
    {
        public AiToolDescriptorV1 Descriptor { get; }
        public int Reads { get; private set; }
        public Tool(AiToolRiskClassV1 risk, AiDataClassV1 dataClass, string id = "test.pilot.v1") => Descriptor = new(id, "1", "Test", risk, dataClass,
            ["test.read"], [], risk == AiToolRiskClassV1.Write ? "test.command.v1" : null,
            new("input@1", "output@1", "fields@1", "limit@1", "catalog@1", "auth@1", "source@1", "redact@1"));
        public Task<AiReadResultV1> ReadAsync(AiToolInputV1 input, AiServerContextV1 context, CancellationToken cancellationToken = default)
        { Reads++; throw new InvalidOperationException("Admission must not dispatch"); }
    }
    private sealed class Policy : IAiExternalReadPilotPolicyV1
    {
        public AiExternalReadPilotPolicyV1? Value;
        public Task<AiExternalReadPilotPolicyV1?> ReadApprovedAsync(Guid companyId, string actorId, CancellationToken cancellationToken) => Task.FromResult(Value);
    }
    private sealed class Company : ICompanyContext
    { public Guid Id = Guid.NewGuid(); public bool IsResolved => true; public Guid CompanyId => Id; public void Set(Guid id) => Id = id; }
    private sealed class User : ICurrentUser
    { public string Actor = "SENTETIK-user"; public bool IsAuthenticated => true; public string? UserId => Actor;
      public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>(); public bool CanAccessCompany(Guid id) => true; }
    private sealed class Session : IActorSessionBindingResolver
    { public string? Value = "SENTETIK-session"; public Task<string?> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value); }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public bool Allowed = true;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionScope(Allowed, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
    }
    private sealed class Audit : ISecurityEventWriterV1
    {
        public bool Recorded = true; public int Calls; public Action? AfterWrite; public SecurityEventV1? Last;
        public Task<SecurityEventWriteResultV1> WriteAsync(SecurityEventV1 securityEvent, CancellationToken cancellationToken = default)
        { Calls++; Last = securityEvent; AfterWrite?.Invoke(); return Task.FromResult(Recorded
            ? SecurityEventWriteResultV1.Written(SecurityEventWriteFailureMode.FailOpenWithAlarm)
            : SecurityEventWriteResultV1.NotWritten(SecurityEventWriteFailureMode.FailOpenWithAlarm, SecurityEventWriteFailureKindsV1.StoreUnavailable)); }
    }
}
