using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Services;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchasingServiceTests
{
    [Fact]
    public async Task Service_preparation_reads_the_actual_owner_and_returns_private_draft_facts_without_an_attempt_or_approval()
    {
        var f = await PreparationFixture(); var original = f.Store.Inner.Saved; var fences = 0;
        f.Authority.Denied.Add(PurchasingPermissionManifestV1.OrdersRead);
        f.Authority.Denied.Add(PurchasingPermissionManifestV1.OrdersPriceRead);
        var answer = await f.Service.ReadApprovalPreparationAsync(f.Id, "1", _ => { fences++; return Task.CompletedTask; });
        Assert.Equal((Company, Approver, PurchaseServiceOrderStatus.Draft, 1L, (string?)null),
            (answer.CompanyId, answer.ActorId, answer.Prepared.Status, answer.Prepared.Version, answer.Prepared.ApprovedBy));
        Assert.Equal(original!.OrderId, answer.Prepared.OrderId); Assert.Equal(original.DocumentNumber, answer.Prepared.DocumentNumber);
        Assert.Equal(original.Lines, answer.Prepared.Lines); Assert.NotSame(original.Lines, answer.Prepared.Lines);
        Assert.Same(original, f.Store.Inner.Saved); Assert.Equal(1, f.Store.Inner.Writes); Assert.Equal(0, f.Store.Replays);
        Assert.Equal(2, f.Store.OwnerReads); Assert.True(fences > 4);
        Assert.Equal(new[] { "supplier", "service", "physical" }, f.Reads);
        Assert.All(f.Authority.Measured, x => Assert.Equal(PurchasingPermissionManifestV1.OrdersApprove, x));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(answer, PurchasingJson.Options));
        Assert.Equal(2, json.RootElement.EnumerateObject().Count()); Assert.False(json.RootElement.TryGetProperty("prepared", out _));
    }

    [Fact]
    public async Task Service_preparation_native_approve_reuses_the_same_reference_sequence_and_still_writes_only_the_native_command()
    {
        var f = await PreparationFixture();
        await f.Service.ReadApprovalPreparationAsync(f.Id, "1", _ => Task.CompletedTask);
        var preparationReads = f.Reads.ToArray(); f.Reads.Clear();
        var accepted = await f.Service.ApproveAsync(f.Id, new("1", "SENTETIK approval"), Metadata("preparation-native-approve"));
        Assert.Equal(preparationReads, f.Reads); Assert.Equal("Approved", accepted.Order.Status); Assert.Equal("2", accepted.Order.Version);
        Assert.Equal(2, f.Store.Inner.Writes); Assert.Equal(1, f.Store.Replays);
    }

    [Theory]
    [InlineData("maker", PurchaseServiceOrder.SameActor)]
    [InlineData("version", PurchaseServiceOrder.VersionConflict)]
    [InlineData("missing-service-before-maker", PurchaseServiceOrder.StateConflict)]
    public async Task Service_preparation_preserves_native_reference_then_version_and_maker_refusal_order(string fault, string code)
    {
        var f = await PreparationFixture();
        if (fault.Contains("maker", StringComparison.Ordinal)) f.User.CurrentActor = Actor;
        if (fault == "missing-service-before-maker") f.References.MissingService = true;
        var expected = fault == "version" ? "2" : "1";
        var prepared = await Assert.ThrowsAsync<PurchasingServiceException>(() => f.Service.ReadApprovalPreparationAsync(f.Id, expected, _ => Task.CompletedTask));
        var native = await Assert.ThrowsAsync<PurchasingServiceException>(() => f.Service.ApproveAsync(f.Id,
            new(expected, "SENTETIK approval"), Metadata("preparation-native-refusal")));
        Assert.Equal((409, code), (prepared.Status, prepared.Code)); Assert.Equal((prepared.Status, prepared.Code), (native.Status, native.Code));
        Assert.Equal(1, f.Store.Inner.Writes); Assert.Equal(PurchaseServiceOrderStatus.Draft, f.Store.Inner.Saved!.Status);
    }

    [Theory]
    [InlineData("company")] [InlineData("id")] [InlineData("missing")] [InlineData("malformed")]
    public async Task Service_preparation_never_certifies_a_foreign_missing_or_malformed_owner_record(string fault)
    {
        var f = await PreparationFixture();
        f.Store.Project = value => fault switch
        {
            "company" => value! with { CompanyId = Guid.NewGuid() },
            "id" => value! with { OrderId = Guid.NewGuid() },
            "missing" => null,
            _ => value! with { Lines = [] }
        };
        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => f.Service.ReadApprovalPreparationAsync(f.Id, "1", _ => Task.CompletedTask));
        Assert.Equal(fault == "missing" ? 404 : 503, refusal.Status); Assert.Empty(f.Reads); Assert.Equal(1, f.Store.Inner.Writes);
    }

    [Theory]
    [InlineData("version")] [InlineData("line")] [InlineData("account")] [InlineData("document")]
    public async Task Service_preparation_final_owner_read_rejects_header_or_line_drift_without_repair(string change)
    {
        var f = await PreparationFixture(); var original = f.Store.Inner.Saved!;
        f.Store.Project = value =>
        {
            if (value is not { } current || f.Store.OwnerReads < 2) return value;
            return change switch
            {
                "version" => current with { Version = current.Version + 1 },
                "line" => current with { Lines = [current.Lines[0] with { Quantity = 9m }] },
                "account" => current with { CompanyAccountId = Guid.NewGuid() },
                _ => current with { DocumentNumber = "SENTETIK-other" }
            };
        };
        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => f.Service.ReadApprovalPreparationAsync(f.Id, "1", _ => Task.CompletedTask));
        Assert.Equal((409, PurchaseServiceOrder.VersionConflict), (refusal.Status, refusal.Code));
        Assert.Same(original, f.Store.Inner.Saved); Assert.Equal(1, f.Store.Inner.Writes); Assert.Equal(0, f.Store.Replays);
    }

    [Theory]
    [InlineData("supplier")] [InlineData("service")] [InlineData("physical")]
    public async Task Service_preparation_token_ignoring_owner_completion_cannot_release_after_cancellation(string phase)
    {
        var f = await PreparationFixture(); using var cancellation = new CancellationTokenSource();
        f.AfterRead = name => { if (name == phase) cancellation.Cancel(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service.ReadApprovalPreparationAsync(f.Id, "1", _ => Task.CompletedTask, cancellation.Token));
        Assert.Equal(1, f.Store.Inner.Writes); Assert.Equal(0, f.Store.Replays);
        Assert.Equal(phase, f.Reads[^1]);
    }

    [Theory]
    [InlineData("authority")] [InlineData("actor")] [InlineData("company")] [InlineData("membership")] [InlineData("cancel")]
    public async Task Service_preparation_rechecks_every_consumer_completion_including_final_release(string loss)
    {
        var baseline = await PreparationFixture(); var boundaries = 0;
        await baseline.Service.ReadApprovalPreparationAsync(baseline.Id, "1", _ => { boundaries++; return Task.CompletedTask; });
        Assert.True(boundaries > 4);
        for (var boundary = 1; boundary <= boundaries; boundary++)
        {
            var f = await PreparationFixture(); using var cancellation = new CancellationTokenSource(); var calls = 0;
            Task Fence(CancellationToken _)
            {
                if (++calls == boundary)
                {
                    if (loss == "authority") f.Authority.Granted = false;
                    if (loss == "actor") f.User.CurrentActor = "SENTETIK-other";
                    if (loss == "company") f.Company.CurrentCompanyId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
                    if (loss == "membership") f.User.Allowed.Clear();
                    if (loss == "cancel") cancellation.Cancel();
                }
                return Task.CompletedTask;
            }
            if (loss == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service.ReadApprovalPreparationAsync(f.Id, "1", Fence, cancellation.Token));
            else Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() => f.Service.ReadApprovalPreparationAsync(f.Id, "1", Fence))).Status);
            Assert.Equal(boundary, calls); Assert.Equal(1, f.Store.Inner.Writes); Assert.Equal(0, f.Store.Replays);
        }
    }

    [Fact]
    public async Task Service_preparation_pre_cancelled_call_and_unconfigured_legacy_store_perform_no_owner_or_command_read()
    {
        var f = await PreparationFixture(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); var callbacks = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service.ReadApprovalPreparationAsync(f.Id, "1", _ => { callbacks++; return Task.CompletedTask; }, cancellation.Token));
        Assert.Equal(0, callbacks); Assert.Equal(0, f.Authority.Calls); Assert.Equal(0, f.Store.OwnerReads);
        var source = new ServiceCommandSource(); var legacy = new ServiceCommandStore(); var authority = new FixedAuthority();
        var service = new PurchaseServiceOrderServiceV1(new MutableCompanyContext(), f.User, authority, legacy, source, source, source, source, TimeProvider.System);
        Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => service.ReadApprovalPreparationAsync(f.Id, "1", _ => Task.CompletedTask))).Status);
        Assert.Equal(0, legacy.Writes); Assert.Null(legacy.Saved);
    }

    [Theory]
    [InlineData("physical", true)] [InlineData("physical", false)]
    [InlineData("tax", true)] [InlineData("tax", false)]
    public async Task Subcontract_preparation_reuses_native_physical_and_tax_helpers_with_preparation_fences(string phase, bool cancel)
    {
        var source = new ServiceCommandSource(); var inner = new ServiceCommandStore();
        var draft = ServiceTaxState(source); inner.Seed(draft); var store = new PreparationStore(inner);
        var authority = new FixedAuthority(); var user = new FixedUser { CurrentActor = Approver };
        using var cancellation = new CancellationTokenSource();
        var physical = new PreparationPhysicalProof(); var tax = new PreparationTaxDefault();
        void Completed() { if (cancel) cancellation.Cancel(); else authority.Granted = false; }
        if (phase == "physical") physical.After = Completed; else tax.After = Completed;
        var service = new PurchaseServiceOrderServiceV1(new MutableCompanyContext(), user, authority, store,
            source, source, source, source, TimeProvider.System, physicalReferences: physical, subcontractTax: tax);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadApprovalPreparationAsync(draft.OrderId, "1", _ => Task.CompletedTask, cancellation.Token));
        else Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() => service.ReadApprovalPreparationAsync(draft.OrderId, "1", _ => Task.CompletedTask))).Status);
        Assert.Equal(1, physical.Reads); Assert.Equal(phase == "tax" ? 1 : 0, tax.Reads);
        Assert.Same(draft, inner.Saved); Assert.Equal(0, inner.Writes); Assert.Equal(0, store.Replays);
    }

    [Fact]
    public async Task Service_preparation_scope_callback_does_not_leak_into_nested_or_later_native_reads()
    {
        var f = await PreparationFixture(); var callbacks = 0; var nested = false;
        await f.Service.ReadApprovalPreparationAsync(f.Id, "1", async _ =>
        {
            callbacks++;
            if (nested) return;
            nested = true; var before = callbacks;
            Assert.Equal(f.Id, (await f.Service.ReadAsync(f.Id)).OrderId);
            Assert.Equal(before, callbacks);
        });
        var completed = callbacks;
        Assert.Equal(f.Id, (await f.Service.ReadAsync(f.Id)).OrderId); Assert.Equal(completed, callbacks);
        Assert.Equal(1, f.Store.Inner.Writes);
    }

    private sealed class PreparationFixtureState
    {
        internal required PurchaseServiceOrderServiceV1 Service;
        internal required PreparationStore Store;
        internal required PreparationReferences References;
        internal required FixedAuthority Authority;
        internal required FixedUser User;
        internal required MutableCompanyContext Company;
        internal Guid Id;
        internal List<string> Reads = [];
        internal Action<string>? AfterRead;
    }
    private static async Task<PreparationFixtureState> PreparationFixture()
    {
        var source = new ServiceCommandSource(); var context = new MutableCompanyContext(); var user = new FixedUser();
        var authority = new FixedAuthority(); var store = new PreparationStore(new ServiceCommandStore());
        var references = new PreparationReferences(source);
        var service = new PurchaseServiceOrderServiceV1(context, user, authority, store, references, references, source, source, TimeProvider.System);
        var created = await service.CreateAsync(ServiceCreate(source), Metadata("preparation-create")); user.CurrentActor = Approver;
        var f = new PreparationFixtureState { Service = service, Store = store, References = references, Authority = authority, User = user, Company = context, Id = created.Order.OrderId };
        references.After = name => { f.Reads.Add(name); f.AfterRead?.Invoke(name); };
        store.Physical = () => { f.Reads.Add("physical"); f.AfterRead?.Invoke("physical"); };
        store.Replays = 0; authority.Measured.Clear();
        // FixedAuthority counters are monotonic; use a separate instance after native setup for the pre-cancel assertion.
        f.Authority = new FixedAuthority();
        f.Service = new PurchaseServiceOrderServiceV1(context, user, f.Authority, store, references, references, source, source, TimeProvider.System);
        return f;
    }

    // Explicit synthetic owner protocol, not SQL/TX/audit acceptance. Read preparation has no caller-state argument.
    private sealed class PreparationStore(ServiceCommandStore inner) : IPurchaseServiceOrderStoreV1
    {
        internal ServiceCommandStore Inner => inner;
        internal int OwnerReads, Replays;
        internal Func<PurchaseServiceOrderState?, PurchaseServiceOrderState?>? Project;
        internal Action? Physical;
        public Task<PurchaseServiceOrderState?> ReadAsync(Guid id, CancellationToken token) => inner.ReadAsync(id, token);
        public async Task<PurchaseServiceOrderState?> ReadForApprovalPreparationAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token)
        {
            await fence(token); OwnerReads++;
            var owner = await inner.ReadAsync(id, token); await fence(token);
            await fence(token); var revision = owner?.OrderId == id ? Project is null ? owner : Project(owner) : null;
            await fence(token); return revision;
        }
        public Task<IReadOnlyList<PurchaseServiceOrderState>> ListAsync(CancellationToken token) => inner.ListAsync(token);
        public Task<bool> HasApprovedPhysicalReferencesAsync(PurchaseServiceOrderState order, CancellationToken token) { Physical?.Invoke(); return Task.FromResult(false); }
        public Task<PurchaseServiceOrderWriteV1?> ReplayAsync(string command, PurchasingWriteEnvelope e, string hash, CancellationToken token)
        { Replays++; return inner.ReplayAsync(command, e, hash, token); }
        public Task<PurchaseServiceOrderWriteV1?> WriteAsync(Guid id, string command, PurchasingWriteEnvelope e, string hash, string reason,
            Func<PurchaseServiceOrderState?, CancellationToken, Task<PurchaseServiceOrderState>> mutate, Func<CancellationToken, Task> fence, CancellationToken token) =>
            inner.WriteAsync(id, command, e, hash, reason, mutate, fence, token);
    }
    private sealed class PreparationPhysicalProof : IPurchaseServiceOrderReferenceProofV1
    {
        internal Action? After; internal int Reads;
        public Task ValidateAsync(Guid company, DateOnly on, IReadOnlyList<PurchaseServiceMaterial> materials,
            IReadOnlyList<PurchaseServiceOutput> outputs, CancellationToken token)
        { Reads++; After?.Invoke(); return Task.CompletedTask; }
    }
    private sealed class PreparationTaxDefault : ISettingsSubcontractTaxDefaultProviderV1
    {
        internal Action? After; internal int Reads;
        public Task<SettingsSubcontractTaxDefaultAnswerV1> GetSubcontractTaxDefaultAsync(Guid company, SubcontractTaxSideV1 side, CancellationToken token = default)
        { Reads++; After?.Invoke(); return Task.FromResult(new SettingsSubcontractTaxDefaultAnswerV1(company, side, new(true, 20m, null, false, null, null), 1)); }
    }
    private sealed class PreparationReferences(ServiceCommandSource source) : IPurchasingPartyAccountPortV2, ISettingsReferenceListReaderV1
    {
        internal Action<string>? After; internal bool MissingService;
        public async Task<PurchasingPartyAccountAnswerV2> GetAuthorizedAsync(Guid party, Guid account, CancellationToken token = default)
        { var answer = await source.GetAuthorizedAsync(party, account, token); After?.Invoke("supplier"); return answer; }
        public Task<SettingsReferenceListV1> ListAsync(Guid company, SettingsReferenceListKindV1 kind, CancellationToken token = default) => source.ListAsync(company, kind, token);
        public async Task<SettingsReferenceListLookupV1> GetAsync(Guid company, SettingsReferenceListKindV1 kind, Guid id, CancellationToken token = default)
        { var answer = await source.GetAsync(company, kind, id, token); After?.Invoke("service"); return MissingService ? new(SettingsReferenceListLookupOutcomeV1.NotFound, null) : answer; }
    }
}
