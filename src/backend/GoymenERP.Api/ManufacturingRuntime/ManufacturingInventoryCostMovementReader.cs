using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Costing;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Kilograms = GoymenERP.Modules.Manufacturing.Domain.Common.Kilograms;

namespace GoymenERP.Api.ManufacturingRuntime;

/// <summary>Typed bridge over real immutable Inventory movement facts; no current-average, FX or identity synthesis.</summary>
internal sealed class ManufacturingInventoryCostMovementReader(ICompanyContext company, ICurrentUser user,
    IInventoryManufacturingCostReadPortV1 owner) : IManufacturingCostMovementReadPortV1
{
    public async Task<ManufacturingCostMovementSnapshotV1?> ReadAsync(Guid companyId, Guid orderId, CancellationToken cancellationToken)
    {
        var actor = user.UserId;
        if (!InScope() || orderId == Guid.Empty) return null;
        try
        {
            var value = await owner.GetAsync(companyId, orderId, cancellationToken);
            if (!InScope() || value is null || value.Outcome != InventoryManufacturingCostReadOutcomeV1.Available
                || value.CompanyId != companyId || value.ManufacturingOrderId != orderId || string.IsNullOrWhiteSpace(value.SourceVersion)
                || value.Issues is null || value.Returns is null || value.Outputs is null) return null;
            var issues = new List<ManufacturingCostIssueSnapshotV1>(); var returns = new List<ManufacturingCostReturnSnapshotV1>();
            var outputs = new List<ManufacturingCostOutputSnapshotV1>();
            foreach (var issue in value.Issues)
            {
                if (issue is null || !Identity(issue.FactId, issue.MovementId, issue.ParentLabelId, issue.QuantityKg)
                    || issue.SourceKind != InventoryManufacturingRealizationContractV1.InputIssueSourceKind || issue.MovementKind != StockMovementKind.Issue
                    || issue.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(issue.ProductVersion)
                    || !Stamp(issue.Tl, tl: true) || !Stamp(issue.Usd, tl: false) || issue.Currency is null
                    || string.IsNullOrWhiteSpace(issue.Currency.SourceVersion)) return null;
                var currency = Currency(issue.Currency); if (currency is null) return null;
                var status = currency.Status == IssueCostCurrencyStatus.Unreadable ? IssueCostReadStatus.Unreadable
                    : currency.Status == IssueCostCurrencyStatus.RateMissing && issue.Tl.UnitCostPerKg is null ? IssueCostReadStatus.Value
                    : issue.Tl.Status == InventoryMovementCostStampStatusV1.Known ? IssueCostReadStatus.Value : IssueCostReadStatus.Unreadable;
                issues.Add(new(issue.FactId, issue.MovementId, issue.ParentLabelId, new(issue.QuantityKg), status,
                    issue.Tl.UnitCostPerKg, currency, issue.Tl.StampVersion + "/" + issue.Currency.SourceVersion,
                    issue.Usd.Status == InventoryMovementCostStampStatusV1.Known && issue.Usd.CurrencyId == ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD")
                        ? issue.Usd.UnitCostPerKg : null,
                    issue.Usd.Status == InventoryMovementCostStampStatusV1.Known ? issue.Usd.StampVersion : null));
            }
            foreach (var entry in value.Returns)
            {
                if (entry is null || !Identity(entry.FactId, entry.MovementId, entry.ParentLabelId, entry.QuantityKg)
                    || entry.SourceKind != InventoryManufacturingRealizationContractV1.InputReturnSourceKind || entry.MovementKind != StockMovementKind.Return
                    || entry.SourceIssueMovementId == Guid.Empty || !Stamp(entry.Tl, tl: true) || !Stamp(entry.Usd, tl: false)) return null;
                returns.Add(new(entry.FactId, entry.MovementId, entry.ParentLabelId, new(entry.QuantityKg), entry.SourceIssueMovementId, entry.Tl.StampVersion));
            }
            foreach (var output in value.Outputs)
            {
                if (output is null || !Identity(output.FactId, output.ReceiptMovementId, output.ParentLabelId, output.DeclaredQuantityKg)
                    || output.SourceKind != InventoryManufacturingRealizationContractV1.OutputSourceKind || output.MovementKind != StockMovementKind.Receipt
                    || output.LabelId == Guid.Empty || output.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(output.ProductVersion)
                    || string.IsNullOrWhiteSpace(output.LabelCode)) return null;
                // A production output's absent final cost is expected before closure; only the actual physical receipt identity is needed here.
                outputs.Add(new(output.FactId, output.ReceiptMovementId, output.ParentLabelId, output.LabelId, output.LabelCode,
                    new(output.DeclaredQuantityKg), value.SourceVersion));
            }
            // V1's actual movement contract contains only kg facts. It does not claim a paint/non-kg consumption fact.
            return new(companyId, orderId, issues, returns, outputs, NonKgInputUse.None, value.SourceVersion);
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }

        bool InScope() => companyId != Guid.Empty && company.IsResolved && company.CompanyId == companyId
            && user.IsAuthenticated && !string.IsNullOrWhiteSpace(actor) && user.UserId == actor && user.CanAccessCompany(companyId);
    }

    private static bool Identity(Guid fact, Guid movement, Guid parent, decimal quantity) => fact != Guid.Empty && movement != Guid.Empty
        && parent != Guid.Empty && quantity > 0m && decimal.Round(quantity, Kilograms.Scale, MidpointRounding.ToZero) == quantity;
    private static bool Stamp(InventoryMovementCostStampV1? stamp, bool tl) => stamp is not null && !string.IsNullOrWhiteSpace(stamp.StampVersion)
        && (stamp.Status switch
        {
            InventoryMovementCostStampStatusV1.Known => stamp.CurrencyId != Guid.Empty && (!tl || stamp.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira)
                && stamp.UnitCostPerKg is >= 0m && stamp.Amount is >= 0m && stamp.LedgerVersion is > 0,
            InventoryMovementCostStampStatusV1.Unavailable => stamp.UnitCostPerKg is null && stamp.Amount is null && stamp.LedgerVersion is null or >= 0
                && (!tl || stamp.CurrencyId == Guid.Empty || stamp.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira),
            _ => false,
        });
    private static IssueCostCurrencyInput? Currency(InventoryIssueCostCurrencyV1 proof) => proof.Status switch
    {
        InventoryIssueCostCurrencyStatusV1.FunctionalCurrency when proof.OriginalCurrencyId is null || proof.OriginalCurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira
            => proof.ForeignUnitCostPerKg is null && proof.ExchangeRate is null && proof.ForeignUnitCostRatio is null ? IssueCostCurrencyInput.FunctionalCurrency : null,
        InventoryIssueCostCurrencyStatusV1.Converted when proof.OriginalCurrencyId is { } id && id != Guid.Empty
            && id != SettingsCurrencyIdentitiesV1.TurkishLira && proof.ForeignUnitCostPerKg is > 0m && proof.ExchangeRate is > 0m && proof.ForeignUnitCostRatio is null
            => new(IssueCostCurrencyStatus.Converted, id, proof.ForeignUnitCostPerKg, proof.ExchangeRate),
        InventoryIssueCostCurrencyStatusV1.Converted when proof.OriginalCurrencyId is { } id && id != Guid.Empty
            && id != SettingsCurrencyIdentitiesV1.TurkishLira && proof.ForeignUnitCostPerKg is null && proof.ExchangeRate is > 0m
            && Ratio(proof.ForeignUnitCostRatio) is { } ratio
            => new(IssueCostCurrencyStatus.Converted, id, null, proof.ExchangeRate, ratio),
        InventoryIssueCostCurrencyStatusV1.RateMissing when proof.OriginalCurrencyId is { } id && id != Guid.Empty
            && id != SettingsCurrencyIdentitiesV1.TurkishLira && proof.ExchangeRate is null && proof.ForeignUnitCostPerKg is null or > 0m && proof.ForeignUnitCostRatio is null
            => new(IssueCostCurrencyStatus.RateMissing, id, proof.ForeignUnitCostPerKg, null),
        InventoryIssueCostCurrencyStatusV1.RateMissing when proof.OriginalCurrencyId is { } id && id != Guid.Empty
            && id != SettingsCurrencyIdentitiesV1.TurkishLira && proof.ExchangeRate is null && proof.ForeignUnitCostPerKg is null
            && Ratio(proof.ForeignUnitCostRatio) is { } ratio
            => new(IssueCostCurrencyStatus.RateMissing, id, null, null, ratio),
        InventoryIssueCostCurrencyStatusV1.Unreadable => new(IssueCostCurrencyStatus.Unreadable, proof.OriginalCurrencyId, proof.ForeignUnitCostPerKg, proof.ExchangeRate),
        _ => null,
    };
    private static ProductionExactRatioV1? Ratio(InventoryExactCostRatioV1? value)
    {
        if (value is null) return null;
        var ratio = new ProductionExactRatioV1(value.Numerator, value.Denominator);
        return ratio.IsValid() ? ratio : null;
    }
}
