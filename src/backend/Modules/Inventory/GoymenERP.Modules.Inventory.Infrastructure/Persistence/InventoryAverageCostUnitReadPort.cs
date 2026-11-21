using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// KLN-D85 (STK-SC33-a): the Inventory-owned company-wide average cost read in a requested unit (contract V1). It reads the
/// company-wide per-kilogram answer (KLN-D83 / KLN-D84), and only for a known figure resolves the unit through the Settings
/// unit identities, the Settings unit conversion policy in force on the caller's date (KIM-D37) and the product's kilograms
/// per unit (<see cref="IInventoryKilogramsPerUnitSourceV1"/>). The arithmetic is <see cref="AverageCostUnitConversion"/>,
/// the one deterministic rule. Every missing or unreadable input refuses the conversion with a code (fail-closed); none
/// has a default. A kilogram unit needs no conversion and reads no policy. The port carries no authority and writes nothing.
/// </summary>
internal sealed class InventoryAverageCostUnitReadPort(
    IInventoryCompanyWideAverageCostReadPortV1 companyWide,
    ISettingsUnitIdentityCatalogV1? units,
    ISettingsUnitConversionPolicyProviderV1? policies,
    IInventoryKilogramsPerUnitSourceV1 kilogramsPerUnit) : IInventoryAverageCostUnitReadPortV1
{
    public async Task<InventoryAverageCostUnitReadAnswerV1> ReadCompanyWideInUnitAsync(
        Guid productId, Guid unitId, DateOnly policyDate, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (unitId == Guid.Empty)
        {
            throw new ArgumentException("The unit is named.", nameof(unitId));
        }

        var perKg = await companyWide.ReadCompanyWideAsync(productId, cancellationToken);
        if (perKg.Status != InventoryAverageCostReadStatusV1.Value)
        {
            return InventoryAverageCostUnitReadAnswerV1.ForUnconvertible(perKg, unitId);
        }

        if (units is null)
        {
            return Refused(perKg, unitId, InventoryAverageCostUnitConversionRefusalCodesV1.UnitIdentityUnavailable);
        }

        UnitIdentityV1? identity;
        try
        {
            identity = await units.ReadAsync(unitId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Refused(perKg, unitId, InventoryAverageCostUnitConversionRefusalCodesV1.UnitIdentityUnavailable);
        }

        if (identity is null || identity.UnitId != unitId)
        {
            return Refused(perKg, unitId, InventoryAverageCostUnitConversionRefusalCodesV1.UnitUnknown);
        }

        if (identity.IsKilogram)
        {
            return InventoryAverageCostUnitReadAnswerV1.ForKilogram(perKg, unitId);
        }

        // KLN-D85: a unit other than the kilogram is converted only with the policy in force; no policy, no conversion.
        if (policies is null)
        {
            return Refused(perKg, unitId, InventoryAverageCostUnitConversionRefusalCodesV1.PolicyUnreadable);
        }

        SettingsUnitConversionPolicyV1 policy;
        try
        {
            policy = await policies.GetUnitConversionPolicyAsync(policyDate, cancellationToken);
        }
        catch (SettingsPrerequisiteException exception)
            when (exception.Code == SettingsPrerequisiteCodesV1.UnitConversionPolicyNotFound)
        {
            return Refused(perKg, unitId, InventoryAverageCostUnitConversionRefusalCodesV1.PolicyNotFound);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Refused(perKg, unitId, InventoryAverageCostUnitConversionRefusalCodesV1.PolicyUnreadable);
        }

        if (policy is null || policy.CompanyId != perKg.CompanyId || policy.Profile is null
            || policy.Profile.ProfileId == Guid.Empty || policy.Profile.Version < 1
            || policy.CoefficientDecimals is < 0 or > AverageCostUnitConversion.MaximumCoefficientDecimals
            || !Enum.IsDefined(policy.CoefficientRounding) || policy.DensityGramsPerCubicCentimetre <= 0m)
        {
            return Refused(perKg, unitId, InventoryAverageCostUnitConversionRefusalCodesV1.PolicyUnreadable);
        }

        var profileId = policy.Profile.ProfileId;
        var version = policy.Profile.Version;
        InventoryKilogramsPerUnitV1 weight;
        try
        {
            weight = await kilogramsPerUnit.ReadAsync(
                productId, unitId, policyDate, policy.DensityGramsPerCubicCentimetre, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Refused(perKg, unitId, InventoryAverageCostUnitConversionRefusalCodesV1.KilogramsPerUnitUnavailable,
                profileId, version);
        }

        switch (weight)
        {
            case { Outcome: InventoryKilogramsPerUnitOutcomeV1.Found, KilogramsPerUnit: > 0m }:
                break;
            case { Outcome: InventoryKilogramsPerUnitOutcomeV1.NoSource }:
                return Refused(perKg, unitId, InventoryAverageCostUnitConversionRefusalCodesV1.KilogramsPerUnitNoSource,
                    profileId, version);
            default:
                // Unavailable, an unknown outcome, or a "found" without a positive figure: never a guess.
                return Refused(perKg, unitId, InventoryAverageCostUnitConversionRefusalCodesV1.KilogramsPerUnitUnavailable,
                    profileId, version);
        }

        var result = AverageCostUnitConversion.Convert(
            perKg.AverageCostPerKg!.Value, weight.KilogramsPerUnit!.Value, policy.CoefficientDecimals, policy.CoefficientRounding);
        return result.Status switch
        {
            AverageCostUnitConversionStatus.Converted => InventoryAverageCostUnitReadAnswerV1.ForConverted(
                perKg, unitId, result.CostPerUnit!.Value, result.Coefficient, profileId, version),
            AverageCostUnitConversionStatus.CoefficientNotPositive => InventoryAverageCostUnitReadAnswerV1.ForRefused(
                perKg, unitId, InventoryAverageCostUnitConversionRefusalCodesV1.CoefficientNotPositive, result.Coefficient,
                profileId, version),
            _ => throw new InvalidOperationException("Birim maliyet çevrimi tanınmayan durumda."),
        };
    }

    private static InventoryAverageCostUnitReadAnswerV1 Refused(
        InventoryCompanyWideAverageCostReadAnswerV1 perKg, Guid unitId, string code, Guid? profileId = null,
        long? version = null) =>
        InventoryAverageCostUnitReadAnswerV1.ForRefused(perKg, unitId, code, null, profileId, version);
}

/// <summary>
/// KLN-D85 module default for <see cref="IInventoryKilogramsPerUnitSourceV1"/>: no source. Inventory cannot reach the Product
/// owner by itself, so without a host adapter (AK-1 registers one over the Product owner's theoretical kg/unit read) every
/// non-kilogram conversion is refused (fail-closed). Nothing here computes a weight.
/// </summary>
public sealed class NoSourceInventoryKilogramsPerUnitSourceV1 : IInventoryKilogramsPerUnitSourceV1
{
    public Task<InventoryKilogramsPerUnitV1> ReadAsync(Guid productId, Guid unitId, DateOnly policyDate,
        decimal densityGramsPerCubicCentimetre, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new InventoryKilogramsPerUnitV1(InventoryKilogramsPerUnitOutcomeV1.NoSource, null, null));
    }
}
