using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;
using GoymenERP.Modules.Product.Infrastructure.PriceLists.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Product.UnitTests;

/// <summary>
/// URN-D13…D16 (STK-S37R) without a database: the import permission is registered (company-wide, not Critical, seeded to
/// nobody), the server reads the table file strictly (header, six fields, two kinds, millimetre / kg-per-metre scale, hollow
/// shapes, one row per measure pair, nothing rounded), the tables are mapped company-scoped and insert-only with the decided
/// checks, the forward-fix rollback refuses first (51483), the default vault port answers "unavailable" (fail-closed), and the
/// service answers its fences and malformed input before anything is read. What SQL Server enforces is proven by
/// <c>ProductPriceListSqlServerRelationalAcceptanceTests</c>. SENTETIK / NOT_ENV.
/// </summary>
public sealed class ProductProfileWeightTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private const string Header = ProductProfileWeightFileFormatV1.Header;

    [Fact]
    public void The_import_permission_is_registered_once_company_wide_not_critical_and_seeded_to_nobody()
    {
        Assert.Equal("product.profile-weights.import", ProductProfileWeightPermissionsV1.Import);
        Assert.Equal(ErpPermissions.ProductProfileWeightsImport, ProductProfileWeightPermissionsV1.Import);
        Assert.True(ErpPermissions.IsKnown(ProductProfileWeightPermissionsV1.Import));
        Assert.Single(ErpPermissions.All, name => name == ProductProfileWeightPermissionsV1.Import);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(ProductProfileWeightPermissionsV1.Import));
        Assert.False(ErpPermissions.IsCritical(ProductProfileWeightPermissionsV1.Import));
        var seed = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "backend", "GoymenERP.Api", "Seed", "identity-seed.json"));
        Assert.DoesNotContain(ProductProfileWeightPermissionsV1.Import, seed, StringComparison.Ordinal);
    }

    [Fact]
    public void A_valid_file_is_read_in_file_order_without_rounding_and_with_a_bom_blank_lines_and_crlf()
    {
        var file = "﻿" + Header + "\r\n"
            + "Profile;40;20;;2;1.72\r\n"
            + "\r\n"
            + "RoundTube;;;48.3;2.6;2.930000\r\n"
            + "Profile;40.000;40;;1.5;1.789012\n";

        var refusal = ProductProfileWeightFile.Refusal(Encoding.UTF8.GetBytes(file), out var rows);

        Assert.Null(refusal);
        Assert.Equal(3, rows.Count);
        Assert.Equal(new ProductProfileWeightRowV1("Profile", 40m, 20m, null, 2m, 1.72m), rows[0]);
        Assert.Equal(("RoundTube", (decimal?)null, (decimal?)null, (decimal?)48.3m, 2.6m),
            (rows[1].SectionKind, rows[1].WidthMm, rows[1].HeightMm, rows[1].OuterDiameterMm, rows[1].WallThicknessMm));
        Assert.Equal("2.93", rows[1].KgPerMetre.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("40", rows[2].WidthMm!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(1.789012m, rows[2].KgPerMetre);
    }

    [Theory]
    // Header and body shape.
    [InlineData("", "başlık")]
    [InlineData("Profile;40;20;;2;1.72\n", "başlık")]
    [InlineData("sectionkind;widthMm;heightMm;outerDiameterMm;wallThicknessMm;kgPerMetre\nProfile;40;20;;2;1.72\n", "başlık")]
    [InlineData(Header + "\n", "en az bir satır")]
    [InlineData(Header + "\nProfile;40;20;;2\n", "Satır 2")]
    [InlineData(Header + "\nProfile;40;20;;2;1.72;x\n", "Satır 2")]
    // Kinds: only the two table kinds, exact case.
    [InlineData(Header + "\nSheet;40;20;;2;1.72\n", "Satır 2")]
    [InlineData(Header + "\nprofile;40;20;;2;1.72\n", "Satır 2")]
    // Numbers: no comma decimal, sign, exponent, grouping or bare dot.
    [InlineData(Header + "\nProfile;40,5;20;;2;1.72\n", "Satır 2")]
    [InlineData(Header + "\nProfile;-40;20;;2;1.72\n", "Satır 2")]
    [InlineData(Header + "\nProfile;4e1;20;;2;1.72\n", "Satır 2")]
    [InlineData(Header + "\nProfile;40.;20;;2;1.72\n", "Satır 2")]
    [InlineData(Header + "\nProfile;.5;20;;2;1.72\n", "Satır 2")]
    // Scale and positivity: never rounded.
    [InlineData(Header + "\nProfile;40.0005;20;;2;1.72\n", "Satır 2")]
    [InlineData(Header + "\nProfile;40;20;;2;1.0000001\n", "Satır 2")]
    [InlineData(Header + "\nProfile;40;20;;0;1.72\n", "Satır 2")]
    [InlineData(Header + "\nProfile;40;20;;2;0\n", "Satır 2")]
    [InlineData(Header + "\nProfile;40;20;;2;\n", "Satır 2")]
    [InlineData(Header + "\nProfile;40;20;;;1.72\n", "Satır 2")]
    // Shapes: a profile has width and height, a tube a diameter; the wall must fit.
    [InlineData(Header + "\nProfile;40;;;2;1.72\n", "Satır 2")]
    [InlineData(Header + "\nProfile;40;20;30;2;1.72\n", "Satır 2")]
    [InlineData(Header + "\nProfile;40;3;;2;1.72\n", "Satır 2")]
    [InlineData(Header + "\nRoundTube;;;;2;1.72\n", "Satır 2")]
    [InlineData(Header + "\nRoundTube;10;;48.3;2;1.72\n", "Satır 2")]
    [InlineData(Header + "\nRoundTube;;;10;5.001;1.72\n", "Satır 2")]
    // One row per measure pair (trailing zeros do not make a new pair).
    [InlineData(Header + "\nProfile;40;20;;2;1.72\nProfile;40.0;20.00;;2.000;1.80\n", "Satır 3")]
    public void An_invalid_file_is_refused_whole_with_its_reason(string file, string expected)
    {
        var refusal = ProductProfileWeightFile.Refusal(Encoding.UTF8.GetBytes(file), out var rows);

        Assert.NotNull(refusal);
        Assert.Contains(expected, refusal, StringComparison.Ordinal);
        Assert.Empty(rows);
    }

    [Fact]
    public void The_extended_header_adds_structural_profile_rows_keyed_by_series_and_size()
    {
        Assert.Equal(Header + ";profileSeries;profileSize", ProductProfileWeightFileFormatV1.ExtendedHeader);
        var file = ProductProfileWeightFileFormatV1.ExtendedHeader + "\n"
            + "StructuralProfile;;;;;22.4;IPE;200\n"
            + "StructuralProfile;;;;;22.40;UPN;200\n"
            + "StructuralProfile;;;;;10.6;HEB;100A\n"
            + "Profile;40;20;;2;1.72;;\n"
            + "RoundTube;;;48.3;2.6;2.93;;\n";

        var refusal = ProductProfileWeightFile.Refusal(Encoding.UTF8.GetBytes(file), out var rows);

        Assert.Null(refusal);
        Assert.Equal(5, rows.Count);
        Assert.Equal(new ProductProfileWeightRowV1("StructuralProfile", null, null, null, null, 22.4m, "IPE", "200"), rows[0]);
        Assert.Equal(("UPN", "200"), (rows[1].ProfileSeries, rows[1].ProfileSize));
        Assert.Equal("22.4", rows[1].KgPerMetre.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(new ProductProfileWeightRowV1("Profile", 40m, 20m, null, 2m, 1.72m), rows[3]);
        Assert.Equal((null, null), (rows[4].ProfileSeries, rows[4].ProfileSize));
    }

    [Theory]
    // A structural row is accepted only under the extended header.
    [InlineData(Header + "\nStructuralProfile;;;;;22.4\n", "Satır 2")]
    // Extended header: eight fields, the known series, an exact size designation, no measure.
    [InlineData(ProductProfileWeightFileFormatV1.ExtendedHeader + "\nStructuralProfile;;;;;22.4;IPE\n", "8 alan")]
    [InlineData(ProductProfileWeightFileFormatV1.ExtendedHeader + "\nStructuralProfile;;;;;22.4;NPU;200\n", "Satır 2")]
    [InlineData(ProductProfileWeightFileFormatV1.ExtendedHeader + "\nStructuralProfile;;;;;22.4;ipe;200\n", "Satır 2")]
    [InlineData(ProductProfileWeightFileFormatV1.ExtendedHeader + "\nStructuralProfile;;;;;22.4;IPE;200a\n", "Satır 2")]
    [InlineData(ProductProfileWeightFileFormatV1.ExtendedHeader + "\nStructuralProfile;;;;;22.4;IPE;\n", "Satır 2")]
    [InlineData(ProductProfileWeightFileFormatV1.ExtendedHeader + "\nStructuralProfile;;;;;22.4;IPE;12345678901234567\n", "Satır 2")]
    [InlineData(ProductProfileWeightFileFormatV1.ExtendedHeader + "\nStructuralProfile;100;;;;22.4;IPE;200\n", "Satır 2")]
    [InlineData(ProductProfileWeightFileFormatV1.ExtendedHeader + "\nStructuralProfile;;;;;;IPE;200\n", "Satır 2")]
    [InlineData(ProductProfileWeightFileFormatV1.ExtendedHeader + "\nProfile;40;20;;2;1.72;IPE;200\n", "Satır 2")]
    [InlineData(ProductProfileWeightFileFormatV1.ExtendedHeader + "\nEqualAngle;;;;;1.72;;\n", "Satır 2")]
    // One row per series and size (the kg/m does not make a new key).
    [InlineData(ProductProfileWeightFileFormatV1.ExtendedHeader + "\nStructuralProfile;;;;;22.4;IPE;200\nStructuralProfile;;;;;22.5;IPE;200\n",
        "Satır 3")]
    public void An_invalid_structural_row_is_refused_whole_with_its_reason(string file, string expected)
    {
        var refusal = ProductProfileWeightFile.Refusal(Encoding.UTF8.GetBytes(file), out var rows);

        Assert.NotNull(refusal);
        Assert.Contains(expected, refusal, StringComparison.Ordinal);
        Assert.Empty(rows);
    }

    [Fact]
    public void A_file_that_is_not_utf8_or_holds_too_many_rows_is_refused()
    {
        byte[] notUtf8 = [.. Encoding.UTF8.GetBytes(Header + "\nProfile;40;20;;2;1.72"), 0xFF, (byte)'\n'];
        Assert.Contains("UTF-8", ProductProfileWeightFile.Refusal(notUtf8, out _), StringComparison.Ordinal);

        var many = new StringBuilder(Header).Append('\n');
        for (var index = 1; index <= ProductProfileWeightInputLimitsV1.MaximumRows + 1; index++)
        {
            many.Append("RoundTube;;;").Append(index + 100).Append(";1;1.5\n");
        }

        var refusal = ProductProfileWeightFile.Refusal(Encoding.UTF8.GetBytes(many.ToString()), out var rows);
        Assert.Contains(ProductProfileWeightInputLimitsV1.MaximumRows.ToString(System.Globalization.CultureInfo.InvariantCulture),
            refusal, StringComparison.Ordinal);
        Assert.Empty(rows);
    }

    [Fact]
    public void The_tables_are_company_scoped_insert_only_and_mapped_with_the_decided_checks_and_precision()
    {
        using var context = Context();
        var design = context.GetService<IDesignTimeModel>().Model;

        var header = design.FindEntityType(typeof(ProductProfileWeightImportRow))!;
        Assert.Equal(("product", "ProductProfileWeightImports"), (header.GetSchema(), header.GetTableName()));
        Assert.Equal(["CompanyId", "Version"], header.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(header.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "ImportId"]));
        Assert.Equal(ProductProfileWeightInputLimitsV1.SourceLabelMaximumLength,
            header.FindProperty(nameof(ProductProfileWeightImportRow.SourceLabel))!.GetMaxLength());
        Assert.False(header.FindProperty(nameof(ProductProfileWeightImportRow.DocumentId))!.IsNullable);
        Assert.Single(context.Model.FindEntityType(typeof(ProductProfileWeightImportRow))!.GetDeclaredQueryFilters());

        var line = design.FindEntityType(typeof(ProductProfileWeightLineRow))!;
        Assert.Equal(("product", "ProductProfileWeightLines"), (line.GetSchema(), line.GetTableName()));
        Assert.Equal(["CompanyId", "ImportVersion", "LineNo"], line.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(line.GetCheckConstraints(), check => check.Name == "CK_ProductProfileWeightLines_Shape"
            && check.Sql == ProductPriceListDbContext.ProfileWeightLineShapeCheck);
        var measures = Assert.Single(line.GetIndexes(), index => index.GetDatabaseName() == "UX_ProductProfileWeightLines_Measures");
        Assert.True(measures.IsUnique);
        Assert.Null(measures.GetFilter());
        // KLN-D87: a structural row is keyed by its series and size designation; it has no wall, so the wall is nullable.
        Assert.Equal(["ProfileSeries", "ProfileSize"], measures.Properties.TakeLast(2).Select(property => property.Name));
        Assert.True(line.FindProperty(nameof(ProductProfileWeightLineRow.WallThicknessMm))!.IsNullable);
        Assert.Equal(ProductDimensionInputLimitsV1.SectionKindMaximumLength,
            line.FindProperty(nameof(ProductProfileWeightLineRow.SectionKind))!.GetMaxLength());
        Assert.Equal(ProductDimensionInputLimitsV1.ProfileSeriesMaximumLength,
            line.FindProperty(nameof(ProductProfileWeightLineRow.ProfileSeries))!.GetMaxLength());
        Assert.Equal(ProductDimensionInputLimitsV1.ProfileSizeMaximumLength,
            line.FindProperty(nameof(ProductProfileWeightLineRow.ProfileSize))!.GetMaxLength());
        foreach (var millimetre in new[]
                 {
                     nameof(ProductProfileWeightLineRow.WidthMm), nameof(ProductProfileWeightLineRow.HeightMm),
                     nameof(ProductProfileWeightLineRow.OuterDiameterMm), nameof(ProductProfileWeightLineRow.WallThicknessMm),
                 })
        {
            var property = line.FindProperty(millimetre)!;
            Assert.Equal((18, ProductDimensionInputLimitsV1.MillimetreDecimals), (property.GetPrecision(), property.GetScale()));
        }

        var kg = line.FindProperty(nameof(ProductProfileWeightLineRow.KgPerMetre))!;
        Assert.False(kg.IsNullable);
        Assert.Equal((18, ProductDimensionInputLimitsV1.KgPerMetreDecimals), (kg.GetPrecision(), kg.GetScale()));
        Assert.Contains(line.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(ProductProfileWeightImportRow));
        Assert.Single(context.Model.FindEntityType(typeof(ProductProfileWeightLineRow))!.GetDeclaredQueryFilters());

        var attempts = design.FindEntityType(typeof(ProductProfileWeightAttemptRow))!;
        Assert.Equal(("product", "ProductProfileWeightAttempts"), (attempts.GetSchema(), attempts.GetTableName()));
        Assert.Equal(["CompanyId", "CommandName", "IdempotencyKeyHash"],
            attempts.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Single(context.Model.FindEntityType(typeof(ProductProfileWeightAttemptRow))!.GetDeclaredQueryFilters());

        // URN-D16: the card's profile branch no longer demands a stored kg/m; sheet and coil still never hold one.
        var card = design.FindEntityType(typeof(ProductCardDimensionRow))!;
        Assert.Contains(card.GetCheckConstraints(), check => check.Name == "CK_ProductCardDimensions_Shape"
            && check.Sql == ProductPriceListDbContext.DimensionShapeCheck);
        Assert.DoesNotContain("[KgPerMetre] IS NOT NULL", ProductPriceListDbContext.DimensionShapeCheck, StringComparison.Ordinal);
    }

    [Fact]
    public void The_kg_per_metre_migration_precedes_the_package_standard_and_section_family_migrations_and_its_Down_refuses_first_with_51483()
    {
        using var context = Context();
        Assert.Equal(["AddProductProfileWeightImports", "AddProductPackageStandards", "AddProductSectionFamilies",
                "AddProductPackageStandardApprovalAndFiles"],
            context.Database.GetMigrations().TakeLast(4).Select(name => name.Split('_', 2)[1]));
        Assert.False(context.Database.HasPendingModelChanges());

        var migration = new AddProductProfileWeightImports();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 51483", guard, StringComparison.Ordinal);
        Assert.Contains("[product].[ProductProfileWeightImports]", guard, StringComparison.Ordinal);
        Assert.Contains("[product].[ProductProfileWeightAttempts]", guard, StringComparison.Ordinal);
        Assert.Contains("[product].[ProductCardDimensions]", guard, StringComparison.Ordinal);
        Assert.Contains("is not rolled back (forward fix)", guard, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
        Assert.Contains(migration.UpOperations.OfType<CreateTableOperation>(),
            operation => operation.Name == "ProductProfileWeightLines");
        // KLN-D87 replaced the card shape check in AddProductSectionFamilies; this migration keeps its own four-kind text.
        var shape = Assert.Single(migration.UpOperations.OfType<AddCheckConstraintOperation>(),
            operation => operation.Name == "CK_ProductCardDimensions_Shape");
        Assert.DoesNotContain("StructuralProfile", shape.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_write_guard_keeps_imports_lines_and_answers_insert_only_and_in_the_acting_company()
    {
        await using (var context = Context())
        {
            var header = Import(CompanyA);
            context.Attach(header);
            context.Entry(header).Property(x => x.SourceLabel).IsModified = true;
            header.SourceLabel = "SENTETIK BAŞKA";
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await using (var context = Context())
        {
            context.Attach(Line(CompanyA));
            context.Remove(context.ChangeTracker.Entries<ProductProfileWeightLineRow>().Single().Entity);
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await using (var context = Context())
        {
            context.Add(Import(CompanyB));
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task The_default_vault_port_answers_unavailable_so_nothing_is_imported()
    {
        IProductProfileWeightFileVaultV1 vault = new UnavailableProductProfileWeightFileVault();
        var answer = await vault.StoreAsync(new ProductProfileWeightFileV1(CompanyA, Guid.NewGuid(), "katalog.csv",
            ProductProfileWeightFileFormatV1.ContentType, [1], new string('a', 64), "user-1", "key-1", "corr-1", null));

        Assert.Equal(ProductProfileWeightFileStoreOutcomeV1.Unavailable, answer.Outcome);
        Assert.Null(answer.DocumentId);
    }

    [Fact]
    public async Task The_fences_and_malformed_input_answer_before_anything_is_read_and_store_nothing()
    {
        var anonymous = Service(new FakeUser(null), new FakeAuthority(granted: true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => anonymous.GetCurrentAsync());

        var denied = new FakeAuthority(granted: false);
        var ungranted = Service(new FakeUser("user-1"), denied);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ungranted.ImportAsync(Request(), Metadata()));
        Assert.Equal([ProductProfileWeightPermissionsV1.Import], denied.Asked);

        var branch = Service(new FakeUser("user-1"), new FakeAuthority(granted: true, companyWide: false));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => branch.GetCurrentAsync());

        var otherCompany = Service(new FakeUser("user-1", CompanyB), new FakeAuthority(granted: true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => otherCompany.GetCurrentAsync());

        var service = Service(new FakeUser("user-1"), new FakeAuthority(granted: true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(Request(), Metadata() with { ActorId = "user-2" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(Request(), Metadata() with { CompanyId = CompanyB }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(Request(), Metadata() with { IdempotencyKey = " " }));

        await AssertUnstored(service, Request() with { ExpectedVersion = "01" }, 400, ProductProfileWeightProblemCodesV1.RequestInvalid);
        await AssertUnstored(service, Request() with { ExpectedVersion = null }, 400, ProductProfileWeightProblemCodesV1.RequestInvalid);
        await AssertUnstored(service, Request() with { SourceLabel = "  " }, 400, ProductProfileWeightProblemCodesV1.SourceLabelRequired);
        await AssertUnstored(service, Request() with
        {
            SourceLabel = new string('k', ProductProfileWeightInputLimitsV1.SourceLabelMaximumLength + 1),
        }, 400, ProductProfileWeightProblemCodesV1.SourceLabelTooLong);
        await AssertUnstored(service, Request() with { Reason = null }, 400, ProductProfileWeightProblemCodesV1.ReasonRequired);
        await AssertUnstored(service, Request() with
        {
            Reason = new string('x', ProductProfileWeightInputLimitsV1.ReasonMaximumLength + 1),
        }, 400, ProductProfileWeightProblemCodesV1.ReasonTooLong);
        await AssertUnstored(service, Request() with { FileName = "katalog.xlsx" }, 400, ProductProfileWeightProblemCodesV1.FileInvalid);
        await AssertUnstored(service, Request() with { FileName = "../katalog.csv" }, 400, ProductProfileWeightProblemCodesV1.FileInvalid);
        await AssertUnstored(service, Request() with { FileName = ".csv" }, 400, ProductProfileWeightProblemCodesV1.FileInvalid);
        await AssertUnstored(service, Request() with { ContentBase64 = "" }, 400, ProductProfileWeightProblemCodesV1.FileInvalid);
        await AssertUnstored(service, Request() with { ContentBase64 = "bm90IGJhc2U2NA=!" }, 400,
            ProductProfileWeightProblemCodesV1.FileInvalid);
        await AssertUnstored(service, Request() with
        {
            ContentBase64 = Convert.ToBase64String(new byte[ProductProfileWeightInputLimitsV1.MaximumFileBytes + 1]),
        }, 400, ProductProfileWeightProblemCodesV1.FileInvalid);
        var badRow = await service.ImportAsync(Request() with { ContentBase64 = Base64(Header + "\nProfile;40;3;;2;1.72\n") },
            Metadata());
        Assert.Equal((400, ProductProfileWeightProblemCodesV1.FileInvalid, false),
            (badRow.Answer.StatusCode, badRow.Answer.Code, badRow.IsReplay));
        Assert.Contains("Satır 2", badRow.Answer.Detail, StringComparison.Ordinal);
    }

    private static async Task AssertUnstored(ProductProfileWeightTableService service, ImportProductProfileWeightTableRequestV1 request,
        int status, string code)
    {
        // The fake database has no reachable server: an answer here proves nothing was read or stored first.
        var result = await service.ImportAsync(request, Metadata());
        Assert.Equal((status, code, false, "corr-1"),
            (result.Answer.StatusCode, result.Answer.Code, result.IsReplay, result.OriginalCorrelationId));
    }

    private static string Base64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    private static ImportProductProfileWeightTableRequestV1 Request() =>
        new("0", "SENTETIK KATALOG", "SENTETIK gerekçe", "katalog.csv", Base64(Header + "\nProfile;40;20;;2;1.72\n"));

    private static ProductProfileWeightCommandMetadataV1 Metadata() =>
        new(CompanyA, "user-1", "corr-1", null, "Web", null, null, "key-1");

    private static ProductProfileWeightImportRow Import(Guid company) => new()
    {
        CompanyId = company, Version = 1, ImportId = Guid.NewGuid(), SourceLabel = "SENTETIK KATALOG", Reason = "SENTETIK",
        FileName = "katalog.csv", FileSha256 = new string('a', 64), FileSizeBytes = 10, DocumentId = Guid.NewGuid(), RowCount = 1,
        ImportedAtUtc = DateTimeOffset.UnixEpoch, ImportedBy = "user-1", CorrelationId = "corr-1",
    };

    private static ProductProfileWeightLineRow Line(Guid company) => new()
    {
        CompanyId = company, ImportVersion = 1, LineNo = 1, SectionKind = ProductDimensionSectionKindsV1.Profile, WidthMm = 40m,
        HeightMm = 20m, WallThicknessMm = 2m, KgPerMetre = 1.72m,
    };

    private static ProductProfileWeightTableService Service(ICurrentUser user, IErpAuthorityRevalidationService authority)
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        var context = new ProductPriceListDbContext(new DbContextOptionsBuilder<ProductPriceListDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
        return new ProductProfileWeightTableService(context, company, user, authority, new NoAudit(), new NoVault(),
            TimeProvider.System);
    }

    private static ProductPriceListDbContext Context()
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        return new ProductPriceListDbContext(new DbContextOptionsBuilder<ProductPriceListDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GoymenERP.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class FakeUser(string? userId, Guid? company = null) : ICurrentUser
    {
        public bool IsAuthenticated => userId is not null;

        public string? UserId => userId;

        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { company ?? CompanyA };

        public bool CanAccessCompany(Guid companyId) => companyId == (company ?? CompanyA);
    }

    private sealed class FakeAuthority(bool granted, bool companyWide = true) : IErpAuthorityRevalidationService
    {
        public List<string> Asked { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Asked.Add(permission);
            return Task.FromResult(granted
                ? new PermissionScope(true, companyWide, new HashSet<string> { "ROLE" }, new HashSet<Guid>(), new HashSet<Guid>(),
                    new HashSet<Guid>())
                : PermissionScope.Denied);
        }
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
