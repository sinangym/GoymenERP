using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Costing;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Security.Cryptography;
using System.Text.Json;
using Kilograms = GoymenERP.Modules.Manufacturing.Domain.Common.Kilograms;

namespace GoymenERP.Api.ManufacturingRuntime;

/// <summary>Typed bridge over real immutable Inventory movement facts; no current-average, FX or identity synthesis.</summary>
internal sealed class ManufacturingInventoryCostMovementReader(ICompanyContext company, ICurrentUser user,
    IInventoryManufacturingCostReadPortV1 owner, IManufacturingInputRatePriorityReadPortV1? priorities = null,
    ManufacturingDbContext? manufacturing = null) : IManufacturingCostMovementReadPortV1
{
    public async Task<ManufacturingCostMovementSnapshotV1?> ReadAsync(Guid companyId, Guid orderId, CancellationToken cancellationToken)
    {
        var actor = user.UserId;
        if (!InScope() || orderId == Guid.Empty) return null;
        try
        {
            // A physical close has already written label/allocation facts on this transaction. Read those facts through
            // Inventory's borrowed owner context; a second SQL connection could block on the host's own label locks.
            await using var shared = manufacturing?.Database.CurrentTransaction is { } transaction
                ? await InventoryManufacturingCostReadSession.EnlistAsync(manufacturing.Database.GetDbConnection(),
                    transaction.GetDbTransaction(), company, cancellationToken) : null;
            var value = await (shared?.CostMovements ?? owner).GetAsync(companyId, orderId, cancellationToken);
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
                ManufacturingInputRateProofV1? selected = null;
                if (currency.Status is IssueCostCurrencyStatus.Converted or IssueCostCurrencyStatus.RateMissing)
                {
                    try
                    {
                        if (priorities is not null)
                            selected = await priorities.ReadAsync(companyId, orderId, issue.FactId, issue.ParentLabelId, currency, cancellationToken);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { /* Cost readiness does not erase the owner-verified physical issue (URT-D67). */ }
                    if (!InScope()) return null;
                    if (selected is null)
                    {
                        // Keep actual movement identities, never the receipt/invoice quote as an order-rate fallback.
                        // This is an unreadable cost input, not an available rate or a new physical-close refusal.
                        selected = new ManufacturingInputRateProofV1(new IssueCostCurrencyInput(IssueCostCurrencyStatus.Unreadable, null, null, null),
                            "manufacturing-input-rate-source-unreadable:v1", JsonSerializer.Serialize(new
                            { reason = "manufacturing_input_rate_source_unreadable", recordedEntryProof = issue.Currency }));
                    }
                    if (string.IsNullOrWhiteSpace(selected.SourceVersion) || string.IsNullOrWhiteSpace(selected.EvidenceJson)) return null;
                    if (selected.Currency.Status != IssueCostCurrencyStatus.Unreadable && !SelectedCurrency(currency, selected.Currency)) return null;
                    using var evidence = JsonDocument.Parse(selected.EvidenceJson);
                    if (evidence.RootElement.ValueKind != JsonValueKind.Object) return null;
                    currency = selected.Currency;
                }
                var status = currency.Status == IssueCostCurrencyStatus.Unreadable ? IssueCostReadStatus.Unreadable
                    : currency.Status == IssueCostCurrencyStatus.RateMissing && issue.Tl.UnitCostPerKg is null ? IssueCostReadStatus.Value
                    : issue.Tl.Status == InventoryMovementCostStampStatusV1.Known ? IssueCostReadStatus.Value : IssueCostReadStatus.Unreadable;
                issues.Add(new ManufacturingCostIssueSnapshotV1(issue.FactId, issue.MovementId, issue.ParentLabelId, new Kilograms(issue.QuantityKg), status,
                    currency.Status is IssueCostCurrencyStatus.RateMissing or IssueCostCurrencyStatus.Unreadable ? null : issue.Tl.UnitCostPerKg, currency,
                    issue.Tl.StampVersion + "/" + issue.Currency.SourceVersion + (selected is null ? "" : "/" + selected.SourceVersion),
                    issue.Usd.Status == InventoryMovementCostStampStatusV1.Known && issue.Usd.CurrencyId == ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD")
                        ? issue.Usd.UnitCostPerKg : null,
                    issue.Usd.Status == InventoryMovementCostStampStatusV1.Known ? issue.Usd.StampVersion : null, selected?.EvidenceJson));
            }
            foreach (var entry in value.Returns)
            {
                if (entry is null || !Identity(entry.FactId, entry.MovementId, entry.ParentLabelId, entry.QuantityKg)
                    || entry.SourceKind != InventoryManufacturingRealizationContractV1.InputReturnSourceKind || entry.MovementKind != StockMovementKind.Return
                    || entry.SourceIssueMovementId == Guid.Empty || !Stamp(entry.Tl, tl: true) || !Stamp(entry.Usd, tl: false)) return null;
                returns.Add(new ManufacturingCostReturnSnapshotV1(entry.FactId, entry.MovementId, entry.ParentLabelId, new Kilograms(entry.QuantityKg), entry.SourceIssueMovementId, entry.Tl.StampVersion));
            }
            foreach (var output in value.Outputs)
            {
                if (output is null || !Identity(output.FactId, output.ReceiptMovementId, output.ParentLabelId, output.DeclaredQuantityKg)
                    || output.SourceKind != InventoryManufacturingRealizationContractV1.OutputSourceKind || output.MovementKind != StockMovementKind.Receipt
                    || output.LabelId == Guid.Empty || output.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(output.ProductVersion)
                    || string.IsNullOrWhiteSpace(output.LabelCode)) return null;
                // A production output's absent final cost is expected before closure; only the actual physical receipt identity is needed here.
                outputs.Add(new ManufacturingCostOutputSnapshotV1(output.FactId, output.ReceiptMovementId, output.ParentLabelId, output.LabelId, output.LabelCode,
                    new Kilograms(output.DeclaredQuantityKg), value.SourceVersion));
            }
            // V1's actual movement contract contains only kg facts. It does not claim a paint/non-kg consumption fact.
            var prioritySources = issues.Where(issue => issue.RateEvidenceJson is not null).OrderBy(issue => issue.FactId)
                .Select(issue => issue.StampVersion).ToArray();
            var sourceVersion = value.SourceVersion + (prioritySources.Length == 0 ? "" : "/manufacturing-input-rate/"
                + Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(prioritySources))).ToLowerInvariant());
            return new ManufacturingCostMovementSnapshotV1(companyId, orderId, issues, returns, outputs, NonKgInputUse.None, sourceVersion);
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
    private static IssueCostCurrencyInput? Currency(InventoryIssueCostCurrencyV1 proof)
    {
        if (proof.Status == InventoryIssueCostCurrencyStatusV1.FunctionalCurrency)
            return (proof.OriginalCurrencyId is null || proof.OriginalCurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira)
                && proof.ForeignUnitCostPerKg is null && proof.ExchangeRate is null && proof.ForeignUnitCostRatio is null && proof.ExchangeRateRatio is null
                ? IssueCostCurrencyInput.FunctionalCurrency : null;
        if (proof.Status == InventoryIssueCostCurrencyStatusV1.Unreadable)
            return new IssueCostCurrencyInput(IssueCostCurrencyStatus.Unreadable, proof.OriginalCurrencyId, proof.ForeignUnitCostPerKg, proof.ExchangeRate);
        if (proof.OriginalCurrencyId is not { } id || id == Guid.Empty || id == SettingsCurrencyIdentitiesV1.TurkishLira) return null;
        var foreignRatio = Ratio(proof.ForeignUnitCostRatio); var rateRatio = Ratio(proof.ExchangeRateRatio);
        if (proof.ForeignUnitCostRatio is not null && foreignRatio is null || proof.ExchangeRateRatio is not null && rateRatio is null) return null;
        var knownForeign = proof.ForeignUnitCostPerKg is > 0m && foreignRatio is null || proof.ForeignUnitCostPerKg is null && foreignRatio is not null;
        if (proof.Status == InventoryIssueCostCurrencyStatusV1.Converted && knownForeign
            && (proof.ExchangeRate is > 0m && (rateRatio is null || rateRatio.Represents(proof.ExchangeRate.Value))
                || proof.ExchangeRate is null && rateRatio is not null))
            return new IssueCostCurrencyInput(IssueCostCurrencyStatus.Converted, id, proof.ForeignUnitCostPerKg, proof.ExchangeRate, foreignRatio, rateRatio);
        if (proof.Status == InventoryIssueCostCurrencyStatusV1.RateMissing && proof.ExchangeRate is null && rateRatio is null
            && (knownForeign || proof.ForeignUnitCostPerKg is null && foreignRatio is null))
            return new IssueCostCurrencyInput(IssueCostCurrencyStatus.RateMissing, id, proof.ForeignUnitCostPerKg, null, foreignRatio);
        return null;
    }
    private static ProductionExactRatioV1? Ratio(InventoryExactCostRatioV1? value)
    {
        if (value is null) return null;
        var ratio = new ProductionExactRatioV1(value.Numerator, value.Denominator);
        return ratio.IsValid() ? ratio : null;
    }
    private static bool SelectedCurrency(IssueCostCurrencyInput recorded, IssueCostCurrencyInput? selected) => selected is not null
        && selected.CurrencyId == recorded.CurrencyId && selected.ForeignUnitCostPerKg == recorded.ForeignUnitCostPerKg
        && selected.ForeignUnitCostRatio == recorded.ForeignUnitCostRatio
        && (selected.Status == IssueCostCurrencyStatus.RateMissing && selected.ExchangeRate is null && selected.ExchangeRateRatio is null
            || selected.Status == IssueCostCurrencyStatus.Converted
            && (selected.ExchangeRate is > 0m && (selected.ExchangeRateRatio is null || selected.ExchangeRateRatio.IsValid() && selected.ExchangeRateRatio.Represents(selected.ExchangeRate.Value))
                || selected.ExchangeRate is null && selected.ExchangeRateRatio?.IsValid() == true));
}
