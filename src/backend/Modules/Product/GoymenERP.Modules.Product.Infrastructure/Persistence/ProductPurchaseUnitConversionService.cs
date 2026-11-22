using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Domain.UnitConversion;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>
/// Product-owned B-07 source. No new formula, unit classification by name, density default or typed coefficient.
/// BS-9 chooses the stored dimension row. KLN-D66 forbids a round-tube formula fallback without a company table.
/// KLN-D67 reads the current card by ProductId; differing Sales/Purchasing card revisions do not change identity.
/// </summary>
public sealed class ProductPurchaseUnitConversionService(ICompanyContext company, ICurrentUser user,
    IProductPurchaseRecordReader records, IProductDimensionReaderV1 dimensions,
    ISettingsPhysicalUnitSemanticsV1 semantics, ProductUnitProfileAdapter profiles,
    ISettingsUnitConversionPolicyProviderV1 policies) : IProductPurchaseUnitConversionV1
{
    private const string NoSourceCode = "product_unit_conversion_no_source";
    private const string InvalidCode = "product_unit_conversion_invalid";
    private const string UnavailableCode = "product_unit_conversion_unavailable";

    /// <summary>The source reason when the card's kg/m weight-table rows disagree (BS-13).</summary>
    internal const string WeightConflictReason = "product_profile_weight_conflict";

    public async Task<ProductPurchaseUnitConversionReadV1> ReadAsync(Guid productId, DateOnly effectiveOn,
        CancellationToken cancellationToken = default) => (await ReadCoreAsync(productId, effectiveOn, cancellationToken)).Read;

    public async Task<ProductPurchaseUnitConversionResultV1> ConvertAsync(ProductPurchaseUnitConversionRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var state = await ReadCoreAsync(request.ProductId, request.EffectiveOn, cancellationToken);
        return Convert(state, request);
    }

    public async Task<ProductPurchaseWeighbridgeResultV1> CheckWeighbridgeAsync(ProductPurchaseWeighbridgeRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var state = await ReadCoreAsync(request.ProductId, request.EffectiveOn, cancellationToken);
        var kg = Convert(state, new(request.ProductId, request.Quantity, request.UnitId, null, request.EffectiveOn));
        if (kg.Outcome != ProductPurchaseUnitConversionOutcomeV1.Available || kg.Quantity is not { } theoretical)
            return new(kg.Outcome, null, null, kg.Source, kg.Code);
        if (state.Settings is null)
            return new(ProductPurchaseUnitConversionOutcomeV1.NoSource, null, theoretical, kg.Source, NoSourceCode);
        var check = ProductUnitConversionCalculatorV1.WithinWeighbridgeTolerance(request.WeighedKg, theoretical, state.Settings);
        return check.Outcome == UnitConversionOutcomeV1.Computed
            ? new(ProductPurchaseUnitConversionOutcomeV1.Available, check.Value, theoretical, kg.Source)
            : new(ProductPurchaseUnitConversionOutcomeV1.Refused, null, theoretical, kg.Source, InvalidCode);
    }

    private static ProductPurchaseUnitConversionResultV1 Convert(State state, ProductPurchaseUnitConversionRequestV1 request)
    {
        var read = state.Read;
        if (read.Outcome != ProductPurchaseUnitConversionOutcomeV1.Available || read.Source is not { } source)
            return new(read.Outcome, null, null, null, read.Source, read.Code);
        if (request.Quantity < 0 || request.FromUnitId == Guid.Empty || request.ToUnitId == Guid.Empty)
            return new(ProductPurchaseUnitConversionOutcomeV1.Refused, null, null, null, source, InvalidCode);
        var from = source.Units.SingleOrDefault(unit => unit.UnitId == request.FromUnitId);
        var to = request.ToUnitId is { } target ? source.Units.SingleOrDefault(unit => unit.UnitId == target) : null;
        if (from is not { KgPerUnit: > 0m } || (request.ToUnitId is not null && to is not { KgPerUnit: > 0m }))
            return new(ProductPurchaseUnitConversionOutcomeV1.NoSource, null, null, null, source, NoSourceCode);
        try
        {
            var kgResult = ProductUnitConversionCalculatorV1.UnitsToKg(request.Quantity, from.KgPerUnit);
            if (kgResult is not { Outcome: UnitConversionOutcomeV1.Computed, Value: { } kg })
                return new(ProductPurchaseUnitConversionOutcomeV1.Refused, null, null, null, source, InvalidCode);
            if (request.ToUnitId is null) return new(ProductPurchaseUnitConversionOutcomeV1.Available, kg, kg, 0m, source);
            if (request.FromUnitId == request.ToUnitId)
                return new(ProductPurchaseUnitConversionOutcomeV1.Available, request.Quantity, kg, 0m, source);
            if (to!.Kind == ProductPhysicalUnitKindV1.Piece)
            {
                if (state.Settings is null)
                    return new(ProductPurchaseUnitConversionOutcomeV1.NoSource, null, null, null, source, NoSourceCode);
                var count = ProductUnitConversionCalculatorV1.KgToWholeUnits(kg, to.KgPerUnit, state.Settings);
                if (count is not { Outcome: UnitConversionOutcomeV1.Computed, Value: { } pieces })
                    return new(ProductPurchaseUnitConversionOutcomeV1.Refused, null, null, null, source, InvalidCode);
                return new(ProductPurchaseUnitConversionOutcomeV1.Available, pieces, kg,
                    checked(kg - pieces * to.KgPerUnit!.Value), source);
            }
            return new(ProductPurchaseUnitConversionOutcomeV1.Available, checked(kg / to.KgPerUnit!.Value), kg, 0m, source);
        }
        catch (Exception error) when (error is OverflowException or ArgumentException)
        {
            return new(ProductPurchaseUnitConversionOutcomeV1.Refused, null, null, null, source, InvalidCode);
        }
    }

    private async Task<State> ReadCoreAsync(Guid productId, DateOnly on, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var scope = Scope();
        if (scope is null) return Without(ProductPurchaseUnitConversionOutcomeV1.Denied);
        try
        {
            var record = await FencedAsync(() => records.ReadAsync(scope, productId, token), scope, token);
            if (record is null) return Without(ProductPurchaseUnitConversionOutcomeV1.NotFound);
            if (record.Id != productId || record.Version <= 0) return Without(ProductPurchaseUnitConversionOutcomeV1.Unavailable);
            _ = record.RestoreProduct();
            var units = record.RestoreReferences().Units;
            var unitIds = new[] { units?.Stock, units?.Purchase, units?.Sales, units?.Price, units?.Production, units?.Package }
                .Where(id => id is not null && id != Guid.Empty).Select(id => id!.Value).Distinct().Order().ToArray();
            var dimension = await FencedAsync(() => dimensions.GetAsync(productId, token), scope, token);
            if (dimension.Outcome == ProductDimensionOutcomeV1.Denied) return Without(ProductPurchaseUnitConversionOutcomeV1.Denied);
            if (dimension.Outcome is ProductDimensionOutcomeV1.Unavailable or ProductDimensionOutcomeV1.NotFound)
                return Without(ProductPurchaseUnitConversionOutcomeV1.Unavailable);
            SettingsUnitConversionPolicyV1? policy = null;
            UnitConversionSettingsV1? settings = null;
            // The policy also supplies the weighbridge rule for a fixed mass-unit conversion.
            {
                try
                {
                    policy = await FencedAsync(() => policies.GetUnitConversionPolicyAsync(on, token), scope, token);
                    if (policy.CompanyId != scope.CompanyId || policy.Profile is not { Version: > 0 }
                        || policy.Profile.ProfileId == Guid.Empty || on < policy.EffectiveFrom
                        || (policy.EffectiveThrough is { } through && on > through)
                        || !string.Equals(policy.CoefficientRounding.ToString(), UnitConversionRoundingModesV1.DecidedCoefficientRounding,
                            StringComparison.Ordinal))
                        return Without(ProductPurchaseUnitConversionOutcomeV1.Unavailable);
                    settings = Settings(policy);
                }
                catch (SettingsPrerequisiteException error) when (error.Code == SettingsPrerequisiteCodesV1.UnitConversionPolicyNotFound)
                { /* A reachable source without a policy provides no product coefficient. */ }
            }
            var answerUnits = new List<ProductPurchaseConversionUnitV1>();
            foreach (var id in unitIds)
            {
                var meaning = await FencedAsync(() => semantics.ReadAsync(id, token), scope, token);
                if (!Valid(meaning, id)) return Without(ProductPurchaseUnitConversionOutcomeV1.Unavailable);
                ProductUnitProfileRead? profile = null;
                try { profile = await FencedAsync(() => profiles.ReadAsync(id, on, false, token), scope, token); }
                catch (SettingsPrerequisiteException error) when (error.Code == SettingsPrerequisiteCodesV1.UnitProfileNotFound) { }
                decimal? factor = null;
                var kind = meaning.Kind is { } unitKind ? (ProductPhysicalUnitKindV1?)(int)unitKind : null;
                if (profile is { IsUsable: true } && meaning.Status == SettingsPhysicalUnitStatusV1.Found)
                {
                    factor = meaning.KgPerUnit ?? Coefficient(kind, dimension, policy, settings);
                    if ((meaning.Kind == SettingsPhysicalUnitKindV1.Kilogram) != profile.Profile.IsKilogram)
                        return Without(ProductPurchaseUnitConversionOutcomeV1.Unavailable);
                }
                answerUnits.Add(new(id, kind, factor, meaning.SourceVersion, profile?.Profile.Profile.ProfileId,
                    profile?.Profile.Profile.Version));
            }
            // A card edit during composition cannot publish a coefficient against its old reference set.
            var fresh = await FencedAsync(() => records.ReadAsync(scope, productId, token), scope, token);
            if (fresh is null || fresh.Version != record.Version || fresh.ReferencesJson != record.ReferencesJson)
                return Without(ProductPurchaseUnitConversionOutcomeV1.Unavailable);
            var freshDimension = await FencedAsync(() => dimensions.GetAsync(productId, token), scope, token);
            if (freshDimension != dimension) return Without(ProductPurchaseUnitConversionOutcomeV1.Unavailable);
            var hasFormula = answerUnits.Any(unit => (unit.Kind is ProductPhysicalUnitKindV1.Metre or ProductPhysicalUnitKindV1.Piece)
                && unit.KgPerUnit > 0m);
            var source = new ProductPurchaseUnitConversionSourceV1(scope.CompanyId, productId,
                record.Version.ToString(CultureInfo.InvariantCulture), units?.Price,
                hasFormula ? UnitConversionClassV1.FormulaBound : UnitConversionClassV1.None, answerUnits,
                ProductUnitConversionCalculatorV1.FormulaVersion, dimension.Version, dimension.KgPerMetreTableVersion,
                dimension.KgPerMetreSourceLabel, policy?.Profile.ProfileId, policy?.Profile.Version,
                dimension.KgPerMetreConflict ? WeightConflictReason : hasFormula ? null : NoSourceCode,
                policy?.DensityGramsPerCubicCentimetre);
            return new(new(ProductPurchaseUnitConversionOutcomeV1.Available, source), settings);
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { return Without(ProductPurchaseUnitConversionOutcomeV1.Denied); }
        catch (SettingsPrerequisiteException error) when (error.Code == "settings_access_denied")
        { return Without(ProductPurchaseUnitConversionOutcomeV1.Denied); }
        catch (Exception)
        {
            // Failure paths also fence the original actor/company; a revoked scope never receives provider details.
            return Scope() == scope ? Without(ProductPurchaseUnitConversionOutcomeV1.Unavailable)
                : Without(ProductPurchaseUnitConversionOutcomeV1.Denied);
        }
    }

    private static decimal? Coefficient(ProductPhysicalUnitKindV1? kind, ProductDimensionReadV1 row,
        SettingsUnitConversionPolicyV1? policy, UnitConversionSettingsV1? settings)
    {
        if (row.Outcome != ProductDimensionOutcomeV1.Found || row.Version is not > 0 || row.KgPerMetreConflict
            || policy is null || settings is null) return null;
        UnitConversionResultV1<decimal> result;
        if (row.SectionKind == ProductDimensionSectionKindsV1.Sheet && kind == ProductPhysicalUnitKindV1.Piece)
            result = ProductUnitConversionCalculatorV1.SheetKgPerPiece(row.ThicknessMm, row.WidthMm, row.LengthMm,
                DensityV1.FromGramsPerCubicCentimetre(policy.DensityGramsPerCubicCentimetre));
        else if (row.SectionKind == ProductDimensionSectionKindsV1.Coil && kind == ProductPhysicalUnitKindV1.Metre)
            result = ProductUnitConversionCalculatorV1.CoilKgPerMetre(row.ThicknessMm, row.WidthMm,
                DensityV1.FromGramsPerCubicCentimetre(policy.DensityGramsPerCubicCentimetre));
        else if (row.SectionKind is ProductDimensionSectionKindsV1.RoundTube or ProductDimensionSectionKindsV1.Profile
            or ProductDimensionSectionKindsV1.ErwTube or ProductDimensionSectionKindsV1.PaintedErwTube
            or ProductDimensionSectionKindsV1.ErwProfile or ProductDimensionSectionKindsV1.PaintedErwProfile
            or ProductDimensionSectionKindsV1.StructuralProfile)
        {
            if (row.KgPerMetre is not > 0m || row.KgPerMetreTableVersion is not > 0 || string.IsNullOrWhiteSpace(row.KgPerMetreSourceLabel))
                return null;
            if (kind == ProductPhysicalUnitKindV1.Piece)
            {
                // F-D rounds kg/m and then the separate piece coefficient, each once in the existing calculator.
                var piece = ProductUnitConversionCalculatorV1.ProfileKgPerPiece(row.KgPerMetre, row.LengthMm, settings);
                return piece.Outcome == UnitConversionOutcomeV1.Computed ? piece.Value : null;
            }
            if (kind != ProductPhysicalUnitKindV1.Metre) return null;
            result = UnitConversionResultV1<decimal>.Computed(row.KgPerMetre.Value);
        }
        else return null;
        if (result is not { Outcome: UnitConversionOutcomeV1.Computed, Value: { } coefficient }) return null;
        var rounded = ProductUnitConversionCalculatorV1.RoundCoefficient(coefficient, settings);
        return rounded > 0m ? rounded : null;
    }

    private static bool Valid(SettingsPhysicalUnitSemanticsV1? value, Guid id) => value is not null && value.UnitId == id
        && (value.Status == SettingsPhysicalUnitStatusV1.NotFound && value.Kind is null && value.KgPerUnit is null
            || value.Status == SettingsPhysicalUnitStatusV1.Found && !string.IsNullOrWhiteSpace(value.SourceVersion)
                && (value.Kind switch
                {
                    SettingsPhysicalUnitKindV1.Kilogram => value.KgPerUnit == 1m,
                    SettingsPhysicalUnitKindV1.Tonne => value.KgPerUnit == 1000m,
                    SettingsPhysicalUnitKindV1.Metre or SettingsPhysicalUnitKindV1.Piece => value.KgPerUnit is null,
                    _ => false,
                }));

    private static UnitConversionSettingsV1 Settings(SettingsUnitConversionPolicyV1 policy) => new(
        policy.CoefficientDecimals, policy.DocumentTotalDecimals, policy.WeighbridgeTolerancePercent,
        policy.CoefficientRounding, policy.WholeUnitRounding,
        policy.ToleranceBoundary switch { SettingsWeighbridgeToleranceBoundaryV1.Inclusive => WeighbridgeToleranceBoundaryV1.Inclusive,
            SettingsWeighbridgeToleranceBoundaryV1.Exclusive => WeighbridgeToleranceBoundaryV1.Exclusive, _ => throw new InvalidOperationException() },
        policy.ToleranceBase switch { SettingsWeighbridgeToleranceBaseV1.TheoreticalKg => WeighbridgeToleranceBaseV1.Calculated,
            SettingsWeighbridgeToleranceBaseV1.WeighedKg => WeighbridgeToleranceBaseV1.Weighed, _ => throw new InvalidOperationException() });

    private ProductPurchaseReadScope? Scope() => company.IsResolved && company.CompanyId != Guid.Empty
        && user.IsAuthenticated && !string.IsNullOrWhiteSpace(user.UserId) && user.CanAccessCompany(company.CompanyId)
            ? new(company.CompanyId, user.UserId) : null;

    private async Task<T> FencedAsync<T>(Func<Task<T>> read, ProductPurchaseReadScope expected, CancellationToken token)
    {
        try { return await read(); }
        finally
        {
            token.ThrowIfCancellationRequested();
            if (Scope() != expected) throw new UnauthorizedAccessException();
        }
    }

    private static State Without(ProductPurchaseUnitConversionOutcomeV1 outcome) => new(new(outcome, null,
        outcome == ProductPurchaseUnitConversionOutcomeV1.Unavailable ? UnavailableCode : null), null);
    private sealed record State(ProductPurchaseUnitConversionReadV1 Read, UnitConversionSettingsV1? Settings);
}
