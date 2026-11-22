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
        Assert.Equal(new[] { "stock-aging", "otif", "recall", "intercompany" }, ReportingCatalogV1.Reports.Select(report => report.Id).ToArray());
        Assert.Equal(new[] { ErpPermissions.ReportingIntercompanyRead },
            ReportingCatalogV1.Find(ReportingCatalogV1.Intercompany)!.RequiredPermissions.ToArray());
    }

    [Fact]
    public void A_measure_the_decisions_do_not_define_is_a_required_parameter_without_a_default()
    {
        var otif = ReportingCatalogV1.Find(ReportingCatalogV1.Otif)!;
        Assert.All(new[] { "onTimeToleranceDays", "inFullTolerancePercent" },
            id => Assert.True(otif.Parameters.Single(parameter => parameter.Id == id).Required));
        var aging = ReportingCatalogV1.Find(ReportingCatalogV1.StockAging)!;
        Assert.All(new[] { "bucketDays", "ageBasis", "asOf" }, id => Assert.True(aging.Parameters.Single(parameter => parameter.Id == id).Required));
        var missing = Assert.Throws<ReportingException>(() => ReportingParameterSetV1.Parse(otif,
            new Dictionary<string, string?> { ["from"] = "2026-11-01", ["to"] = "2026-11-30", ["onTimeToleranceDays"] = "2" }));
        Assert.Equal((422, ReportingProblemCodesV1.ParameterRequired, "inFullTolerancePercent"), (missing.StatusCode, missing.Code, missing.Field));
    }

    [Theory]
    [InlineData("nope", "1", 400, "reporting_parameter_unknown")]
    [InlineData("bucketDays", "30,30", 422, "reporting_parameter_invalid")]
    [InlineData("bucketDays", "60,30", 422, "reporting_parameter_invalid")]
    [InlineData("bucketDays", "0", 422, "reporting_parameter_invalid")]
    [InlineData("bucketDays", "30, 60", 422, "reporting_parameter_invalid")]
    [InlineData("asOf", "2026-13-01", 422, "reporting_parameter_invalid")]
    [InlineData("ageBasis", "guess", 422, "reporting_parameter_invalid")]
    [InlineData("productId", "00000000-0000-0000-0000-000000000000", 422, "reporting_parameter_invalid")]
    public void A_wrong_parameter_is_refused_with_its_status_and_field(string name, string value, int status, string code)
    {
        var raw = new Dictionary<string, string?> { ["asOf"] = "2026-11-22", ["ageBasis"] = "label-created", ["bucketDays"] = "30,60" };
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
        var otif = ReportingParameterSetV1.Parse(ReportingCatalogV1.Find(ReportingCatalogV1.Otif)!, new Dictionary<string, string?>
        {
            ["to"] = "2026-11-30", ["from"] = "2026-11-01", ["onTimeToleranceDays"] = "2", ["inFullTolerancePercent"] = "2.50",
        });
        Assert.Equal(new[] { "from", "inFullTolerancePercent", "onTimeToleranceDays", "to" }, otif.Canonical.Keys.ToArray());
        Assert.Equal("2.5", otif.Canonical["inFullTolerancePercent"]);
        Assert.Equal(2.5m, otif.Decimal("inFullTolerancePercent"));
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
    public void Otif_separates_a_first_shipment_on_time_the_total_in_full_and_the_quantity_in_full_by_the_deadline()
    {
        var requested = new DateOnly(2026, 11, 10);
        // 60 on the 9th, 40 on the 20th: on time (first shipment), in full (total), not OTIF (only 60 by the deadline).
        var late = ReportingOtifCalculatorV1.Evaluate(requested, 100, [new(new DateOnly(2026, 11, 20), 40), new(new DateOnly(2026, 11, 9), 60)], 0, 0);
        Assert.Equal((100m, (DateOnly?)new DateOnly(2026, 11, 20), true, true, false), (late.ShippedQuantity, late.InFullDate, late.OnTime, late.InFull, late.Otif));
        // The tolerances widen both sides: 2 days late and 2% short still count.
        var tolerated = ReportingOtifCalculatorV1.Evaluate(requested, 100, [new(new DateOnly(2026, 11, 12), 98)], 2, 2);
        Assert.Equal((true, true, true), (tolerated.OnTime, tolerated.InFull, tolerated.Otif));
        var strict = ReportingOtifCalculatorV1.Evaluate(requested, 100, [new(new DateOnly(2026, 11, 12), 98)], 1, 1);
        Assert.Equal((false, false, false), (strict.OnTime, strict.InFull, strict.Otif));
        var none = ReportingOtifCalculatorV1.Evaluate(requested, 100, [], 5, 5);
        Assert.Equal((0m, (DateOnly?)null, false, false, false), (none.ShippedQuantity, none.InFullDate, none.OnTime, none.InFull, none.Otif));
        Assert.Throws<ArgumentException>(() => ReportingOtifCalculatorV1.Evaluate(requested, 100, [new(requested, 0)], 0, 0));
        Assert.Equal("66.67", ReportingOtifCalculatorV1.Percent(2, 3));
        Assert.Null(ReportingOtifCalculatorV1.Percent(0, 0));
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
