using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Accounting.UnitTests;

public sealed partial class AccountingPostingAccountMappingLifecycleV1Tests
{
    private static readonly Guid DecisionCompany = Guid.Parse("b0520000-0000-4000-8000-000000000001");
    private static readonly JsonSerializerOptions MappingDecisionJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Mapping_purpose_read_emits_full_rows_and_distinguishes_prepared_plan_from_current_plan_without_general_read()
    {
        var store = new MappingDecisionMemory(); var authority = new MappingDecisionAuthority(); var callbacks = 0;
        var result = await MappingDecisionService(store, authority).ReadDecisionAsync(1, _ => { callbacks++; return Task.CompletedTask; });
        Assert.Equal(AccountingApiStatus.Ok, result.Status); var detail = Assert.IsType<AccountingPostingAccountMappingDecisionDetailV1>(result.Resource);
        Assert.Equal(1L, detail.Mapping.MappingVersion); Assert.Equal(1L, detail.ExpectedVersion); Assert.Equal(0L, detail.ExpectedActiveMappingVersion);
        Assert.Equal(store.Record.Rows.Count, detail.Mapping.Rows.Count); Assert.Equal("prepared-plan", detail.PreparedAccountPlan.SourceVersion);
        Assert.Equal("current-plan", detail.CurrentActiveAccountPlan.Reference.SourceVersion); Assert.NotEqual(detail.PreparedAccountPlan, detail.CurrentActiveAccountPlan.Reference);
        Assert.Equal(Revenue600, Assert.Single(detail.Mapping.Rows).AccountId); Assert.Equal("SENTETIK current account", Assert.Single(detail.CurrentActiveAccountPlan.Accounts).Name);
        Assert.Equal(AccountingPostingAccountMappingServiceV1.MappingSourceFactsDigest(store.Record), detail.SourceFactsDigest);
        Assert.Equal(AccountingPostingAccountMappingServiceV1.MappingPreviewDigest(detail.Mapping, detail.CurrentActiveAccountPlan, 1, 0), detail.PreviewDigest);
        Assert.All(authority.Permissions, permission => Assert.Equal(AccountingPostingAccountMappingPermissionNamesV1.Activate, permission));
        Assert.Equal(5, callbacks); Assert.Equal(1, store.PreviewReads); Assert.Equal(0, store.Activations); Assert.Empty(store.Commands);
        var wire = JsonSerializer.SerializeToElement(detail, MappingDecisionJson);
        Assert.Equal(7, wire.EnumerateObject().Count()); Assert.Equal(12, wire.GetProperty("mapping").EnumerateObject().Count());
        Assert.Equal(8, wire.GetProperty("mapping").GetProperty("rows")[0].EnumerateObject().Count());
    }

    [Fact]
    public async Task Mapping_purpose_preserves_native_long_identity_and_all_one_thousand_persisted_mapping_rows()
    {
        var store = new MappingDecisionMemory(); var first = store.Record.Rows[0];
        store.Record = store.Record with { MappingVersion = long.MaxValue, Rows = Enumerable.Range(1, AccountingPostingAccountMappingLimitsV1.MaximumRows)
            .Select(index => first with { LineNumber = index, LinePurpose = "line-" + index }).ToArray() };
        var result = await MappingDecisionService(store).ReadDecisionAsync(long.MaxValue, _ => Task.CompletedTask);
        Assert.Equal(AccountingApiStatus.Ok, result.Status); Assert.Equal(long.MaxValue, result.Resource?.Mapping.MappingVersion);
        Assert.Equal(AccountingPostingAccountMappingLimitsV1.MaximumRows, result.Resource!.Mapping.Rows.Count);
        Assert.Equal(store.Record.Rows.Select(row => row.LinePurpose), result.Resource.Mapping.Rows.Select(row => row.LinePurpose));
        Assert.Empty(store.Commands);
    }

    [Theory]
    [InlineData("denied")] [InlineData("scoped")] [InlineData("null-authority")] [InlineData("missing")] [InlineData("provider")]
    [InlineData("null-record")] [InlineData("foreign-company")] [InlineData("foreign-id")] [InlineData("empty-rows")]
    [InlineData("duplicate-row")] [InlineData("wrong-line-number")] [InlineData("wrong-status-version")] [InlineData("malformed-plan-hash")]
    [InlineData("null-plan")] [InlineData("foreign-plan")] [InlineData("malformed-current-plan")] [InlineData("duplicate-current-account")] [InlineData("negative-active")]
    public async Task Mapping_purpose_read_fails_closed_on_unavailable_malformed_or_unauthorized_sources(string fault)
    {
        var store = new MappingDecisionMemory { Missing = fault == "missing", Failure = fault == "provider" };
        store.Transform = source => fault switch
        {
            "null-record" => source with { Record = null! }, "foreign-company" => source with { Record = source.Record with { CompanyId = Guid.NewGuid() } },
            "foreign-id" => source with { Record = source.Record with { MappingVersion = 9 } }, "empty-rows" => source with { Record = source.Record with { Rows = [] } },
            "duplicate-row" => source with { Record = source.Record with { Rows = [source.Record.Rows[0], source.Record.Rows[0] with { LineNumber = 2 }] } },
            "wrong-line-number" => source with { Record = source.Record with { Rows = [source.Record.Rows[0] with { LineNumber = 2 }] } },
            "wrong-status-version" => source with { Record = source.Record with { Version = 2 } },
            "malformed-plan-hash" => source with { Record = source.Record with { AccountPlanSourceSha256 = new string('A', 63) + "\n" } },
            "null-plan" => source with { CurrentActiveAccountPlan = null! },
            "foreign-plan" => source with { CurrentActiveAccountPlan = source.CurrentActiveAccountPlan with { CompanyId = Guid.NewGuid() } },
            "malformed-current-plan" => source with { CurrentActiveAccountPlan = source.CurrentActiveAccountPlan with { Reference = new("current", "bad") } },
            "duplicate-current-account" => source with { CurrentActiveAccountPlan = source.CurrentActiveAccountPlan with { Accounts = [source.CurrentActiveAccountPlan.Accounts[0], source.CurrentActiveAccountPlan.Accounts[0]] } },
            "negative-active" => source with { ActiveMappingVersion = -1 }, _ => source
        };
        var authority = new MappingDecisionAuthority { Allowed = fault != "denied", CompanyWide = fault != "scoped", NullScope = fault == "null-authority" };
        var result = await MappingDecisionService(store, authority).ReadDecisionAsync(1, _ => Task.CompletedTask);
        Assert.Equal(fault is "denied" or "scoped" or "null-authority" ? AccountingApiStatus.Denied : fault == "missing" ? AccountingApiStatus.NotFound : AccountingApiStatus.Unavailable, result.Status);
        Assert.Null(result.Resource); Assert.Empty(store.Commands); Assert.Equal(0, store.Activations);
        if (fault is "denied" or "scoped" or "null-authority") Assert.Equal(0, store.PreviewReads);
    }

    [Theory]
    [InlineData("permission")] [InlineData("company")] [InlineData("unresolved")] [InlineData("actor")] [InlineData("member")] [InlineData("cancel")]
    public async Task Mapping_final_consumer_callback_cannot_release_facts_after_fresh_context_or_authority_loss(string fault)
    {
        var store = new MappingDecisionMemory(); var company = new MappingEntryCompany(DecisionCompany); var user = new MappingEntryUser(DecisionCompany) { Actor = Checker };
        var authority = new MappingDecisionAuthority(); var service = MappingDecisionService(store, authority, company, user); var calls = 0;
        using var cancellation = new CancellationTokenSource();
        Task Consumer(CancellationToken _)
        {
            if (++calls == 5)
            {
                if (fault == "permission") authority.Allowed = false;
                if (fault == "company") company.Set(Guid.NewGuid()); if (fault == "unresolved") company.Set(Guid.Empty);
                if (fault == "actor") user.Actor = Preparer; if (fault == "member") user.Member = false; if (fault == "cancel") cancellation.Cancel();
            }
            return Task.CompletedTask;
        }
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadDecisionAsync(1, Consumer, cancellation.Token));
        else { var answer = await service.ReadDecisionAsync(1, Consumer); Assert.Equal(AccountingApiStatus.Denied, answer.Status); Assert.Null(answer.Resource); }
        Assert.Equal(5, calls); Assert.Equal(1, store.PreviewReads); Assert.Equal(0, store.Activations); Assert.Empty(store.Commands);
    }

    [Fact]
    public async Task Mapping_preparer_read_is_denied_but_native_self_activation_still_stores_and_replays_its_original_refusal()
    {
        var store = new MappingDecisionMemory(); var user = new MappingEntryUser(DecisionCompany); var service = MappingDecisionService(store, user: user);
        var read = await service.ReadDecisionAsync(1, _ => Task.CompletedTask);
        Assert.Equal(AccountingApiStatus.Denied, read.Status); Assert.Equal(AccountingPostingAccountMappingCodesV1.SelfActivation, read.Code); Assert.Empty(store.Commands);
        var first = await service.ActivateAsync(1, new(1, 0), "maker"); var replay = await service.ActivateAsync(1, new(1, 0), "maker");
        Assert.Equal(AccountingPostingAccountMappingCodesV1.SelfActivation, first.Code); Assert.Equal("activation", first.Field); Assert.False(first.IsReplay);
        Assert.True(replay.IsReplay); Assert.Equal(first.Code, replay.Code); Assert.Single(store.Commands); Assert.Null(first.Resource); Assert.Null(replay.Resource);
        Assert.Equal(AccountingPostingAccountMappingStatusV1.Prepared, store.Record.Status); Assert.Equal(0, store.ReceiptReads);
    }

    [Fact]
    public async Task Mapping_original_receipt_survives_supersession_and_missing_or_changed_current_plan_without_reading_it()
    {
        var store = new MappingDecisionMemory(); var service = MappingDecisionService(store); var request = new ActivateAccountingPostingAccountMappingRequestV1(1, 0); const string key = "original";
        var before = (await service.ReadDecisionAsync(1, _ => Task.CompletedTask)).Resource!;
        var original = await service.ActivateAsync(1, request, key); var proof = Assert.IsType<AccountingPostingAccountMappingActivationReceiptV1>(original.Resource?.ActivationReceipt);
        var command = Assert.Single(store.Commands.Values); Assert.Equal(Checker, proof.ActorId); Assert.Equal(before.SourceFactsDigest, proof.SourceFactsDigest);
        Assert.Equal(MappingLocalHash(key), proof.IdempotencyKeyHash);
        Assert.Equal(MappingLocalHash(AccountingPostingAccountMappingCommandNamesV1.Activate + "|" + JsonSerializer.Serialize(new { mappingVersion = 1L, request.ExpectedVersion, request.ExpectedActiveMappingVersion }, MappingDecisionJson)), proof.PayloadHash);
        Assert.Equal(MappingLocalHash(command.ResultJson), proof.OriginalResultDigest); Assert.Equal(AccountingPostingAccountMappingServiceV1.ActivationOriginalResultDigest(original), proof.OriginalResultDigest);
        Assert.False(command.ResultJson.Contains("activationReceipt", StringComparison.Ordinal));
        store.Record = store.Record with { Status = AccountingPostingAccountMappingStatusV1.Superseded, Version = 3 }; store.Plan = null;
        var replay = await service.ActivateAsync(1, request, key);
        Assert.True(replay.IsReplay); Assert.Equal(command.OriginalCorrelationId, replay.OriginalCorrelationId); Assert.Equal("Active", replay.Resource?.Status); Assert.Equal(2L, replay.Resource?.Version);
        Assert.Equal(proof, replay.Resource?.ActivationReceipt); Assert.Equal(1, store.PreviewReads); Assert.Equal(1, store.Activations); Assert.Equal(2, store.ReceiptReads);
        Assert.Equal(JsonSerializer.Serialize(original.Resource, MappingDecisionJson), JsonSerializer.Serialize(replay.Resource, MappingDecisionJson));
    }

    [Theory]
    [InlineData("missing")] [InlineData("null-json")] [InlineData("null-envelope")] [InlineData("malformed-json")] [InlineData("unknown-root")]
    [InlineData("unknown-resource")] [InlineData("private-proof")] [InlineData("missing-resource")] [InlineData("foreign-company")] [InlineData("foreign-id")]
    [InlineData("wrong-command")] [InlineData("wrong-key")] [InlineData("wrong-actor")] [InlineData("wrong-payload")] [InlineData("empty-correlation")]
    [InlineData("wrong-correlation")] [InlineData("wrong-version")] [InlineData("wrong-source-plan")] [InlineData("wrong-row")]
    [InlineData("wrong-preparer")] [InlineData("wrong-time")] [InlineData("wrong-current-answer")] [InlineData("wrong-expected-version")]
    [InlineData("wrong-expected-active")] [InlineData("maker")]
    public async Task Mapping_receipt_never_certifies_malformed_foreign_legacy_or_current_replacement_data(string fault)
    {
        var store = new MappingDecisionMemory(); var request = new ActivateAccountingPostingAccountMappingRequestV1(1, 0); const string key = "bind";
        var answer = await MappingDecisionService(store).ActivateAsync(1, request, key); AccountingCommandRecord? command = Assert.Single(store.Commands.Values);
        var source = store.Record; var actor = Checker;
        if (fault == "missing") command = null; if (fault == "null-json") command = command! with { ResultJson = null! };
        if (fault == "null-envelope") command = command! with { ResultJson = "null" }; if (fault == "malformed-json") command = command! with { ResultJson = "{" };
        if (fault == "foreign-company") source = source with { CompanyId = Guid.NewGuid() }; if (fault == "foreign-id") source = source with { MappingVersion = 9 };
        if (fault == "wrong-command") command = command! with { CommandName = "other" }; if (fault == "wrong-key") command = command! with { IdempotencyKeyHash = MappingLocalHash("other") };
        if (fault == "wrong-actor") actor = Guid.NewGuid(); if (fault == "maker") actor = Preparer;
        if (fault == "wrong-payload") command = command! with { RequestHash = new string('A', 64) }; if (fault == "empty-correlation") command = command! with { OriginalCorrelationId = "" };
        if (fault == "wrong-correlation") answer = answer with { IsReplay = true, OriginalCorrelationId = "other" };
        if (fault == "wrong-source-plan") source = source with { AccountPlanSourceVersion = "other" };
        if (fault == "wrong-row") source = source with { Rows = [source.Rows[0] with { AccountCode = "other" }] };
        if (fault == "wrong-preparer") source = source with { PreparedByUserId = Guid.NewGuid() }; if (fault == "wrong-time") source = source with { ActivatedAtUtc = Now.AddTicks(1) };
        if (fault == "wrong-current-answer") answer = answer with { Resource = answer.Resource! with { Status = "Superseded", Version = 3 } };
        if (fault == "wrong-expected-version") request = request with { ExpectedVersion = 2 }; if (fault == "wrong-expected-active") request = request with { ExpectedActiveMappingVersion = 1 };
        if (fault is "unknown-root" or "unknown-resource" or "private-proof" or "missing-resource" or "wrong-version")
        {
            var json = JsonNode.Parse(command!.ResultJson)!;
            if (fault == "unknown-root") json["unexpected"] = true; if (fault == "unknown-resource") json["resource"]!["unexpected"] = true;
            if (fault == "private-proof") json["resource"]!["activationReceipt"] = new JsonObject();
            if (fault == "missing-resource") json.AsObject().Remove("resource"); if (fault == "wrong-version") json["resource"]!["version"] = 3;
            command = command with { ResultJson = json.ToJsonString() };
        }
        Assert.Null(AccountingPostingAccountMappingServiceV1.BindMappingActivationReceipt(command, source, answer, request, key, actor));
    }

    [Theory]
    [InlineData("permission")] [InlineData("company")] [InlineData("actor")] [InlineData("member")] [InlineData("cancel")]
    public async Task Mapping_original_key_receipt_read_rechecks_current_authority_before_release(string fault)
    {
        var store = new MappingDecisionMemory(); var company = new MappingEntryCompany(DecisionCompany); var user = new MappingEntryUser(DecisionCompany) { Actor = Checker };
        var authority = new MappingDecisionAuthority(); var service = MappingDecisionService(store, authority, company, user);
        var first = await service.ActivateAsync(1, new(1, 0), "release"); Assert.NotNull(first.Resource?.ActivationReceipt);
        using var cancellation = new CancellationTokenSource();
        store.AfterReceiptRead = () =>
        {
            if (fault == "permission") authority.Allowed = false; if (fault == "company") company.Set(Guid.NewGuid());
            if (fault == "actor") user.Actor = Preparer; if (fault == "member") user.Member = false; if (fault == "cancel") cancellation.Cancel();
        };
        // The fixture has no relational owner; the native freshness exception is deliberately not swallowed by a fake transaction.
        if (fault == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ActivateAsync(1, new(1, 0), "release", cancellation.Token));
        else await Assert.ThrowsAsync<NormalJournalAuthorityDeniedException>(() => service.ActivateAsync(1, new(1, 0), "release"));
        Assert.Single(store.Commands); Assert.Equal(1, store.Activations);
    }

    [Fact]
    public async Task Mapping_purpose_freezes_provider_lists_before_the_final_consumer_await()
    {
        var store = new MappingDecisionMemory(); var rows = store.Record.Rows.ToList(); var accounts = store.Plan!.Accounts.ToList();
        store.Record = store.Record with { Rows = rows }; store.Plan = store.Plan with { Accounts = accounts }; var calls = 0;
        var result = await MappingDecisionService(store).ReadDecisionAsync(1, _ =>
        { if (++calls == 4) { rows.Clear(); accounts.Clear(); } return Task.CompletedTask; });
        Assert.Equal(AccountingApiStatus.Ok, result.Status); Assert.Single(result.Resource!.Mapping.Rows); Assert.Single(result.Resource.CurrentActiveAccountPlan.Accounts);
        Assert.Empty(rows); Assert.Empty(accounts);
    }

    [Fact]
    public async Task Mapping_default_reader_absence_is_unavailable_and_legacy_native_success_has_no_invented_proof()
    {
        var inner = new MappingDecisionMemory(); var service = MappingDecisionService(inner, readerless: true);
        Assert.Equal(AccountingApiStatus.Unavailable, (await service.ReadDecisionAsync(1, _ => Task.CompletedTask)).Status);
        var answer = await service.ActivateAsync(1, new(1, 0), "legacy");
        Assert.Equal(AccountingApiStatus.Ok, answer.Status); Assert.Null(answer.Resource?.ActivationReceipt); Assert.Single(inner.Commands); Assert.Equal(0, inner.ReceiptReads);
    }

    private static string MappingLocalHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static AccountingPostingAccountMappingServiceV1 MappingDecisionService(MappingDecisionMemory store, MappingDecisionAuthority? authority = null,
        MappingEntryCompany? company = null, MappingEntryUser? user = null, bool readerless = false) =>
        new(readerless ? new SyntheticMappingActivationProtocolStore(store) : store, store, authority ?? new(), company ?? new(DecisionCompany),
            user ?? new MappingEntryUser(DecisionCompany) { Actor = Checker }, new FixedCorrelation("SENTETIK-original-correlation"), new FixedTimeProvider(Now));
    private sealed class MappingDecisionAuthority : IErpAuthorityRevalidationService
    {
        public bool Allowed = true, CompanyWide = true, NullScope; public List<string> Permissions { get; } = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default)
        { Permissions.Add(permission); return Task.FromResult(NullScope ? null! : new PermissionScope(Allowed, CompanyWide, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
    // Algorithm/read/provenance fixture only, not evidence of relational transactions or durable Audit.
    private sealed class MappingDecisionMemory : IAccountingPostingAccountMappingStoreV1, IAccountingPostingAccountMappingDecisionStoreV1, IAccountingCommandLog
    {
        public AccountingPostingAccountMappingRecordV1 Record = new(DecisionCompany, 1, EffectiveFrom, AccountingPostingAccountMappingStatusV1.Prepared, 1,
            "prepared-plan", new string('A', 64), Preparer, Now, null, null,
            [new(1, AccountingPostingAccountMappingPurposesV1.SalesInvoice, AccountingPostingAccountMappingKeysV1.Revenue, null, null, null, Revenue600, "600.01")]);
        public AccountingPostingMappingDecisionPlanV1? Plan = new(DecisionCompany, new("current-plan", new string('B', 64)), [new(Revenue600, "600.02", "SENTETIK current account")]);
        public Dictionary<string, AccountingCommandRecord> Commands { get; } = []; public int Activations, PreviewReads, ReceiptReads;
        public Action? AfterReceiptRead;
        public bool Missing, Failure; public Func<AccountingPostingAccountMappingDecisionSourceV1, AccountingPostingAccountMappingDecisionSourceV1>? Transform;
        public async Task<AccountingApiResult<AccountingPostingAccountMappingViewV1>> RunActivationAsync(Func<bool, Task<AccountingApiResult<AccountingPostingAccountMappingViewV1>>> attempt,
            Func<CancellationToken, Task> fence, CancellationToken token = default) { await fence(token); var answer = await attempt(false); await fence(token); return answer; }
        public Task<AccountingCommandRecord?> FindCommandAsync(string command, string key, CancellationToken token = default) => Task.FromResult(Commands.GetValueOrDefault(command + key));
        public Task RecordCommandAsync(AccountingCommandRecord command, CancellationToken token = default) { Commands.Add(command.CommandName + command.IdempotencyKeyHash, command); return Task.CompletedTask; }
        public Task<AccountingPostingAccountMappingRecordV1?> ReadAsync(long version, CancellationToken token = default) => Task.FromResult<AccountingPostingAccountMappingRecordV1?>(version == Record.MappingVersion ? Record : null);
        public Task<AccountingPostingAccountMappingRecordV1?> ReadActiveAsync(CancellationToken token = default) => Task.FromResult<AccountingPostingAccountMappingRecordV1?>(Record.Status == AccountingPostingAccountMappingStatusV1.Active ? Record : null);
        public Task<AccountingPostingAccountMappingRecordV1> PrepareAsync(long latest, DateOnly from, IReadOnlyList<AccountingPostingAccountMappingDraftRowV1> rows, Guid actor,
            DateTimeOffset at, Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default) => throw new NotSupportedException();
        public async Task<AccountingPostingAccountMappingRecordV1> ActivateAsync(long version, long expected, long active, Guid actor, DateTimeOffset at,
            Func<CancellationToken, Task> fence, AccountingCommandMetadata metadata, CancellationToken token = default)
        {
            if (Record.Version != expected) throw new AccountingVersionConflictException(expected, Record.Version);
            if (Record.PreparedByUserId == actor) throw new AccountingPostingAccountMappingRefusalException(AccountingPostingAccountMappingCodesV1.SelfActivation, "activation");
            await fence(token); Activations++; Record = Record with { Status = AccountingPostingAccountMappingStatusV1.Active, Version = expected + 1, ActivatedByUserId = actor, ActivatedAtUtc = at };
            await RecordCommandAsync(metadata.Completion!(Record), token); await fence(token); return Record;
        }
        public async Task<AccountingPostingAccountMappingRecordV1?> ReadActivationReceiptSourceAsync(long version, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { await fence(token); ReceiptReads++; AfterReceiptRead?.Invoke(); await fence(token); return Record; }
        public async Task<AccountingPostingAccountMappingDecisionSourceV1?> ReadActivationDecisionAsync(long version, Func<CancellationToken, Task> fence, CancellationToken token = default)
        {
            await fence(token); PreviewReads++; if (Failure) throw new AccountingStoreException(AccountingStoreCodes.ProviderUnavailable, "SENTETIK");
            var source = Missing ? null : new AccountingPostingAccountMappingDecisionSourceV1(Record, Record.Status == AccountingPostingAccountMappingStatusV1.Active ? Record.MappingVersion : 0, Plan!);
            if (source is not null && Transform is not null) source = Transform(source); await fence(token); return source;
        }
    }
}
