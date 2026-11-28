using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.SalesContracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesContractServiceTests
{
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Selected_contract_read_uses_only_approve_and_native_optional_prices_and_retains_every_human_field(bool prices)
    {
        var f = ContractReadFixture(); f.Authority.PriceGranted = prices;
        var answer = await f.Service.ReadContractDiscountDecisionAsync(ContractId, _ => Task.CompletedTask);
        Assert.Equal(SalesContractDiscountDecisionReadStateV1.Found, answer.State); var detail = answer.Detail; Assert.NotNull(detail);
        Assert.True(SalesContractService.MatchesContractDiscountDecisionDetail(detail)); Assert.True(detail.ApprovalSupported); Assert.True(detail.RejectionSupported);
        Assert.Equal(prices ? SalesPriceDisclosure.Visible : SalesPriceDisclosure.Withheld, detail.Contract.AmountDisclosure);
        Assert.Equal(prices ? "1500.00" : null, Assert.Single(detail.Contract.Installments).Amount);
        Assert.Equal(prices ? "5" : null, Assert.Single(detail.Contract.DiscountRules).RatePercent);
        Assert.Equal((Pending().ValidFrom, Pending().ValidThrough, Seller, Now), (detail.Contract.ValidFrom, detail.Contract.ValidThrough, detail.Contract.CreatedBy, detail.Contract.CreatedAtUtc));
        Assert.DoesNotContain(SalesPermissionManifestV1.ContractsRead, f.Authority.Asked); Assert.Equal(SalesPermissionManifestV1.OrdersPriceRead, f.Authority.Asked[^1]);
        Assert.Empty(f.Inner.Calls); Assert.Equal(1, f.Reader.Reads);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(detail, SalesDraftJson.Options)); Assert.Equal(6, json.RootElement.EnumerateObject().Count());
        Assert.False(json.RootElement.TryGetProperty("source", out _)); Assert.True(json.RootElement.TryGetProperty("contract", out var contractJson)); Assert.Equal(24, contractJson.EnumerateObject().Count());
    }
    [Theory] [InlineData("permission")] [InlineData("member")] [InlineData("maker")]
    public async Task Selected_contract_deny_does_not_borrow_general_read_or_release_private_source(string fault)
    {
        var f = ContractReadFixture(fault == "maker" ? Seller : Manager);
        if (fault == "permission") f.Authority.Granted = false; if (fault == "member") f.User.Member = false;
        var answer = await f.Service.ReadContractDiscountDecisionAsync(ContractId, _ => Task.CompletedTask);
        Assert.Equal(SalesContractDiscountDecisionReadStateV1.Denied, answer.State); Assert.Null(answer.Detail);
        Assert.Equal(fault == "maker" ? 1 : 0, f.Reader.Reads); Assert.Empty(f.Inner.Calls);
    }
    [Theory] [InlineData("missing")] [InlineData("company")] [InlineData("target")] [InlineData("null-record")]
    public async Task Selected_contract_missing_or_malformed_provider_has_no_forged_detail(string fault)
    {
        var f = ContractReadFixture();
        f.Reader.Source = fault switch { "missing" => null, "company" => new(Guid.NewGuid(), Pending()), "target" => new(Company, OtherContract()), _ => new(Company, null!) };
        var answer = await f.Service.ReadContractDiscountDecisionAsync(ContractId, _ => Task.CompletedTask);
        Assert.Equal(fault == "missing" ? SalesContractDiscountDecisionReadStateV1.NotFound : SalesContractDiscountDecisionReadStateV1.Unavailable, answer.State); Assert.Null(answer.Detail);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Last_completed_consumer_permission_loss_or_cancel_cannot_release_found_contract(bool cancel)
    {
        var f = ContractReadFixture(); using var cts = new CancellationTokenSource(); var calls = 0;
        Task Fence(CancellationToken _) { if (++calls == 5) { if (cancel) cts.Cancel(); else f.Authority.Granted = false; } return Task.CompletedTask; }
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service.ReadContractDiscountDecisionAsync(ContractId, Fence, cts.Token));
        else { var answer = await f.Service.ReadContractDiscountDecisionAsync(ContractId, Fence); Assert.Equal(SalesContractDiscountDecisionReadStateV1.Denied, answer.State); Assert.Null(answer.Detail); }
        Assert.Equal(5, calls); Assert.Empty(f.Inner.Calls);
    }
    [Fact]
    public async Task Final_approve_await_can_revoke_price_read_and_the_last_disclosure_withholds_all_native_amounts()
    {
        var f = ContractReadFixture(); var consumers = 0;
        f.Authority.AfterRead = permission => { if (consumers == 5 && permission == SalesPermissionManifestV1.ContractsDiscountApprove) f.Authority.PriceGranted = false; return Task.CompletedTask; };
        var answer = await f.Service.ReadContractDiscountDecisionAsync(ContractId, _ => { consumers++; return Task.CompletedTask; });
        var c = answer.Detail!.Contract; Assert.Equal(SalesPriceDisclosure.Withheld, c.AmountDisclosure); Assert.Null(c.PlannedTotalAmount); Assert.Null(c.DiscountLimitPercent);
        Assert.All(c.Installments, x => Assert.Null(x.Amount)); Assert.All(c.DiscountRules, x => Assert.Null(x.RatePercent));
        Assert.Equal(SalesPermissionManifestV1.OrdersPriceRead, f.Authority.Asked[^1]);
    }
    [Fact]
    public async Task Provider_domain_instance_is_frozen_before_final_consumer_await_and_cannot_replace_observed_facts()
    {
        var f = ContractReadFixture(); var calls = 0; var raw = f.Reader.Source!.Contract;
        var answer = await f.Service.ReadContractDiscountDecisionAsync(ContractId, _ => { if (++calls == 4) raw.ApproveDiscount(1, Manager, Now); return Task.CompletedTask; });
        Assert.Equal(SalesContractState.Active, raw.State); Assert.Equal(("PendingDiscountApproval", "1"), (answer.Detail!.Contract.State, answer.Detail.Contract.Version));
        Assert.NotSame(raw, answer.Detail.Source!.Contract);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void Genuine_contract_receipt_binds_attempt_one_immutable_audit_and_separates_current_http_from_original_decision(bool approve)
    {
        var f = ContractReceiptFixture(approve); var receipt = RestoreContractReceipt(f); Assert.NotNull(receipt);
        Assert.Equal(("1", "2", Manager), (receipt.ExpectedVersion, receipt.ResultVersion, receipt.ActorId));
        Assert.Equal(f.Hash, receipt.PayloadHash); Assert.Equal(f.KeyHash, receipt.IdempotencyKeyHash);
        var later = f.Response with { IsReplay = true, Contract = f.Response.Contract with { IsInEffectToday = !f.Response.Contract.IsInEffectToday } };
        var next = RestoreContractReceipt(f with { Response = later }); Assert.NotNull(next);
        Assert.Equal(receipt.OriginalDecisionDigest, next.OriginalDecisionDigest); Assert.NotEqual(receipt.CurrentResponseDigest, next.CurrentResponseDigest);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(f.Response with { OriginalDecisionReceipt = receipt }, SalesDraftJson.Options));
        Assert.Equal(3, json.RootElement.EnumerateObject().Count()); Assert.False(json.RootElement.TryGetProperty("originalDecisionReceipt", out _));
    }
    [Theory]
    [InlineData("missing")] [InlineData("duplicate")] [InlineData("key")] [InlineData("actor")] [InlineData("company")] [InlineData("target")]
    [InlineData("hash")] [InlineData("corr")] [InlineData("permission")] [InlineData("audit-actor")] [InlineData("audit-version")]
    [InlineData("unknown-json")] [InlineData("changed-current")] [InlineData("raw-version")] [InlineData("raw-reason")]
    public void Contract_receipt_never_reconstructs_success_from_missing_ambiguous_mismatched_or_unprovable_history(string fault)
    {
        var f = ContractReceiptFixture(false); var a = f.Attempt; var audit = f.Audit; var response = f.Response;
        if (fault == "key") a.IdempotencyKeyHash = new string('A', 64); if (fault == "actor") a.ActorId = "other"; if (fault == "company") a.CompanyId = Guid.NewGuid();
        if (fault == "target") a.ContractId = Guid.NewGuid(); if (fault == "hash") a.RequestHash = new string('b', 64); if (fault == "corr") response = response with { OriginalCorrelationId = "other" };
        if (fault == "permission") audit = audit with { PermissionCode = SalesPermissionManifestV1.ContractsRead }; if (fault == "audit-actor") audit = audit with { ActorId = "other" };
        if (fault == "audit-version") audit = audit with { EntityVersionBefore = 2 }; if (fault == "unknown-json") audit = audit with { AfterJson = audit.AfterJson.TrimEnd('}') + ",\"unknown\":true}" };
        if (fault == "changed-current") response = response with { Contract = response.Contract with { Clauses = "changed" } };
        if (fault is "raw-version" or "raw-reason") { a.RequestHash = SalesContractHash.Compute(a.CommandName, ContractId, new(fault == "raw-version" ? "01" : "1", fault == "raw-reason" ? "  SENTETIK ret  " : "SENTETIK ret")); f = f with { Hash = a.RequestHash }; }
        var stored = AuditEvent.Create(new(audit, AuditEventIntegrity.Compute(audit))); IReadOnlyList<AuditEvent> events = fault == "missing" ? [] : fault == "duplicate" ? [stored, stored] : [stored];
        Assert.Null(SalesStore.RestoreOriginalContractDiscountReceipt(a, events, Manager, f.KeyHash, f.Hash, a.CommandName, response, f.After));
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void Contract_binding_keeps_original_expected_version_key_body_and_receipt_after_terminal_current_read(bool approve)
    {
        var f = ContractReceiptFixture(approve); var receipt = RestoreContractReceipt(f)!;
        var pending = SalesContractService.ProjectContractDiscountDecision(f.Before, Today, SalesPriceDisclosure.Visible);
        var p = SalesContractDiscountDecisionBindingV1.Proof(pending); var item = SalesContractDiscountDecisionBindingV1.Item(pending)!;
        var snapshot = new WorkApprovalDecisionSnapshotV1(Company, item, "1", true) { SalesContractDiscountProof = p };
        var response = f.Response with { OriginalDecisionReceipt = receipt }; var body = new SalesDraftStateCommandRequest("1", approve ? null : "SENTETIK ret");
        Assert.True(SalesContractDiscountDecisionBindingV1.MatchesOriginalDecisionReceipt(snapshot, response, f.Attempt.CommandName, body, ContractReceiptKey, Manager));
        var historical = SalesContractDiscountDecisionBindingV1.Proof(receipt);
        var retained = new WorkApprovalDecisionSnapshotV1(Company, SalesContractDiscountDecisionBindingV1.Item(receipt), "1", false) { SalesContractDiscountProof = historical };
        Assert.Equal(("1", "2", false), (retained.SourceVersion, historical.CurrentVersion, retained.Pending));
        Assert.True(SalesContractDiscountDecisionBindingV1.MatchesOriginalDecisionReceipt(retained, response with { IsReplay = true }, f.Attempt.CommandName, body, ContractReceiptKey, Manager));
        Assert.False(SalesContractDiscountDecisionBindingV1.MatchesOriginalDecisionReceipt(retained, response, f.Attempt.CommandName, body, "wrong-key", Manager));
        Assert.False(SalesContractDiscountDecisionBindingV1.MatchesOriginalDecisionReceipt(retained, response with { OriginalDecisionReceipt = null }, f.Attempt.CommandName, body, ContractReceiptKey, Manager));
        Assert.False(SalesContractDiscountDecisionBindingV1.MatchesDecisionProof(item with { References = [item.References[0] with { RecordId = Guid.NewGuid() }] }, p));
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task Selected_contract_source_uses_guarded_owner_and_keeps_native_list_formatter_without_a_general_read(bool prices)
    {
        var f = ContractReadFixture(); f.Authority.PriceGranted = prices;
        var source = new SalesContractDiscountWorkInboxSourceV1(new SalesWorkInboxApprovalFakeStore(), new CompanyContext(), new User(Manager), f.Authority, f.Service);
        var snapshot = await source.ReadDecisionAsync(new(Company, Manager, "SENTETIK selected", null), ContractId, default); Assert.NotNull(snapshot);
        var detail = (await f.Service.ReadContractDiscountDecisionAsync(ContractId, _ => Task.CompletedTask)).Detail!;
        Assert.True(SalesContractDiscountDecisionBindingV1.MatchesDecisionSnapshot(snapshot, detail)); Assert.True(snapshot.Pending); Assert.Equal("1", snapshot.SourceVersion);
        Assert.Equal(Seller, snapshot.Item.PreparedBy); Assert.Equal($"/onaylarim/{SalesContractDiscountWorkInboxSourceV1.KindName}/{ContractId:D}", snapshot.Item.Link); Assert.Empty(f.Inner.Calls);
        var native = Pending(); var legacy = SalesContractDiscountWorkInboxSourceV1.Project(Company, [new(native.Id, Company, native.ContractNo, native.CreatedBy, native.CreatedAtUtc, native.DiscountLimitPercent, native.DiscountRules)], 1, prices).Items.Single();
        Assert.True(SalesContractDiscountDecisionDigestsV1.Same(legacy, snapshot.Item)); Assert.DoesNotContain(SalesPermissionManifestV1.ContractsRead, f.Authority.Asked);
    }
    [Fact]
    public async Task Selected_contract_source_missing_owner_fails_closed_before_any_legacy_list_downgrade()
    {
        var source = new SalesContractDiscountWorkInboxSourceV1(new SalesWorkInboxApprovalFakeStore(), new CompanyContext(), new User(Manager), new Authority());
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadDecisionAsync(new(Company, Manager, "SENTETIK", null), ContractId, default));
    }
    [Fact]
    public async Task Selected_source_last_price_measurement_redacts_the_native_over_limit_rule_after_owner_return()
    {
        var f = ContractReadFixture(); var c = Pending();
        var limited = SalesContract.Restore(c.Id, c.CompanyId, c.ContractNo, c.PartyId, c.CompanyAccountId, c.CurrencyId, c.MoneyProfileId, c.MoneyProfileVersion,
            c.MoneyScale, c.ValidFrom, c.ValidThrough, c.Installments, c.DiscountRules, c.Clauses, 3m, c.State, c.CreatedBy, c.CreatedAtUtc, null, c.Version, c.UpdatedAtUtc);
        f.Reader.Source = new(Company, limited);
        f.Authority.AfterRead = permission => { if (permission == SalesPermissionManifestV1.OrdersPriceRead && f.Authority.PriceReads == 3) f.Authority.PriceGranted = false; return Task.CompletedTask; };
        var source = new SalesContractDiscountWorkInboxSourceV1(new SalesWorkInboxApprovalFakeStore(), new CompanyContext(), new User(Manager), f.Authority, f.Service);
        var snapshot = await source.ReadDecisionAsync(new(Company, Manager, "SENTETIK last-price", null), ContractId, default); Assert.NotNull(snapshot);
        Assert.False(snapshot.SalesContractDiscountProof!.PriceDisclosed);
        Assert.Equal(new WorkApprovalRuleResultV1(SalesContractDiscountWorkInboxSourceV1.OverLimitRule, null, null, null, null), Assert.Single(snapshot.Item.Rules));
        Assert.Equal(SalesPermissionManifestV1.OrdersPriceRead, f.Authority.Asked[^1]); Assert.Equal(3, f.Authority.PriceReads);
    }
    [Fact]
    public async Task Source_defense_rejects_valid_found_maker_even_when_a_controlled_owner_omits_its_denied_answer()
    {
        var owner = new ControlledContractReader(SalesContractService.ProjectContractDiscountDecision(new(Company, Pending()), Today, SalesPriceDisclosure.Withheld));
        var source = new SalesContractDiscountWorkInboxSourceV1(new SalesWorkInboxApprovalFakeStore(), new CompanyContext(), new User(Seller), new Authority(), owner);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.ReadDecisionAsync(new(Company, Seller, "SENTETIK maker", null), ContractId, default));
        Assert.Equal(1, owner.Reads);
    }
    [Theory] [InlineData("company")] [InlineData("target")] [InlineData("rule")] [InlineData("digest")]
    public void Source_owned_identity_precheck_or_full_human_binding_rejects_controlled_polluted_facts(string fault)
    {
        var detail = SalesContractService.ProjectContractDiscountDecision(new(Company, Pending()), Today, SalesPriceDisclosure.Withheld);
        var p = SalesContractDiscountDecisionBindingV1.Proof(detail); var item = SalesContractDiscountDecisionBindingV1.Item(detail)!;
        if (fault == "company") p = p with { CompanyId = Guid.Empty }; if (fault == "target") item = item with { ItemId = Guid.NewGuid() };
        if (fault == "rule") item = item with { Rules = [new("sales.discount-limit-undefined", null, null, null, null)] };
        if (fault == "digest") p = p with { SourceFactsDigest = "bad" };
        Assert.False(SalesContractDiscountDecisionBindingV1.MatchesDecisionProof(item, p));
    }
    private sealed class ControlledContractReader(SalesContractDiscountDecisionDetailV1 detail) : ISalesContractDiscountDecisionReadV1
    {
        internal int Reads;
        public Task<SalesContractDiscountDecisionReadResultV1> ReadContractDiscountDecisionAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token = default)
        { Reads++; return Task.FromResult(new SalesContractDiscountDecisionReadResultV1(SalesContractDiscountDecisionReadStateV1.Found, detail)); }
        public Task<SalesContractDiscountOriginalVersionReadResultV1> ReadContractDiscountOriginalVersionAsync(Guid id, string version, Func<CancellationToken, Task> fence, CancellationToken token = default) => throw new NotSupportedException();
    }
    private static SalesContract OtherContract() { var p = Pending(); return SalesContract.Restore(Guid.NewGuid(), Company, 2, p.PartyId, p.CompanyAccountId, p.CurrencyId, p.MoneyProfileId, p.MoneyProfileVersion, p.MoneyScale, p.ValidFrom, p.ValidThrough, p.Installments, p.DiscountRules, p.Clauses, p.DiscountLimitPercent, p.State, p.CreatedBy, p.CreatedAtUtc, null, 1, p.CreatedAtUtc); }
    private static (SalesContractService Service, ContractDecisionReadStore Reader, Store Inner, ContractSafetyAuthority Authority, ContractSafetyUser User) ContractReadFixture(string actor = Manager)
    {
        var inner = new Store(); var reader = new ContractDecisionReadStore(inner) { Source = new(Company, Pending()) }; var authority = new ContractSafetyAuthority(); var user = new ContractSafetyUser(actor);
        return (new(new CompanyContext(), user, authority, new Correlation(), reader, new Accounts(), new Settings(), new IstanbulNoonClock()), reader, inner, authority, user);
    }
    private sealed class ContractDecisionReadStore(Store inner) : ISalesContractStore, ISalesContractDiscountDecisionReadStoreV1
    {
        internal SalesContractDiscountDecisionSourceV1? Source; internal int Reads;
        public async Task<SalesContractDiscountDecisionSourceV1?> ReadContractDiscountDecisionSourceAsync(Guid id, Func<CancellationToken,Task> fence, CancellationToken token = default)
        { await fence(token); Reads++; var source = Source; await fence(token); return source; }
        public Task<SalesContractDiscountOriginalReceiptV1?> ReadContractDiscountDecisionReceiptAsync(SalesDraftWriteEnvelope e, string h, string c, SalesContractCommandResult r, Func<CancellationToken,Task> f, CancellationToken t) => throw new NotSupportedException();
        public Task<SalesContractDiscountOriginalVersionReadResultV1> ReadContractDiscountOriginalVersionAsync(Guid id, long v, string a, Func<CancellationToken,Task> f, CancellationToken t) => Task.FromResult(new SalesContractDiscountOriginalVersionReadResultV1(SalesContractDiscountDecisionReadStateV1.Unavailable, null));
        public Task<SalesContractReplay?> FindContractReplayAsync(SalesDraftWriteEnvelope e, string h, string c, CancellationToken t = default) => inner.FindContractReplayAsync(e,h,c,t);
        public Task<SalesContractStoreResult> StoreContractAsync(SalesDraftWriteEnvelope e, string h, Func<int,DateTimeOffset,SalesContract> b, SalesDraftStateAudit a, CancellationToken t = default) => inner.StoreContractAsync(e,h,b,a,t);
        public Task RecordContractRefusalAsync(SalesDraftWriteEnvelope e,string h,string c,Guid id,SalesDraftTerminalRefusal r,CancellationToken t=default)=>inner.RecordContractRefusalAsync(e,h,c,id,r,t);
        public Task<SalesContractStoreResult?> DecideContractAsync(SalesDraftWriteEnvelope e,string h,string c,Guid id,Action<SalesContract,DateTimeOffset> m,SalesDraftStateAudit a,CancellationToken t=default)=>inner.DecideContractAsync(e,h,c,id,m,a,t);
        public Task<SalesContract?> FindContractAsync(Guid id,CancellationToken t=default)=>inner.FindContractAsync(id,t);
        public Task<IReadOnlyList<SalesContract>> ListContractsAsync(int n,CancellationToken t=default)=>inner.ListContractsAsync(n,t);
    }
    private const string ContractReceiptKey = "SENTETIK-contract-receipt";
    private static ContractReceiptFixtureV1 ContractReceiptFixture(bool approve)
    {
        var before = new SalesContractDiscountDecisionSourceV1(Company, Pending()); var after = SalesContractDiscountDecisionDigestsV1.Freeze(before); var at = Now.AddTicks(1);
        if (approve) after.Contract.ApproveDiscount(1, Manager, at); else after.Contract.RejectDiscount(1, Manager, "SENTETIK ret", at);
        var command = approve ? SalesContractCommands.ApproveDiscountV1 : SalesContractCommands.RejectDiscountV1;
        var response = new SalesContractCommandResult(SalesContractService.ProjectContractDiscountDecision(after, Today, SalesPriceDisclosure.Visible).Contract, false, "SENTETIK-original-contract");
        var key = SalesStore.IdempotencyKeyHash(ContractReceiptKey); var hash = SalesContractHash.Compute(command, ContractId, new("1", approve ? null : "SENTETIK ret"));
        var attempt = new ContractAttemptRow { CompanyId = Company, ContractId = ContractId, CommandName = command, ActorId = Manager, IdempotencyKeyHash = key, RequestHash = hash, Outcome = 1, CorrelationId = response.OriginalCorrelationId, RecordedAtUtc = at };
        var audit = new AuditEventEnvelope(Guid.NewGuid(), Company, at, approve ? "sales.contract.discount-approved.v1" : "sales.contract.discount-rejected.v1", "Sales.Contract", ContractId.ToString("D"),
            approve ? "ApproveContractDiscount" : "RejectContractDiscount", 1, 2, AuditActorTypes.User, Manager, SalesPermissionManifestV1.ContractsDiscountApprove, "[]", ContractFixtureAudit(before.Contract), ContractFixtureAudit(after.Contract), "SENTETIK karar", null, AuditAiInvolvement.None, null, "Web", null, null, response.OriginalCorrelationId, null, key, AuditOutcomes.Succeeded, 1);
        return new(before, after, response, attempt, audit, key, hash);
    }
    private static string ContractFixtureAudit(SalesContract c) => JsonSerializer.Serialize(new { c.Id, c.ContractNo, c.PartyId, c.CompanyAccountId, c.CurrencyId, c.MoneyProfileId, c.MoneyProfileVersion, c.MoneyScale, c.ValidFrom, c.ValidThrough,
        installments = SalesContractRows.SerializeInstallments(c.Installments), discountRules = SalesContractRows.SerializeDiscountRules(c.DiscountRules), c.Clauses, discountLimitPercent = c.DiscountLimitPercent?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        c.DiscountApprovalRequired, state = c.State.ToString(), version = c.Version, c.CreatedBy, c.CreatedAtUtc, decidedBy = c.Decision?.DecidedBy, decidedAtUtc = c.Decision?.DecidedAtUtc, rejectionReason = c.Decision?.RejectionReason }, SalesDraftJson.Options);
    private static SalesContractDiscountOriginalReceiptV1? RestoreContractReceipt(ContractReceiptFixtureV1 f) => SalesStore.RestoreOriginalContractDiscountReceipt(f.Attempt,
        [AuditEvent.Create(new(f.Audit, AuditEventIntegrity.Compute(f.Audit)))], Manager, f.KeyHash, f.Hash, f.Attempt.CommandName, f.Response, f.After);
    private sealed record ContractReceiptFixtureV1(SalesContractDiscountDecisionSourceV1 Before, SalesContractDiscountDecisionSourceV1 After,
        SalesContractCommandResult Response, ContractAttemptRow Attempt, AuditEventEnvelope Audit, string KeyHash, string Hash);
}
