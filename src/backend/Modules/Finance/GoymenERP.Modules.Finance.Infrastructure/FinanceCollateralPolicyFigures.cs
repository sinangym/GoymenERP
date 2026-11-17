using System.Globalization;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>K6 source resolution only. The exact weighted calculation remains FinancePartyCollateralFigures.</summary>
internal static class FinanceCollateralPolicyFigures
{
    internal static async Task<FinancePartyCollateralFigures.Figure> WeighAsync(Guid companyId,
        IReadOnlyList<FinancePartyCollateralFigures.HeldCollateral> inputs, FinanceCollateralTypeCatalogAnswer? catalog,
        IFinanceCollateralPolicyReaderV1? policies, DateOnly asOf, CancellationToken token)
    {
        var held = inputs.Where(x => FinancePartyCollateralFigures.IsInForce(x.ValidFrom, x.ValidUntil, asOf)).ToArray();
        if (held.Length == 0) return FinancePartyCollateralFigures.Weigh(held, catalog, asOf);
        // Old source-only compositions without K6 retain their existing type reader for unbound legacy rows.
        // A stored bank proof can never silently lose its more specific policy when a provider is missing.
        if (policies is null) return held.Any(x => x.BankInstitutionId is not null || x.BankSourceVersion is not null)
            ? Unavailable() : FinancePartyCollateralFigures.Weigh(held, catalog, asOf);
        if (catalog is not { State: FinanceCollateralTypeCatalogState.Available, Items: { } types }) return Unavailable();
        var resolved = new List<FinancePartyCollateralFigures.HeldCollateral>();
        var proofs = new List<FinanceCollateralRateProofV1>();
        foreach (var input in held)
        {
            token.ThrowIfCancellationRequested();
            var type = types.Where(x => x.CollateralTypeId == input.CollateralTypeId).Take(2).ToArray();
            if (input.CollateralId == Guid.Empty || input.Revision <= 0 || type.Length != 1 || type[0].IsNegotiableInstrument
                || string.IsNullOrWhiteSpace(type[0].Version) || (input.BankInstitutionId is null) != (input.BankSourceVersion is null))
                return Unavailable();
            FinanceCollateralRateReadV1 answer;
            try { answer = await policies.GetRateAsync(companyId, input.CollateralTypeId, input.BankInstitutionId,
                input.BankSourceVersion, input.ValidUntil, asOf, token); }
            catch (Exception exception) when (exception is not OperationCanceledException and not UnauthorizedAccessException)
            { return Unavailable(); }
            if (answer is not { IsAvailable: true } || !FinancePartyCollateralFigures.TryRate(answer.RatePercent, out _)
                || answer.CollateralTypeVersion != type[0].Version
                || string.IsNullOrWhiteSpace(answer.SourceVersion) || answer.SourceVersion.Length > 256
                || answer.Scope is not ("Type" or "TypeBank" or "TypeBankRemainingDays" or "Company")
                || input.BankInstitutionId is null && answer.Scope is "TypeBank" or "TypeBankRemainingDays") return Unavailable();
            resolved.Add(input with { ResolvedRatePercent = answer.RatePercent });
            proofs.Add(new(companyId, input.CollateralId, input.Revision.ToString(CultureInfo.InvariantCulture),
                input.CollateralTypeId, type[0].Version, input.BankInstitutionId, input.BankSourceVersion,
                answer.RatePercent!, answer.SourceVersion, answer.Scope, asOf));
        }
        var result = FinancePartyCollateralFigures.Weigh(resolved, catalog, asOf);
        return result.IsAvailable ? result with { PolicyProofs = proofs } : result;
    }
    private static FinancePartyCollateralFigures.Figure Unavailable() =>
        FinancePartyCollateralFigures.Figure.Unavailable(FinancePartyCollateralFigures.TypeRateUnavailable);
}
