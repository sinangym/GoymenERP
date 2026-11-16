using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;
using GoymenERP.Modules.Product.Infrastructure.PriceLists.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D382 / SAT-D383 / SAT-D397 / SAT-D401 (STK-SC36-c) on a REAL SQL Server. A customer's override takes effect from its
/// approved effective date over the product default, and the read port answers by the consumer's business date. An approval date
/// earlier than today or than the latest approved date is refused. A waiting version refuses a new import, and the refused file
/// is kept with its full report; an inactive product is a row error in a file and a refusal in an edit. The kept files refuse
/// update and delete in the database (51871), and the forward-fix rollback of
/// <see cref="AddProductPackageStandardApprovalAndFiles"/> passes on an empty store and refuses (51872) while an approval, an
/// override or a kept file exists. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class ProductPriceListSqlServerRelationalAcceptanceTests
{
    private const int PackageImportFileGuardRefusal = 51871;
    private const int PackageApprovalRollbackRefusal = 51872;

    [SqlServerFact]
    public async Task The_package_standard_approval_migrations_Down_passes_on_an_empty_store_and_refuses_while_a_kept_file_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddProductPackageStandardApprovalAndFiles().DownOperations[0]).Sql;
        var clock = new FixedClock(PackageClockNow);

        await using (var scope = run.Request())
        {
            await scope.Context.Database.ExecuteSqlRawAsync(guard);

            var refused = await PackageService(scope, run.CreatorActorId, clock).ImportAsync(
                PackageImportRequest("0", "SENTETIK-YOK", "6"), PackageMetadata(run.CreatorActorId, "package-guard-0"));
            Assert.Equal(400, refused.Answer.StatusCode);

            var refusal = await Assert.ThrowsAsync<SqlException>(() => scope.Context.Database.ExecuteSqlRawAsync(guard));
            Assert.Equal(PackageApprovalRollbackRefusal, refusal.Number);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductPackageStandardImportFiles"));
    }

    [SqlServerFact]
    public async Task The_kept_package_standard_import_files_refuse_update_and_delete_in_the_database()
    {
        await using var run = await Run.StartAsync();
        var clock = new FixedClock(PackageClockNow);

        await using (var scope = run.Request())
        {
            var refused = await PackageService(scope, run.CreatorActorId, clock).ImportAsync(
                PackageImportRequest("0", "SENTETIK-YOK", "6"), PackageMetadata(run.CreatorActorId, "package-kept-0"));
            Assert.Equal(400, refused.Answer.StatusCode);
        }

        await using (var scope = run.Request())
        {
            var update = await Assert.ThrowsAsync<SqlException>(() => scope.Context.Database.ExecuteSqlRawAsync(
                "UPDATE product.ProductPackageStandardImportFiles SET SourceLabel = N'SENTETIK DEGISTI'"));
            Assert.Equal(PackageImportFileGuardRefusal, update.Number);

            var delete = await Assert.ThrowsAsync<SqlException>(() => scope.Context.Database.ExecuteSqlRawAsync(
                "DELETE FROM product.ProductPackageStandardImportFiles"));
            Assert.Equal(PackageImportFileGuardRefusal, delete.Number);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM product.ProductPackageStandardImportFiles WHERE SourceLabel = N'SENTETIK KAYNAK'"));
    }

    [SqlServerFact]
    public async Task A_customer_override_takes_effect_from_its_approved_future_date_over_the_product_default()
    {
        await using var run = await Run.StartAsync();
        var code = await run.ScalarAsync<string>("SELECT Code FROM product.Products WHERE Id = @id", ProductId);
        var clock = new FixedClock(PackageClockNow);
        var today = ProductPriceBusinessDayV2.DateOf(clock.GetUtcNow());
        var future = today.AddDays(7);
        var approver = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var otherCustomer = Guid.NewGuid();
        var activeCustomers = new PackageCustomers(ProductPackageStandardCustomerOutcomeV3.Found, customer);

        await using (var scope = run.Request())
        {
            Assert.Equal(204, (await PackageService(scope, run.CreatorActorId, clock).ImportAsync(
                PackageImportRequest("0", code, "12"), PackageMetadata(run.CreatorActorId, "package-override-import"))).Answer
                .StatusCode);
            Assert.Equal(204, (await PackageService(scope, approver, clock).ApproveAsync(1,
                new ApproveProductPackageStandardVersionRequestV3(ProductPriceListWire.FormatDate(today), "SENTETIK onay"),
                PackageMetadata(approver, "package-override-approve-1"))).Answer.StatusCode);
        }

        await using (var scope = run.Request())
        {
            // The Cari owner cannot answer: fail-closed, nothing is stored and the key stays usable.
            var unavailable = await PackageService(scope, run.CreatorActorId, clock).SetCustomerAsync(customer, ProductId,
                new SetProductPackageStandardRequestV2("1", 18, "SENTETIK gerekçe"),
                PackageMetadata(run.CreatorActorId, "package-override-set-0"));
            Assert.Equal((503, ProductPackageStandardProblemCodesV3.CustomerProviderUnavailable),
                (unavailable.Answer.StatusCode, unavailable.Answer.Code));

            var preparer = PackageService(scope, run.CreatorActorId, clock, activeCustomers);
            var notCustomer = await preparer.SetCustomerAsync(otherCustomer, ProductId,
                new SetProductPackageStandardRequestV2("1", 18, "SENTETIK gerekçe"),
                PackageMetadata(run.CreatorActorId, "package-override-set-1"));
            Assert.Equal((409, ProductPackageStandardProblemCodesV3.CustomerNotFound),
                (notCustomer.Answer.StatusCode, notCustomer.Answer.Code));

            var set = await preparer.SetCustomerAsync(customer, ProductId,
                new SetProductPackageStandardRequestV2("1", 18, "SENTETIK gerekçe"),
                PackageMetadata(run.CreatorActorId, "package-override-set-0"));
            Assert.Equal(204, set.Answer.StatusCode);

            var view = await PackageService(scope, approver, clock, activeCustomers).GetApprovalStateForApproverAsync();
            Assert.Equal(("2", ProductPriceListWire.FormatDate(today), "1", "2"),
                (view.HeadVersion, view.Today, view.EffectiveVersion, view.Pending?.Version));
            var change = Assert.Single(view.PendingChanges);
            Assert.Equal(((Guid?)customer, ProductId, (int?)null, (int?)18),
                (change.CustomerId, change.ProductId, change.PiecesBefore, change.PiecesAfter));
            Assert.Empty(view.CustomerEntries);
        }

        await using (var scope = run.Request())
        {
            var decider = PackageService(scope, approver, clock, activeCustomers);
            var yesterday = await decider.ApproveAsync(2, new ApproveProductPackageStandardVersionRequestV3(
                ProductPriceListWire.FormatDate(today.AddDays(-1)), "SENTETIK onay"), PackageMetadata(approver, "package-override-approve-x"));
            Assert.Equal((400, ProductPackageStandardProblemCodesV3.EffectiveDateInvalid),
                (yesterday.Answer.StatusCode, yesterday.Answer.Code));
            Assert.Equal(204, (await decider.ApproveAsync(2, new ApproveProductPackageStandardVersionRequestV3(
                ProductPriceListWire.FormatDate(future), "SENTETIK onay"), PackageMetadata(approver, "package-override-approve-2")))
                .Answer.StatusCode);

            var reader = (IProductPackageStandardReaderV3)decider;
            var beforeDate = await reader.GetAsync(ProductId, customer, today);
            Assert.Equal((12, 1L, ProductPackageStandardEntryScopesV3.Product, (DateOnly?)today),
                (beforeDate.PiecesPerPackage, beforeDate.StandardVersion, beforeDate.EntryScope, beforeDate.EffectiveFrom));
            var fromDate = await reader.GetAsync(ProductId, customer, future);
            Assert.Equal((ProductPackageStandardReadOutcomeV2.Found, 18, 2L, ProductPackageStandardEntryScopesV3.Customer,
                (DateOnly?)future), (fromDate.Outcome, fromDate.PiecesPerPackage, fromDate.StandardVersion, fromDate.EntryScope,
                fromDate.EffectiveFrom));
            var otherFromDate = await reader.GetAsync(ProductId, otherCustomer, future);
            Assert.Equal((12, 2L, ProductPackageStandardEntryScopesV3.Product),
                (otherFromDate.PiecesPerPackage, otherFromDate.StandardVersion, otherFromDate.EntryScope));
            var defaultFromDate = await reader.GetAsync(ProductId, null, future);
            Assert.Equal((12, ProductPackageStandardEntryScopesV3.Product),
                (defaultFromDate.PiecesPerPackage, defaultFromDate.EntryScope));
            Assert.Equal(12, (await ((IProductPackageStandardReaderV2)decider).GetAsync(ProductId)).PiecesPerPackage);
        }

        await using (var scope = run.Request())
        {
            // A newer version never takes effect before the latest approved date; the refusal is the key's terminal answer.
            var preparer = PackageService(scope, run.CreatorActorId, clock, activeCustomers);
            Assert.Equal(204, (await preparer.SetAsync(ProductId, new SetProductPackageStandardRequestV2("2", 14, "SENTETIK gerekçe"),
                PackageMetadata(run.CreatorActorId, "package-override-set-2"))).Answer.StatusCode);
            var decider = PackageService(scope, approver, clock, activeCustomers);
            var early = await decider.ApproveAsync(3, new ApproveProductPackageStandardVersionRequestV3(
                ProductPriceListWire.FormatDate(today), "SENTETIK onay"), PackageMetadata(approver, "package-override-approve-3"));
            Assert.Equal((409, ProductPackageStandardProblemCodesV3.EffectiveDateInvalid), (early.Answer.StatusCode, early.Answer.Code));
            var replay = await decider.ApproveAsync(3, new ApproveProductPackageStandardVersionRequestV3(
                ProductPriceListWire.FormatDate(today), "SENTETIK onay"), PackageMetadata(approver, "package-override-approve-3"));
            Assert.Equal((true, 409), (replay.IsReplay, replay.Answer.StatusCode));
            Assert.Equal(204, (await decider.RejectAsync(3, new RejectProductPackageStandardVersionRequestV3("SENTETIK ret"),
                PackageMetadata(approver, "package-override-reject-3"))).Answer.StatusCode);
        }

        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductPackageStandardVersions"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM product.ProductPackageStandardCustomerLines WHERE CustomerId = @id", customer));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductPackageStandardDecisions"));
        // The unavailable owner and the past date stored nothing; every other command is one answer.
        Assert.Equal(8, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductPackageStandardAttempts"));

        await using (var scope = run.Request())
        {
            var zero = await Assert.ThrowsAsync<SqlException>(() => scope.Context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO product.ProductPackageStandardCustomerLines (CompanyId, Version, CustomerId, ProductId, PiecesPerPackage)
                VALUES ({TestCompanyIds.CompanyA}, 3, {otherCustomer}, {ProductId}, 0)
                """));
            Assert.Equal(CheckConstraintViolation, zero.Number);

            var guard = Assert.IsType<SqlOperation>(new AddProductPackageStandardApprovalAndFiles().DownOperations[0]).Sql;
            var refusal = await Assert.ThrowsAsync<SqlException>(() => scope.Context.Database.ExecuteSqlRawAsync(guard));
            Assert.Equal(PackageApprovalRollbackRefusal, refusal.Number);
        }
    }

    [SqlServerFact]
    public async Task A_waiting_version_refuses_a_new_import_and_keeps_the_file_and_an_inactive_product_is_refused()
    {
        await using var run = await Run.StartAsync();
        var code = await run.ScalarAsync<string>("SELECT Code FROM product.Products WHERE Id = @id", ProductId);
        var clock = new FixedClock(PackageClockNow);

        await using (var scope = run.Request())
        {
            var service = PackageService(scope, run.CreatorActorId, clock);
            Assert.Equal(204, (await service.ImportAsync(PackageImportRequest("0", code, "12"),
                PackageMetadata(run.CreatorActorId, "package-wait-import-1"))).Answer.StatusCode);
            var waiting = await service.ImportAsync(PackageImportRequest("1", code, "24"),
                PackageMetadata(run.CreatorActorId, "package-wait-import-2"));
            Assert.Equal((409, ProductPackageStandardProblemCodesV3.ApprovalPending), (waiting.Answer.StatusCode, waiting.Answer.Code));
            var waitingReplay = await service.ImportAsync(PackageImportRequest("1", code, "24"),
                PackageMetadata(run.CreatorActorId, "package-wait-import-2"));
            Assert.Equal((true, ProductPackageStandardProblemCodesV3.ApprovalPending),
                (waitingReplay.IsReplay, waitingReplay.Answer.Code));
        }

        await run.ExecuteAsync("UPDATE product.Products SET Lifecycle = 2 WHERE Id = @id", ProductId);

        Guid refusedImportId;
        await using (var scope = run.Request())
        {
            var service = PackageService(scope, run.CreatorActorId, clock);
            var inactiveRow = await service.ImportAsync(PackageImportRequest("1", code, "24"),
                PackageMetadata(run.CreatorActorId, "package-wait-import-3"));
            Assert.Equal((400, ProductPackageStandardProblemCodesV2.FileInvalid), (inactiveRow.Answer.StatusCode, inactiveRow.Answer.Code));
            Assert.Contains("pasif", inactiveRow.Answer.Detail, StringComparison.Ordinal);

            var inactiveSet = await service.SetAsync(ProductId, new SetProductPackageStandardRequestV2("1", 10, "SENTETIK gerekçe"),
                PackageMetadata(run.CreatorActorId, "package-wait-set-1"));
            Assert.Equal((409, ProductPackageStandardProblemCodesV3.ProductInactive),
                (inactiveSet.Answer.StatusCode, inactiveSet.Answer.Code));

            var list = await service.ListImportsAsync();
            Assert.Equal(3, list.Items.Count);
            Assert.Equal(1, list.Items.Count(item => item.Outcome == ProductPackageStandardImportOutcomesV3.Accepted
                && item.Version == "1"));
            var refused = Assert.Single(list.Items, item => item.ProblemCode == ProductPackageStandardProblemCodesV2.FileInvalid);
            Assert.Equal((ProductPackageStandardImportOutcomesV3.Refused, (string?)null, "paket.xlsx", 1, 1),
                (refused.Outcome, refused.Version, refused.FileName, refused.RowCount, refused.ErrorCount));
            refusedImportId = refused.ImportId;

            var report = Assert.IsType<ProductPackageStandardImportReportV3>(await service.GetImportReportAsync(refusedImportId));
            Assert.Equal((0, ProductPackageStandardImportOutcomesV3.Refused), (report.AcceptedCount, report.Outcome));
            Assert.Contains("pasif", Assert.Single(report.Errors), StringComparison.Ordinal);
            Assert.Null(await service.GetImportReportAsync(Guid.NewGuid()));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductPackageStandardVersions"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Product.PackageStandardImportFile'"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Product.PackageStandardImportFile' AND EntityId = @id",
            refusedImportId.ToString("D")));
        Assert.Equal(64, await run.ScalarAsync<int>(
            "SELECT LEN(FileSha256) FROM product.ProductPackageStandardImportFiles WHERE ImportId = @id", refusedImportId));
    }

    /// <summary>The Cari owner's fake: answers <paramref name="outcome"/> and names only <paramref name="active"/> customers.</summary>
    private sealed class PackageCustomers(ProductPackageStandardCustomerOutcomeV3 outcome, params Guid[] active)
        : IProductPackageStandardCustomerPortV3
    {
        public Task<ProductPackageStandardCustomerAnswerV3> ReadAsync(IReadOnlyList<Guid> customerIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProductPackageStandardCustomerAnswerV3(outcome, customerIds
                .Where(id => active.Contains(id))
                .Select(id => new ProductPackageStandardCustomerV3(id, "SENTETIK-C", "SENTETIK Müşteri"))
                .ToList()));
    }
}
