using System.IO.Compression;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Reporting.Domain;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Reporting.UnitTests;

/// <summary>SENTETIK data only. The report centre service over the in-memory store with fake owner sources.</summary>
public sealed class ReportingServiceTests
{
    private static readonly Guid Reader = Guid.Parse("a0000000-0000-4000-8000-000000000001");
    private static readonly Guid Other = Guid.Parse("a0000000-0000-4000-8000-000000000002");
    private static readonly Guid Label = Guid.Parse("1a000000-0000-4000-8000-000000000001");
    private static readonly Guid Product = Guid.Parse("b0000000-0000-4000-8000-000000000001");
    private static readonly Guid Location = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly string[] InventoryRead = [ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead];

    private static Dictionary<string, string?> AgingParameters() => new()
    {
        ["asOf"] = "2026-11-22", ["bucketDays"] = "30,60",
    };

    private static ReportingHarness WithStock()
    {
        var harness = new ReportingHarness();
        // Entered 2026-11-01 Istanbul time (21 days); the second inherited 2026-09-01 (82 days) from its split source; the third
        // has an entry the owner could not name.
        harness.StockAging.Rows.Add(new(Label, "ETK-1", Product, Location, "D-1", null, "1250.5", new DateTimeOffset(2026, 10, 31, 22, 30, 0, TimeSpan.Zero),
            false));
        harness.StockAging.Rows.Add(new(Guid.Parse("1a000000-0000-4000-8000-000000000002"), "=ETK-2", Product, Location, null, null, "300",
            new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero), true));
        harness.StockAging.Rows.Add(new(Guid.Parse("1a000000-0000-4000-8000-000000000003"), "ETK-3", Product, Location, null, null, "50", null, false));
        return harness;
    }

    private static ReportingCommandMetadataV1 Meta(string key, string correlation = "corr-1") => new(key, correlation, null);

    [Fact]
    public async Task The_catalogue_lists_only_reports_whose_every_owner_permission_is_held_now()
    {
        var harness = new ReportingHarness();
        var none = await harness.Service(Reader).ListReportsAsync();
        Assert.Empty(none.Reports);
        Assert.False(none.CanBuildAuditorPackage);

        var inventory = await harness.Service(Reader, [.. InventoryRead, ErpPermissions.ReportingExport]).ListReportsAsync();
        Assert.Equal(ReportingHarness.Company, inventory.CompanyId);
        Assert.Equal(new[] { "stock-aging", "recall" }, inventory.Reports.Select(report => report.Id).ToArray());
        Assert.All(inventory.Reports, report => Assert.True(report.CanExport));

        // ROL-D15: the intercompany dump appears only for its own separate permission.
        var intercompany = await harness.Service(Reader, ErpPermissions.ReportingIntercompanyRead).ListReportsAsync();
        Assert.Equal(new[] { "intercompany" }, intercompany.Reports.Select(report => report.Id).ToArray());
        Assert.False(intercompany.Reports[0].CanExport);
        Assert.Equal("ascending-whole-numbers", inventory.Reports[0].Parameters.Single(parameter => parameter.Id == "bucketDays").Kind);
    }

    [Fact]
    public async Task Stock_aging_asks_the_owner_for_the_context_company_and_computes_ages_and_buckets_in_the_backend()
    {
        var harness = WithStock();
        var table = await harness.Service(Reader, InventoryRead).RunAsync("stock-aging", AgingParameters(), null, null);
        Assert.Equal(new[] { ReportingHarness.Company }, harness.StockAging.AskedCompanies.ToArray());
        Assert.Equal(ReportingHarness.Company, table.CompanyId);
        Assert.Equal(new string?[] { "ETK-1", Label.ToString("D"), Product.ToString("D"), Location.ToString("D"), "D-1", null, "1250.5",
            "2026-11-01", "false", "21", "0-30" }, table.Rows[0].ToArray());
        Assert.Equal(new string?[] { "2026-09-01", "true", "82", "61+" }, table.Rows[1].Skip(7).ToArray());
        // An entry the owner could not name stays unknown, never guessed.
        Assert.Equal(new string?[] { null, "false", null, null }, table.Rows[2].Skip(7).ToArray());
        Assert.Contains(table.Summary, item => item is { Id: "bucketKg:0-30", Value: "1250.5" });
        Assert.Contains(table.Summary, item => item is { Id: "bucketKg:61+", Value: "300" });
        Assert.Contains(table.Summary, item => item is { Id: "unknownAgeKg", Value: "50" });
        Assert.Contains(table.Summary, item => item is { Id: "pageTotalKg", Value: "1600.5" });
    }

    [Fact]
    public async Task Without_an_owner_permission_the_report_is_forbidden_and_no_owner_is_asked()
    {
        var harness = WithStock();
        var refused = await Assert.ThrowsAsync<ReportingException>(() =>
            harness.Service(Reader, ErpPermissions.InventoryLabelsRead).RunAsync("stock-aging", AgingParameters(), null, null));
        Assert.Equal(403, refused.StatusCode);
        Assert.Empty(harness.StockAging.AskedCompanies);
        Assert.Equal(404, (await Assert.ThrowsAsync<ReportingException>(() =>
            harness.Service(Reader, InventoryRead).RunAsync("unknown", new Dictionary<string, string?>(), null, null))).StatusCode);
    }

    [Fact]
    public async Task A_permission_revoked_while_the_owner_read_ran_releases_nothing()
    {
        var harness = WithStock();
        var authority = new FakeAuthority(InventoryRead) { RevokeAfter = 2 };
        var service = harness.Service(Reader, ReportingHarness.Company, authority);
        var refused = await Assert.ThrowsAsync<ReportingException>(() => service.RunAsync("stock-aging", AgingParameters(), null, null));
        Assert.Equal(403, refused.StatusCode);
        Assert.Single(harness.StockAging.AskedCompanies);
    }

    [Fact]
    public async Task An_owner_answer_for_another_company_or_a_failed_owner_is_fail_closed()
    {
        var harness = WithStock();
        harness.StockAging.AnswerAs = ReportingHarness.OtherCompany;
        Assert.Equal((503, ReportingProblemCodesV1.SourceUnavailable), await Code(() =>
            harness.Service(Reader, InventoryRead).RunAsync("stock-aging", AgingParameters(), null, null)));
        harness.StockAging.AnswerAs = null;
        harness.StockAging.Outcome = ReportingSourceOutcomeV1.Unavailable;
        Assert.Equal((503, ReportingProblemCodesV1.SourceUnavailable), await Code(() =>
            harness.Service(Reader, InventoryRead).RunAsync("stock-aging", AgingParameters(), null, null)));
        harness.StockAging.Outcome = ReportingSourceOutcomeV1.Denied;
        Assert.Equal((403, "forbidden"), await Code(() =>
            harness.Service(Reader, InventoryRead).RunAsync("stock-aging", AgingParameters(), null, null)));
    }

    [Fact]
    public async Task Otif_and_intercompany_are_not_connected_and_never_answer_an_empty_report()
    {
        var harness = new ReportingHarness();
        Assert.Equal((503, ReportingProblemCodesV1.SourceNotConnected), await Code(() => harness.Service(Reader, ErpPermissions.SalesReportsRead)
            .RunAsync("otif", new Dictionary<string, string?> { ["from"] = "2026-11-01", ["to"] = "2026-11-30" }, null, null)));
        Assert.Equal((503, ReportingProblemCodesV1.SourceNotConnected), await Code(() => harness.Service(Reader, ErpPermissions.ReportingIntercompanyRead)
            .RunAsync("intercompany", new Dictionary<string, string?> { ["from"] = "2026-11-01", ["to"] = "2026-11-30" }, null, null)));
    }

    [Fact]
    public async Task Recall_pages_the_owner_trace_and_counts_customers_and_labels()
    {
        var harness = new ReportingHarness();
        var at = new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
        harness.Recall.Rows.Add(new(Label, "ETK-1", "SupplierReceipt", at, "1000", "Supplier", "S-1", "Tedarikçi", "purchasing.goods-receipt", Guid.NewGuid()));
        harness.Recall.Rows.Add(new(Label, "ETK-1", "Shipment", at.AddDays(2), "400", "Customer", "C-1", "Müşteri Bir", "logistics.definite-shipment", Guid.NewGuid()));
        harness.Recall.Rows.Add(new(Label, "ETK-1", "Shipment", at.AddDays(3), "600", "Customer", "C-2", "Müşteri İki", "logistics.definite-shipment", Guid.NewGuid()));
        var parameters = new Dictionary<string, string?> { ["labelId"] = Label.ToString("D"), ["direction"] = "forward" };
        var page = await harness.Service(Reader, InventoryRead).RunAsync("recall", parameters, 1, 2);
        Assert.Equal(2, page.Rows.Count);
        Assert.True(page.HasMore);
        Assert.Equal("2026-11-03T10:00:00Z", page.Rows[0][3]);
        Assert.Contains(page.Summary, item => item is { Id: "customers", Value: "2" });
        Assert.Contains(page.SourceStatuses, item => item is { Id: "windowFull", Value: "false" });
        var last = await harness.Service(Reader, InventoryRead).RunAsync("recall", parameters, 2, 2);
        Assert.False(last.HasMore);
        Assert.Equal("C-2", Assert.Single(last.Rows)[6]);
        harness.Recall.Outcome = ReportingSourceOutcomeV1.NotFound;
        Assert.Equal((404, "not_found"), await Code(() => harness.Service(Reader, InventoryRead).RunAsync("recall", parameters, null, null)));
    }

    [Fact]
    public async Task An_export_needs_the_export_permission_and_writes_one_record_and_one_audit_event_with_the_content_hash()
    {
        var harness = WithStock();
        var request = new ReportingExportRequestV1("csv", AgingParameters(), 1, 100, ["labelCode", "physicalKg", "bucket"]);
        Assert.Equal(403, (await Assert.ThrowsAsync<ReportingException>(() => harness.Service(Reader, InventoryRead).ExportAsync("stock-aging", request))).StatusCode);
        await using (var empty = harness.Database())
            Assert.Empty(await empty.Exports.ToListAsync());
        Assert.Empty(harness.Audit.Events);

        var file = await harness.Service(Reader, [.. InventoryRead, ErpPermissions.ReportingExport]).ExportAsync("stock-aging", request);
        var text = Encoding.UTF8.GetString(file.Content, 3, file.Content.Length - 3);
        Assert.Equal("Etiket,Fiziksel kg,Yaş aralığı\r\nETK-1,1250.5,0-30\r\n'=ETK-2,300,61+\r\nETK-3,50,\r\n", text);
        Assert.Equal(3, file.RowCount);
        await using var database = harness.Database();
        var record = Assert.Single(await database.Exports.ToListAsync());
        Assert.Equal((ReportingHarness.Company, Reader, "stock-aging", "csv", 1, 100, 3, file.ContentSha256, file.ExportId, "corr-test"),
            (record.CompanyId, record.ActorUserId, record.ReportId, record.Format, record.Page, record.PageSize, record.RowCount, record.ContentSha256,
                record.Id, record.CorrelationId));
        Assert.Equal("""{"asOf":"2026-11-22","bucketDays":"30,60"}""", record.ParametersJson);
        var audit = Assert.Single(harness.Audit.Events);
        Assert.Equal(("reporting.report.exported.v1", ReportingHarness.Company, ErpPermissions.ReportingExport, file.ExportId.ToString("D")),
            (audit.EventType, audit.CompanyId, audit.PermissionCode, audit.EntityId));
        Assert.Contains(file.ContentSha256, audit.AfterJson);
    }

    [Fact]
    public async Task An_export_refuses_an_unknown_column_format_or_oversized_page_before_reading()
    {
        var harness = WithStock();
        var service = harness.Service(Reader, [.. InventoryRead, ErpPermissions.ReportingExport]);
        Assert.Equal((400, ReportingProblemCodesV1.FormatInvalid), await Code(() => service.ExportAsync("stock-aging",
            new("pdf", AgingParameters(), null, null, null))));
        Assert.Equal((400, ReportingProblemCodesV1.PageInvalid), await Code(() => service.ExportAsync("stock-aging",
            new("xlsx", AgingParameters(), 1, ReportingCatalogV1.MaximumExportPageSize + 1, null))));
        Assert.Equal((400, ReportingProblemCodesV1.ParameterInvalid), await Code(() => service.ExportAsync("stock-aging",
            new("xlsx", AgingParameters(), null, null, ["labelCode", "secret"]))));
        Assert.Empty(harness.StockAging.AskedCompanies);
    }

    [Fact]
    public async Task The_auditor_package_holds_only_sections_the_actor_may_read_and_is_recorded()
    {
        var harness = WithStock();
        var service = harness.Service(Reader, [ErpPermissions.ReportingAuditorPackageExport, ErpPermissions.AccountingJournalsRead]);
        var file = await service.BuildAuditorPackageAsync(new("2026-01-01", "2026-11-22"));
        using var archive = new ZipArchive(new MemoryStream(file.Content));
        Assert.Equal(new[] { "manifest.json", "sections/trial-balance.csv" }, archive.Entries.Select(entry => entry.FullName).ToArray());
        using var manifest = JsonDocument.Parse(new StreamReader(archive.GetEntry("manifest.json")!.Open()).ReadToEnd());
        var sections = manifest.RootElement.GetProperty("sections").EnumerateArray()
            .ToDictionary(section => section.GetProperty("id").GetString()!, section => section.GetProperty("status").GetString());
        Assert.Equal("included", sections["trial-balance"]);
        Assert.Equal("forbidden", sections["stock-positions"]);
        Assert.Equal("forbidden", sections["intercompany"]);
        Assert.Equal("forbidden", sections["receivables-aging"]);
        Assert.False(sections.ContainsKey("counterparty-balances"));
        Assert.Equal("not-connected", sections["document-trail"]);
        Assert.Equal(ReportingHarness.Company, manifest.RootElement.GetProperty("companyId").GetGuid());
        Assert.Empty(harness.StockAging.AskedCompanies);
        await using var database = harness.Database();
        var record = Assert.Single(await database.Exports.ToListAsync());
        Assert.Equal((2, "auditor-package", "zip", (int?)null), (record.Kind, record.ReportId, record.Format, record.Page));
        Assert.Equal("reporting.auditor-package.exported.v1", Assert.Single(harness.Audit.Events).EventType);
        Assert.Equal(403, (await Assert.ThrowsAsync<ReportingException>(() =>
            harness.Service(Reader, ErpPermissions.AccountingJournalsRead).BuildAuditorPackageAsync(new("2026-01-01", "2026-11-22")))).StatusCode);
    }

    [Fact]
    public async Task A_saved_view_is_personal_replays_its_key_and_refuses_a_reused_key_with_another_body()
    {
        var harness = new ReportingHarness();
        var request = new ReportingSavedViewRequestV1("stock-aging", " 90 gün ", new Dictionary<string, string?> { ["bucketDays"] = "30,60,90" },
            ["labelCode", "bucket"], null);
        var created = await harness.Service(Reader, InventoryRead).CreateSavedViewAsync(request, Meta("k1"));
        Assert.False(created.IsReplay);
        Assert.Equal(("90 gün", "1", true), (created.View.Name, created.View.Version, created.View.IsActive));
        Assert.Equal("30,60,90", created.View.Parameters["bucketDays"]);
        var replay = await harness.Service(Reader, InventoryRead).CreateSavedViewAsync(request, Meta("k1", "corr-2"));
        Assert.Equal((true, "corr-1", created.View.Id), (replay.IsReplay, replay.CorrelationId, replay.View.Id));
        Assert.Equal((409, ReportingProblemCodesV1.SameKeyPayloadMismatch), await Code(() =>
            harness.Service(Reader, InventoryRead).CreateSavedViewAsync(request with { Name = "Başka" }, Meta("k1"))));
        Assert.Equal((409, ReportingProblemCodesV1.SavedViewNameTaken), await Code(() =>
            harness.Service(Reader, InventoryRead).CreateSavedViewAsync(request, Meta("k2"))));

        Assert.Empty((await harness.Service(Other, InventoryRead).ListSavedViewsAsync("stock-aging")).Items);
        Assert.Equal((404, "not_found"), await Code(() => harness.Service(Other, InventoryRead)
            .PassivateSavedViewAsync(created.View.Id, new("1"), Meta("o1"))));
        Assert.Single((await harness.Service(Reader, InventoryRead).ListSavedViewsAsync("stock-aging")).Items);
        Assert.Equal("reporting.saved-view.created.v1", Assert.Single(harness.Audit.Events).EventType);
    }

    [Fact]
    public async Task A_saved_view_moves_by_expected_version_and_a_stale_version_is_a_durable_refusal()
    {
        var harness = new ReportingHarness();
        var created = await harness.Service(Reader, InventoryRead).CreateSavedViewAsync(
            new("recall", "Geri çağırma", new Dictionary<string, string?> { ["direction"] = "forward" }, ["labelCode", "partyName"], null), Meta("c1"));
        var updated = await harness.Service(Reader, InventoryRead).UpdateSavedViewAsync(created.View.Id,
            new("recall", "Geri çağırma (ileri)", new Dictionary<string, string?> { ["direction"] = "forward" }, ["partyName"], "1"), Meta("u1"));
        Assert.Equal(("2", "Geri çağırma (ileri)"), (updated.View.Version, updated.View.Name));
        var stale = new ReportingSavedViewRequestV1("recall", "Eski", null, ["labelCode"], "1");
        Assert.Equal((409, ReportingProblemCodesV1.VersionMismatch), await Code(() =>
            harness.Service(Reader, InventoryRead).UpdateSavedViewAsync(created.View.Id, stale, Meta("u2"))));
        var again = await Assert.ThrowsAsync<ReportingException>(() =>
            harness.Service(Reader, InventoryRead).UpdateSavedViewAsync(created.View.Id, stale, Meta("u2", "corr-9")));
        Assert.Equal((409, ReportingProblemCodesV1.VersionMismatch, true), (again.StatusCode, again.Code, again.IsReplay));
        Assert.Equal((422, ReportingProblemCodesV1.SavedViewInvalid), await Code(() => harness.Service(Reader, InventoryRead)
            .UpdateSavedViewAsync(created.View.Id, new("recall", "X", new Dictionary<string, string?> { ["direction"] = "sideways" }, ["labelCode"], "2"),
                Meta("u3"))));
        var passive = await harness.Service(Reader, InventoryRead).PassivateSavedViewAsync(created.View.Id, new("2"), Meta("p1"));
        Assert.Equal(("3", false), (passive.View.Version, passive.View.IsActive));
        Assert.Equal((409, ReportingProblemCodesV1.SavedViewPassive), await Code(() =>
            harness.Service(Reader, InventoryRead).PassivateSavedViewAsync(created.View.Id, new("3"), Meta("p2"))));
        Assert.Empty((await harness.Service(Reader, InventoryRead).ListSavedViewsAsync("recall")).Items);
        await using var database = harness.Database();
        Assert.Single(await database.SavedViews.ToListAsync());
    }

    [Fact]
    public async Task Another_companys_saved_view_and_export_records_are_invisible()
    {
        var harness = new ReportingHarness();
        var created = await harness.Service(Reader, InventoryRead).CreateSavedViewAsync(
            new("recall", "Benim", null, ["labelCode"], null), Meta("x1"));
        var foreign = harness.Service(Reader, ReportingHarness.OtherCompany, new FakeAuthority(InventoryRead));
        Assert.Empty((await foreign.ListSavedViewsAsync("recall")).Items);
        Assert.Equal((404, "not_found"), await Code(() => foreign.PassivateSavedViewAsync(created.View.Id, new("1"), Meta("x2"))));
        await using var other = harness.Database(new FixedCompany(ReportingHarness.OtherCompany));
        Assert.Empty(await other.SavedViews.ToListAsync());
    }

    [Fact]
    public async Task Party_aging_groups_open_items_per_party_account_and_currency_in_the_decided_buckets_without_mixing_currencies()
    {
        var harness = new ReportingHarness();
        var party = Guid.Parse("e0000000-0000-4000-8000-000000000001");
        var account = Guid.Parse("e1000000-0000-4000-8000-000000000001");
        var tryCurrency = Guid.Parse("e2000000-0000-4000-8000-000000000001");
        var usd = Guid.Parse("e2000000-0000-4000-8000-000000000002");
        harness.PartyAging.Receivables.Add(new(party, account, tryCurrency, new DateOnly(2026, 12, 1), "100.50"));
        harness.PartyAging.Receivables.Add(new(party, account, tryCurrency, new DateOnly(2026, 11, 22), "10"));
        harness.PartyAging.Receivables.Add(new(party, account, tryCurrency, new DateOnly(2026, 8, 1), "5"));
        harness.PartyAging.Receivables.Add(new(party, account, tryCurrency, null, "1"));
        harness.PartyAging.Receivables.Add(new(party, account, usd, new DateOnly(2026, 10, 1), "7"));
        var permissions = new[] { ErpPermissions.FinanceOpenItemsRead, ErpPermissions.FinanceOpenItemsAmountRead };
        var table = await harness.Service(Reader, permissions).RunAsync("party-aging",
            new Dictionary<string, string?> { ["asOf"] = "2026-11-22", ["direction"] = "receivable" }, null, null);
        Assert.Equal(new[] { (ReportingHarness.Company, true) }, harness.PartyAging.Asked.ToArray());
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(new string?[] { party.ToString("D"), account.ToString("D"), tryCurrency.ToString("D"), "100.5", "10", "0", "0", "5", "1", "116.5", "4" },
            table.Rows[0].ToArray());
        Assert.Equal(new string?[] { "0", "0", "7", "0", "0", "0", "7", "1" }, table.Rows[1].Skip(3).ToArray());

        Assert.Equal(403, (await Assert.ThrowsAsync<ReportingException>(() => harness.Service(Reader, ErpPermissions.FinanceOpenItemsRead)
            .RunAsync("party-aging", new Dictionary<string, string?> { ["asOf"] = "2026-11-22", ["direction"] = "payable" }, null, null))).StatusCode);
        harness.PartyAging.WindowFull = true;
        Assert.Equal((503, ReportingProblemCodesV1.SourceWindowFull), await Code(() => harness.Service(Reader, permissions)
            .RunAsync("party-aging", new Dictionary<string, string?> { ["asOf"] = "2026-11-22", ["direction"] = "receivable" }, null, null)));
    }

    [Fact]
    public async Task The_auditor_package_carries_receivable_and_payable_aging_as_of_the_period_end_for_a_finance_reader()
    {
        var harness = new ReportingHarness();
        harness.PartyAging.Payables.Add(new(Guid.Parse("e0000000-0000-4000-8000-000000000009"), Guid.Parse("e1000000-0000-4000-8000-000000000009"),
            Guid.Parse("e2000000-0000-4000-8000-000000000001"), new DateOnly(2026, 6, 30), "40"));
        var file = await harness.Service(Reader, ErpPermissions.ReportingAuditorPackageExport, ErpPermissions.FinanceOpenItemsRead,
            ErpPermissions.FinanceOpenItemsAmountRead).BuildAuditorPackageAsync(new("2026-01-01", "2026-06-30"));
        using var archive = new ZipArchive(new MemoryStream(file.Content));
        Assert.Contains("sections/payables-aging.csv", archive.Entries.Select(entry => entry.FullName));
        Assert.Contains("sections/receivables-aging.csv", archive.Entries.Select(entry => entry.FullName));
        var payables = new StreamReader(archive.GetEntry("sections/payables-aging.csv")!.Open()).ReadToEnd();
        // Due on the period end itself: 0 days overdue.
        Assert.Contains(",0,40,0,0,0,0,40,1", payables);
    }

    private static async Task<(int, string)> Code(Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<ReportingException>(action);
        return (exception.StatusCode, exception.Code);
    }
}
