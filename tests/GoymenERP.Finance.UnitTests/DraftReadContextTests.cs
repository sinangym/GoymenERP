using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Finance.UnitTests;

public sealed class DraftReadContextTests
{
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
    [InlineData("party-company")]
    [InlineData("party-inactive")]
    [InlineData("account-inactive")]
    public async Task IneligibleOwnerDataHasNoContextPayload(string defect)
    {
        var f = new Fixture();
        if (defect == "bank-company") f.Bank = f.Bank with { CompanyId = Guid.NewGuid() };
        if (defect == "bank-inactive") f.Bank = f.Bank with { IsActive = false };
        if (defect == "party-company") f.Party = f.Party with { CompanyId = Guid.NewGuid() };
        if (defect == "party-inactive") f.Party = f.Party with { IsPartyActive = false };
        if (defect == "account-inactive") f.Party = f.Party with { IsActive = false };
        var result = await f.Read();
        Assert.Equal(DraftReadOutcome.NotFound, result.Outcome);
        Assert.Null(result.Resource);
        Assert.Null(f.LastOperation);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("currency")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("operation")]
    [InlineData("metadata")]
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
        public int BankReads;
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
        public Task<DraftReadContextResult> Read() => Reader.GetAsync(Request, "SENTETIK-operation", "SENTETIK");
        public bool IsResolved => true;
        public Guid CompanyId => Company;
        public void Set(Guid companyId) => Company = companyId;
        public bool IsAuthenticated => true;
        public string? UserId => "SENTETIK";
        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => CompanyAllowed && companyId == Company;
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId, CancellationToken cancellationToken)
        {
            Assert.Equal(FinanceAction.ReadBankAccounts, action);
            return Task.FromResult(Permission);
        }
        public Task<ReadResult<BankAccountView>> GetAuthorizedAsync(FinanceContext context, Guid accountId, CancellationToken cancellationToken)
        {
            BankReads++;
            return Task.FromResult(new ReadResult<BankAccountView>(ReadOutcome.Found, Bank));
        }
        public Task<ReadResult<BankAccountPage>> ListAuthorizedAsync(FinanceContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId, Guid companyAccountId,
            CancellationToken cancellationToken) => Task.FromResult(new ReadResult<PartyAccountEligibility>(ReadOutcome.Found, Party));
        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn, CancellationToken cancellationToken = default)
        {
            LastOperation = operation;
            LastEffectiveOn = effectiveOn;
            AfterSettings?.Invoke();
            return SettingsError is { } code ? Task.FromException<MoneyProfileV1>(new SettingsPrerequisiteException(code)) : Task.FromResult(Profile);
        }
        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
