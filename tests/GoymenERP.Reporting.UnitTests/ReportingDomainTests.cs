using System.IO.Compression;
using System.Text;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Reporting.Domain;

namespace GoymenERP.Reporting.UnitTests;

/// <summary>SENTETIK values only. The pure parts of the report centre: catalogue, parameters, calculators and file writers.</summary>
public sealed class ReportingDomainTests
{
    [Fact]
    public void The_manifest_names_are_registered_permissions_and_every_required_permission_is_known()
    {
        Assert.Equal(ErpPermissions.ReportingExport, ReportingPermissionManifestV1.Export);
        Assert.Equal(ErpPermissions.ReportingAuditorPackageExport, ReportingPermissionManifestV1.AuditorPackageExport);
        Assert.Equal(ErpPermissions.ReportingIntercompanyRead, ReportingPermissionManifestV1.IntercompanyRead);
        Assert.All(ReportingPermissionManifestV1.Names, name => Assert.True(ErpPermissions.IsKnown(name)));
        Assert.All(ReportingCatalogV1.Reports.SelectMany(report => report.RequiredPermissions),
            permission => Assert.True(ErpPermissions.IsKnown(permission)));
        Assert.Equal(new[] { "stock-aging", "otif", "recall", "intercompany", "party-aging" }, ReportingCatalogV1.Reports.Select(report => report.Id).ToArray());
        Assert.Equal(new[] { ErpPermissions.ReportingIntercompanyRead },
            ReportingCatalogV1.Find(ReportingCatalogV1.Intercompany)!.RequiredPermissions.ToArray());
    }

    [Fact]
    public void Decided_measures_take_no_parameter_and_the_undecided_stock_buckets_stay_required()
    {
        // YOL-D29: OTIF has no tolerance; only the period is asked.
        var otif = ReportingCatalogV1.Find(ReportingCatalogV1.Otif)!;
        Assert.Equal(new[] { "from", "to" }, otif.Parameters.Select(parameter => parameter.Id).ToArray());
        // YOL-D30: the receivable/payable buckets are fixed; only the measuring date and the direction are asked.
        var party = ReportingCatalogV1.Find(ReportingCatalogV1.PartyAging)!;
        Assert.Equal(new[] { "asOf", "direction" }, party.Parameters.Select(parameter => parameter.Id).ToArray());
        Assert.Equal(new[] { ErpPermissions.FinanceOpenItemsRead, ErpPermissions.FinanceOpenItemsAmountRead }, party.RequiredPermissions.ToArray());
        var aging = ReportingCatalogV1.Find(ReportingCatalogV1.StockAging)!;
        Assert.True(aging.Parameters.Single(parameter => parameter.Id == "bucketDays").Required);
        Assert.DoesNotContain(aging.Parameters, parameter => parameter.Id == "ageBasis");
        var missing = Assert.Throws<ReportingException>(() => ReportingParameterSetV1.Parse(aging,
            new Dictionary<string, string?> { ["asOf"] = "2026-11-22" }));
        Assert.Equal((422, ReportingProblemCodesV1.ParameterRequired, "bucketDays"), (missing.StatusCode, missing.Code, missing.Field));
    }

    [Theory]
    [InlineData("nope", "1", 400, "reporting_parameter_unknown")]
    [InlineData("bucketDays", "30,30", 422, "reporting_parameter_invalid")]
    [InlineData("bucketDays", "60,30", 422, "reporting_parameter_invalid")]
    [InlineData("bucketDays", "0", 422, "reporting_parameter_invalid")]
    [InlineData("bucketDays", "30, 60", 422, "reporting_parameter_invalid")]
    [InlineData("asOf", "2026-13-01", 422, "reporting_parameter_invalid")]
    [InlineData("productId", "00000000-0000-0000-0000-000000000000", 422, "reporting_parameter_invalid")]
    public void A_wrong_parameter_is_refused_with_its_status_and_field(string name, string value, int status, string code)
    {
        var raw = new Dictionary<string, string?> { ["asOf"] = "2026-11-22", ["bucketDays"] = "30,60" };
        raw[name] = value;
        var refused = Assert.Throws<ReportingException>(() => ReportingParameterSetV1.Parse(ReportingCatalogV1.Find("stock-aging")!, raw));
        Assert.Equal((status, code, name), (refused.StatusCode, refused.Code, refused.Field));
    }

    [Fact]
    public void A_period_is_ordered_and_bounded_and_canonical_values_are_recorded()
    {
        var report = ReportingCatalogV1.Find(ReportingCatalogV1.Intercompany)!;
        Assert.Equal("to", Assert.Throws<ReportingException>(() => ReportingParameterSetV1.Parse(report,
            new Dictionary<string, string?> { ["from"] = "2026-11-02", ["to"] = "2026-11-01" })).Field);
        Assert.Equal("to", Assert.Throws<ReportingException>(() => ReportingParameterSetV1.Parse(report,
            new Dictionary<string, string?> { ["from"] = "2025-01-01", ["to"] = "2026-01-02" })).Field);
        var aging = ReportingParameterSetV1.Parse(ReportingCatalogV1.Find(ReportingCatalogV1.StockAging)!, new Dictionary<string, string?>
        {
            ["bucketDays"] = "30,60", ["asOf"] = "2026-11-22",
        });
        Assert.Equal(new[] { "asOf", "bucketDays" }, aging.Canonical.Keys.ToArray());
        Assert.Equal(new[] { 30, 60 }, aging.WholeNumbers("bucketDays").ToArray());
    }

    [Theory]
    [InlineData(0, "0-30")]
    [InlineData(30, "0-30")]
    [InlineData(31, "31-60")]
    [InlineData(90, "61-90")]
    [InlineData(91, "91+")]
    [InlineData(-1, "after-as-of")]
    public void An_age_falls_into_the_bucket_whose_upper_boundary_it_does_not_exceed(int age, string bucket)
    {
        Assert.Equal(bucket, ReportingStockAgingCalculatorV1.Bucket(age, [30, 60, 90]));
        Assert.Equal(new[] { "0-30", "31-60", "61-90", "91+", "after-as-of" }, ReportingStockAgingCalculatorV1.Buckets([30, 60, 90]).ToArray());
        Assert.Equal(21, ReportingStockAgingCalculatorV1.AgeDays(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 22)));
    }

    [Fact]
    public void Otif_is_the_whole_quantity_shipped_on_or_before_the_promised_day_without_tolerance()
    {
        var promised = new DateOnly(2026, 11, 10);
        var onTheDay = ReportingOtifCalculatorV1.Evaluate(promised, 100, [new(promised, 100)]);
        Assert.Equal((100m, 100m, (DateOnly?)promised, true), (onTheDay.ShippedQuantity, onTheDay.ShippedByPromisedDate, onTheDay.InFullDate, onTheDay.Otif));
        // Partial by the promise and the rest a day late: not OTIF (YOL-D29), the in-full date is the late day.
        var late = ReportingOtifCalculatorV1.Evaluate(promised, 100, [new(new DateOnly(2026, 11, 11), 40), new(new DateOnly(2026, 11, 9), 60)]);
        Assert.Equal((100m, 60m, (DateOnly?)new DateOnly(2026, 11, 11), false), (late.ShippedQuantity, late.ShippedByPromisedDate, late.InFullDate, late.Otif));
        // 99.9 of 100 on time is still partial: there is no tolerance.
        Assert.False(ReportingOtifCalculatorV1.Evaluate(promised, 100, [new(promised, 99.9m)]).Otif);
        var none = ReportingOtifCalculatorV1.Evaluate(promised, 100, []);
        Assert.Equal((0m, (DateOnly?)null, false), (none.ShippedQuantity, none.InFullDate, none.Otif));
        Assert.Throws<ArgumentException>(() => ReportingOtifCalculatorV1.Evaluate(promised, 100, [new(promised, 0)]));
        Assert.Equal("66.67", ReportingOtifCalculatorV1.Percent(2, 3));
        Assert.Null(ReportingOtifCalculatorV1.Percent(0, 0));
    }

    [Theory]
    [InlineData("2026-11-23", "notDue")]
    [InlineData("2026-11-22", "overdue0To30")]
    [InlineData("2026-10-23", "overdue0To30")]
    [InlineData("2026-10-22", "overdue31To60")]
    [InlineData("2026-09-23", "overdue31To60")]
    [InlineData("2026-09-22", "overdue61To90")]
    [InlineData("2026-08-24", "overdue61To90")]
    [InlineData("2026-08-23", "overdue90Plus")]
    [InlineData(null, "dueUnknown")]
    public void Party_aging_counts_the_delay_from_the_due_date_in_the_decided_buckets(string? due, string bucket)
    {
        var dueOn = due is null ? (DateOnly?)null : DateOnly.Parse(due, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(bucket, ReportingPartyAgingCalculatorV1.Bucket(dueOn, new DateOnly(2026, 11, 22)));
    }

    [Fact]
    public void The_csv_quotes_guards_formulas_and_writes_turkish_headers_with_a_bom()
    {
        var columns = new[] { new ReportingSheetColumnV1("partyName", ReportingColumnKindV1.Text), new ReportingSheetColumnV1("quantityKg", ReportingColumnKindV1.Number),
            new ReportingSheetColumnV1("otif", ReportingColumnKindV1.Flag) };
        var bytes = ReportingFileWritersV1.Csv(columns, [["=HYPERLINK(\"x\")", "-12.5", "true"], ["Çelik, A.Ş.", null, "false"]]);
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes.Take(3).ToArray());
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.Equal("Cari unvanı,Miktar kg,Zamanında ve tam\r\n\"'=HYPERLINK(\"\"x\"\")\",-12.5,Evet\r\n\"Çelik, A.Ş.\",,Hayır\r\n", text);
        Assert.Throws<ArgumentException>(() => ReportingFileWritersV1.Csv(columns, [["only-one"]]));
    }

    [Fact]
    public void The_xlsx_is_a_deterministic_workbook_with_numbers_as_numbers_and_guarded_text()
    {
        var columns = new[] { new ReportingSheetColumnV1("labelCode", ReportingColumnKindV1.Text), new ReportingSheetColumnV1("physicalKg", ReportingColumnKindV1.Number) };
        IReadOnlyList<IReadOnlyList<string?>> rows = [["@SUM(A1)", "1250.5"], ["<E&T>", "n/a"]];
        var first = ReportingFileWritersV1.Xlsx("stock-aging", columns, rows);
        Assert.Equal(first, ReportingFileWritersV1.Xlsx("stock-aging", columns, rows));
        using var archive = new ZipArchive(new MemoryStream(first));
        Assert.Equal(new[] { "[Content_Types].xml", "_rels/.rels", "xl/workbook.xml", "xl/_rels/workbook.xml.rels", "xl/worksheets/sheet1.xml" },
            archive.Entries.Select(entry => entry.FullName).ToArray());
        using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var sheet = reader.ReadToEnd();
        Assert.Contains("<c r=\"B2\"><v>1250.5</v></c>", sheet);
        Assert.Contains("<t xml:space=\"preserve\">&apos;@SUM(A1)</t>", sheet);
        Assert.Contains("<t xml:space=\"preserve\">&lt;E&amp;T&gt;</t>", sheet);
        Assert.Contains("<c r=\"B3\" t=\"inlineStr\"><is><t xml:space=\"preserve\">n/a</t></is></c>", sheet);
        Assert.Contains("<t xml:space=\"preserve\">Fiziksel kg</t>", sheet);
        Assert.Equal("AA", ReportingFileWritersV1.ColumnName(26));
    }
}
