using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinanceCollateralPolicyFiguresTests
{
    private static readonly Guid Company = Guid.NewGuid(), Type = Guid.NewGuid(), Currency = Guid.NewGuid(), Collateral = Guid.NewGuid(), Bank = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 11, 18);
    private static readonly FinanceCollateralTypeCatalogAnswer Catalog = new(FinanceCollateralTypeCatalogState.Available,
        [new(Type, "SYN", "Synthetic", true, "100", "7", false)]);

    [Fact]
    public async Task The_actual_scoped_rate_and_factual_revision_are_preserved_without_recalculating_or_rounding_the_amount()
    {
        var owner = new Policies(new(true, "33.33", "settings.collateral-rate:synthetic:9", "TypeBankRemainingDays", null, "7"));
        var result = await FinanceCollateralPolicyFigures.WeighAsync(Company, [Held()], Catalog, owner, Today, default);
        Assert.True(result.IsAvailable);
        Assert.Equal("0.003333", Assert.Single(result.Amounts).Amount);
        var proof = Assert.Single(result.PolicyProofs!);
        Assert.Equal((Company, Collateral, "4", Type, "7", Bank, "settings.bank-institutions:synthetic:3", "33.33", Today),
            (proof.CompanyId, proof.CollateralId, proof.CollateralRevision, proof.CollateralTypeId, proof.TypeVersion,
                proof.BankInstitutionId, proof.BankSourceVersion, proof.RatePercent, proof.BusinessDate));
        Assert.Equal((Company, Type, Bank, Today.AddDays(20), Today), owner.Last);
    }

    [Fact]
    public async Task A_missing_scoped_rate_does_not_fall_back_to_the_type_or_return_a_partial_sum()
    {
        var owner = new Policies(new(false, null, null, null, "source_unavailable"));
        var unavailable = await FinanceCollateralPolicyFigures.WeighAsync(Company, [Held()], Catalog, owner, Today, default);
        Assert.False(unavailable.IsAvailable); Assert.Empty(unavailable.Amounts); Assert.Null(unavailable.PolicyProofs);
        var absentOwner = await FinanceCollateralPolicyFigures.WeighAsync(Company, [Held()], Catalog, null, Today, default);
        Assert.False(absentOwner.IsAvailable);
    }

    [Fact]
    public async Task A_real_zero_rate_is_available_and_an_unbound_legacy_record_uses_only_its_real_type_source()
    {
        var owner = new Policies(new(true, "0", "settings.collateral-type:synthetic:7", "Type", null, "7"));
        var result = await FinanceCollateralPolicyFigures.WeighAsync(Company,
            [Held() with { BankInstitutionId = null, BankSourceVersion = null }], Catalog, owner, Today, default);
        Assert.True(result.IsAvailable); Assert.Equal("0", Assert.Single(result.Amounts).Amount);
        Assert.Null(Assert.Single(result.PolicyProofs!).BankInstitutionId);
    }

    [Fact]
    public void Legacy_null_bank_request_members_do_not_change_the_ADR011_wire_payload()
    {
        var request = new PartyCollateralRecordRequest(Guid.NewGuid(), Guid.NewGuid(), Type, Currency, "1", Today, null, null);
        var json = JsonSerializer.Serialize(request, FinanceJson.CreateOptions());
        Assert.DoesNotContain("bankInstitutionId", json); Assert.DoesNotContain("bankSourceVersion", json);
        Assert.Equal(FinancePartyCollateralService.PayloadHash("record", request),
            FinancePartyCollateralService.PayloadHash("record", request with { BankInstitutionId = null, BankSourceVersion = null }));
        Assert.NotEqual(FinancePartyCollateralService.PayloadHash("record", request),
            FinancePartyCollateralService.PayloadHash("record", request with { BankInstitutionId = Bank, BankSourceVersion = "source:1" }));
    }

    [Fact]
    public async Task A_type_changing_between_the_catalog_and_scoped_rate_read_never_produces_a_mixed_version_proof()
    {
        var owner = new Policies(new(true, "40", "settings.collateral-type:synthetic:8", "Type", null, "8"));
        var result = await FinanceCollateralPolicyFigures.WeighAsync(Company,
            [Held() with { BankInstitutionId = null, BankSourceVersion = null }], Catalog, owner, Today, default);
        Assert.False(result.IsAvailable); Assert.Null(result.PolicyProofs);
    }

    private static FinancePartyCollateralFigures.HeldCollateral Held() => new(Type, Currency, "0.01", Today.AddDays(-70), Today.AddDays(20),
        Collateral, 4, Bank, "settings.bank-institutions:synthetic:3");
    private sealed class Policies(FinanceCollateralRateReadV1 rate) : IFinanceCollateralPolicyReaderV1
    {
        public (Guid Company, Guid Type, Guid? Bank, DateOnly? Until, DateOnly Day) Last { get; private set; }
        public Task<FinanceCollateralBankCatalogV1> GetBankInstitutionsAsync(Guid companyId, DateOnly businessDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FinanceCollateralRateReadV1> GetRateAsync(Guid companyId, Guid collateralTypeId, Guid? bankInstitutionId,
            string? bankSourceVersion, DateOnly? validUntil, DateOnly businessDate, CancellationToken cancellationToken = default)
        { Last = (companyId, collateralTypeId, bankInstitutionId, validUntil, businessDate); return Task.FromResult(rate); }
    }
}
