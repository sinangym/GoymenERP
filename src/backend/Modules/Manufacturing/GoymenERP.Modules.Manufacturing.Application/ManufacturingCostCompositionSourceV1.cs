using System.Security.Cryptography;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Costing;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Modules.Manufacturing.Application;

/// <summary>Manufacturing-facing projection of immutable Inventory facts; the host adapts Inventory's owner contract.</summary>
public interface IManufacturingCostMovementReadPortV1
{
    Task<ManufacturingCostMovementSnapshotV1?> ReadAsync(Guid companyId, Guid orderId, CancellationToken cancellationToken);
}
public sealed record ManufacturingCostIssueSnapshotV1(Guid FactId, Guid MovementId, Guid ParentLabelId, Kilograms Mass,
    IssueCostReadStatus Status, decimal? StampedUnitCostTl, IssueCostCurrencyInput Currency, string StampVersion,
    decimal? StampedUnitCostUsd = null, string? UsdStampVersion = null);
public sealed record ManufacturingCostReturnSnapshotV1(Guid FactId, Guid MovementId, Guid ParentLabelId, Kilograms Mass,
    Guid? SourceIssueMovementId, string StampVersion);
public sealed record ManufacturingCostOutputSnapshotV1(Guid FactId, Guid MovementId, Guid ParentLabelId, Guid LabelId,
    string LabelCode, Kilograms DeclaredMass, string StampVersion);
public sealed record ManufacturingCostMovementSnapshotV1(Guid CompanyId, Guid OrderId,
    IReadOnlyList<ManufacturingCostIssueSnapshotV1> Issues, IReadOnlyList<ManufacturingCostReturnSnapshotV1> Returns,
    IReadOnlyList<ManufacturingCostOutputSnapshotV1> Outputs, NonKgInputUse NonKgInputs, string SourceVersion);

/// <summary>The real Settings owner adapter supplies the company's immutable policy value and exact version.</summary>
public interface IManufacturingScrapCostPolicyReadPortV1
{
    Task<ManufacturingScrapCostPolicySnapshotV1?> ReadAsync(Guid companyId, CancellationToken cancellationToken);
}
public sealed record ManufacturingScrapCostPolicySnapshotV1(Guid CompanyId, bool IncludeScrapCost, string SourceVersion);
public sealed record ManufacturingCostMonthlyPriceSnapshotV1(Guid ScrapPriceId, int Year, int Month, int Version, decimal TlPerKg);
public sealed record ManufacturingCostInputProvenanceV1(long OrderVersion, string MovementSourceVersion, string ScrapPolicyVersion,
    IReadOnlyList<ManufacturingCostMonthlyPriceSnapshotV1> MonthlyPrices, string InputFingerprint);
public interface IManufacturingScrapUsdRateReadPortV1
{
    Task<ManufacturingScrapUsdRateSnapshotV1?> ReadAsync(Guid companyId, DateOnly weighingDay, CancellationToken cancellationToken);
}
public sealed record ManufacturingScrapUsdRateSnapshotV1(Guid CompanyId, DateOnly WeighingDay, DateOnly BulletinDay,
    decimal TryPerUsd, Guid RecordId, string RecordVersion, string Source);

/// <summary>
/// URT-SC32/SC36: join the actual order facts, immutable movement stamps, policy version and weighing-month prices.
/// Only the existing domain calculator computes cost. No moving average, issue identity or FX is reconstructed here.
/// This source must be registered only with real owner adapters; missing stamps remain an unreadable input.
/// </summary>
public sealed class ManufacturingCostCompositionSourceV1(ICompanyContext companyContext, IManufacturingOrderStore orders,
    IManufacturingCostMovementReadPortV1 movements, IManufacturingScrapCostPolicyReadPortV1 policy,
    IManufacturingMonthlyScrapPriceStore prices, IManufacturingScrapUsdRateReadPortV1? usdRates = null) : IManufacturingScrapCostDistributionSourceV1
{
    public async Task<ManufacturingScrapCostDistributionSourceReadV1> ReadAsync(Guid companyId, Guid orderId, CancellationToken cancellationToken)
    {
        if (!InScope() || orderId == Guid.Empty) return Unavailable();
        try
        {
            var order = await orders.FindOrderAsync(orderId, cancellationToken);
            if (!InScope()) return Unavailable();
            if (order is null) return new(ManufacturingScrapCostDistributionSourceStatusV1.NotFound, companyId, orderId, null);
            if (order.CompanyId != companyId || order.Id != orderId) return Unavailable();
            var version = order.Version;
            var stock = await movements.ReadAsync(companyId, orderId, cancellationToken);
            if (!await FreshAsync(version)) return Unavailable();
            if (stock is null || stock.CompanyId != companyId || stock.OrderId != orderId || string.IsNullOrWhiteSpace(stock.SourceVersion)
                || stock.Issues is null || stock.Returns is null || stock.Outputs is null || !Enum.IsDefined(stock.NonKgInputs)
                || stock.Issues.Any(item => item is null || item.MovementId == Guid.Empty || item.Currency is null || string.IsNullOrWhiteSpace(item.StampVersion))
                || stock.Returns.Any(item => item is null || item.MovementId == Guid.Empty || string.IsNullOrWhiteSpace(item.StampVersion))
                || stock.Outputs.Any(item => item is null || item.MovementId == Guid.Empty || item.LabelId == Guid.Empty || string.IsNullOrWhiteSpace(item.LabelCode)
                    || string.IsNullOrWhiteSpace(item.StampVersion))
                || stock.Issues.Select(item => item.FactId).Distinct().Count() != stock.Issues.Count
                || stock.Returns.Select(item => item.FactId).Distinct().Count() != stock.Returns.Count
                || stock.Outputs.Select(item => item.FactId).Distinct().Count() != stock.Outputs.Count
                || stock.Issues.Select(item => item.MovementId).Concat(stock.Returns.Select(item => item.MovementId))
                    .Concat(stock.Outputs.Select(item => item.MovementId)).Distinct().Count() != stock.Issues.Count + stock.Returns.Count + stock.Outputs.Count
                || !stock.Issues.Select(item => item.FactId).ToHashSet().SetEquals(order.Issues.Select(item => item.IssueId))
                || !stock.Returns.Select(item => item.FactId).ToHashSet().SetEquals(order.Returns.Select(item => item.ReturnId))
                || !stock.Outputs.Select(item => item.FactId).ToHashSet().SetEquals(order.Outputs.Where(item => item.Class != OutputClass.ProcessLoss).Select(item => item.OutputId)))
                return Unavailable();
            var issueInputs = new List<IssueCostInput>(); var returnInputs = new List<ReturnCostInput>();
            foreach (var fact in order.Issues)
            {
                var stamp = stock.Issues.Single(item => item.FactId == fact.IssueId);
                if (stamp.ParentLabelId != fact.ParentRollId || stamp.Mass != fact.Mass || !Enum.IsDefined(stamp.Status)) return Unavailable();
                issueInputs.Add(new(stamp.MovementId, fact.Mass, stamp.Status, stamp.StampedUnitCostTl, stamp.Currency));
            }
            foreach (var fact in order.Returns)
            {
                var stamp = stock.Returns.Single(item => item.FactId == fact.ReturnId);
                if (stamp.ParentLabelId != fact.ParentRollId || stamp.Mass != fact.Mass) return Unavailable();
                if (stamp.SourceIssueMovementId is { } source && !stock.Issues.Any(issue => issue.MovementId == source && issue.ParentLabelId == stamp.ParentLabelId)) return Unavailable();
                // A missing/ambiguous original issue stays unreadable in the single calculator, never FIFO or current average.
                returnInputs.Add(new(stamp.MovementId, stamp.SourceIssueMovementId ?? Guid.Empty, fact.Mass));
            }
            var selectedPolicy = await policy.ReadAsync(companyId, cancellationToken);
            if (!await FreshAsync(version) || selectedPolicy is null || selectedPolicy.CompanyId != companyId
                || string.IsNullOrWhiteSpace(selectedPolicy.SourceVersion)) return Unavailable();
            var outputInputs = new List<ProductionScrapCostLabelInput>(); var scrapInputs = new List<ScrapWeighingInput>();
            var priceSnapshots = new List<ManufacturingCostMonthlyPriceSnapshotV1>();
            var years = new Dictionary<int, IReadOnlyList<MonthlyScrapPrice>>();
            foreach (var output in order.Outputs)
            {
                var netMass = order.NetMassOf(output.OutputId);
                if (output.Class == OutputClass.ProcessLoss) continue;
                var label = stock.Outputs.Single(item => item.FactId == output.OutputId);
                if (label.ParentLabelId != output.ParentRollId || label.DeclaredMass != output.Mass) return Unavailable();
                if (netMass.Value == 0m) continue;
                if (output.Class != OutputClass.Scrap) { outputInputs.Add(new(label.LabelId, label.LabelCode, output.Class, netMass)); continue; }
                var month = ScrapPricePeriod.Of(ManufacturingBusinessDay.DateOf(output.DeclaredAtUtc));
                if (!years.TryGetValue(month.Year, out var entries))
                {
                    entries = await prices.ListScrapPricesAsync(month.Year, cancellationToken);
                    if (!await FreshAsync(version) || entries is null || entries.Any(entry => entry is null || entry.CompanyId != companyId
                        || entry.Period.Year != month.Year) || entries.Select(entry => entry.Period.Month).Distinct().Count() != entries.Count) return Unavailable();
                    years.Add(month.Year, entries);
                }
                var price = entries.SingleOrDefault(entry => entry.Period == month);
                scrapInputs.Add(new(label.LabelId, netMass, price?.ToInput() ?? ScrapPriceInput.Missing));
                if (price is not null && priceSnapshots.All(entry => entry.ScrapPriceId != price.Id))
                    priceSnapshots.Add(new(price.Id, month.Year, month.Month, price.Version, price.Price.Value));
            }
            var cost = LegacyClosureCostV1.WithClosureMeasure(new(order.Status == OrderStatus.Closed, issueInputs, returnInputs,
                Total(OutputClass.GoodProduct), Total(OutputClass.SecondQuality),
                Total(OutputClass.UsableRemnant), scrapInputs, Total(OutputClass.ProcessLoss),
                selectedPolicy.IncludeScrapCost ? ScrapCostSettingStatus.On : ScrapCostSettingStatus.Off, stock.NonKgInputs), order);
            var input = new ProductionScrapCostDistributionInput(cost, outputInputs);
            if (!await FreshAsync(version)) return Unavailable();
            var orderedPrices = priceSnapshots.OrderBy(entry => entry.Year).ThenBy(entry => entry.Month).ToArray();
            var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
                { companyId, orderId, version, stock.SourceVersion, policyVersion = selectedPolicy.SourceVersion, input, prices = orderedPrices }))).ToLowerInvariant();
            // USD is an independent display dimension. Its absence never changes the TL input or its recognition fingerprint.
            ProductionScrapCostDistributionInput? usdInput = null; var usdReasons = new List<string>();
            var usdSnapshots = new List<ManufacturingScrapUsdRateSnapshotV1>();
            var usdIssues = stock.Issues.Select(issue => new IssueCostInput(issue.MovementId, issue.Mass,
                issue.StampedUnitCostUsd is not null && !string.IsNullOrWhiteSpace(issue.UsdStampVersion) ? IssueCostReadStatus.Value : IssueCostReadStatus.Unreadable,
                issue.StampedUnitCostUsd, IssueCostCurrencyInput.FunctionalCurrency)).ToArray();
            var usdWeighings = new List<ScrapWeighingInput>();
            foreach (var weighing in scrapInputs)
            {
                if (weighing.Price.Status != ScrapPriceReadStatus.Value || weighing.Mass.Value == 0)
                { usdWeighings.Add(weighing); continue; }
                var physical = stock.Outputs.Single(label => label.LabelId == weighing.ScrapOutputId);
                var fact = order.Outputs.Single(output => output.OutputId == physical.FactId);
                var day = ManufacturingBusinessDay.DateOf(fact.DeclaredAtUtc);
                ManufacturingScrapUsdRateSnapshotV1? fx = null;
                try { if (usdRates is not null) fx = await usdRates.ReadAsync(companyId, day, cancellationToken); }
                catch (OperationCanceledException) { throw; }
                catch { /* A USD source failure leaves the independent display unavailable. */ }
                if (!await FreshAsync(version)) return Unavailable();
                if (fx is null || fx.CompanyId != companyId || fx.WeighingDay != day || fx.TryPerUsd <= 0
                    || fx.RecordId == Guid.Empty || string.IsNullOrWhiteSpace(fx.RecordVersion))
                { usdReasons.Add("scrap_usd_exchange_rate_unavailable"); continue; }
                try
                {
                    var priceUsd = weighing.Price.TlPerKg!.Value / fx.TryPerUsd;
                    usdSnapshots.Add(fx);
                    usdWeighings.Add(weighing with { Price = new(ScrapPriceReadStatus.Value, priceUsd) });
                }
                catch (OverflowException) { usdReasons.Add("scrap_usd_exchange_rate_unavailable"); }
            }
            if (usdReasons.Count == 0) usdInput = new(cost with { Issues = usdIssues, ScrapWeighings = usdWeighings }, outputInputs);
            return new(ManufacturingScrapCostDistributionSourceStatusV1.Found, companyId, orderId, input)
            { Provenance = new(version, stock.SourceVersion, selectedPolicy.SourceVersion, orderedPrices, fingerprint), MovementEvidence = stock,
                UsdInput = usdInput, UsdRateEvidence = usdSnapshots, UsdUnavailableReason = usdReasons.FirstOrDefault() };

            Kilograms Total(OutputClass outputClass) => order.PlannedInputs.Aggregate(Kilograms.Zero,
                (sum, input) => sum + order.BalanceOf(input.ParentRollId).OutputOf(outputClass));
        }
        catch (OperationCanceledException) { throw; }
        catch { return Unavailable(); }

        bool InScope() => companyId != Guid.Empty && companyContext.IsResolved && companyContext.CompanyId == companyId;
        async Task<bool> FreshAsync(long expected)
        {
            if (!InScope()) return false;
            var current = await orders.FindOrderAsync(orderId, cancellationToken);
            return InScope() && current is not null && current.CompanyId == companyId && current.Id == orderId && current.Version == expected;
        }
        ManufacturingScrapCostDistributionSourceReadV1 Unavailable() => new(ManufacturingScrapCostDistributionSourceStatusV1.Unavailable, companyId, orderId, null);
    }
}
