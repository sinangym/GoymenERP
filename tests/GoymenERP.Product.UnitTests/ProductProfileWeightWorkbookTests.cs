using System.IO.Compression;
using System.Security;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Product.UnitTests;

/// <summary>
/// URN-D18/D21 (URN-SC34) without a database: an <c>.xlsx</c> kg/m table is read from its first worksheet under the same row
/// rules as the CSV (number cells as the exact stored value, shared and inline text), a formula cell, a macro-enabled or
/// template workbook, a VBA project, an external link and a non-workbook ZIP are refused, the kind named by the extension is
/// proven by the content in both directions, XLSM / XLS names are refused, and the size bound is the Settings record (null →
/// the decided 5 MB starting value; an unreachable or non-positive answer refuses 503 before anything is read). SENTETIK / NOT_ENV.
/// </summary>
public sealed class ProductProfileWeightWorkbookTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly string[] HeaderCells =
        ["sectionKind", "widthMm", "heightMm", "outerDiameterMm", "wallThicknessMm", "kgPerMetre"];

    [Fact]
    public void A_workbook_is_read_from_its_first_sheet_with_exact_numbers_shared_and_inline_text_and_sheet_row_numbers()
    {
        var workbook = SyntheticWorkbook.Build(
        [
            HeaderCells,
            [],
            ["Profile", "#40", "#20", null, "#2", "#1.72"],
            ["RoundTube", null, null, "#48.3", "#2.6", "#2.9300000000000002"],
            ["Profile", "40", "inline:40", null, "1.5", "#1.789012"],
        ]);

        var refusal = ProductProfileWeightFile.Refusal(ProductProfileWeightFileKindV1.Xlsx, workbook, out var rows);

        Assert.Null(refusal);
        Assert.Equal(3, rows.Count);
        Assert.Equal(new ProductProfileWeightRowV1("Profile", 40m, 20m, null, 2m, 1.72m), rows[0]);
        Assert.Equal(new ProductProfileWeightRowV1("RoundTube", null, null, 48.3m, 2.6m, 2.93m), rows[1]);
        Assert.Equal(new ProductProfileWeightRowV1("Profile", 40m, 40m, null, 1.5m, 1.789012m), rows[2]);
    }

    [Fact]
    public void A_workbook_row_refusal_names_the_sheet_row_and_a_value_is_never_rounded_to_fit()
    {
        var tooPrecise = SyntheticWorkbook.Build([HeaderCells, ["Profile", "#40", "#20", null, "#2", "#1.1234567"]]);
        Assert.Contains("Satır 2", ProductProfileWeightFile.Refusal(ProductProfileWeightFileKindV1.Xlsx, tooPrecise, out var none),
            StringComparison.Ordinal);
        Assert.Empty(none);

        var extraColumn = SyntheticWorkbook.Build([HeaderCells, ["Profile", "#40", "#20", null, "#2", "#1.72", "fazla"]]);
        Assert.Contains("6 alan", ProductProfileWeightFile.Refusal(ProductProfileWeightFileKindV1.Xlsx, extraColumn, out _),
            StringComparison.Ordinal);

        var badHeader = SyntheticWorkbook.Build([["sectionKind", "widthMm"], ["Profile", "#40", "#20", null, "#2", "#1.72"]]);
        Assert.Contains("başlık", ProductProfileWeightFile.Refusal(ProductProfileWeightFileKindV1.Xlsx, badHeader, out _),
            StringComparison.Ordinal);

        var negative = SyntheticWorkbook.Build([HeaderCells, ["Profile", "#-40", "#20", null, "#2", "#1.72"]]);
        Assert.NotNull(ProductProfileWeightFile.Refusal(ProductProfileWeightFileKindV1.Xlsx, negative, out _));
    }

    [Fact]
    public void A_formula_cell_a_macro_or_template_workbook_a_vba_project_an_external_link_and_a_plain_zip_are_refused()
    {
        var formula = SyntheticWorkbook.Build([HeaderCells, ["Profile", "#40", "#20", null, "#2", "=B2*0.043"]]);
        Assert.Contains("formül", ProductProfileWeightFile.Refusal(ProductProfileWeightFileKindV1.Xlsx, formula, out var rows),
            StringComparison.Ordinal);
        Assert.Empty(rows);

        var valid = new string?[][] { HeaderCells, ["Profile", "#40", "#20", null, "#2", "#1.72"] };
        var macroEnabled = SyntheticWorkbook.Build(valid,
            mainContentType: "application/vnd.ms-excel.sheet.macroEnabled.main+xml");
        Assert.Contains("XLSM", ProductProfileWeightFile.Refusal(ProductProfileWeightFileKindV1.Xlsx, macroEnabled, out _),
            StringComparison.Ordinal);

        var template = SyntheticWorkbook.Build(valid,
            mainContentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.template.main+xml");
        Assert.NotNull(ProductProfileWeightFile.Refusal(ProductProfileWeightFileKindV1.Xlsx, template, out _));

        var vba = SyntheticWorkbook.Build(valid, extraEntry: "xl/vbaProject.bin");
        Assert.Contains("Makro", ProductProfileWeightFile.Refusal(ProductProfileWeightFileKindV1.Xlsx, vba, out _),
            StringComparison.Ordinal);

        var externalLink = SyntheticWorkbook.Build(valid, extraEntry: "xl/externalLinks/externalLink1.xml");
        Assert.Contains("dış bağlantı", ProductProfileWeightFile.Refusal(ProductProfileWeightFileKindV1.Xlsx, externalLink, out _),
            StringComparison.Ordinal);

        Assert.NotNull(ProductProfileWeightFile.Refusal(ProductProfileWeightFileKindV1.Xlsx, PlainZip(), out _));
        Assert.Contains(".xlsx", ProductProfileWeightFile.Refusal(ProductProfileWeightFileKindV1.Xlsx,
            Encoding.UTF8.GetBytes(ProductProfileWeightFileFormatV1.Header + "\nProfile;40;20;;2;1.72\n"), out _),
            StringComparison.Ordinal);
        Assert.NotNull(ProductProfileWeightFile.Refusal(ProductProfileWeightFileKindV1.Xlsx, [0x50, 0x4B, 0x03, 0x04, 0x00], out _));
    }

    [Fact]
    public void The_extension_names_the_kind_and_the_content_proves_it_in_both_directions()
    {
        var workbook = SyntheticWorkbook.Build([HeaderCells, ["Profile", "#40", "#20", null, "#2", "#1.72"]]);
        Assert.Contains(".xlsx", ProductProfileWeightFile.Refusal(ProductProfileWeightFileKindV1.Csv, workbook, out var rows),
            StringComparison.Ordinal);
        Assert.Empty(rows);
        Assert.Null(ProductProfileWeightFile.Refusal(Encoding.UTF8.GetBytes(ProductProfileWeightFileFormatV1.Header
            + "\nProfile;40;20;;2;1.72\n"), out var csvRows));
        Assert.Single(csvRows);
        Assert.Equal(".xlsx", ProductProfileWeightFileFormatV1.WorkbookFileExtension);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ProductProfileWeightFileFormatV1.WorkbookContentType);
    }

    [Fact]
    public async Task Xlsm_xls_and_mismatched_names_are_refused_before_anything_is_read()
    {
        var service = Service(new FixedLimit(null));
        var workbook = Convert.ToBase64String(SyntheticWorkbook.Build([HeaderCells, ["Profile", "#40", "#20", null, "#2", "#1.72"]]));

        await AssertUnstored(service, Request("katalog.xlsm", workbook), 400, ProductProfileWeightProblemCodesV1.FileInvalid);
        await AssertUnstored(service, Request("katalog.xls", workbook), 400, ProductProfileWeightProblemCodesV1.FileInvalid);
        await AssertUnstored(service, Request(".xlsx", workbook), 400, ProductProfileWeightProblemCodesV1.FileInvalid);
        await AssertUnstored(service, Request("katalog.csv", workbook), 400, ProductProfileWeightProblemCodesV1.FileInvalid);
        await AssertUnstored(service, Request("katalog.xlsx", Base64(ProductProfileWeightFileFormatV1.Header + "\nProfile;40;20;;2;1.72\n")),
            400, ProductProfileWeightProblemCodesV1.FileInvalid);
    }

    [Fact]
    public async Task The_size_bound_is_the_settings_record_or_the_decided_5_MB_starting_value_and_an_unreadable_source_refuses()
    {
        Assert.Equal(5 * 1024 * 1024, ProductProfileWeightInputLimitsV1.MaximumFileBytes);
        var oversized = Convert.ToBase64String(new byte[ProductProfileWeightInputLimitsV1.MaximumFileBytes + 1]);
        await AssertUnstored(Service(new FixedLimit(null)), Request("katalog.csv", oversized), 400,
            ProductProfileWeightProblemCodesV1.FileInvalid);
        await AssertUnstored(Service(limits: null), Request("katalog.csv", oversized), 400,
            ProductProfileWeightProblemCodesV1.FileInvalid);

        // A smaller recorded value applies: a file just above it is refused although it is far below the starting value.
        var small = Base64(ProductProfileWeightFileFormatV1.Header + "\nProfile;40;20;;2;1.72\n");
        var recorded = new FixedLimit(16);
        var tooLarge = await Service(recorded).ImportAsync(Request("katalog.csv", small), Metadata());
        Assert.Equal((400, ProductProfileWeightProblemCodesV1.FileInvalid), (tooLarge.Answer.StatusCode, tooLarge.Answer.Code));
        Assert.Contains("16 bayt", tooLarge.Answer.Detail, StringComparison.Ordinal);
        Assert.Equal(CompanyA, Assert.Single(recorded.Asked));

        await AssertUnstored(Service(new FixedLimit(0)), Request("katalog.csv", small), 503,
            ProductProfileWeightProblemCodesV1.ProviderUnavailable);
        await AssertUnstored(Service(new FixedLimit(-1)), Request("katalog.csv", small), 503,
            ProductProfileWeightProblemCodesV1.ProviderUnavailable);
        await AssertUnstored(Service(new BrokenLimit()), Request("katalog.csv", small), 503,
            ProductProfileWeightProblemCodesV1.ProviderUnavailable);
    }

    private static async Task AssertUnstored(ProductProfileWeightTableService service, ImportProductProfileWeightTableRequestV1 request,
        int status, string code)
    {
        // The fake database has no reachable server: an answer here proves nothing was read or stored first.
        var result = await service.ImportAsync(request, Metadata());
        Assert.Equal((status, code, false), (result.Answer.StatusCode, result.Answer.Code, result.IsReplay));
    }

    private static byte[] PlainZip()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("note.txt").Open(), new UTF8Encoding(false));
            writer.Write("SENTETIK");
        }

        return buffer.ToArray();
    }

    private static string Base64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    private static ImportProductProfileWeightTableRequestV1 Request(string fileName, string contentBase64) =>
        new("0", "SENTETIK KATALOG", "SENTETIK gerekçe", fileName, contentBase64);

    private static ProductProfileWeightCommandMetadataV1 Metadata() =>
        new(CompanyA, "user-1", "corr-1", null, "Web", null, null, "key-1");

    private static ProductProfileWeightTableService Service(IProductProfileWeightImportLimitSourceV1? limits)
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        var context = new ProductPriceListDbContext(new DbContextOptionsBuilder<ProductPriceListDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
        return new ProductProfileWeightTableService(context, company, new FakeUser(), new FakeAuthority(), new NoAudit(), new NoVault(),
            TimeProvider.System, limits);
    }

    private sealed class FixedLimit(long? value) : IProductProfileWeightImportLimitSourceV1
    {
        public List<Guid> Asked { get; } = [];

        public Task<long?> GetMaximumFileBytesAsync(Guid companyId, CancellationToken cancellationToken = default)
        {
            Asked.Add(companyId);
            return Task.FromResult(value);
        }
    }

    private sealed class BrokenLimit : IProductProfileWeightImportLimitSourceV1
    {
        public Task<long?> GetMaximumFileBytesAsync(Guid companyId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SENTETIK: the Settings source is unreachable.");
    }

    private sealed class FakeUser : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public string? UserId => "user-1";

        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { CompanyA };

        public bool CanAccessCompany(Guid companyId) => companyId == CompanyA;
    }

    private sealed class FakeAuthority : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionScope(true, true, new HashSet<string> { "ROLE" }, new HashSet<Guid>(), new HashSet<Guid>(),
                new HashSet<Guid>()));
    }

    private sealed class NoAudit : IAuditEventCollector
    {
        public void Enqueue(AuditEventDraft auditEvent) => throw new InvalidOperationException("No audit is expected in this test.");
    }

    private sealed class NoVault : IProductProfileWeightFileVaultV1
    {
        public Task<ProductProfileWeightFileStoreResultV1> StoreAsync(ProductProfileWeightFileV1 file,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The vault is not reached before the input is valid.");
    }
}

/// <summary>
/// A minimal SENTETIK <c>.xlsx</c> package for the tests: cell text "#…" is a number cell holding that raw value, "=…" a formula
/// cell, "inline:…" an inline string, null an absent cell and anything else a shared string.
/// </summary>
internal static class SyntheticWorkbook
{
    private const string WorkbookMain = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";

    internal static byte[] Build(IReadOnlyList<string?[]> rows, string mainContentType = WorkbookMain, string? extraEntry = null)
    {
        var shared = new List<string>();
        var sheet = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet "
            + "xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var rowNo = rowIndex + 1;
            sheet.Append("<row r=\"").Append(rowNo).Append("\">");
            for (var column = 0; column < rows[rowIndex].Length; column++)
            {
                if (rows[rowIndex][column] is not { } value)
                {
                    continue;
                }

                var reference = $"{(char)('A' + column)}{rowNo}";
                if (value.StartsWith('#'))
                {
                    sheet.Append($"<c r=\"{reference}\"><v>{value[1..]}</v></c>");
                }
                else if (value.StartsWith('='))
                {
                    sheet.Append($"<c r=\"{reference}\"><f>{SecurityElement.Escape(value[1..])}</f><v>1.72</v></c>");
                }
                else if (value.StartsWith("inline:", StringComparison.Ordinal))
                {
                    sheet.Append($"<c r=\"{reference}\" t=\"inlineStr\"><is><t>{SecurityElement.Escape(value["inline:".Length..])}</t></is></c>");
                }
                else
                {
                    shared.Add(value);
                    sheet.Append($"<c r=\"{reference}\" t=\"s\"><v>{shared.Count - 1}</v></c>");
                }
            }

            sheet.Append("</row>");
        }

        sheet.Append("</sheetData></worksheet>");
        var strings = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><sst "
            + "xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        foreach (var text in shared)
        {
            strings.Append("<si><t>").Append(SecurityElement.Escape(text)).Append("</t></si>");
        }

        strings.Append("</sst>");

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
                + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                + $"<Override PartName=\"/xl/workbook.xml\" ContentType=\"{mainContentType}\"/>"
                + "<Override PartName=\"/xl/worksheets/sheet1.xml\" "
                + "ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"
                + "<Override PartName=\"/xl/sharedStrings.xml\" "
                + "ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/></Types>");
            Add(archive, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" "
                + "Target=\"xl/workbook.xml\"/></Relationships>");
            Add(archive, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" "
                + "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>"
                + "<sheet name=\"SENTETIK\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add(archive, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" "
                + "Target=\"worksheets/sheet1.xml\"/>"
                + "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\" "
                + "Target=\"sharedStrings.xml\"/></Relationships>");
            Add(archive, "xl/worksheets/sheet1.xml", sheet.ToString());
            Add(archive, "xl/sharedStrings.xml", strings.ToString());
            if (extraEntry is not null)
            {
                Add(archive, extraEntry, "SENTETIK");
            }
        }

        return buffer.ToArray();
    }

    private static void Add(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
