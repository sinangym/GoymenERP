using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// MOD-03: the line tax port the Sales order line / invoice consume (SAT-D93/D134/D135/D139/D140). It reads only Settings' own
/// tables under the company filter plus the reviewed SAT-D139 catalog; the product's item type comes from the Product owner
/// through the host-composed <see cref="ISettingsProductItemTypeSourceV1"/>, the buyer situation from the order (an input).
/// Access first (authenticated actor, member of the server company): else <c>settings_access_denied</c>. An unreadable store
/// or source is <c>settings_provider_unavailable</c>, never an empty or zero answer. No permission is measured here: the answer
/// is reference data and the consumer fences its own operation.
/// </summary>
internal sealed class SettingsTaxMasterReader(
    SettingsDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    ISettingsProductItemTypeSourceV1? productItemTypes = null) : ISettingsLineTaxResolverV1
{
    public async Task<SettingsTaxDefaultResolutionV1> ResolveAsync(Guid productId, string buyerSituation, DateOnly on,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireAccess();
        if (productId == Guid.Empty || !TaxBuyerSituationsV1.IsKnown(buyerSituation))
        {
            throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.RequestInvalid);
        }

        var source = productItemTypes
            ?? throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.ProviderUnavailable);
        var answer = await source.GetAsync(companyId, productId, cancellationToken);
        if (answer.Outcome == SettingsProductItemTypeOutcomeV1.Denied)
        {
            throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.AccessDenied);
        }

        if (answer.Outcome is not (SettingsProductItemTypeOutcomeV1.Found or SettingsProductItemTypeOutcomeV1.NotFound
            or SettingsProductItemTypeOutcomeV1.NotSet))
        {
            throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.ProviderUnavailable);
        }

        if (answer.Outcome == SettingsProductItemTypeOutcomeV1.NotFound)
        {
            return Map(companyId, productId, null, buyerSituation, on, new TaxDefaultResolutionPartsV1(
                TaxResolutionStatesV1.Unresolved, TaxResolutionReasonsV1.ProductNotFound, null, null, null, null, null, null, null,
                null, null, null, null));
        }

        var data = await ReadAsync(companyId, cancellationToken);
        var itemType = answer.Outcome == SettingsProductItemTypeOutcomeV1.Found ? answer.ItemType : null;
        return Map(companyId, productId, itemType, buyerSituation, on,
            TaxMasterCalculatorV1.Resolve(data, itemType, buyerSituation, on));
    }

    public async Task<SettingsTaxDefaultResolutionV1> GetItemTypeDefaultAsync(string itemType, string buyerSituation, DateOnly on,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireAccess();
        if (!TaxItemTypesV1.IsKnown(itemType) || !TaxBuyerSituationsV1.IsKnown(buyerSituation))
        {
            throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.RequestInvalid);
        }

        var data = await ReadAsync(companyId, cancellationToken);
        return Map(companyId, null, itemType, buyerSituation, on, TaxMasterCalculatorV1.Resolve(data, itemType, buyerSituation, on));
    }

    public async Task<SettingsTaxCodeListResolutionV1> GetCodeListAsync(string kind, DateOnly on,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireAccess();
        if (!TaxCodeListKindsV1.IsKnown(kind))
        {
            throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.RequestInvalid);
        }

        var data = await ReadAsync(companyId, cancellationToken);
        return TaxMasterCalculatorV1.ListInForce(data, kind, on) is { } list
            ? new SettingsTaxCodeListResolutionV1(companyId, kind, on, TaxResolutionStatesV1.Resolved, list.ListVersion,
                list.EffectiveFrom, list.Entries, null)
            : new SettingsTaxCodeListResolutionV1(companyId, kind, on, TaxResolutionStatesV1.Unresolved, null, null, [],
                TaxResolutionReasonsV1.CodeListMissing);
    }

    public async Task<SettingsWithholdingThresholdResolutionV1> GetWithholdingThresholdAsync(DateOnly on,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireAccess();
        var data = await ReadAsync(companyId, cancellationToken);
        return TaxMasterCalculatorV1.ThresholdInForce(data, on) is { } record
            ? new SettingsWithholdingThresholdResolutionV1(companyId, on, TaxResolutionStatesV1.Resolved, record.Amount,
                record.CurrencyCode, record.RecordId, record.RecordVersion, record.EffectiveFrom, null)
            : new SettingsWithholdingThresholdResolutionV1(companyId, on, TaxResolutionStatesV1.Unresolved, null, null, null, null,
                null, TaxResolutionReasonsV1.WithholdingThresholdMissing);
    }

    private static SettingsTaxDefaultResolutionV1 Map(Guid companyId, Guid? productId, string? itemType, string buyerSituation,
        DateOnly on, TaxDefaultResolutionPartsV1 parts) =>
        new SettingsTaxDefaultResolutionV1(
            companyId,
            productId,
            itemType,
            buyerSituation,
            on,
            parts.State,
            parts.Reason,
            parts.Record?.Source,
            parts.Record?.RecordId,
            parts.Record?.RecordVersion,
            parts.VatState,
            parts.VatPercent,
            parts.ExemptionCodeState,
            parts.ExemptionCode,
            parts.ExemptionCodeReason,
            parts.WithholdingState,
            parts.WithholdingPercent,
            parts.WithholdingCodeState,
            parts.WithholdingCode,
            parts.WithholdingCodeReason,
            parts.BuyerAdjustment);

    private Guid RequireAccess()
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId)
            || !companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.AccessDenied);
        }

        return companyContext.CompanyId;
    }

    private async Task<TaxMasterDataV1> ReadAsync(Guid companyId, CancellationToken cancellationToken)
    {
        try
        {
            return (await TaxMasterRows.ReadAsync(database, track: false, cancellationToken)).ToData(companyId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.ProviderUnavailable);
        }
    }
}
