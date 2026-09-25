using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Finance.UnitTests;

public sealed class DraftReadContextTests
{
    [Fact]
    public void ManualReceiptMoneyOperationIsPinnedWithoutBusinessValues()
    {
        Assert.Equal("finance.manual-bank-receipt.v1", FinanceMoneyOperations.ManualBankReceiptV1);
        Assert.DoesNotContain("currency", FinanceMoneyOperations.ManualBankReceiptV1, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("scale", FinanceMoneyOperations.ManualBankReceiptV1, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OwnerReadsSupplyEffectiveProfileCurrencyAndPartyLabels()
    {
        var f = new Fixture();
        var result = await f.Read();
        Assert.Equal(DraftReadOutcome.Found, result.Outcome);
        Assert.Equal(f.Company, result.Resource!.CompanyId);
        Assert.Equal("SENTETIK", result.Resource.Currency.DisplayCode);
        Assert.Equal("9007199254740993", result.Resource.Currency.Profile.Reference.Version);
        Assert.Equal("SENTETIK legal name", result.Resource.Party.LegalName);
        Assert.Equal(f.Request.OccurredOn, f.LastEffectiveOn);
        Assert.Equal("SENTETIK-operation", f.LastOperation);
        var json = JsonSerializer.Serialize(result.Resource, FinanceJson.CreateOptions());
        Assert.DoesNotContain("balance", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("amount", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(result.Resource, JsonSerializer.Deserialize<DraftReadContextView>(json, FinanceJson.CreateOptions()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoOwnerReadWithoutCompanyOrActionAuthority(bool companyAllowed)
    {
        var f = new Fixture { CompanyAllowed = companyAllowed, Permission = PermissionDecision.Denied };
        var result = await f.Read();
        Assert.Equal(DraftReadOutcome.Denied, result.Outcome);
        Assert.Null(result.Resource);
        Assert.Equal(0, f.BankReads);
    }

    [Fact]
    public async Task CompanyAccessIsCheckedBeforeThePermissionPortOrAnyOwner()
    {
        var f = new Fixture { CompanyAllowed = false, Permission = PermissionDecision.Allowed };
        var result = await f.Read();
        Assert.Equal(DraftReadOutcome.Denied, result.Outcome);
        Assert.Null(result.Resource);
        Assert.Equal(0, f.PermissionChecks);
        Assert.Equal(0, f.BankReads);
    }

    [Theory]
    [InlineData("treasury")]
    [InlineData("party")]
    [InlineData("account")]
    public async Task EmptyRequestIdentityIsInvalidBeforeOwnerReads(string field)
    {
        var f = new Fixture();
        f.Request = field switch
        {
            "treasury" => f.Request with { TreasuryAccountId = Guid.Empty },
            "party" => f.Request with { PartyId = Guid.Empty },
            _ => f.Request with { CompanyAccountId = Guid.Empty },
        };
        var result = await f.Read();
        Assert.Equal(DraftReadOutcome.InvalidRequest, result.Outcome);
        Assert.Equal("finance_draft_context_invalid", result.Code);
        Assert.Equal(0, f.BankReads);
    }

    [Theory]
    [InlineData("bank", "denied", DraftReadOutcome.Denied, null)]
    [InlineData("bank", "not-found", DraftReadOutcome.NotFound, null)]
    [InlineData("bank", "unavailable", DraftReadOutcome.Unavailable, "finance_bank_source_unavailable")]
    [InlineData("party", "denied", DraftReadOutcome.Denied, null)]
    [InlineData("party", "not-found", DraftReadOutcome.NotFound, null)]
    [InlineData("party", "unavailable", DraftReadOutcome.Unavailable, "finance_party_source_unavailable")]
    public async Task OwnerOutcomesPropagateWithoutReachingTheNextOwner(string stage, string completion,
        DraftReadOutcome expected, string? code)
    {
        var f = new Fixture();
        f.Complete(stage, completion);
        var result = await f.Read();
        Assert.Equal(expected, result.Outcome);
        Assert.Equal(code, result.Code);
        Assert.Null(result.Resource);
        if (stage == "bank") Assert.Equal(0, f.PartyReads);
        Assert.Equal(0, f.SettingsReads);
    }

    [Fact]
    public async Task MissingOperationMappingIsUnavailableBeforeOwnerReads()
    {
        var f = new Fixture();
        var result = await f.Reader.GetAsync(f.Request, null, "SENTETIK");
        Assert.Equal("finance_operation_mapping_unavailable", result.Code);
        Assert.Equal(0, f.BankReads);
    }

    [Theory]
    [InlineData("bank-company")]
    [InlineData("bank-inactive")]
    [InlineData("bank-not-usable")]
    [InlineData("bank-id-mismatch")]
    [InlineData("bank-currency-empty")]
    [InlineData("party-company")]
    [InlineData("party-inactive")]
    [InlineData("account-inactive")]
    public async Task IneligibleOwnerDataHasNoContextPayload(string defect)
    {
        var f = new Fixture();
        if (defect == "bank-company") f.Bank = f.Bank with { CompanyId = Guid.NewGuid() };
        if (defect == "bank-inactive") f.Bank = f.Bank with { IsActive = false };
        if (defect == "bank-not-usable") f.Bank = f.Bank with { IsUsableForReceipt = false };
        if (defect == "bank-id-mismatch") f.Bank = f.Bank with { Id = Guid.NewGuid() };
        if (defect == "bank-currency-empty") f.Bank = f.Bank with { CurrencyId = Guid.Empty };
        if (defect == "party-company") f.Party = f.Party with { CompanyId = Guid.NewGuid() };
        if (defect == "party-inactive") f.Party = f.Party with { IsPartyActive = false };
        if (defect == "account-inactive") f.Party = f.Party with { IsActive = false };
        var result = await f.Read();
        Assert.Equal(DraftReadOutcome.NotFound, result.Outcome);
        Assert.Null(result.Resource);
        Assert.Null(f.LastOperation);
    }

    [Theory]
    [InlineData("display-name-blank")]
    [InlineData("version-blank")]
    public async Task BankOwnerDataWithoutLabelOrVersionIsUnavailable(string defect)
    {
        var f = new Fixture();
        f.Bank = defect == "display-name-blank" ? f.Bank with { DisplayName = " " } : f.Bank with { Version = "" };
        var result = await f.Read();
        Assert.Equal(DraftReadOutcome.Unavailable, result.Outcome);
        Assert.Equal("finance_bank_source_invalid", result.Code);
        Assert.Null(result.Resource);
        Assert.Equal(0, f.PartyReads);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("actor")]
    public async Task ContextChangeDuringTheFirstPermissionCheckReachesNoOwner(string change)
    {
        var f = new Fixture();
        f.DuringFirstPermission = change == "company" ? () => f.Set(Guid.NewGuid()) : () => f.Actor = "SENTETIK-other";
        var result = await f.Read();
        Assert.Equal(DraftReadOutcome.Denied, result.Outcome);
        Assert.Null(result.Resource);
        Assert.Equal(1, f.PermissionChecks);
        Assert.Equal(0, f.BankReads);
    }

    [Theory]
    [InlineData("label-party")]
    [InlineData("label-account")]
    [InlineData("label-missing")]
    [InlineData("label-code-blank")]
    [InlineData("label-legal-name-blank")]
    [InlineData("label-party-version-blank")]
    [InlineData("label-account-version-blank")]
    public async Task PartyLabelNotBoundToTheAccountIsUnavailable(string defect)
    {
        var f = new Fixture();
        f.Party = defect switch
        {
            "label-party" => f.Party with { Label = f.Party.Label! with { PartyId = Guid.NewGuid() } },
            "label-account" => f.Party with { Label = f.Party.Label! with { CompanyAccountId = Guid.NewGuid() } },
            "label-code-blank" => f.Party with { Label = f.Party.Label! with { Code = " " } },
            "label-legal-name-blank" => f.Party with { Label = f.Party.Label! with { LegalName = "" } },
            "label-party-version-blank" => f.Party with { Label = f.Party.Label! with { PartyVersion = " " } },
            "label-account-version-blank" => f.Party with { Label = f.Party.Label! with { CompanyAccountVersion = "" } },
            _ => f.Party with { Label = null },
        };
        var result = await f.Read();
        Assert.Equal(DraftReadOutcome.Unavailable, result.Outcome);
        Assert.Equal("finance_party_source_invalid", result.Code);
        Assert.Null(result.Resource);
        Assert.Equal(0, f.SettingsReads);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("currency")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("operation")]
    [InlineData("metadata")]
    [InlineData("profile-id-empty")]
    [InlineData("version-zero")]
    [InlineData("inactive")]
    [InlineData("scale-negative")]
    [InlineData("currency-code-blank")]
    public async Task WrongOrStaleProfileFailsClosed(string defect)
    {
        var f = new Fixture();
        f.Profile = defect switch
        {
            "company" => f.Profile with { CompanyId = Guid.NewGuid() },
            "currency" => f.Profile with { CurrencyId = Guid.NewGuid() },
            "expired" => f.Profile with { EffectiveThrough = f.Request.OccurredOn.AddDays(-1) },
            "future" => f.Profile with { EffectiveFrom = f.Request.OccurredOn.AddDays(1) },
            "operation" => f.Profile with { Operation = "other" },
            "profile-id-empty" => f.Profile with { Profile = new(Guid.Empty, 1) },
            "version-zero" => f.Profile with { Profile = new(Guid.NewGuid(), 0) },
            "inactive" => f.Profile with { IsActive = false },
            "scale-negative" => f.Profile with { Scale = -1 },
            "currency-code-blank" => f.Profile with { CurrencyCode = " " },
            _ => f.Profile with { Profile = null! }
        };
        var result = await f.Read();
        Assert.Equal("settings_profile_invalid", result.Code);
        Assert.Null(result.Resource);
    }

    [Theory]
    [InlineData("settings_access_denied", DraftReadOutcome.Denied)]
    [InlineData("settings_provider_unavailable", DraftReadOutcome.Unavailable)]
    public async Task DistinguishesSettingsDenialAndMissingSource(string code, DraftReadOutcome expected)
    {
        var f = new Fixture { SettingsError = code };
        var result = await f.Read();
        Assert.Equal(expected, result.Outcome);
        Assert.Null(result.Resource);
    }

    [Fact]
    public async Task LateCompanySwitchCannotDisclosePriorContext()
    {
        var f = new Fixture();
        f.AfterSettings = () => f.Company = Guid.NewGuid();
        Assert.Equal(DraftReadOutcome.Denied, (await f.Read()).Outcome);
    }

    [Fact]
    public async Task RevokedPermissionCannotDiscloseReadResult()
    {
        var f = new Fixture();
        f.AfterSettings = () => f.Permission = PermissionDecision.Denied;
        Assert.Equal(DraftReadOutcome.Denied, (await f.Read()).Outcome);
    }

    public static TheoryData<string, string, string> PendingOwnerCompletions()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var stage in new[] { "bank", "party", "settings" })
            foreach (var completion in new[] { "found", "not-found", "unavailable", "throws" })
                foreach (var change in new[] { "company", "actor", "permission" })
                    data.Add(stage, completion, change);
        return data;
    }

    [Theory]
    [MemberData(nameof(PendingOwnerCompletions))]
    public async Task AuthorityChangeWhileAnOwnerIsPendingDeniesWithoutTheStaleOutcomeOrTheNextOwner(
        string stage, string completion, string change)
    {
        var f = new Fixture();
        f.Complete(stage, completion);
        f.During(stage, () =>
        {
            if (change == "company") f.Company = Guid.NewGuid();
            else if (change == "actor") f.Actor = "SENTETIK-other";
            else f.Permission = PermissionDecision.Denied;
        });

        var result = await f.Read();

        Assert.Equal(DraftReadOutcome.Denied, result.Outcome);
        Assert.Null(result.Resource);
        Assert.Null(result.Code);
        if (stage == "bank") Assert.Equal(0, f.PartyReads);
        if (stage != "settings") Assert.Equal(0, f.SettingsReads);
    }

    [Theory]
    [InlineData("bank")]
    [InlineData("party")]
    [InlineData("settings")]
    public async Task PermissionPortTurningUnavailableWhileAnOwnerIsPendingReportsItsOwnCode(string stage)
    {
        var f = new Fixture();
        f.Complete(stage, "not-found");
        f.During(stage, () => f.Permission = PermissionDecision.Unavailable);
        var result = await f.Read();
        Assert.Equal(DraftReadOutcome.Unavailable, result.Outcome);
        Assert.Equal("finance_read_mapping_unavailable", result.Code);
        Assert.Null(result.Resource);
    }

    [Theory]
    [InlineData("bank")]
    [InlineData("party")]
    [InlineData("settings")]
    public async Task AnOwnerFailureUnderCurrentAuthorityIsNotHidden(string stage)
    {
        var f = new Fixture();
        f.Complete(stage, "throws");
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Read());
    }

    [Fact]
    public async Task ActualUnavailableBankAdapterNeverReturnsEmptySuccess()
    {
        var f = new Fixture();
        var adapter = new UnavailableFinanceBankAccountReader(f, f);
        var result = await adapter.GetAuthorizedAsync(new(f.Company, "SENTETIK", "SENTETIK", null), f.Request.TreasuryAccountId, default);
        Assert.Equal(ReadOutcome.Unavailable, result.Outcome);
        Assert.Null(result.Resource);
        f.CompanyAllowed = false;
        Assert.Equal(ReadOutcome.Denied, (await adapter.ListAuthorizedAsync(new(f.Company, "SENTETIK", "SENTETIK", null), default)).Outcome);
    }

    private sealed class Fixture : ICompanyContext, ICurrentUser, IFinancePermissionPort,
        IFinanceBankAccountReader, IFinancePartyAccountPort, ISettingsPrerequisiteProvider
    {
        public Guid Company = Guid.NewGuid();
        public bool CompanyAllowed = true;
        public PermissionDecision Permission = PermissionDecision.Allowed;
        public string? SettingsError;
        public Action? AfterSettings;
        public Action? DuringFirstPermission;
        public Action? DuringBank;
        public Action? DuringParty;
        public string BankCompletion = "found";
        public string PartyCompletion = "found";
        public string SettingsCompletion = "found";
        public string Actor = "SENTETIK";
        public int BankReads;
        public int PartyReads;
        public int SettingsReads;
        public int PermissionChecks;
        public string? LastOperation;
        public DateOnly LastEffectiveOn;
        public BankAccountView Bank;
        public PartyAccountEligibility Party;
        public MoneyProfileV1 Profile;
        public DraftReadContextRequest Request;
        public Fixture()
        {
            var currency = Guid.NewGuid();
            Request = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new(2026, 9, 13));
            Bank = new(Request.TreasuryAccountId, Company, currency, "SENTETIK bank", true, true, "1");
            Party = new(Request.PartyId, Request.CompanyAccountId, Company, true,
                new(Request.PartyId, Request.CompanyAccountId, "SENTETIK-code", "SENTETIK legal name", null, "1", "1"), true);
            Profile = new(Company, currency, "SENTETIK", "SENTETIK-operation", new(Guid.NewGuid(), 9007199254740993L),
                0, true, Request.OccurredOn, Request.OccurredOn);
        }
        public DraftReadContextReader Reader => new(this, this, this, this, this, this);
        public void Complete(string stage, string completion)
        {
            if (stage == "bank") BankCompletion = completion;
            else if (stage == "party") PartyCompletion = completion;
            else SettingsCompletion = completion;
        }
        public void During(string stage, Action action)
        {
            if (stage == "bank") DuringBank = action;
            else if (stage == "party") DuringParty = action;
            else AfterSettings = action;
        }
        private static ReadResult<T> Owner<T>(string completion, T resource) where T : class => completion switch
        {
            "found" => new(ReadOutcome.Found, resource),
            "not-found" => new(ReadOutcome.NotFound, null),
            "unavailable" => new(ReadOutcome.Unavailable, null),
            "denied" => new(ReadOutcome.Denied, null),
            _ => throw new InvalidOperationException("SENTETIK owner failure"),
        };
        public Task<DraftReadContextResult> Read() => Reader.GetAsync(Request, "SENTETIK-operation", "SENTETIK");
        public bool IsResolved => true;
        public Guid CompanyId => Company;
        public void Set(Guid companyId) => Company = companyId;
        public bool IsAuthenticated => true;
        public string? UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => CompanyAllowed && companyId == Company;
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId, CancellationToken cancellationToken)
        {
            Assert.Equal(FinanceAction.ReadBankAccounts, action);
            PermissionChecks++;
            if (PermissionChecks == 1) DuringFirstPermission?.Invoke();
            return Task.FromResult(Permission);
        }
        public Task<ReadResult<BankAccountView>> GetAuthorizedAsync(FinanceContext context, Guid accountId, CancellationToken cancellationToken)
        {
            BankReads++;
            DuringBank?.Invoke();
            return Task.FromResult(Owner(BankCompletion, Bank));
        }
        public Task<ReadResult<BankAccountPage>> ListAuthorizedAsync(FinanceContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId, Guid companyAccountId,
            CancellationToken cancellationToken)
        {
            PartyReads++;
            DuringParty?.Invoke();
            return Task.FromResult(Owner(PartyCompletion, Party));
        }
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn, CancellationToken cancellationToken = default)
        {
            LastOperation = operation;
            LastEffectiveOn = effectiveOn;
            SettingsReads++;
            AfterSettings?.Invoke();
            if (SettingsError is { } code) return Task.FromException<MoneyProfileV1>(new SettingsPrerequisiteException(code));
            return SettingsCompletion switch
            {
                "found" => Task.FromResult(Profile),
                "not-found" => Task.FromException<MoneyProfileV1>(new SettingsPrerequisiteException("settings_profile_not_found")),
                "unavailable" => Task.FromException<MoneyProfileV1>(new SettingsPrerequisiteException("settings_provider_unavailable")),
                _ => Task.FromException<MoneyProfileV1>(new InvalidOperationException("SENTETIK owner failure")),
            };
        }
        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
