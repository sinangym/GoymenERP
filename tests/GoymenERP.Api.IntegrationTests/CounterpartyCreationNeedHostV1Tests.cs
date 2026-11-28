using System.Reflection;
using GoymenERP.Api.Contracts;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Work.Domain;

namespace GoymenERP.Api.IntegrationTests;

// Public-port protocol tests, not HTTP/SQL/native Work transaction acceptance. No Counterparty writer is injected at all.
public sealed class CounterpartyCreationNeedHostV1Tests
{
    private static readonly Guid Company = Guid.Parse("94230000-0000-4000-8000-000000000001"), Actor = Guid.Parse("94230000-0000-4000-8000-000000000002"),
        Recipient = Guid.Parse("94230000-0000-4000-8000-000000000003"), Record = Guid.Parse("94230000-0000-4000-8000-000000000004");
    private static readonly DateTimeOffset At = new(2026, 11, 28, 10, 0, 0, TimeSpan.Zero);
    private const string Key = "SENTETIK-counterparty-need", Original = "SENTETIK-need-original";
    private static CounterpartyCreationNeedRequestV1 Request(bool bound = false) => new("SENTETIK cari oluşturma ihtiyacı", "  SENTETIK ihtiyaç açıklaması  ",
        Recipient, At.AddDays(1), bound ? "sales-order-draft" : null, bound ? Record : null);

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Need_delegates_exact_user_fields_key_and_trace_to_the_existing_Work_task_without_a_Counterparty_command(bool bound)
    {
        var r = new Rig(); var q = Request(bound); var answer = await r.Create(q);
        Assert.Equal(q.ToWorkTask(), r.Native.LastBody); Assert.Equal(r.Metadata, r.Native.LastMetadata);
        Assert.Equal((Company, Actor, Recipient, q.DueAtUtc, q.RecordType, q.RecordId),
            (answer.Task.CompanyId, answer.Task.AssignerUserId, answer.Task.AssigneeUserId, answer.Task.DueAtUtc, answer.Task.RecordType, answer.Task.RecordId));
        Assert.Equal(("open", "1", false, Original), (answer.Task.Status, answer.Task.Version, answer.IsReplay, answer.OriginalCorrelationId));
        Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands);
        Assert.All(r.Authority.Permissions, x => Assert.Equal(WorkPermissionManifestV1.TasksAssign, x));
        Assert.Equal(2, r.Holders.Requests.Count);
        Assert.All(r.Holders.Requests, x => Assert.Equal(new UserPermissionHolderRequestV1(Company, Actor.ToString("D"), Recipient, ErpPermissions.CounterpartyPartiesCreate), x));
    }

    [Theory] [InlineData("unauthenticated", 401)] [InlineData("unresolved", 403)] [InlineData("member", 403)]
    [InlineData("assign", 403)] [InlineData("scope", 403)] [InlineData("trace", 503)]
    public async Task Missing_actor_company_assign_or_trace_fails_before_recipient_or_task_IO(string fault, int status)
    {
        var r = new Rig(); if (fault == "unauthenticated") r.User.Authenticated = false; if (fault == "unresolved") r.Company.Id = Guid.Empty;
        if (fault == "member") r.User.Member = false; if (fault == "assign") r.Authority.Granted = false; if (fault == "scope") r.Authority.CompanyWide = false;
        if (fault == "trace") r.Host = r.Build(new CorrelationContext());
        Assert.Equal(status, (await Assert.ThrowsAsync<WorkCollaborationException>(() => r.Create(Request()))).Status);
        Assert.Empty(r.Holders.Requests); Assert.Equal(0, r.Native.Calls); Assert.Equal(0, r.Native.Effects);
    }

    [Theory] [InlineData(UserDirectoryOutcomeV1.Found, false, 403)] [InlineData(UserDirectoryOutcomeV1.Denied, false, 403)]
    [InlineData(UserDirectoryOutcomeV1.Unavailable, false, 503)]
    public async Task Recipient_without_current_companywide_create_permission_or_unavailable_owner_never_creates_a_task(UserDirectoryOutcomeV1 outcome, bool holds, int status)
    {
        var r = new Rig(); r.Holders.Outcome = outcome; r.Holders.Granted = holds;
        Assert.Equal(status, (await Assert.ThrowsAsync<WorkCollaborationException>(() => r.Create(Request()))).Status);
        Assert.Single(r.Holders.Requests); Assert.Equal(0, r.Native.Calls); Assert.Equal(0, r.Native.Effects);
    }

    [Theory] [InlineData("actor")] [InlineData("company")] [InlineData("membership")] [InlineData("cancel")] [InlineData("assign")]
    public async Task Normally_completing_recipient_await_cannot_dispatch_after_context_cancel_or_assign_loss(string fault)
    {
        var r = new Rig(); using var cts = new CancellationTokenSource();
        r.Holders.AfterRead = () => r.Lose(fault, cts);
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => r.Create(Request(), cts.Token));
        else Assert.Equal(403, (await Assert.ThrowsAsync<WorkCollaborationException>(() => r.Create(Request(), cts.Token))).Status);
        Assert.Equal(0, r.Native.Calls); Assert.Equal(0, r.Native.Effects);
    }

    [Theory] [InlineData("assign")] [InlineData("recipient")] [InlineData("actor")] [InlineData("company")] [InlineData("membership")] [InlineData("cancel")]
    public async Task Postdispatch_denial_is_unknown_and_the_same_original_key_body_reconciles_one_retained_task(string fault)
    {
        var r = new Rig(); var q = Request(); using var cts = new CancellationTokenSource(); r.Native.AfterEffect = () => r.Lose(fault, cts);
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => r.Create(q, cts.Token));
        else Assert.Equal(503, (await Assert.ThrowsAsync<WorkCollaborationException>(() => r.Create(q, cts.Token))).Status);
        Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands);
        r.Native.AfterEffect = null; r.Restore(); r.SetTrace("SENTETIK-need-reconcile"); var replay = await r.Create(q);
        Assert.True(replay.IsReplay); Assert.Equal(Original, replay.OriginalCorrelationId); Assert.Equal(1, r.Native.Effects);
        Assert.Equal(q.ToWorkTask(), r.Native.LastBody); Assert.Equal(Key, r.Native.LastMetadata!.IdempotencyKey);
    }

    [Theory] [InlineData(401)] [InlineData(403)]
    public async Task A_native_Work_authority_refusal_after_dispatch_cannot_be_advertised_as_a_proved_uncommitted_need(int status)
    {
        var r = new Rig(); var q = Request(); r.Native.AfterEffect = () => throw new WorkCollaborationException(status, "SENTETIK-native-release");
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkCollaborationException>(() => r.Create(q))).Status); Assert.Equal(1, r.Native.Effects);
        r.Native.AfterEffect = null; Assert.True((await r.Create(q)).IsReplay); Assert.Equal(1, r.Native.Effects);
    }

    [Fact]
    public async Task Last_normally_completed_recipient_read_cannot_release_a_retained_task_after_assign_permission_revocation()
    {
        var r = new Rig(); var q = Request(); r.Holders.AfterRead = () => { if (r.Holders.Requests.Count == 2) r.Authority.Granted = false; };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkCollaborationException>(() => r.Create(q))).Status);
        Assert.Equal(1, r.Native.Effects); Assert.Single(r.Native.Commands);
        r.Holders.AfterRead = null; r.Authority.Granted = true; r.SetTrace("SENTETIK-final-read-recovery");
        var replay = await r.Create(q); Assert.True(replay.IsReplay); Assert.Equal(Original, replay.OriginalCorrelationId); Assert.Equal(1, r.Native.Effects);
    }

    [Theory] [InlineData("company")] [InlineData("assigner")] [InlineData("recipient")] [InlineData("title")] [InlineData("details")]
    [InlineData("deadline")] [InlineData("record")] [InlineData("version")] [InlineData("correlation")] [InlineData("missing")]
    public async Task Unbound_native_reply_is_unknown_then_original_key_recovers_without_rewriting_the_payload(string fault)
    {
        var r = new Rig(); var q = Request(true); r.Native.Change = a => fault switch
        {
            "company" => a with { Task = a.Task with { CompanyId = Guid.NewGuid() } }, "assigner" => a with { Task = a.Task with { AssignerUserId = Recipient } },
            "recipient" => a with { Task = a.Task with { AssigneeUserId = Actor } }, "title" => a with { Task = a.Task with { Title = "SENTETIK changed" } },
            "details" => a with { Task = a.Task with { Details = null } }, "deadline" => a with { Task = a.Task with { DueAtUtc = q.DueAtUtc.AddTicks(1) } },
            "record" => a with { Task = a.Task with { RecordId = Guid.NewGuid() } }, "version" => a with { Task = a.Task with { Version = "01" } },
            "correlation" => a with { OriginalCorrelationId = "SENTETIK-other" }, _ => a with { Task = null! },
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkCollaborationException>(() => r.Create(q))).Status); Assert.Equal(1, r.Native.Effects);
        r.Native.Change = null; r.SetTrace("SENTETIK-reply-recovery"); Assert.True((await r.Create(q)).IsReplay); Assert.Equal(1, r.Native.Effects);
    }

    [Fact]
    public async Task Native_replay_after_deadline_and_completion_keeps_original_fields_trace_and_one_task()
    {
        var r = new Rig(); var q = Request() with { DueAtUtc = At.AddYears(-1) };
        // The public task port owns deadline admission. This retained protocol answer deliberately models a replay after its deadline.
        r.Native.Commands[Key] = new(q.ToWorkTask(), new(new(Guid.NewGuid(), Company, null, null, q.Title, q.Details, Actor, "SENTETIK assigner", Recipient,
            "SENTETIK recipient", q.DueAtUtc, "completed", "2", At.AddYears(-2), At.AddYears(-1), Recipient, null), false, Original));
        r.SetTrace("SENTETIK-after-deadline"); var answer = await r.Create(q);
        Assert.True(answer.IsReplay); Assert.Equal(("completed", "2", Original), (answer.Task.Status, answer.Task.Version, answer.OriginalCorrelationId));
        Assert.Equal(0, r.Native.Effects); Assert.Single(r.Native.Commands);
        Assert.Equal(409, (await Assert.ThrowsAsync<WorkCollaborationException>(() => r.Create(q with { Details = "SENTETIK replacement" }))).Status);
        Assert.Single(r.Native.Commands);
    }

    [Fact]
    public async Task Recipient_admission_is_explicitly_not_an_atomic_participant_in_the_existing_Work_task_transaction()
    {
        var r = new Rig(); r.Native.BeforeEffect = () => r.Holders.Granted = false; r.Native.AfterEffect = () => r.Holders.Granted = true;
        var answer = await r.Create(Request());
        Assert.False(r.Native.RecipientGrantedAtEffect); Assert.Equal(1, r.Native.Effects); Assert.Equal("open", answer.Task.Status);
        // Only pre/post checks are established here. This adapter cannot certify a create grant at Work's internal Save/Commit.
        Assert.Equal(2, r.Holders.Requests.Count);
    }

    [Fact]
    public async Task Empty_or_half_bound_record_and_missing_metadata_are_refused_without_task_dispatch_or_defaulting()
    {
        foreach (var q in new[] { Request() with { Title = " " }, Request() with { Details = " " }, Request() with { AssigneeUserId = Guid.Empty },
            Request() with { RecordType = "counterparty" }, Request() with { RecordId = Record }, Request(true) with { RecordId = Guid.Empty } })
        {
            var r = new Rig(); Assert.Equal(422, (await Assert.ThrowsAsync<WorkCollaborationException>(() => r.Create(q))).Status);
            Assert.Equal(0, r.Native.Calls); Assert.Empty(r.Holders.Requests);
        }
        var bad = new Rig(); Assert.Equal(400, (await Assert.ThrowsAsync<WorkCollaborationException>(() =>
            bad.Host.CreateAsync(Request(), bad.Metadata with { CorrelationId = "SENTETIK-foreign-trace" }))).Status);
        Assert.Empty(bad.Authority.Permissions); Assert.Equal(0, bad.Native.Calls);
    }

    private sealed class Rig
    {
        internal readonly MutableCompany Company = new(); internal readonly MutableUser User = new(); internal readonly Authority Authority = new();
        internal readonly Holders Holders = new(); internal readonly Protocol Native; internal readonly IWorkCollaborationServiceV1 Tasks;
        private CorrelationContext correlation = new(); internal CounterpartyCreationNeedHostV1 Host; internal WorkCommandMetadataV1 Metadata = new(Key, Original, "SENTETIK-cause");
        internal Rig()
        {
            correlation.Set(Original, Metadata.CausationId, "Web", null, null); Native = new(this); Tasks = DispatchProxy.Create<IWorkCollaborationServiceV1, TaskProxy>();
            ((TaskProxy)(object)Tasks).CreateTask = Native.Create; Host = Build(correlation);
        }
        internal CounterpartyCreationNeedHostV1 Build(ICorrelationContext trace) => new(Company, User, trace, Authority, Holders, Tasks);
        internal Task<WorkTaskResponseV1> Create(CounterpartyCreationNeedRequestV1 q, CancellationToken token = default) => Host.CreateAsync(q, Metadata, token);
        internal void SetTrace(string trace) { correlation = new(); correlation.Set(trace, Metadata.CausationId, "Web", null, null); Metadata = Metadata with { CorrelationId = trace }; Host = Build(correlation); }
        internal void Restore() { User.Authenticated = true; User.ActorId = Actor; User.Member = true; Company.Id = CounterpartyCreationNeedHostV1Tests.Company; Authority.Granted = true; Holders.Granted = true; }
        internal void Lose(string fault, CancellationTokenSource cts)
        { if (fault == "assign") Authority.Granted = false; if (fault == "recipient") Holders.Granted = false; if (fault == "actor") User.ActorId = Recipient;
            if (fault == "company") Company.Id = Guid.NewGuid(); if (fault == "membership") User.Member = false; if (fault == "cancel") cts.Cancel(); }
    }
    public class TaskProxy : DispatchProxy
    {
        internal Func<CreateWorkTaskRequestV1, WorkCommandMetadataV1, CancellationToken, Task<WorkTaskResponseV1>> CreateTask = (_, _, _) => throw new NotSupportedException();
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name == nameof(IWorkCollaborationServiceV1.CreateTaskAsync)
            ? CreateTask((CreateWorkTaskRequestV1)args![0]!, (WorkCommandMetadataV1)args[1]!, (CancellationToken)args[2]!) : throw new NotSupportedException("No unrelated task/read/native Party command");
    }
    private sealed record Saved(CreateWorkTaskRequestV1 Body, WorkTaskResponseV1 Answer);
    private sealed class Protocol(Rig rig)
    {
        internal readonly Dictionary<string, Saved> Commands = []; internal int Calls, Effects; internal CreateWorkTaskRequestV1? LastBody;
        internal WorkCommandMetadataV1? LastMetadata; internal Action? BeforeEffect, AfterEffect; internal bool RecipientGrantedAtEffect;
        internal Func<WorkTaskResponseV1, WorkTaskResponseV1>? Change;
        internal Task<WorkTaskResponseV1> Create(CreateWorkTaskRequestV1 q, WorkCommandMetadataV1 m, CancellationToken token)
        {
            Calls++; LastBody = q; LastMetadata = m; token.ThrowIfCancellationRequested(); var replay = Commands.TryGetValue(m.IdempotencyKey, out var saved);
            if (replay && saved!.Body != q) throw new WorkCollaborationException(409, WorkCollaborationProblemCodesV1.SameKeyPayloadMismatch);
            if (!replay)
            {
                BeforeEffect?.Invoke(); RecipientGrantedAtEffect = rig.Holders.Granted;
                var view = new WorkTaskViewV1(Guid.NewGuid(), CounterpartyCreationNeedHostV1Tests.Company, q.RecordType, q.RecordId, q.Title, q.Details, Actor,
                    "SENTETIK assigner", q.AssigneeUserId, "SENTETIK recipient", q.DueAtUtc.ToUniversalTime(), "open", "1", At, null, null, null);
                saved = new(q, new(view, false, m.CorrelationId)); Commands.Add(m.IdempotencyKey, saved); Effects++; AfterEffect?.Invoke();
            }
            var answer = saved!.Answer with { IsReplay = replay }; return Task.FromResult(Change?.Invoke(answer) ?? answer);
        }
    }
    private sealed class MutableCompany : ICompanyContext { internal Guid Id = Company; public bool IsResolved => Id != Guid.Empty; public Guid CompanyId => Id; public void Set(Guid companyId) => Id = companyId; }
    private sealed class MutableUser : ICurrentUser
    { internal Guid ActorId = Actor; internal bool Authenticated = true, Member = true; public bool IsAuthenticated => Authenticated; public string UserId => ActorId.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([Company]); public bool CanAccessCompany(Guid id) => Member && id == Company; }
    private sealed class Authority : IErpAuthorityRevalidationService
    {
        internal bool Granted = true, CompanyWide = true; internal readonly List<string> Permissions = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken token = default)
        { Permissions.Add(permission); Assert.Equal(Company, companyId); return Task.FromResult(new PermissionScope(Granted, CompanyWide, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
    private sealed class Holders : IUserPermissionHolderReadPortV1
    {
        internal bool Granted = true; internal UserDirectoryOutcomeV1 Outcome = UserDirectoryOutcomeV1.Found; internal Action? AfterRead;
        internal readonly List<UserPermissionHolderRequestV1> Requests = [];
        public Task<UserPermissionHolderAnswerV1> HoldsAsync(UserPermissionHolderRequestV1 request, CancellationToken token = default)
        { Requests.Add(request); var answer = new UserPermissionHolderAnswerV1(Outcome, Granted); AfterRead?.Invoke(); return Task.FromResult(answer); }
    }
}
