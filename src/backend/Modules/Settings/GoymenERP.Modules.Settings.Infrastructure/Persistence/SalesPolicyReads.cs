using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// The reads the Sales policy provider and administration share. Every query runs under the company query filter, so only
/// the server-resolved company's rows are ever seen.
/// </summary>
internal static class SalesPolicyReads
{
    public static Task<bool> IsTransferredAsync(SettingsDbContext database, string family, CancellationToken cancellationToken) =>
        database.SalesPolicyBaselineTransfers.AsNoTracking().AnyAsync(row => row.PolicyFamily == family, cancellationToken);

    public static Task<SalesDeliveryToleranceVersionRow[]> LoadTolerancesAsync(SettingsDbContext database,
        CancellationToken cancellationToken) =>
        database.SalesDeliveryToleranceVersions.AsNoTracking().OrderBy(row => row.Version).ToArrayAsync(cancellationToken);

    public static Task<SalesApprovalAmountThresholdVersionRow[]> LoadThresholdsAsync(SettingsDbContext database,
        Guid? currencyId, CancellationToken cancellationToken)
    {
        var query = database.SalesApprovalAmountThresholdVersions.AsNoTracking();
        if (currencyId is { } currency)
        {
            query = query.Where(row => row.CurrencyId == currency);
        }

        return query.OrderBy(row => row.CurrencyId).ThenBy(row => row.Version).ToArrayAsync(cancellationToken);
    }

    public static IReadOnlyList<SalesPolicyTimelineEntryV1<SalesDeliveryToleranceVersionRow>> ToleranceTimeline(
        IEnumerable<SalesDeliveryToleranceVersionRow> rows) =>
        SalesPolicyTimelineV1.Build(rows, row => row.Version, row => row.EffectiveFrom);

    /// <summary>One currency's timeline; the rows must all belong to that currency.</summary>
    public static IReadOnlyList<SalesPolicyTimelineEntryV1<SalesApprovalAmountThresholdVersionRow>> ThresholdTimeline(
        IEnumerable<SalesApprovalAmountThresholdVersionRow> rows) =>
        SalesPolicyTimelineV1.Build(rows, row => row.Version, row => row.EffectiveFrom);

    /// <summary>The amount text a stored threshold answers with (the spelling it was recorded with); null when disabled.</summary>
    public static string? AmountText(SalesApprovalAmountThresholdVersionRow row)
    {
        if (!row.IsEnabled)
        {
            return null;
        }

        if (row.ThresholdAmount is not { } amount || row.ThresholdAmountScale is not { } scale
            || scale > SalesPolicyInputLimitsV1.AmountMaximumScale)
        {
            throw new SettingsPrerequisiteException("settings_profile_invalid");
        }

        return SalesPolicyTimelineV1.FormatAmount(amount, scale);
    }
}
