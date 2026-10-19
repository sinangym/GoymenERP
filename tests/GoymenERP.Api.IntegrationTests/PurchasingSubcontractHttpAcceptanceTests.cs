using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class PurchasingSubcontractHttpAcceptanceTests
{
    [Fact]
    public void Wire_contract_is_strict_and_exposes_save_reopen_admit_routes()
    {
        var line = Guid.NewGuid(); var service = Guid.NewGuid(); var unit = Guid.NewGuid(); var physical = Guid.NewGuid();
        var json = $$"""
            {"supplierPartyId":"{{Guid.NewGuid()}}","expectedSupplierPartyVersion":"party-v1",
             "companyAccountId":"{{Guid.NewGuid()}}","expectedCompanyAccountVersion":"account-v1",
             "lines":[{"lineId":"{{line}}","serviceId":"{{service}}","expectedServiceVersion":"service-v1",
             "serviceUnitId":"{{unit}}","serviceQuantity":"12.5000","sourcePhysicalRequestId":"{{physical}}",
             "expectedSourcePhysicalRequestVersion":"physical-v1"}]}
            """;
        var request = PurchasingSubcontractJsonV1.ReadCreate(json);
        Assert.Equal("12.5000", Assert.Single(request.Lines).ServiceQuantity);
        Assert.Equal("/api/v1/purchasing/subcontracts", PurchasingSubcontractApiV1.Path);
        Assert.Equal("/api/v1/purchasing/subcontracts/{id}/admission", PurchasingSubcontractApiV1.AdmissionPath);
        Assert.Throws<JsonException>(() => PurchasingSubcontractJsonV1.ReadAdmit("{\"expectedVersion\":\"1\",\"extra\":true}"));
    }

    [Fact]
    public void Response_contract_carries_replay_and_reopened_admission_snapshot_without_effects()
    {
        var properties = typeof(PurchasingSubcontractResponseV1).GetProperties().Select(x => x.Name).ToArray();
        Assert.Contains(nameof(PurchasingSubcontractResponseV1.IsReplay), properties);
        Assert.Contains(nameof(PurchasingSubcontractResponseV1.OriginalCorrelationId), properties);
        Assert.DoesNotContain(typeof(PurchasingSubcontractAdmissionViewV1).GetProperties(),
            property => property.Name.Contains("Quantity", StringComparison.OrdinalIgnoreCase));
    }
}
