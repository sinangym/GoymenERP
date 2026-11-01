using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// MOD-03: the line tax port the Sales order line / invoice consume (SAT-D93/D134/D135/D139/D140/D147/D158). It reads only
/// Settings' own tables under the company filter plus the reviewed SAT-D139 and SAT-D158 catalogs; the product's item type comes
/// from the Product owner (the shared product card, SAT-D149) through the host-composed <see cref="ISettingsProductItemTypeSourceV1"/>,
/// the buyer situation code and the chosen exemption code from the order (inputs).
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
        string? exemptionCode = null, CancellationToken cancellationToken = default)
    {
        var companyId = RequireAccess();
        if (productId == Guid.Empty || !ValidBuyerInput(buyerSituation, exemptionCode))
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
            TaxMasterCalculatorV1.Resolve(data, itemType, buyerSituation, on, Normalized(exemptionCode)));
    }

    public async Task<SettingsTaxDefaultResolutionV1> GetItemTypeDefaultAsync(string itemType, string buyerSituation, DateOnly on,
        string? exemptionCode = null, CancellationToken cancellationToken = default)
    {
        var companyId = RequireAccess();
        if (!TaxItemTypesV1.IsKnown(itemType) || !ValidBuyerInput(buyerSituation, exemptionCode))
        {
            throw new SettingsPrerequisiteException(TaxMasterProblemCodesV1.RequestInvalid);
        }

        var data = await ReadAsync(companyId, cancellationToken);
        return Map(companyId, null, itemType, buyerSituation, on,
            TaxMasterCalculatorV1.Resolve(data, itemType, buyerSituation, on, Normalized(exemptionCode)));
    }

    public async Task<SettingsTaxBuyerSituationListResolutionV1> GetBuyerSituationsAsync(DateOnly on,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireAccess();
        var data = await ReadAsync(companyId, cancellationToken);
        return TaxMasterCalculatorV1.BuyerSituationListInForce(data, on) is { } list
            ? new SettingsTaxBuyerSituationListResolutionV1(companyId, on, TaxResolutionStatesV1.Resolved, list.ListVersion,
                list.EffectiveFrom, list.Entries, null)
            : new SettingsTaxBuyerSituationListResolutionV1(companyId, on, TaxResolutionStatesV1.Unresolved, null, null, [],
                TaxResolutionReasonsV1.BuyerSituationListMissing);
    }

    /// <summary>A well-formed situation code and, when given, a well-formed exemption code (the list check is the calculator's).</summary>
    private static bool ValidBuyerInput(string? buyerSituation, string? exemptionCode) =>
        TaxMasterCalculatorV1.IsWellFormedSituationCode(buyerSituation)
        && (exemptionCode is null || TaxMasterCalculatorV1.TryNormalizeListCode(exemptionCode, out _));

    private static string? Normalized(string? exemptionCode) =>
        exemptionCode is not null && TaxMasterCalculatorV1.TryNormalizeListCode(exemptionCode, out var code) ? code : null;

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
            parts.BuyerAdjustment,
            parts.BuyerSituationList?.ListVersion,
            parts.BuyerSituation?.VatMode,
            parts.BuyerSituation?.DocumentRequired ?? false);

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
