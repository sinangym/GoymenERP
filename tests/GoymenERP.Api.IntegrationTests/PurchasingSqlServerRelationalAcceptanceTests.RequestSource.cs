using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Requests;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// A1-2 (plan API-02; Üretim A-2, Satış S-A1, ALS-D16) on real SQL Server: a request line's optional source (kind + opaque
/// source identity) is stored with the line in the request's ONE save, written into the audit record and rebuilt through the
/// domain (detail and list); a manual line stores none of the three columns; CK_PurchaseRequestLines_Source refuses a direct
/// write that breaks the kind/identity pairing or uses an undefined kind; the write guard never lets a stored source move;
/// company B sees nothing; and the migration's Down guard (51167) refuses while a sourced line exists. Purchasing never reads
/// a Sales, Manufacturing or Inventory table: the identities are SENTETIK opaque references. SENTETIK owners.
/// </summary>
public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    private static readonly Guid SourceSalesOrder = Guid.Parse("c3c3c3c3-0000-4000-8000-000000000001");
    private static readonly Guid SourceSalesOrderLine = Guid.Parse("c3c3c3c3-0000-4000-8000-000000000002");
    private static readonly Guid SourcePlan = Guid.Parse("c3c3c3c3-0000-4000-8000-000000000003");
    private static readonly Guid SourcePlanLine = Guid.Parse("c3c3c3c3-0000-4000-8000-000000000004");

    [SqlServerFact]
    public async Task A_sourced_request_stores_its_line_sources_in_one_save_audits_them_and_rebuilds_them_and_the_check_holds_the_pairing()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        PurchaseRequest built = PurchaseRequest.Create(Guid.NewGuid(), owners.Company, run.Actor, T0);
        Guid salesLine = Guid.NewGuid();
        Guid manualLine = Guid.NewGuid();
        Guid productionLine = Guid.NewGuid();
        built.AddLine(built.Version, salesLine, Product, KgUnit, Kg(5m), T0,
            PurchaseRequestLineSource.Of(PurchaseRequestSourceKind.Sales, SourceSalesOrder, SourceSalesOrderLine));
        built.AddLine(built.Version, manualLine, Product, KgUnit, Kg(6m), T0);
        built.AddLine(built.Version, productionLine, Product, KgUnit, Kg(7m), T0,
            PurchaseRequestLineSource.Of(PurchaseRequestSourceKind.Production, SourcePlan, SourcePlanLine));
        Guid requestId;
        await using (StoreScope a = run.Store(CompanyA))
        {
            var stored = await a.Store.StoreRequestAsync(run.Envelope("k-sourced"), Hash("sourced"), Day, () => built, VersionsOf(built));
            requestId = stored.Request.Request.Id;
            var replay = await a.Store.StoreRequestAsync(run.Envelope("k-sourced", correlation: "SENTETIK-second"), Hash("sourced"), Day,
                () => throw new InvalidOperationException("a replay never builds"), VersionsOf(built));
            Assert.Equal(PurchasingWriteOutcome.Replayed, replay.Outcome);
            Assert.Equal(PurchaseRequestSourceKind.Production, replay.Request.Request.Lines[2].Source!.Kind);
        }

        // The columns as stored: Sales = 2, Production = 3, manual = all three null.
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT SourceKind FROM purchasing.PurchaseRequestLines WHERE LineId = @line", ("@line", salesLine)));
        Assert.Equal(SourceSalesOrderLine, await run.ScalarAsync<Guid>(
            "SELECT SourceDocumentLineId FROM purchasing.PurchaseRequestLines WHERE LineId = @line", ("@line", salesLine)));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT SourceKind FROM purchasing.PurchaseRequestLines WHERE LineId = @line", ("@line", productionLine)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.PurchaseRequestLines WHERE LineId = @line AND SourceKind IS NULL AND SourceDocumentId IS NULL AND SourceDocumentLineId IS NULL",
            ("@line", manualLine)));
        // The audit record carries the source (ADR-009): one create event whose after-image names the sales line.
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'purchasing.purchase-request.created.v1' AND EntityId = @id "
            + "AND AfterJson LIKE @sales AND AfterJson LIKE N'%\"source\":null%'",
            ("@id", requestId.ToString("D")), ("@sales", $"%\"kind\":\"Sales\",\"documentId\":\"{SourceSalesOrder:D}\",\"documentLineId\":\"{SourceSalesOrderLine:D}\"%")));

        await using (StoreScope a = run.Store(CompanyA))
        {
            // Rebuilt through the domain, on the detail and on the list, and kept through a submission.
            var found = (await a.Store.FindRequestAsync(requestId))!.Request;
            Assert.Equal((PurchaseRequestSourceKind.Sales, SourceSalesOrder, SourceSalesOrderLine),
                (found.Lines[0].Source!.Kind, found.Lines[0].Source!.DocumentId, found.Lines[0].Source!.DocumentLineId));
            Assert.Null(found.Lines[1].Source);
            Assert.Equal(SourcePlanLine, found.Lines[2].Source!.DocumentLineId);
            var listed = Assert.Single((await a.Store.ListRequestsAsync(10, null)).Items).Request;
            Assert.Equal(PurchaseRequestSourceKind.Production, listed.Lines[2].Source!.Kind);
            var submitted = await a.Store.MutateRequestAsync(run.Envelope("k-sourced-submit"), Hash("sourced-submit"), PurchasingCommands.RequestSubmitV1,
                requestId, (r, _, now) => r.Submit(4, run.Actor, now), Submitted);
            Assert.Equal((PurchaseRequestStatus.Submitted, SourceSalesOrder), (submitted!.Request.Request.Status, submitted.Request.Request.Lines[0].Source!.DocumentId));
        }

        await using (StoreScope b = run.Store(CompanyB))
        {
            Assert.Null(await b.Store.FindRequestAsync(requestId));
            Assert.Empty((await b.Store.ListRequestsAsync(10, null)).Items);
        }

        // CK_PurchaseRequestLines_Source on a direct write (547): an undefined kind, a kind without its identities, identities
        // without a kind, and an empty identity are all refused; the stored line is unchanged.
        foreach (var write in new[]
        {
            "UPDATE purchasing.PurchaseRequestLines SET SourceKind = 4 WHERE LineId = @line",
            "UPDATE purchasing.PurchaseRequestLines SET SourceKind = 0 WHERE LineId = @line",
            "UPDATE purchasing.PurchaseRequestLines SET SourceDocumentLineId = NULL WHERE LineId = @line",
            "UPDATE purchasing.PurchaseRequestLines SET SourceKind = NULL WHERE LineId = @line",
            "UPDATE purchasing.PurchaseRequestLines SET SourceDocumentId = '00000000-0000-0000-0000-000000000000' WHERE LineId = @line",
        })
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(write, ("@line", salesLine)));
            Assert.Equal(547, refused.Number);
            Assert.Contains("CK_PurchaseRequestLines_Source", refused.Message, StringComparison.Ordinal);
        }

        var manualKind = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE purchasing.PurchaseRequestLines SET SourceKind = 1 WHERE LineId = @line", ("@line", manualLine)));
        Assert.Equal(547, manualKind.Number);
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT SourceKind FROM purchasing.PurchaseRequestLines WHERE LineId = @line", ("@line", salesLine)));
    }

    [SqlServerFact]
    public async Task The_line_source_migrations_Down_passes_with_only_manual_lines_and_refuses_while_a_sourced_line_exists()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        await SubmittedRequestAsync(run, owners, 5m);
        var migration = new AddPurchaseRequestLineSource();
        string guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;

        // Only manual lines: the guard lets Down proceed (nothing would be lost).
        await run.ExecuteAsync(guard);

        await using (StoreScope a = run.Store(CompanyA))
        {
            PurchaseRequest built = PurchaseRequest.Create(Guid.NewGuid(), owners.Company, run.Actor, T0);
            built.AddLine(built.Version, Guid.NewGuid(), Product, KgUnit, Kg(5m), T0,
                PurchaseRequestLineSource.Of(PurchaseRequestSourceKind.Stock, SourcePlan, SourcePlanLine));
            await a.Store.StoreRequestAsync(run.Envelope("k-stock"), Hash("stock"), Day, () => built, VersionsOf(built));
        }

        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51167, refused.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM purchasing.PurchaseRequestLines WHERE SourceKind = 1"));
    }
}
