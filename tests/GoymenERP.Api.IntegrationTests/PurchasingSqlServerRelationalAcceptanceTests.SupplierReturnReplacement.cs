using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Returns;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    /// <summary>
    /// ALS-D91 (K3-B P1): a supplier return marked "yerine yenisi gelecek" on the receipt-adjusting path is stored by the real store
    /// (the earlier CHECK refused it on SQL Server); the widened CHECK still refuses the flag without the adjustment metadata, and the
    /// migration's Down refuses with 52300 while such a return exists (forward fix), leaving the row unchanged.
    /// </summary>
    [SqlServerFact]
    public async Task A_replacement_expected_supplier_return_is_stored_the_check_keeps_its_adjustment_metadata_and_Down_refuses_52300()
    {
        await using var run = await Run.StartAsync();
        var seed = await SeedApprovedAcceptanceOrderAsync(run);
        var acceptance = Guid.NewGuid();
        await StageAndCommitAsync(run, CompanyA, AcceptanceRequest(seed, acceptance, "4"), Envelope("replacement-source", "replacement-source"));
        var returnId = Guid.NewGuid();
        await using (var scope = run.Factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
            var owner = scope.ServiceProvider.GetRequiredService<IPurchaseSupplierReturnStore>();
            await owner.SaveNewAsync(new PurchaseSupplierReturnDraft(returnId, Guid.NewGuid(), seed.OrderId, seed.LineId, acceptance,
                    Guid.NewGuid(), PurchaseSupplierReturnStatus.PendingCostException, "Free", 1m,
                    PurchaseSupplierReturnCostBasis.ManualCostWithException, 2m, ReplacementExpected: true,
                    "SENTETIK yerine yenisi gelecek", AcceptanceTime, 4, 1m),
                Envelope("replacement-key", "replacement-correlation"), AcceptanceHash("replacement-key"), PurchaseSupplierReturnCommandsV1.CreateV1);
        }

        Assert.True(await run.ScalarAsync<bool>("SELECT ReplacementExpected FROM purchasing.PurchaseSupplierReturns WHERE CompanyId=@company AND ReturnId=@return",
            ("@company", CompanyA), ("@return", returnId)));
        // The flag without the receipt-adjusting facts is still refused by SQL Server.
        var check = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE purchasing.PurchaseSupplierReturns SET ExpectedPurchaseOrderVersion = NULL, UndoQuantity = NULL "
            + "WHERE CompanyId=@company AND ReturnId=@return; SELECT @@ROWCOUNT;", ("@company", CompanyA), ("@return", returnId)));
        Assert.Equal(547, check.Number);
        Assert.Contains("CK_PurchaseSupplierReturns_Values", check.Message, StringComparison.Ordinal);

        const string snapshot = "SELECT * FROM purchasing.PurchaseSupplierReturns WHERE CompanyId=@company AND ReturnId=@return FOR JSON PATH";
        var before = await run.ScalarAsync<string>(snapshot, ("@company", CompanyA), ("@return", returnId));
        var guard = Assert.IsType<SqlOperation>(new AllowPurchaseSupplierReturnReplacement().DownOperations[0]).Sql;
        var refusal = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + "\nSELECT 1;"));
        Assert.Equal(52300, refusal.Number);
        Assert.Equal(before, await run.ScalarAsync<string>(snapshot, ("@company", CompanyA), ("@return", returnId)));
    }
}
