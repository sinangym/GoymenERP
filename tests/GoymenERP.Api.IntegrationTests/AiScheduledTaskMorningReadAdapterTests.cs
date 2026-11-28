using System.Text.Json;
using GoymenERP.Api.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using GoymenERP.Api.Ai.ScheduledTasks;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.AiKnowledge.Domain;
using GoymenERP.Modules.Work.Domain;

namespace GoymenERP.Api.IntegrationTests;

// Controlled adapter contract tests. Work's own DB/grant tests remain separate; no SQL/worker/DI acceptance.
public sealed class AiScheduledTaskMorningReadAdapterTests
{
    [Fact]
    public void Runtime_registers_only_the_scoped_read_adapter_without_a_scheduled_task_worker()
    {
        var services = new ServiceCollection();
        services.AddAiRuntime(new ConfigurationBuilder().Build());
        var registration = Assert.Single(services.Where(x => x.ServiceType == typeof(IAiScheduledTaskMorningReadV1)));
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
        Assert.Equal(typeof(AiScheduledTaskMorningReadAdapterV1), registration.ImplementationType);
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IAiScheduledTaskServiceV1));
    }
    private static readonly Guid Company = TestCompanyIds.CompanyA;
    private static readonly DateTimeOffset Slot = new(2026, 11, 28, 6, 0, 0, TimeSpan.Zero);
    [Fact]
    public async Task Existing_owner_receipt_is_projected_without_counts_or_an_extra_awaited_callback()
    {
        var f = new Fixture();
        var answer = await f.Adapter.ReadCurrentAsync();
        Assert.NotNull(answer);
        Assert.Equal(new AiScheduledTaskMorningReadV1(Company, "settings-1", Slot, f.Owner.Result!.DigestId,
            AiScheduledTaskMorningReadOutcomeV1.Available), answer);
        Assert.Equal(1, f.Owner.Reads);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(answer, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(new[] { "companyId", "scheduleVersion", "slotUtc", "sourceReceiptId", "outcome" },
            json.RootElement.EnumerateObject().Select(x => x.Name));
        Assert.DoesNotContain("counts", json.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SENTETIK-private-count-kind", json.RootElement.GetRawText(), StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("missing-owner")] [InlineData("missing-digest")] [InlineData("foreign-company")]
    [InlineData("empty-receipt")] [InlineData("non-utc")] [InlineData("blank-version")]
    [InlineData("version-control")] [InlineData("version-padding")]
    public async Task Missing_or_unbound_owner_facts_never_invent_a_receipt_or_slot(string fault)
    {
        var f = new Fixture();
        f.Owner.Result = fault switch
        {
            "missing-digest" => null,
            "foreign-company" => f.Owner.Result! with { CompanyId = TestCompanyIds.CompanyB },
            "empty-receipt" => f.Owner.Result! with { DigestId = Guid.Empty },
            "non-utc" => f.Owner.Result! with { SlotUtc = Slot.ToOffset(TimeSpan.FromHours(3)) },
            "blank-version" => f.Owner.Result! with { ScheduleVersion = "" },
            "version-control" => f.Owner.Result! with { ScheduleVersion = "settings\n1" },
            "version-padding" => f.Owner.Result! with { ScheduleVersion = " settings-1" },
            _ => f.Owner.Result
        };
        var adapter = fault == "missing-owner" ? new AiScheduledTaskMorningReadAdapterV1(f.Company, f.User) : f.Adapter;
        Assert.Null(await adapter.ReadCurrentAsync());
        Assert.Equal(fault == "missing-owner" ? 0 : 1, f.Owner.Reads);
    }
    [Theory]
    [InlineData(401)] [InlineData(403)] [InlineData(503)]
    public async Task Owner_denial_and_unavailability_are_not_successful_reads(int status)
    {
        var f = new Fixture(); f.Owner.Failure = new WorkCollaborationException(status, "SENTETIK-owner-refusal");
        var answer = await f.Adapter.ReadCurrentAsync();
        if (status == 503) Assert.Null(answer);
        else
        {
            Assert.NotNull(answer); Assert.Equal(AiScheduledTaskMorningReadOutcomeV1.Denied, answer.Outcome);
            Assert.Null(answer.SourceReceiptId);
        }
    }
    [Theory]
    [InlineData("actor")] [InlineData("company")] [InlineData("membership")] [InlineData("logout")]
    public async Task Token_ignoring_owner_cannot_release_after_actual_scope_drift(string fault)
    {
        var f = new Fixture();
        f.Owner.AfterRead = () =>
        {
            if (fault == "actor") f.User.Actor = "SENTETIK-other";
            if (fault == "company") f.Company.Set(TestCompanyIds.CompanyB);
            if (fault == "membership") f.User.Member = false;
            if (fault == "logout") f.User.Authenticated = false;
        };
        var failure = await Assert.ThrowsAsync<AiScheduledTaskException>(() => f.Adapter.ReadCurrentAsync());
        Assert.Equal(403, failure.Status); Assert.Equal(1, f.Owner.Reads);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Cancellation_is_checked_before_read_and_after_normal_completion(bool duringRead)
    {
        var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        if (duringRead) f.Owner.AfterRead = cancellation.Cancel; else cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Adapter.ReadCurrentAsync(cancellation.Token));
        Assert.Equal(duringRead ? 1 : 0, f.Owner.Reads);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Missing_actual_authentication_or_company_membership_never_calls_owner(bool authenticated)
    {
        var f = new Fixture(); f.User.Authenticated = authenticated; f.User.Member = false;
        var failure = await Assert.ThrowsAsync<AiScheduledTaskException>(() => f.Adapter.ReadCurrentAsync());
        Assert.Equal(authenticated ? 403 : 401, failure.Status); Assert.Equal(0, f.Owner.Reads);
    }
    private sealed class Fixture
    {
        internal readonly MutableCompany Company = new(); internal readonly User User = new(); internal readonly Owner Owner = new();
        internal Fixture() { Company.Set(AiScheduledTaskMorningReadAdapterTests.Company); }
        internal AiScheduledTaskMorningReadAdapterV1 Adapter => new(Company, User, Owner);
    }
    private sealed class MutableCompany : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty; public Guid CompanyId { get; private set; }
        public void Set(Guid id) => CompanyId = id;
    }
    private sealed class User : ICurrentUser
    {
        internal string Actor = "SENTETIK-current-owner"; internal bool Authenticated = true; internal bool Member = true;
        public bool IsAuthenticated => Authenticated; public string UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds => Member ? new HashSet<Guid> { Company } : new HashSet<Guid>();
        public bool CanAccessCompany(Guid id) => Member && id == Company;
    }
    private sealed class Owner : IWorkMorningSummaryReadV1
    {
        internal int Reads; internal Action? AfterRead; internal Exception? Failure;
        internal WorkMorningSummaryV1? Result = new(Company, Guid.NewGuid(), Slot, "settings-1", [new("SENTETIK-private-count-kind", 7)]);
        public async Task<WorkMorningSummaryV1?> ReadAsync(CancellationToken cancellationToken = default)
        {
            Reads++; await Task.Yield(); AfterRead?.Invoke();
            if (Failure is { } failure) throw failure;
            return Result;
        }
    }
}
