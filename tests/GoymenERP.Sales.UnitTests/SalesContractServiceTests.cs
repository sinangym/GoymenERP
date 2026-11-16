using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.SalesContracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// S-11 service skeleton (K3; SAT-D28, NS-13, SAT-D346): the operation's own name is measured before anything is read, the replay
/// comes before any owner read, no limit source (or a null answer) means "not configured" so every discount goes to approval, a
/// limit source that cannot answer fails closed, the creator deciding their own discount and a stale version are terminal refusals
/// recorded under the key, and a rule refusal (missing reason, over-scale amount) writes nothing. SAT-D361: the amounts (plan and
/// planned total) are answered only beside the sales order price read granted company-wide, otherwise null beside Withheld; the
/// price read is asked, never required. SAT-D362: no end date is an open-ended contract. Every identity is synthetic.
/// </summary>
public sealed class SalesContractServiceTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Party = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Account = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Currency = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid Profile = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid ContractId = Guid.Parse("acacacac-acac-4cac-8cac-acacacacacac");
    private static readonly DateOnly Today = new(2026, 11, 15);
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);
    private const string Seller = "synthetic-seller";
    private const string Manager = "synthetic-manager";

    private static readonly SalesCommandMetadata Metadata = new("SENTETIK-key-1", "SENTETIK-correlation", null, Now);

    private static CreateSalesContractRequest Request(params SalesContractDiscountRuleRequest[] rules) => new(
        Party, Account, Currency, new DateOnly(2026, 11, 1), new DateOnly(2027, 10, 31),
        [new SalesContractInstallmentRequest(new DateOnly(2026, 12, 1), "1500"), new SalesContractInstallmentRequest(new DateOnly(2027, 1, 1), "1500.25")],
        rules, "SENTETIK teslim maddesi");

    private static SalesContractDiscountRuleRequest LineRule(string rate) => new(SalesContractApiV1.LineKind, "SENTETIK hacim", rate);

    private static SalesContract Pending(string createdBy = Seller) => SalesContract.Create(ContractId, Company, 1, Party, Account,
        new MoneyProfileSnapshot(Company, Currency, SalesOrderDraft.MoneyOperation, Profile, "3", Today, true, new DateOnly(2026, 9, 1),
            null, 2),
        new DateOnly(2026, 11, 1), new DateOnly(2027, 10, 31), [new SalesContractInstallmentInput(new DateOnly(2026, 12, 1), 1500m)],
        [new SalesContractDiscountRuleInput(SalesContractDiscountKind.Line, "SENTETIK hacim", 5m)], null, null, createdBy, Now);

    private static (SalesContractService Service, Store Store, Accounts Accounts, Authority Authority) Build(string actor = Seller,
        ISalesContractDiscountLimitSourceV1? limits = null)
    {
        var store = new Store();
        var accounts = new Accounts();
        var authority = new Authority();
        var service = new SalesContractService(new CompanyContext(), new User(actor), authority, new Correlation(), store, accounts,
            new Settings(), new IstanbulNoonClock(), limits);
        return (service, store, accounts, authority);
    }

    private static async Task<SalesDraftServiceException> Refused(Func<Task> act) =>
        await Assert.ThrowsAsync<SalesDraftServiceException>(act);

    [Fact]
    public async Task Create_without_the_write_permission_is_403_before_any_replay_or_owner_read()
    {
        var (service, store, accounts, authority) = Build();
        authority.Denied.Add(SalesPermissionManifestV1.ContractsWrite);

        var refused = await Refused(() => service.CreateContractAsync(Request(), Metadata));

        Assert.Equal((403, "forbidden"), (refused.Status, refused.Code));
        Assert.Empty(store.Calls);
        Assert.Equal(0, accounts.Calls);
    }

    [Fact]
    public async Task Without_a_limit_source_a_discount_is_recorded_pending_approval_and_the_limit_is_not_defined_SAT_D346()
    {
        var (service, store, _, authority) = Build();

        var result = await service.CreateContractAsync(Request(LineRule("0.5")), Metadata);

        Assert.False(result.IsReplay);
        Assert.Equal(("PendingDiscountApproval", "1", true, false, null), (result.Contract.State, result.Contract.Version,
            result.Contract.DiscountApprovalRequired, result.Contract.DiscountLimitDefined, result.Contract.DiscountLimitPercent));
        Assert.Equal("3000.25", result.Contract.PlannedTotalAmount);
        Assert.Equal(1, store.Calls.Count(call => call == "store"));
        Assert.Equal(SalesPermissionManifestV1.ContractsWrite, authority.Asked[0]);
    }

    [Fact]
    public async Task A_null_limit_answer_is_not_configured_and_still_sends_every_discount_to_approval()
    {
        var (service, _, _, _) = Build(limits: new Limits(null));

        var result = await service.CreateContractAsync(Request(LineRule("0.5")), Metadata);

        Assert.Equal(("PendingDiscountApproval", false), (result.Contract.State, result.Contract.DiscountLimitDefined));
    }

    [Fact]
    public async Task A_configured_limit_is_snapshotted_and_only_a_rate_above_it_needs_approval_NS_13()
    {
        var (within, _, _, _) = Build(limits: new Limits(10m));
        var active = await within.CreateContractAsync(Request(LineRule("10")), Metadata);
        Assert.Equal(("Active", false, true, "10", true), (active.Contract.State, active.Contract.DiscountApprovalRequired,
            active.Contract.DiscountLimitDefined, active.Contract.DiscountLimitPercent, active.Contract.IsInEffectToday));

        var (above, _, _, _) = Build(limits: new Limits(10m));
        var pending = await above.CreateContractAsync(Request(LineRule("12.5")), Metadata);
        Assert.Equal(("PendingDiscountApproval", true, "10", false), (pending.Contract.State, pending.Contract.DiscountApprovalRequired,
            pending.Contract.DiscountLimitPercent, pending.Contract.IsInEffectToday));
    }

    [Fact]
    public async Task A_contract_without_discount_rules_is_active_whatever_the_limit()
    {
        var (service, _, _, _) = Build();

        var result = await service.CreateContractAsync(Request(), Metadata);

        Assert.Equal(("Active", false), (result.Contract.State, result.Contract.DiscountApprovalRequired));
    }

    [Fact]
    public async Task A_limit_owner_that_cannot_answer_or_answers_out_of_range_fails_closed_and_nothing_is_stored()
    {
        var (unavailable, unavailableStore, _, _) = Build(limits: new Limits(error: new SalesContractDiscountLimitSourceException(false)));
        var down = await Refused(() => unavailable.CreateContractAsync(Request(LineRule("1")), Metadata));
        Assert.Equal((503, SalesDraftProblemCodesV1.DiscountLimitUnavailable), (down.Status, down.Code));
        Assert.DoesNotContain("store", unavailableStore.Calls);

        var (denied, deniedStore, _, _) = Build(limits: new Limits(error: new SalesContractDiscountLimitSourceException(true)));
        var forbidden = await Refused(() => denied.CreateContractAsync(Request(LineRule("1")), Metadata));
        Assert.Equal((403, "settings_access_denied"), (forbidden.Status, forbidden.Code));
        Assert.DoesNotContain("store", deniedStore.Calls);

        foreach (var answer in new[] { -1m, 100.5m, 1.23456m })
        {
            var (invalid, invalidStore, _, _) = Build(limits: new Limits(answer));
            var refused = await Refused(() => invalid.CreateContractAsync(Request(LineRule("1")), Metadata));
            Assert.Equal((503, SalesDraftProblemCodesV1.DiscountLimitUnavailable), (refused.Status, refused.Code));
            Assert.DoesNotContain("store", invalidStore.Calls);
        }
    }

    [Fact]
    public async Task A_stored_answer_under_the_key_is_replayed_before_the_customer_is_read()
    {
        var (service, store, accounts, _) = Build();
        store.Replay = new SalesContractReplay(Pending(), null, "SENTETIK-original-correlation");

        var result = await service.CreateContractAsync(Request(LineRule("5")), Metadata);

        Assert.True(result.IsReplay);
        Assert.Equal(("SENTETIK-original-correlation", ContractId), (result.OriginalCorrelationId, result.Contract.Id));
        Assert.Equal(0, accounts.Calls);
        Assert.Equal(["replay"], store.Calls);
    }

    [Fact]
    public async Task An_amount_off_the_money_profiles_scale_is_422_on_the_plan_and_nothing_is_rounded_or_stored()
    {
        var (service, store, _, _) = Build();
        var request = Request() with
        {
            Installments = [new SalesContractInstallmentRequest(new DateOnly(2026, 12, 1), "1500.255")],
        };

        var refused = await Refused(() => service.CreateContractAsync(request, Metadata));

        Assert.Equal((422, SalesContractProblemCodesV1.InstallmentsInvalid), (refused.Status, refused.Code));
        Assert.Equal("installments", Assert.Single(refused.Errors!).Field);
        Assert.Null(store.Stored);
    }

    [Fact]
    public async Task An_unknown_discount_kind_or_a_numeric_shape_error_is_400_before_anything_is_read()
    {
        var (service, store, accounts, _) = Build();

        var kind = await Refused(() => service.CreateContractAsync(
            Request(new SalesContractDiscountRuleRequest("Header", "SENTETIK", "1")), Metadata));
        var rate = await Refused(() => service.CreateContractAsync(Request(LineRule("1,5")), Metadata));

        Assert.Equal((400, 400), (kind.Status, rate.Status));
        Assert.Empty(store.Calls);
        Assert.Equal(0, accounts.Calls);
    }

    [Fact]
    public async Task An_authorized_approver_who_is_not_the_creator_activates_the_pending_contract()
    {
        var (service, store, _, authority) = Build(Manager);
        store.Stored = Pending();

        var result = await service.ApproveContractDiscountAsync(ContractId, new SalesDraftStateCommandRequest("1"), Metadata);

        Assert.Equal(("Active", "2", Manager, null), (result.Contract.State, result.Contract.Version, result.Contract.DecidedBy,
            result.Contract.RejectionReason));
        Assert.Equal(SalesPermissionManifestV1.ContractsDiscountApprove, authority.Asked[0]);
        Assert.Empty(store.Refusals);
    }

    [Fact]
    public async Task The_creator_approving_their_own_discount_is_a_recorded_409_and_the_contract_stays_pending()
    {
        var (service, store, _, _) = Build(Seller);
        store.Stored = Pending();

        var refused = await Refused(() => service.ApproveContractDiscountAsync(ContractId, new SalesDraftStateCommandRequest("1"), Metadata));

        Assert.Equal((409, SalesContractProblemCodesV1.SelfDecision), (refused.Status, refused.Code));
        var (command, recorded) = Assert.Single(store.Refusals);
        Assert.Equal((SalesContractCommands.ApproveDiscountV1, 409, SalesContractProblemCodesV1.SelfDecision),
            (command, recorded.Status, recorded.Code));
        Assert.Equal((SalesContractState.PendingDiscountApproval, 1L), (store.Stored.State, store.Stored.Version));
    }

    [Fact]
    public async Task A_recorded_self_decision_is_answered_again_under_the_same_key_without_a_second_decision()
    {
        var (service, store, _, _) = Build(Seller);
        store.Replay = new SalesContractReplay(null,
            new SalesDraftTerminalRefusal(409, SalesContractProblemCodesV1.SelfDecision, []), "SENTETIK-original-correlation");

        var refused = await Refused(() => service.RejectContractDiscountAsync(ContractId,
            new SalesDraftStateCommandRequest("1", "SENTETIK gerekçe"), Metadata));

        Assert.Equal((409, SalesContractProblemCodesV1.SelfDecision, true, "SENTETIK-original-correlation"),
            (refused.Status, refused.Code, refused.IsReplay, refused.OriginalCorrelationId));
        Assert.Equal(["replay"], store.Calls);
    }

    [Fact]
    public async Task Rejecting_stores_the_trimmed_reason_and_a_missing_reason_is_422_and_records_nothing()
    {
        var (service, store, _, _) = Build(Manager);
        store.Stored = Pending();

        var missing = await Refused(() => service.RejectContractDiscountAsync(ContractId, new SalesDraftStateCommandRequest("1", " "), Metadata));
        Assert.Equal((422, "validation_failed"), (missing.Status, missing.Code));
        Assert.Empty(store.Refusals);
        Assert.Equal(SalesContractState.PendingDiscountApproval, store.Stored.State);

        var tooLong = await Refused(() => service.RejectContractDiscountAsync(ContractId,
            new SalesDraftStateCommandRequest("1", new string('x', SalesContract.MaximumReasonLength + 1)), Metadata));
        Assert.Equal((422, SalesContractProblemCodesV1.ReasonTooLong), (tooLong.Status, tooLong.Code));
        Assert.Empty(store.Refusals);

        var result = await service.RejectContractDiscountAsync(ContractId, new SalesDraftStateCommandRequest("1", " SENTETIK oran yüksek "),
            Metadata);
        Assert.Equal(("DiscountRejected", "2", "SENTETIK oran yüksek", false), (result.Contract.State, result.Contract.Version,
            result.Contract.RejectionReason, result.Contract.IsInEffectToday));
    }

    [Fact]
    public async Task A_stale_expected_version_is_a_recorded_409_version_conflict()
    {
        var (service, store, _, _) = Build(Manager);
        store.Stored = Pending();

        var refused = await Refused(() => service.ApproveContractDiscountAsync(ContractId, new SalesDraftStateCommandRequest("2"), Metadata));

        Assert.Equal((409, SalesDraftProblemCodesV1.VersionConflict), (refused.Status, refused.Code));
        Assert.Equal(SalesContractCommands.ApproveDiscountV1, Assert.Single(store.Refusals).Command);
        Assert.Equal(SalesContractState.PendingDiscountApproval, store.Stored.State);
    }

    [Fact]
    public async Task Deciding_an_already_decided_contract_is_a_recorded_409_state_conflict()
    {
        var (service, store, _, _) = Build(Manager);
        var item = Pending();
        item.ApproveDiscount(1, Manager, Now);
        store.Stored = item;

        var refused = await Refused(() => service.ApproveContractDiscountAsync(ContractId, new SalesDraftStateCommandRequest("2"), Metadata));

        Assert.Equal((409, SalesDraftProblemCodesV1.StateConflict), (refused.Status, refused.Code));
        Assert.Single(store.Refusals);
    }

    [Fact]
    public async Task Deciding_without_the_approve_permission_or_with_a_malformed_version_writes_and_reads_nothing()
    {
        var (service, store, _, authority) = Build(Manager);
        authority.Denied.Add(SalesPermissionManifestV1.ContractsDiscountApprove);
        var forbidden = await Refused(() => service.ApproveContractDiscountAsync(ContractId, new SalesDraftStateCommandRequest("1"), Metadata));
        Assert.Equal((403, "forbidden"), (forbidden.Status, forbidden.Code));
        Assert.Empty(store.Calls);

        var (allowed, allowedStore, _, _) = Build(Manager);
        foreach (var version in new[] { "0", "-1", "x", "" })
        {
            var refused = await Refused(() => allowed.ApproveContractDiscountAsync(ContractId, new SalesDraftStateCommandRequest(version),
                Metadata));
            Assert.Equal((400, "validation_failed"), (refused.Status, refused.Code));
        }

        Assert.Empty(allowedStore.Calls);
    }

    [Fact]
    public async Task Deciding_or_reading_a_missing_contract_is_404()
    {
        var (service, _, _, _) = Build(Manager);

        var decided = await Refused(() => service.ApproveContractDiscountAsync(ContractId, new SalesDraftStateCommandRequest("1"), Metadata));
        var read = await Refused(() => service.GetContractAsync(ContractId));

        Assert.Equal((404, 404), (decided.Status, read.Status));
    }

    [Fact]
    public async Task Reading_needs_the_read_permission_and_lists_the_stored_contracts()
    {
        var (service, store, _, authority) = Build(Manager);
        store.Stored = Pending();

        var list = await service.GetContractsAsync();
        Assert.Equal(ContractId, Assert.Single(list.Items).Id);
        Assert.Equal(SalesPermissionManifestV1.ContractsRead, authority.Asked[0]);

        authority.Denied.Add(SalesPermissionManifestV1.ContractsRead);
        store.Calls.Clear();
        var refused = await Refused(() => service.GetContractsAsync());
        Assert.Equal((403, "forbidden"), (refused.Status, refused.Code));
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task With_the_price_read_the_contract_answers_its_amounts_and_asks_the_price_read_last_SAT_D361()
    {
        var (service, _, _, authority) = Build();

        var result = await service.CreateContractAsync(Request(LineRule("0.5")), Metadata);

        Assert.Equal(SalesPriceDisclosure.Visible, result.Contract.AmountDisclosure);
        Assert.Equal(["1500.00", "1500.25"], result.Contract.Installments.Select(item => item.Amount));
        Assert.Equal("3000.25", result.Contract.PlannedTotalAmount);
        Assert.Equal(SalesPermissionManifestV1.OrdersPriceRead, authority.Asked[^1]);
    }

    [Fact]
    public async Task Without_the_price_read_the_contract_is_still_recorded_and_answered_with_its_amounts_withheld_SAT_D361()
    {
        var (service, store, _, authority) = Build();
        authority.Denied.Add(SalesPermissionManifestV1.OrdersPriceRead);

        var result = await service.CreateContractAsync(Request(LineRule("0.5")), Metadata);

        Assert.Equal(1, store.Calls.Count(call => call == "store"));
        Assert.Equal(SalesPriceDisclosure.Withheld, result.Contract.AmountDisclosure);
        Assert.All(result.Contract.Installments, item => Assert.Null(item.Amount));
        Assert.Null(result.Contract.PlannedTotalAmount);
        // SAT-D363: dates and rule labels remain; discount percentages are withheld together with amounts.
        Assert.Equal([new DateOnly(2026, 12, 1), new DateOnly(2027, 1, 1)], result.Contract.Installments.Select(item => item.DueDate));
        Assert.Null(Assert.Single(result.Contract.DiscountRules).RatePercent);
        Assert.Equal(("PendingDiscountApproval", new DateOnly(2026, 11, 1), (DateOnly?)new DateOnly(2027, 10, 31), "SENTETIK teslim maddesi"),
            (result.Contract.State, result.Contract.ValidFrom, result.Contract.ValidThrough, result.Contract.Clauses));
    }

    [Fact]
    public async Task A_price_read_that_is_not_company_wide_withholds_the_amounts_on_reads_lists_and_replays_SAT_D361()
    {
        var (service, store, _, authority) = Build(Manager);
        authority.Narrow.Add(SalesPermissionManifestV1.OrdersPriceRead);
        store.Stored = Pending();

        var one = await service.GetContractAsync(ContractId);
        var list = await service.GetContractsAsync();
        store.Replay = new SalesContractReplay(Pending(), null, "SENTETIK-original-correlation");
        var replayed = await service.CreateContractAsync(Request(LineRule("5")), Metadata);

        foreach (var view in new[] { one, Assert.Single(list.Items), replayed.Contract })
        {
            Assert.Equal(SalesPriceDisclosure.Withheld, view.AmountDisclosure);
            Assert.Null(Assert.Single(view.Installments).Amount);
            Assert.Null(view.PlannedTotalAmount);
            Assert.All(view.DiscountRules, rule => Assert.Null(rule.RatePercent));
            Assert.Null(view.DiscountLimitPercent);
        }

        authority.Narrow.Clear();
        var visible = await service.GetContractAsync(ContractId);
        Assert.Equal((SalesPriceDisclosure.Visible, "1500.00", "1500.00"),
            (visible.AmountDisclosure, Assert.Single(visible.Installments).Amount, visible.PlannedTotalAmount));
    }

    [Fact]
    public async Task A_decision_answer_withholds_the_amounts_without_the_price_read_and_the_decision_still_holds_SAT_D361()
    {
        var (service, store, _, authority) = Build(Manager);
        authority.Denied.Add(SalesPermissionManifestV1.OrdersPriceRead);
        store.Stored = Pending();

        var result = await service.ApproveContractDiscountAsync(ContractId, new SalesDraftStateCommandRequest("1"), Metadata);

        Assert.Equal(("Active", SalesPriceDisclosure.Withheld, (string?)null), (result.Contract.State, result.Contract.AmountDisclosure,
            result.Contract.PlannedTotalAmount));
        Assert.Equal(SalesContractState.Active, store.Stored!.State);
    }

    [Fact]
    public async Task A_configured_discount_limit_and_rule_rate_are_withheld_without_changing_the_stored_contract_SAT_D363()
    {
        var (service, store, _, authority) = Build(limits: new Limits(10m));
        authority.Denied.Add(SalesPermissionManifestV1.OrdersPriceRead);
        var hidden = (await service.CreateContractAsync(Request(LineRule("0.5")), Metadata)).Contract;
        Assert.True(hidden.DiscountLimitDefined);
        Assert.Null(hidden.DiscountLimitPercent);
        Assert.Null(Assert.Single(hidden.DiscountRules).RatePercent);
        Assert.Equal(10m, store.Stored!.DiscountLimitPercent);
        Assert.Equal(0.5m, Assert.Single(store.Stored.DiscountRules).RatePercent);
        authority.Denied.Clear();
        var visible = await service.GetContractAsync(hidden.Id);
        Assert.Equal("10", visible.DiscountLimitPercent);
        Assert.Equal("0.5", Assert.Single(visible.DiscountRules).RatePercent);
    }

    [Fact]
    public async Task A_contract_without_an_end_date_is_recorded_open_ended_and_in_effect_SAT_D362()
    {
        var (service, store, _, _) = Build();

        var result = await service.CreateContractAsync(Request() with { ValidThrough = null }, Metadata);

        Assert.Equal(("Active", (DateOnly?)null, true), (result.Contract.State, result.Contract.ValidThrough, result.Contract.IsInEffectToday));
        Assert.Null(store.Stored!.ValidThrough);
        Assert.NotEqual(SalesContractHash.Compute(Request()), SalesContractHash.Compute(Request() with { ValidThrough = null }));
    }

    [Fact]
    public async Task An_end_date_before_the_start_is_still_422_and_nothing_is_stored_SAT_D362()
    {
        var (service, store, _, _) = Build();

        var refused = await Refused(() => service.CreateContractAsync(Request() with { ValidThrough = new DateOnly(2026, 10, 31) }, Metadata));

        Assert.Equal((422, SalesContractProblemCodesV1.ValidityInvalid), (refused.Status, refused.Code));
        Assert.Equal("validThrough", Assert.Single(refused.Errors!).Field);
        Assert.Null(store.Stored);
    }

    [Fact]
    public void Another_reason_under_the_same_key_is_a_different_request()
    {
        var first = SalesContractHash.Compute(SalesContractCommands.RejectDiscountV1, ContractId, new SalesDraftStateCommandRequest("1", "A"));
        var second = SalesContractHash.Compute(SalesContractCommands.RejectDiscountV1, ContractId, new SalesDraftStateCommandRequest("1", "B"));
        var approve = SalesContractHash.Compute(SalesContractCommands.ApproveDiscountV1, ContractId, new SalesDraftStateCommandRequest("1"));

        Assert.NotEqual(first, second);
        Assert.NotEqual(first, approve);
        Assert.NotEqual(SalesContractHash.Compute(Request(LineRule("1"))), SalesContractHash.Compute(Request(LineRule("2"))));
        Assert.Equal(SalesContractHash.Compute(Request(LineRule("1"))), SalesContractHash.Compute(Request(LineRule("1"))));
    }

    private sealed class CompanyContext : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId => Company;
        public void Set(Guid companyId) => throw new NotSupportedException();
    }

    private sealed class User(string actor) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }

    private sealed class Authority : IErpAuthorityRevalidationService
    {
        public HashSet<string> Denied { get; } = new(StringComparer.Ordinal);

        /// <summary>Granted, but only for some sites — never company-wide.</summary>
        public HashSet<string> Narrow { get; } = new(StringComparer.Ordinal);
        public List<string> Asked { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Asked.Add(permission);
            return Task.FromResult(Denied.Contains(permission)
                ? PermissionScope.Denied
                : new PermissionScope(true, !Narrow.Contains(permission), new HashSet<string>(StringComparer.Ordinal),
                    new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }

    private sealed class Correlation : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => "SENTETIK-correlation";
        public string? CausationId => null;
        public string SourceChannel => "Web";
        public string? ClientIpAddress => "127.0.0.1";
        public string? UserAgent => "SENTETIK-agent";

        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent)
        {
        }
    }

    /// <summary>SAT-D10: 09:00 UTC is noon of the same Europe/Istanbul business day.</summary>
    private sealed class IstanbulNoonClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Accounts : ISalesPartyAccountPortV1
    {
        public int Calls { get; private set; }

        public Task<SalesPartyAccountAnswerV1> GetAuthorizedAsync(Guid partyId, Guid companyAccountId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new SalesPartyAccountAnswerV1(SalesPartyAccountOutcomeV1.Found,
                new SalesPartyAccountV1(partyId, companyAccountId, Company, true, true, "1", "1")));
        }
    }

    private sealed class Settings : ISettingsPrerequisiteProvider
    {
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MoneyProfileV1(Company, currencyId, "TRY", operation, new ProfileVersionV1(Profile, 3), 2, true,
                new DateOnly(2026, 9, 1), null));

        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Limits(decimal? limit = null, Exception? error = null) : ISalesContractDiscountLimitSourceV1
    {
        public Task<decimal?> GetContractDiscountLimitPercentAsync(DateOnly effectiveOn, CancellationToken cancellationToken = default) =>
            error is null ? Task.FromResult(limit) : throw error;
    }

    private sealed class Store : ISalesContractStore
    {
        public List<string> Calls { get; } = [];
        public SalesContractReplay? Replay { get; set; }
        public SalesContract? Stored { get; set; }
        public List<(string Command, SalesDraftTerminalRefusal Refusal)> Refusals { get; } = [];

        public Task<SalesContractReplay?> FindContractReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("replay");
            return Task.FromResult(Replay);
        }

        public Task<SalesContractStoreResult> StoreContractAsync(SalesDraftWriteEnvelope envelope, string requestHash,
            Func<int, DateTimeOffset, SalesContract> build, SalesDraftStateAudit audit, CancellationToken cancellationToken = default)
        {
            Calls.Add("store");
            Stored = build(1, Now);
            return Task.FromResult(new SalesContractStoreResult(Stored, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId));
        }

        public Task RecordContractRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName, Guid contractId,
            SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken = default)
        {
            Calls.Add("refusal");
            Refusals.Add((commandName, refusal));
            return Task.CompletedTask;
        }

        public Task<SalesContractStoreResult?> DecideContractAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
            Guid contractId, Action<SalesContract, DateTimeOffset> mutate, SalesDraftStateAudit audit,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("decide");
            if (Stored is null || Stored.Id != contractId)
            {
                return Task.FromResult<SalesContractStoreResult?>(null);
            }

            mutate(Stored, Now);
            return Task.FromResult<SalesContractStoreResult?>(
                new SalesContractStoreResult(Stored, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId));
        }

        public Task<SalesContract?> FindContractAsync(Guid contractId, CancellationToken cancellationToken = default)
        {
            Calls.Add("find");
            return Task.FromResult(Stored is not null && Stored.Id == contractId ? Stored : null);
        }

        public Task<IReadOnlyList<SalesContract>> ListContractsAsync(int limit, CancellationToken cancellationToken = default)
        {
            Calls.Add("list");
            return Task.FromResult<IReadOnlyList<SalesContract>>(Stored is null ? [] : [Stored]);
        }
    }
}
