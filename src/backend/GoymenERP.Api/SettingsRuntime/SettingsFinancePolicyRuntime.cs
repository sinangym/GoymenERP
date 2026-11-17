using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure;

namespace GoymenERP.Api.SettingsRuntime;

/// <summary>Composition only: each owner reads its own tables. No Finance write path is activated by this authoring UI.</summary>
public static class SettingsFinancePolicyRuntime
{
    public static IServiceCollection AddSettingsFinancePolicyRuntime(this IServiceCollection services)
    {
        services.AddSettingsFinancePolicyAuthoring();
        services.AddScoped<IIdentityFinancePolicyRoleReferencesV1, IdentityFinancePolicyRoleReferences>();
        services.AddScoped<IFinancePolicyCashReferencesV1, FinancePolicyCashReferences>();
        services.AddScoped<ISettingsFinancePolicyReferencesV1, SettingsFinancePolicyReferences>();
        services.AddScoped<IFinanceCollateralPolicyReaderV1, SettingsFinanceCollateralPolicyReader>();
        return services;
    }
}

public sealed class SettingsFinancePolicyReferences(IIdentityFinancePolicyRoleReferencesV1 roles,
    IFinancePolicyCashReferencesV1 cash, ISettingsMoneyCandidateReader currencies,
    ICompanyContext company, ICurrentUser user, IErpAuthorityRevalidationService authority) : ISettingsFinancePolicyReferencesV1
{
    public async Task<SettingsFinancePolicyReferencesV1> GetAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var actor = user.UserId;
        await FenceAsync(companyId, actor, cancellationToken);
        try
        {
            var roleSource = await roles.GetAsync(companyId, cancellationToken);
            await FenceAsync(companyId, actor, cancellationToken);
            var cashSource = await cash.GetAsync(companyId, cancellationToken);
            await FenceAsync(companyId, actor, cancellationToken);
            var currencySource = MoneyCurrencyCandidatesV1.EnsureWellFormed(await currencies.GetMoneyCurrencyCandidatesAsync(cancellationToken));
            await FenceAsync(companyId, actor, cancellationToken);
            if (roleSource is null || roleSource.CompanyId != companyId || roleSource.Roles is null
                || cashSource is null || cashSource.CompanyId != companyId || cashSource.CashAccounts is null)
                throw new SettingsPrerequisiteException(SettingsFinancePolicyCodesV1.Unavailable);
            return new(companyId, Operations(), roleSource.Roles.Select(x => new SettingsFinanceRoleReferenceV1(x.RoleId, x.Code, x.Name, x.SourceVersion)).ToArray(),
                cashSource.CashAccounts.Select(x => new SettingsFinanceCashReferenceV1(x.CashAccountId, x.CurrencyId, x.Name, x.Version.ToString(CultureInfo.InvariantCulture))).ToArray(),
                currencySource.Select(x => new SettingsFinanceCurrencyReferenceV1(x.CurrencyId, x.Code)).ToArray());
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not UnauthorizedAccessException and not SettingsPrerequisiteException)
        { throw new SettingsPrerequisiteException(SettingsFinancePolicyCodesV1.Unavailable); }
    }

    private static IReadOnlyList<SettingsFinanceOperationReferenceV1> Operations()
    {
        // Existing write authorities are references, not a new grant nor an activation of a dynamic approval flow.
        (FinanceAction Action, string Name)[] source =
        [
            (FinanceAction.SaveDraft, "Elle tahsilat taslağı"), (FinanceAction.Record, "Elle tahsilat kaydı"),
            (FinanceAction.ProposeReversal, "Tahsilat ters kayıt önerisi"), (FinanceAction.ApproveReversal, "Tahsilat ters kayıt onayı"),
            (FinanceAction.SaveManualBankPaymentDraft, "Banka ödemesi taslağı"), (FinanceAction.ApproveManualBankPayment, "Banka ödemesi onayı"),
            (FinanceAction.RecordManualBankPayment, "Banka ödemesi kaydı"), (FinanceAction.WriteAllocation, "Tahsilat dağıtımı"),
            (FinanceAction.ReceiveCustomerInstrument, "Müşteri evrakı kabulü"), (FinanceAction.ChangeCustomerInstrumentStatus, "Müşteri evrakı durum değişikliği"),
            (FinanceAction.CollectCustomerInstrument, "Müşteri evrakı tahsilatı"), (FinanceAction.ProposePaymentReversal, "Ödeme ters kayıt önerisi"),
            (FinanceAction.ApprovePaymentReversal, "Ödeme ters kayıt onayı"), (FinanceAction.ProposeCreditLimit, "Kredi limiti önerisi"),
            (FinanceAction.ApproveCreditLimit, "Kredi limiti onayı"), (FinanceAction.SaveExpensePaymentDraft, "Masraf ödemesi taslağı"),
            (FinanceAction.ApproveExpensePayment, "Masraf ödemesi onayı"), (FinanceAction.RecordExpensePayment, "Masraf ödemesi kaydı"),
            (FinanceAction.CommitCashTransfer, "Kasa/banka virman kaydı"), (FinanceAction.RecordCollateral, "Teminat kaydı"),
            (FinanceAction.ProposeOpeningBalance, "Açılış bakiyesi önerisi"), (FinanceAction.ApproveOpeningBalance, "Açılış bakiyesi onayı"),
            (FinanceAction.ProposeCredit, "Kredi önerisi"), (FinanceAction.ApproveCredit, "Kredi onayı"),
            (FinanceAction.RecordPartyBalanceTransfer, "Cari bakiye aktarımı")
        ];
        var rows = source.Select(x => new SettingsFinanceOperationReferenceV1(FinancePermissionPort.NameOf(x.Action) ?? string.Empty, x.Name)).ToArray();
        if (rows.Any(x => !ErpPermissions.IsKnown(x.Permission))) throw new SettingsPrerequisiteException(SettingsFinancePolicyCodesV1.Unavailable);
        return rows.DistinctBy(x => x.Permission, StringComparer.Ordinal).ToArray();
    }

    private async Task FenceAsync(Guid id, string? actor, CancellationToken token)
    {
        Same(id, actor, token);
        if (!ErpPermissions.IsKnown(SettingsFinancePolicyPermissionsV1.Prepare)) throw new SettingsPrerequisiteException(SettingsFinancePolicyCodesV1.Unavailable);
        var scope = await authority.RevalidatePermissionScopeAsync(SettingsFinancePolicyPermissionsV1.Prepare, id, token);
        Same(id, actor, token);
        if (!scope.IsGranted || !scope.IsCompanyWide) throw new UnauthorizedAccessException();
    }
    private void Same(Guid id, string? actor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (id == Guid.Empty || !company.IsResolved || company.CompanyId != id || !user.IsAuthenticated
            || string.IsNullOrWhiteSpace(actor) || user.UserId != actor || !user.CanAccessCompany(id)) throw new UnauthorizedAccessException();
    }
}
