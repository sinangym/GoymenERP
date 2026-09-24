using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Finance.UnitTests;

public sealed class ManualBankPaymentDraftContextReaderTests
{
    [Fact]
    public void Money_operation_is_pinned_without_business_values()
    {
        Assert.Equal("finance.manual-bank-payment.v1", FinanceMoneyOperations.ManualBankPaymentV1);
        Assert.DoesNotContain("currency", FinanceMoneyOperations.ManualBankPaymentV1,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("scale", FinanceMoneyOperations.ManualBankPaymentV1,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Owner_reads_supply_only_the_company_bound_payment_context()
    {
        var fixture = new Fixture();

        var result = await fixture.ReadAsync();

        Assert.Equal(DraftReadOutcome.Found, result.Outcome);
        Assert.Null(result.Code);
        Assert.Equal(fixture.Company, result.Resource!.CompanyId);
        Assert.Equal(fixture.Request.TreasuryAccountId, result.Resource.TreasuryAccount.Id);
        Assert.True(result.Resource.TreasuryAccount.IsActive);
        Assert.True(result.Resource.TreasuryAccount.IsUsableForPayment);
        Assert.Equal("TRY", result.Resource.Currency.DisplayCode);
        Assert.Equal("9007199254740993", result.Resource.Currency.Profile.Reference.Version);
        Assert.Equal("SENTETIK supplier", result.Resource.Party.LegalName);
        Assert.Equal(FinanceMoneyOperations.ManualBankPaymentV1, fixture.LastOperation);
        Assert.Equal(fixture.Request.OccurredOn, fixture.LastEffectiveOn);
        Assert.Equal(4, fixture.PermissionChecks);
        Assert.Equal(1, fixture.TreasuryReads);
        Assert.Equal(1, fixture.PartyReads);
        Assert.Equal(1, fixture.SettingsReads);

        var json = JsonSerializer.Serialize(result.Resource, FinanceJson.CreateOptions());
        Assert.DoesNotContain("amount", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("balance", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(result.Resource,
            JsonSerializer.Deserialize<ManualBankPaymentDraftContextView>(json, FinanceJson.CreateOptions()));
    }

    [Theory]
    [InlineData(PermissionDecision.Denied, DraftReadOutcome.Denied, null)]
    [InlineData(PermissionDecision.Unavailable, DraftReadOutcome.Unavailable,
        "finance_manual_payment_read_mapping_unavailable")]
    public async Task Initial_permission_refusal_reaches_no_owner(PermissionDecision permission,
        DraftReadOutcome expected, string? code)
    {
        var fixture = new Fixture { Permission = permission };

        var result = await fixture.ReadAsync();

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(code, result.Code);
        Assert.Null(result.Resource);
        Assert.Equal(0, fixture.TreasuryReads);
        Assert.Equal(0, fixture.PartyReads);
        Assert.Equal(0, fixture.SettingsReads);
    }

    [Theory]
    [InlineData("treasury")]
    [InlineData("party")]
    [InlineData("account")]
    public async Task Empty_request_identity_is_invalid_before_owner_reads(string field)
    {
        var fixture = new Fixture();
        fixture.Request = field switch
        {
            "treasury" => fixture.Request with { TreasuryAccountId = Guid.Empty },
            "party" => fixture.Request with { PartyId = Guid.Empty },
            _ => fixture.Request with { CompanyAccountId = Guid.Empty },
        };

        var result = await fixture.ReadAsync();

        Assert.Equal(DraftReadOutcome.InvalidRequest, result.Outcome);
        Assert.Equal("finance_manual_payment_context_invalid", result.Code);
        Assert.Null(result.Resource);
        Assert.Equal(0, fixture.TreasuryReads);
    }

    [Theory]
    [InlineData("treasury", "denied", DraftReadOutcome.Denied, null)]
    [InlineData("treasury", "not-found", DraftReadOutcome.NotFound, null)]
    [InlineData("treasury", "unavailable", DraftReadOutcome.Unavailable,
        "finance_manual_payment_treasury_unavailable")]
    [InlineData("party", "denied", DraftReadOutcome.Denied, null)]
    [InlineData("party", "not-found", DraftReadOutcome.NotFound, null)]
    [InlineData("party", "unavailable", DraftReadOutcome.Unavailable,
        "finance_party_source_unavailable")]
    public async Task Owner_outcomes_propagate_without_reaching_the_next_owner(string stage,
        string completion, DraftReadOutcome expected, string? code)
    {
        var fixture = new Fixture();
        fixture.Complete(stage, completion);

        var result = await fixture.ReadAsync();

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(code, result.Code);
        Assert.Null(result.Resource);
        if (stage == "treasury") Assert.Equal(0, fixture.PartyReads);
        Assert.Equal(0, fixture.SettingsReads);
    }

    [Theory]
    [InlineData("id", DraftReadOutcome.NotFound, null)]
    [InlineData("company", DraftReadOutcome.NotFound, null)]
    [InlineData("currency", DraftReadOutcome.NotFound, null)]
    [InlineData("not-approved", DraftReadOutcome.NotFound, null)]
    [InlineData("not-payment-eligible", DraftReadOutcome.NotFound, null)]
    [InlineData("display-name-blank", DraftReadOutcome.Unavailable,
        "finance_manual_payment_treasury_invalid")]
    [InlineData("version-zero", DraftReadOutcome.Unavailable,
        "finance_manual_payment_treasury_invalid")]
    public async Task Ineligible_or_unversioned_treasury_never_becomes_payment_context(string defect,
        DraftReadOutcome expected, string? code)
    {
        var fixture = new Fixture();
        fixture.Treasury = defect switch
        {
            "id" => fixture.Treasury with { Id = Guid.NewGuid() },
            "company" => fixture.Treasury with { CompanyId = Guid.NewGuid() },
            "currency" => fixture.Treasury with { CurrencyId = Guid.Empty },
            "not-approved" => fixture.Treasury with { IsActive = false },
            "not-payment-eligible" => fixture.Treasury with { IsUsableForPayment = false },
            "display-name-blank" => fixture.Treasury with { DisplayName = " " },
            _ => fixture.Treasury with { Version = "0" },
        };

        var result = await fixture.ReadAsync();

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(code, result.Code);
        Assert.Null(result.Resource);
        Assert.Equal(0, fixture.PartyReads);
    }

    [Theory]
    [InlineData("company", DraftReadOutcome.NotFound, null)]
    [InlineData("party", DraftReadOutcome.NotFound, null)]
    [InlineData("account", DraftReadOutcome.NotFound, null)]
    [InlineData("account-inactive", DraftReadOutcome.NotFound, null)]
    [InlineData("party-inactive", DraftReadOutcome.NotFound, null)]
    [InlineData("missing-label", DraftReadOutcome.Unavailable, "finance_party_source_invalid")]
    [InlineData("label-account", DraftReadOutcome.Unavailable, "finance_party_source_invalid")]
    [InlineData("blank-version", DraftReadOutcome.Unavailable, "finance_party_source_invalid")]
    public async Task Party_owner_data_must_be_active_and_bound_to_its_label(string defect,
        DraftReadOutcome expected, string? code)
    {
        var fixture = new Fixture();
        fixture.Party = defect switch
        {
            "company" => fixture.Party with { CompanyId = Guid.NewGuid() },
            "party" => fixture.Party with { PartyId = Guid.NewGuid() },
            "account" => fixture.Party with { CompanyAccountId = Guid.NewGuid() },
            "account-inactive" => fixture.Party with { IsActive = false },
            "party-inactive" => fixture.Party with { IsPartyActive = false },
            "missing-label" => fixture.Party with { Label = null },
            "label-account" => fixture.Party with
            {
                Label = fixture.Party.Label! with { CompanyAccountId = Guid.NewGuid() },
            },
            _ => fixture.Party with
            {
                Label = fixture.Party.Label! with { CompanyAccountVersion = " " },
            },
        };

        var result = await fixture.ReadAsync();

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(code, result.Code);
        Assert.Null(result.Resource);
        Assert.Equal(0, fixture.SettingsReads);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("currency")]
    [InlineData("operation")]
    [InlineData("inactive")]
    [InlineData("scale-negative")]
    [InlineData("currency-code-blank")]
    [InlineData("future")]
    [InlineData("expired")]
    [InlineData("profile-missing")]
    [InlineData("profile-id-empty")]
    [InlineData("version-zero")]
    public async Task Wrong_or_stale_money_profile_fails_closed(string defect)
    {
        var fixture = new Fixture();
        fixture.Profile = defect switch
        {
            "company" => fixture.Profile with { CompanyId = Guid.NewGuid() },
            "currency" => fixture.Profile with { CurrencyId = Guid.NewGuid() },
            "operation" => fixture.Profile with { Operation = FinanceMoneyOperations.ManualBankReceiptV1 },
            "inactive" => fixture.Profile with { IsActive = false },
            "scale-negative" => fixture.Profile with { Scale = -1 },
            "currency-code-blank" => fixture.Profile with { CurrencyCode = " " },
            "future" => fixture.Profile with { EffectiveFrom = fixture.Request.OccurredOn.AddDays(1) },
            "expired" => fixture.Profile with { EffectiveThrough = fixture.Request.OccurredOn.AddDays(-1) },
            "profile-missing" => fixture.Profile with { Profile = null! },
            "profile-id-empty" => fixture.Profile with { Profile = new(Guid.Empty, 1) },
            _ => fixture.Profile with { Profile = new(Guid.NewGuid(), 0) },
        };

        var result = await fixture.ReadAsync();

        Assert.Equal(DraftReadOutcome.Unavailable, result.Outcome);
        Assert.Equal("settings_profile_invalid", result.Code);
        Assert.Null(result.Resource);
    }

    public static TheoryData<string, string> Pending_owner_completions()
    {
        var data = new TheoryData<string, string>();
        foreach (var stage in new[] { "treasury", "party", "settings" })
        foreach (var completion in new[] { "found", "not-found", "unavailable", "throws" })
            data.Add(stage, completion);
        return data;
    }

    [Theory]
    [MemberData(nameof(Pending_owner_completions))]
    public async Task Permission_revoked_during_an_owner_read_refuses_the_stale_result(
        string stage, string completion)
    {
        var fixture = new Fixture();
        fixture.Complete(stage, completion);
        fixture.During(stage, () => fixture.Permission = PermissionDecision.Denied);

        var result = await fixture.ReadAsync();

        Assert.Equal(DraftReadOutcome.Denied, result.Outcome);
        Assert.Null(result.Resource);
        Assert.Null(result.Code);
        if (stage == "treasury") Assert.Equal(0, fixture.PartyReads);
        if (stage != "settings") Assert.Equal(0, fixture.SettingsReads);
    }

    [Theory]
    [InlineData("treasury")]
    [InlineData("party")]
    [InlineData("settings")]
    public async Task Permission_mapping_lost_during_an_owner_read_reports_unavailable(string stage)
    {
        var fixture = new Fixture();
        fixture.During(stage, () => fixture.Permission = PermissionDecision.Unavailable);

        var result = await fixture.ReadAsync();

        Assert.Equal(DraftReadOutcome.Unavailable, result.Outcome);
        Assert.Equal("finance_manual_payment_read_mapping_unavailable", result.Code);
        Assert.Null(result.Resource);
    }

    [Theory]
    [InlineData("settings_access_denied", DraftReadOutcome.Denied)]
    [InlineData("settings_provider_unavailable", DraftReadOutcome.Unavailable)]
    public async Task Settings_refusal_is_distinguished_after_the_fresh_fence(string code,
        DraftReadOutcome expected)
    {
        var fixture = new Fixture { SettingsError = code };

        var result = await fixture.ReadAsync();

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(expected == DraftReadOutcome.Denied ? null : code, result.Code);
        Assert.Null(result.Resource);
    }

    [Theory]
    [InlineData("treasury", "finance_manual_payment_treasury_unavailable")]
    [InlineData("party", "finance_party_source_unavailable")]
    [InlineData("settings", "settings_provider_unavailable")]
    public async Task Owner_exception_under_current_authority_is_unavailable(string stage, string code)
    {
        var fixture = new Fixture();
        fixture.Complete(stage, "throws");

        var result = await fixture.ReadAsync();

        Assert.Equal(DraftReadOutcome.Unavailable, result.Outcome);
        Assert.Equal(code, result.Code);
        Assert.Null(result.Resource);
    }

    private sealed class Fixture : IFinancePermissionPort, IManualBankPaymentDraftContextTreasuryPort,
        IFinancePartyAccountPort, ISettingsPrerequisiteProvider
    {
        public Guid Company { get; } = Guid.NewGuid();
        public PermissionDecision Permission = PermissionDecision.Allowed;
        public string TreasuryCompletion = "found";
        public string PartyCompletion = "found";
        public string SettingsCompletion = "found";
        public string? SettingsError;
        public Action? DuringTreasury;
        public Action? DuringParty;
        public Action? DuringSettings;
        public int PermissionChecks;
        public int TreasuryReads;
        public int PartyReads;
        public int SettingsReads;
        public string? LastOperation;
        public DateOnly LastEffectiveOn;
        public ManualBankPaymentDraftContextRequest Request;
        public ManualBankPaymentTreasuryEligibility Treasury;
        public PartyAccountEligibility Party;
        public MoneyProfileV1 Profile;

        public Fixture()
        {
            var currency = Guid.NewGuid();
            Request = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 24));
            Treasury = new(Request.TreasuryAccountId, Company, currency, true, true,
                "SENTETIK payment bank", "7");
            Party = new(Request.PartyId, Request.CompanyAccountId, Company, true,
                new DraftPartyLabel(Request.PartyId, Request.CompanyAccountId, "SUP-004",
                    "SENTETIK supplier", null, "PARTY-3", "ACCOUNT-5"), true);
            Profile = new MoneyProfileV1(Company, currency, "TRY",
                FinanceMoneyOperations.ManualBankPaymentV1,
                new ProfileVersionV1(Guid.NewGuid(), 9007199254740993L), 2, true,
                new DateOnly(2026, 1, 1), null);
        }

        private ManualBankPaymentDraftContextReader Reader => new(this, this, this, this);

        public Task<ManualBankPaymentDraftContextResult> ReadAsync() => Reader.GetAuthorizedAsync(
            new FinanceContext(Company, "SENTETIK-actor", "SENTETIK-correlation", null), Request, default);

        public void Complete(string stage, string completion)
        {
            if (stage == "treasury") TreasuryCompletion = completion;
            else if (stage == "party") PartyCompletion = completion;
            else SettingsCompletion = completion;
        }

        public void During(string stage, Action action)
        {
            if (stage == "treasury") DuringTreasury = action;
            else if (stage == "party") DuringParty = action;
            else DuringSettings = action;
        }

        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action,
            Guid? sourceId, CancellationToken cancellationToken)
        {
            Assert.Equal(Company, context.CompanyId);
            Assert.Equal(FinanceAction.ReadManualBankPayments, action);
            Assert.Null(sourceId);
            PermissionChecks++;
            return Task.FromResult(Permission);
        }

        public Task<ReadResult<ManualBankPaymentTreasuryEligibility>> GetAsync(FinanceContext context,
            Guid treasuryAccountId, CancellationToken cancellationToken)
        {
            TreasuryReads++;
            DuringTreasury?.Invoke();
            return Task.FromResult(Owner(TreasuryCompletion, Treasury));
        }

        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context,
            Guid partyId, Guid companyAccountId, CancellationToken cancellationToken)
        {
            PartyReads++;
            DuringParty?.Invoke();
            return Task.FromResult(Owner(PartyCompletion, Party));
        }

        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation,
            DateOnly effectiveOn, CancellationToken cancellationToken = default)
        {
            SettingsReads++;
            LastOperation = operation;
            LastEffectiveOn = effectiveOn;
            DuringSettings?.Invoke();
            if (SettingsError is { } code)
                return Task.FromException<MoneyProfileV1>(new SettingsPrerequisiteException(code));
            return SettingsCompletion switch
            {
                "found" => Task.FromResult(Profile),
                "not-found" => Task.FromException<MoneyProfileV1>(
                    new SettingsPrerequisiteException("settings_profile_not_found")),
                "unavailable" => Task.FromException<MoneyProfileV1>(
                    new SettingsPrerequisiteException("settings_provider_unavailable")),
                _ => Task.FromException<MoneyProfileV1>(new InvalidOperationException(
                    "SENTETIK settings owner failure")),
            };
        }

        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn,
            DateOnly? valueOn, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private static ReadResult<T> Owner<T>(string completion, T resource) where T : class =>
            completion switch
            {
                "found" => new(ReadOutcome.Found, resource),
                "not-found" => new(ReadOutcome.NotFound, null),
                "unavailable" => new(ReadOutcome.Unavailable, null),
                "denied" => new(ReadOutcome.Denied, null),
                _ => throw new InvalidOperationException("SENTETIK owner failure"),
            };
    }
}
