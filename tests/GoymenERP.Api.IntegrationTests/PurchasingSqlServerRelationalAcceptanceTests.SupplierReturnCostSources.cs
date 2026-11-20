using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Supplier_return_real_owner_preview_preserves_independent_units_and_pending_selection_without_price_disclosure()
    {
        await using var run = await Run.StartAsync();
        // The physical receipt is 5 kg while Purchasing accepted 4 units: no conversion may be guessed.
        var receipt = await SeedReturnInvoiceCostReceiptAsync(run, 5m);
        var guard = Assert.IsType<SqlOperation>(new AddPurchaseSupplierReturnCostAllocations().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);
        var actor = Guid.Parse(run.Actor);
        await GrantReturnDisclosureActorAsync(run, actor, price: false);
        await using var scope = run.Factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var service = ReturnDisclosureService(services, actor, services.GetRequiredService<IPurchaseSupplierReturnCostSourceReadPortV1>());
        var preview = await service.GetSupplierReturnCostSourcesAsync(receipt.Match.OrderId, receipt.Match.AcceptanceId);
        Assert.Equal(new PurchaseSupplierReturnExactQuantityV1("5", "1"), preview.PendingQuantityKg);
        Assert.Equal(new PurchaseSupplierReturnExactQuantityV1("4", "1"), preview.PendingUndoQuantity);
        Assert.Empty(preview.InvoicedSources);
        var proof = await services.GetRequiredService<PurchaseSupplierReturnPurchaseSourceReader>().ReadAsync(CompanyA,
            receipt.Match.OrderId, receipt.Match.AcceptanceId);
        Assert.Equal(4m, proof.AcceptedQuantity);
        Assert.Empty(proof.ReceiptAdjustments);

        var request = DisclosureRequest(receipt.Match.AcceptanceId) with
        {
            QuantityKg = "2", UndoQuantity = "1", ExpectedPurchaseOrderVersion = receipt.Match.OrderVersion.ToString(CultureInfo.InvariantCulture),
            CostAllocation = new(preview.SourceVersion, new(false, "2", "1"), []),
        };
        var metadata = new PurchasingCommandMetadata("SENTETIK-selected-pending-return", run.Correlation, null, AcceptanceTime);
        var created = await service.CreateSupplierReturnAsync(receipt.Match.OrderId, request, metadata);
        AssertReturnSqlPrice(created.Return, visible: false);
        Assert.Equal("PendingCostException", created.Return.Status);
        Assert.Empty(created.Return.CostAllocationSources!);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM inventory.Movements WHERE Id=@id", ("@id", request.InventoryMovementId)));
        var json = await run.ScalarAsync<string>("SELECT CostAllocationJson FROM purchasing.PurchaseSupplierReturns WHERE ReturnId=@id", ("@id", request.ReturnId));
        using (var snapshot = JsonDocument.Parse(json))
        {
            Assert.Equal(preview.SourceVersion, snapshot.RootElement.GetProperty("selection").GetProperty("expectedSourceVersion").GetString());
            Assert.Empty(snapshot.RootElement.GetProperty("invoiceSources").EnumerateArray());
        }
        await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE purchasing.PurchaseSupplierReturns SET CostAllocationJson='not-json' WHERE ReturnId=@id", ("@id", request.ReturnId)));
        Assert.Equal(52012, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard))).Number);
        Assert.Equal(json, await run.ScalarAsync<string>("SELECT CostAllocationJson FROM purchasing.PurchaseSupplierReturns WHERE ReturnId=@id", ("@id", request.ReturnId)));

        await using var reload = run.Factory.Services.CreateAsyncScope();
        var reader = ReturnDisclosureService(reload.ServiceProvider, actor);
        var detail = await reader.GetSupplierReturnAsync(receipt.Match.OrderId, request.ReturnId);
        Assert.Equal(preview.SourceVersion, detail.Return.CostAllocation!.ExpectedSourceVersion);
        Assert.Equal(new PurchaseSupplierReturnSourceSelectionV1(false, "2", "1"), detail.Return.CostAllocation.Pending);
        AssertReturnSqlPrice(detail.Return, visible: false);
        var replay = await reader.CreateSupplierReturnAsync(receipt.Match.OrderId, request, metadata);
        Assert.True(replay.IsReplay);
        Assert.Equal(preview.SourceVersion, replay.Return.CostAllocation!.ExpectedSourceVersion);
        AssertReturnSqlPrice(replay.Return, visible: false);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseSupplierReturnAttempts WHERE ReturnId=@id", ("@id", request.ReturnId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType='Purchasing.SupplierReturn' AND EntityId=@id", ("@id", request.ReturnId.ToString("D"))));
    }

    [SqlServerFact]
    public async Task Supplier_return_purchase_source_reader_keeps_company_and_acceptance_identity_boundaries()
    {
        await using var run = await Run.StartAsync();
        var receipt = await SeedReturnInvoiceCostReceiptAsync(run, 7m);
        await using var scope = run.Factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var company = services.GetRequiredService<ICompanyContext>();
        company.Set(CompanyA);
        var reader = services.GetRequiredService<PurchaseSupplierReturnPurchaseSourceReader>();
        var source = await reader.ReadAsync(CompanyA, receipt.Match.OrderId, receipt.Match.AcceptanceId);
        Assert.Equal(source.SourceVersion, (await reader.ReadAsync(CompanyA, receipt.Match.OrderId, receipt.Match.AcceptanceId)).SourceVersion);
        Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            reader.ReadAsync(CompanyB, receipt.Match.OrderId, receipt.Match.AcceptanceId))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            reader.ReadAsync(CompanyA, Guid.NewGuid(), receipt.Match.AcceptanceId))).Status);
        // A different tenant is a different request scope; never switch an already resolved context.
        await using var otherCompanyScope = run.Factory.Services.CreateAsyncScope();
        otherCompanyScope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyB);
        var otherCompanyReader = otherCompanyScope.ServiceProvider.GetRequiredService<PurchaseSupplierReturnPurchaseSourceReader>();
        Assert.Equal(404, (await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            otherCompanyReader.ReadAsync(CompanyB, receipt.Match.OrderId, receipt.Match.AcceptanceId))).Status);
    }
}
