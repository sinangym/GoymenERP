using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>Reads only actual Inventory movements, their original command result and immutable cost stamps.</summary>
public sealed class InventoryManufacturingCostReadPort(InventoryDbContext db, ICompanyContext company)
    : IInventoryManufacturingCostReadPortV1
{
    private const string Version = "inventory.manufacturing-cost.v1";
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task<InventoryManufacturingCostReadV1> GetAsync(Guid companyId, Guid manufacturingOrderId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!SameCompany(companyId) || manufacturingOrderId == Guid.Empty) return Unreadable(companyId, manufacturingOrderId);
        try
        {
            var kinds = new[] { InventoryManufacturingRealizationContractV1.InputIssueSourceKind,
                InventoryManufacturingRealizationContractV1.InputReturnSourceKind, InventoryManufacturingRealizationContractV1.OutputSourceKind };
            var rows = await (from movement in db.Movements.AsNoTracking()
                join line in db.MovementLines.AsNoTracking() on movement.Id equals line.MovementId
                join label in db.Labels.AsNoTracking() on line.LabelId equals label.Id
                where movement.SourceId == manufacturingOrderId && kinds.Contains(movement.SourceKind)
                orderby movement.Id, line.LineNumber
                select new Fact(movement, line, label)).ToListAsync(cancellationToken);
            if (!SameCompany(companyId) || rows.Any(fact => !ValidFact(fact, companyId))
                || rows.Select(fact => fact.Movement.Id).Distinct().Count() != rows.Count
                || rows.Select(fact => (fact.Movement.SourceKind, fact.Movement.SourceLineId)).Distinct().Count() != rows.Count)
                return Unreadable(companyId, manufacturingOrderId);
            var ids = rows.Select(fact => fact.Movement.Id).ToArray();
            var actualMovementCount = await db.Movements.AsNoTracking().CountAsync(movement =>
                movement.SourceId == manufacturingOrderId && kinds.Contains(movement.SourceKind), cancellationToken);
            if (actualMovementCount != ids.Length) return Unreadable(companyId, manufacturingOrderId);
            var stamps = await db.MovementCostStamps.AsNoTracking().Where(row => ids.Contains(row.MovementId)).ToListAsync(cancellationToken);
            var outputIds = rows.Where(fact => fact.Movement.SourceKind == InventoryManufacturingRealizationContractV1.OutputSourceKind)
                .Select(fact => fact.Movement.SourceLineId!.Value).ToArray();
            var commands = await db.Commands.AsNoTracking().Where(row => row.TargetId != null && outputIds.Contains(row.TargetId.Value)
                && (row.CommandName == InventoryManufacturingRealizationContractV1.OutputCommandName
                    || row.CommandName == InventoryManufacturingRealizationContractV2.OutputCommandName)).ToListAsync(cancellationToken);
            if (!SameCompany(companyId)) return Unreadable(companyId, manufacturingOrderId);
            var issues = new List<InventoryManufacturingCostIssueV1>();
            var returns = new List<InventoryManufacturingCostReturnV1>();
            var outputs = new List<InventoryManufacturingCostOutputV1>();
            foreach (var fact in rows)
            {
                var movement = fact.Movement;
                var line = fact.Line;
                var tlRow = stamps.SingleOrDefault(row => row.MovementId == movement.Id && row.LineNumber == 1
                    && row.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira);
                var usdRow = stamps.SingleOrDefault(row => row.MovementId == movement.Id && row.LineNumber == 1
                    && row.CurrencyId == ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD"));
                var tl = InventoryMovementCostStampProjection.Read(tlRow, line.QuantityKg, SettingsCurrencyIdentitiesV1.TurkishLira);
                var usd = InventoryMovementCostStampProjection.Read(usdRow, line.QuantityKg, ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD"));
                if (movement.SourceKind == InventoryManufacturingRealizationContractV1.InputIssueSourceKind)
                {
                    issues.Add(new InventoryManufacturingCostIssueV1(movement.SourceLineId!.Value, movement.Id, movement.SourceKind, (StockMovementKind)movement.Kind,
                        line.LabelId, line.ProductId, fact.Label.ProductVersion!, line.QuantityKg, tl, usd, Currency(tlRow)));
                }
                else if (movement.SourceKind == InventoryManufacturingRealizationContractV1.InputReturnSourceKind)
                {
                    if (tlRow is not null && usdRow is not null && tlRow.SourceIssueMovementId != usdRow.SourceIssueMovementId)
                        return Unreadable(companyId, manufacturingOrderId);
                    returns.Add(new InventoryManufacturingCostReturnV1(movement.SourceLineId!.Value, movement.Id, movement.SourceKind, (StockMovementKind)movement.Kind,
                        line.LabelId, line.QuantityKg, tlRow?.SourceIssueMovementId ?? usdRow?.SourceIssueMovementId, tl, usd));
                }
                else
                {
                    var commandRows = commands.Where(command => command.TargetId == movement.SourceLineId).ToArray();
                    if (commandRows.Length != 1) return Unreadable(companyId, manufacturingOrderId);
                    // V2 adds classification/genealogy beside the same V1 fields; only the original factual fields are read.
                    var original = JsonSerializer.Deserialize<InventoryManufacturingOutputResultV1>(commandRows[0].ResultJson, Json);
                    if (original is null || original.CompanyId != companyId || original.ManufacturingOrderId != manufacturingOrderId
                        || original.OutputId != movement.SourceLineId || original.MovementId != movement.Id || original.LabelId != line.LabelId
                        || original.ProductId != line.ProductId || original.ProductVersion != fact.Label.ProductVersion
                        || original.ParentLabelId == Guid.Empty || original.QuantityKg != line.QuantityKg.ToString("0.###", CultureInfo.InvariantCulture)
                        || original.IsReplay || original.OriginalCorrelationId != commandRows[0].CorrelationId)
                        return Unreadable(companyId, manufacturingOrderId);
                    outputs.Add(new InventoryManufacturingCostOutputV1(original.OutputId, movement.Id, movement.SourceKind, (StockMovementKind)movement.Kind,
                        original.ParentLabelId, line.LabelId, fact.Label.Code, line.ProductId, fact.Label.ProductVersion!, line.QuantityKg));
                }
            }
            if (returns.Any(returned => returned.SourceIssueMovementId is { } id
                && !issues.Any(issue => issue.MovementId == id && issue.ParentLabelId == returned.ParentLabelId)))
                return Unreadable(companyId, manufacturingOrderId);
            var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { issues, returns, outputs }, Json))));
            return new InventoryManufacturingCostReadV1(InventoryManufacturingCostReadOutcomeV1.Available, companyId, manufacturingOrderId,
                issues, returns, outputs, Version + "/" + fingerprint);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Unreadable(companyId, manufacturingOrderId); }
    }

    private bool SameCompany(Guid id) => id != Guid.Empty && company.IsResolved && company.CompanyId == id;
    private static bool ValidFact(Fact fact, Guid companyId) => fact.Movement.CompanyId == companyId
        && fact.Line.CompanyId == companyId && fact.Label.CompanyId == companyId && fact.Line.LineNumber == 1
        && fact.Movement.SourceGrain == (int)StockSourceGrain.Line && fact.Movement.SourceLineId is { } id && id != Guid.Empty
        && fact.Line.QuantityKg > 0m && fact.Label.ProductId == fact.Line.ProductId && !string.IsNullOrWhiteSpace(fact.Label.ProductVersion)
        && (fact.Movement.SourceKind switch
        {
            InventoryManufacturingRealizationContractV1.InputIssueSourceKind => fact.Movement.Kind == (int)StockMovementKind.Issue
                && fact.Line.Direction == (int)StockDirection.Out,
            InventoryManufacturingRealizationContractV1.InputReturnSourceKind => fact.Movement.Kind == (int)StockMovementKind.Return
                && fact.Line.Direction == (int)StockDirection.In,
            InventoryManufacturingRealizationContractV1.OutputSourceKind => fact.Movement.Kind == (int)StockMovementKind.Receipt
                && fact.Line.Direction == (int)StockDirection.In,
            _ => false,
        });

    private static InventoryIssueCostCurrencyV1 Currency(MovementCostStampRow? row)
    {
        if (row is null) return new InventoryIssueCostCurrencyV1(InventoryIssueCostCurrencyStatusV1.Unreadable, null, null, null,
            "legacy-not-recorded", "movement_currency_proof_not_recorded");
        var proof = new InventoryIssueCostCurrencyV1((InventoryIssueCostCurrencyStatusV1)row.EntryCurrencyStatus, row.OriginalCurrencyId,
            row.ForeignUnitCostPerKg, row.EntryExchangeRate, row.CurrencySourceVersion, row.CurrencyReason,
            row.ForeignUnitCostRatioJson is { } ratio ? JsonSerializer.Deserialize<InventoryExactCostRatioV1>(ratio, Json) : null,
            row.EntryExchangeRateRatioJson is { } rateRatio ? JsonSerializer.Deserialize<InventoryExactCostRatioV1>(rateRatio, Json) : null);
        if (string.IsNullOrWhiteSpace(proof.SourceVersion)) throw new InvalidOperationException();
        switch (proof.Status)
        {
            case InventoryIssueCostCurrencyStatusV1.Converted:
                if (proof.OriginalCurrencyId is null || proof.OriginalCurrencyId == Guid.Empty || proof.Reason is not null
                    || InventoryExactCostRatioV1.Resolve(proof.ForeignUnitCostPerKg, proof.ForeignUnitCostRatio).Sign <= 0
                    || InventoryExactCostRatioV1.Resolve(proof.ExchangeRate, proof.ExchangeRateRatio).Sign <= 0)
                    throw new InvalidOperationException();
                break;
            case InventoryIssueCostCurrencyStatusV1.FunctionalCurrency:
                if (proof.OriginalCurrencyId is not null || proof.ForeignUnitCostPerKg is not null || proof.ForeignUnitCostRatio is not null
                    || proof.ExchangeRate is not null || proof.ExchangeRateRatio is not null || proof.Reason is not null)
                    throw new InvalidOperationException();
                break;
            case InventoryIssueCostCurrencyStatusV1.RateMissing:
                if (proof.OriginalCurrencyId is null || proof.OriginalCurrencyId == Guid.Empty || proof.ExchangeRate is not null
                    || proof.ExchangeRateRatio is not null || string.IsNullOrWhiteSpace(proof.Reason)) throw new InvalidOperationException();
                if (proof.ForeignUnitCostPerKg is not null || proof.ForeignUnitCostRatio is not null)
                    if (InventoryExactCostRatioV1.Resolve(proof.ForeignUnitCostPerKg, proof.ForeignUnitCostRatio).Sign <= 0)
                        throw new InvalidOperationException();
                break;
            case InventoryIssueCostCurrencyStatusV1.Unreadable:
                if (proof.OriginalCurrencyId is not null || proof.ForeignUnitCostPerKg is not null || proof.ForeignUnitCostRatio is not null
                    || proof.ExchangeRate is not null || proof.ExchangeRateRatio is not null || string.IsNullOrWhiteSpace(proof.Reason))
                    throw new InvalidOperationException();
                break;
            default: throw new InvalidOperationException();
        }
        return proof;
    }
    private static InventoryManufacturingCostReadV1 Unreadable(Guid company, Guid order) =>
        new InventoryManufacturingCostReadV1(InventoryManufacturingCostReadOutcomeV1.Unreadable, company, order, [], [], [], Version + "/unreadable", "inventory_cost_source_unreadable");
    private sealed record Fact(MovementRow Movement, MovementLineRow Line, LabelRow Label);
}
