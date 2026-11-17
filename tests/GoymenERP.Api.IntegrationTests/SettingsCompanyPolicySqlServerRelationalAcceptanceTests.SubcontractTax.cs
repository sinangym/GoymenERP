using System.Net;
using System.Net.Http.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SettingsCompanyPolicySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Sales_and_Purchasing_subcontract_defaults_use_separate_existing_approval_authorities_and_real_versioned_rate_lists()
    {
        await using var run = await HostRun.StartAsync();
        var salesSubject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, ErpPermissions.SalesOrdersApprove);
        var purchaseSubject = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, ErpPermissions.PurchasingOrdersApprove);
        await run.GrantAsync(salesSubject, CompanyA, SettingsTaxMasterPermissionManifestV1.TaxMasterManage);
        using var sales = run.Client(salesSubject, CompanyA); using var purchase = run.Client(purchaseSubject, CompanyA);
        const string salesPath = "/api/v1/settings/company-policies/sales-subcontract-tax-default";
        const string purchasePath = "/api/v1/settings/company-policies/purchasing-subcontract-tax-default";
        var initial = await purchase.GetFromJsonAsync<SubcontractTaxDefaultViewV1>(purchasePath);
        Assert.Equal("0", initial!.Version); Assert.Null(initial.Current); Assert.Empty(initial.Versions);
        var empty = await purchase.GetFromJsonAsync<SubcontractTaxOptionsV1>(purchasePath + "/options");
        Assert.Equal((SubcontractTaxSideV1.Purchasing, "0", null), (empty!.Side, empty.DefaultVersion, empty.Default));
        Assert.Empty(empty.VatRates);
        using (var denied = await purchase.GetAsync(salesPath)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await sales.GetAsync(purchasePath)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var rates = new PublishTaxRateListRequestV1("0", [new("20", "SENTETIK oran A", "20"), new("10", "SENTETIK oran B", "10")], "SENTETIK liste");
        await run.CommandAsync(sales, "/api/v1/settings/tax-master/rate-lists/VatRate/versions", "tax-real-list", rates);
        var salesValue = new RecordSubcontractTaxDefaultRequestV1("0", true, "20", null, false, null, null, "SENTETIK satış varsayılanı");
        var purchaseValue = salesValue with { VatPercent = "10", Reason = "SENTETIK alış varsayılanı" };
        using (var denied = await sales.SendAsync(Json(purchasePath + "/versions", "wrong-side", purchaseValue))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await run.CommandAsync(sales, salesPath + "/versions", "tax-sales", salesValue);
        await run.CommandAsync(purchase, purchasePath + "/versions", "tax-purchase", purchaseValue);
        await run.CommandAsync(purchase, purchasePath + "/versions", "tax-purchase", purchaseValue, replay: true);
        Assert.Equal("20", (await sales.GetFromJsonAsync<SubcontractTaxDefaultViewV1>(salesPath))!.Current!.VatPercent);
        var selected = await purchase.GetFromJsonAsync<SubcontractTaxOptionsV1>(purchasePath + "/options");
        Assert.Equal((CompanyA, SubcontractTaxSideV1.Purchasing, "1", "10"),
            (selected!.CompanyId, selected.Side, selected.DefaultVersion, selected.Default!.VatPercent));
        Assert.Equal(2, selected.VatRates.Count); Assert.False(string.IsNullOrWhiteSpace(selected.SourceVersion));
        using (var stale = await purchase.SendAsync(Json(purchasePath + "/versions", "tax-stale", purchaseValue)))
            await ProblemAsync(stale, HttpStatusCode.Conflict, CompanyPolicyProblemCodesV1.VersionConflict);
        using (var unknown = await purchase.SendAsync(Json(purchasePath + "/versions", "tax-unknown-rate", purchaseValue with { ExpectedVersion = "1", VatPercent = "30" })))
            await ProblemAsync(unknown, HttpStatusCode.UnprocessableEntity, CompanyPolicyProblemCodesV1.RateUnknown);
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Settings.PurchasingSubcontractTaxDefault'"));
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Settings.SalesSubcontractTaxDefault'"));
        Assert.Single((await purchase.GetFromJsonAsync<SubcontractTaxDefaultViewV1>(purchasePath))!.Versions);
    }
}
