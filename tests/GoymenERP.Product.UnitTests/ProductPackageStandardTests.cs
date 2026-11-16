using System.IO.Compression;
using System.Security;
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
/// B-08 / BS-10 (STK-S12) without a database: the manage permission is registered (company-wide, not Critical, seeded to
/// nobody), the server reads the Excel file strictly (first sheet, the two header titles, text product codes, positive whole
/// pieces, no formula, one row per product, a row bound), the tables are mapped company-scoped and insert-only with the decided
/// checks, the forward-fix rollback refuses first (51492), the service answers its fences and malformed input before anything is
/// read, and the read port answers Denied / NotFound without a database. What SQL Server enforces (NoSource, versions, replay,
/// checks) is proven by <c>ProductPriceListSqlServerRelationalAcceptanceTests</c>. SENTETIK / NOT_ENV.
/// </summary>
public sealed class ProductPackageStandardTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private const string CodeHeader = ProductPackageStandardFileFormatV2.ProductCodeHeader;
    private const string PiecesHeader = ProductPackageStandardFileFormatV2.PiecesPerPackageHeader;

    [Fact]
    public void The_manage_permission_is_registered_once_company_wide_not_critical_and_seeded_to_nobody()
    {
        Assert.Equal("product.package-standards.manage", ProductPackageStandardPermissionsV2.Manage);
        Assert.Equal(ErpPermissions.ProductPackageStandardsManage, ProductPackageStandardPermissionsV2.Manage);
        Assert.True(ErpPermissions.IsKnown(ProductPackageStandardPermissionsV2.Manage));
        Assert.Single(ErpPermissions.All, name => name == ProductPackageStandardPermissionsV2.Manage);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(ProductPackageStandardPermissionsV2.Manage));
        Assert.False(ErpPermissions.IsCritical(ProductPackageStandardPermissionsV2.Manage));
        var seed = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "backend", "GoymenERP.Api", "Seed", "identity-seed.json"));
        Assert.DoesNotContain(ProductPackageStandardPermissionsV2.Manage, seed, StringComparison.Ordinal);
    }

    [Fact]
    public void A_valid_workbook_is_read_from_the_first_sheet_in_row_order_ignoring_other_columns_and_empty_rows()
    {
        var file = Workbook(
            Row(1, Text("A", "Not"), Text("B", CodeHeader), Text("C", PiecesHeader)),
            Row(2, Text("A", "x"), Shared("B", 0), Number("C", "12")),
            Row(3),
            Row(5, Text("B", " SNT-002 "), Text("C", "6")));

        var result = ProductPackageStandardWorkbook.Read(file);

        Assert.Empty(result.Errors);
        Assert.Equal(
            [new PackageStandardSourceRow(2, "SNT-001", 12), new PackageStandardSourceRow(5, "SNT-002", 6)],
            result.Rows);
    }

    [Theory]
    [InlineData("headers")]
    [InlineData("duplicate-header")]
    [InlineData("no-data")]
    [InlineData("empty-sheet")]
    public void A_workbook_without_the_header_or_data_is_refused_whole(string shape)
    {
        var file = shape switch
        {
            "headers" => Workbook(Row(1, Text("A", "Ürün"), Text("B", PiecesHeader)), Row(2, Text("A", "SNT-001"), Number("B", "3"))),
            "duplicate-header" => Workbook(Row(1, Text("A", CodeHeader), Text("B", CodeHeader), Text("C", PiecesHeader))),
            "no-data" => Workbook(Row(1, Text("A", CodeHeader), Text("B", PiecesHeader)), Row(2)),
            _ => Workbook(),
        };

        var result = ProductPackageStandardWorkbook.Read(file);

        Assert.Empty(result.Rows);
        Assert.Single(result.Errors);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-4")]
    [InlineData("2.5")]
    [InlineData("1e2")]
    [InlineData("012")]
    [InlineData("+3")]
    [InlineData("99999999999")]
    [InlineData("")]
    public void A_row_with_invalid_pieces_is_a_row_error(string pieces)
    {
        var file = Workbook(
            Row(1, Text("A", CodeHeader), Text("B", PiecesHeader)),
            Row(2, Text("A", "SNT-001"), Number("B", pieces)));

        var result = ProductPackageStandardWorkbook.Read(file);

        Assert.Empty(result.Rows);
        Assert.Contains(result.Errors, error => error.StartsWith("Satır 2:", StringComparison.Ordinal));
    }

    [Fact]
    public void Codes_must_be_present_text_bounded_and_unique_within_the_file()
    {
        var file = Workbook(
            Row(1, Text("A", CodeHeader), Text("B", PiecesHeader)),
            Row(2, Number("A", "1001"), Number("B", "4")),
            Row(3, Text("A", new string('k', ProductPackageStandardWorkbook.ProductCodeMaximumLength + 1)), Number("B", "4")),
            Row(4, Text("A", "SNT-001"), Number("B", "4")),
            Row(5, Text("A", "SNT-001"), Number("B", "5")),
            Row(6, Number("B", "5")));

        var result = ProductPackageStandardWorkbook.Read(file);

        Assert.Equal([new PackageStandardSourceRow(4, "SNT-001", 4)], result.Rows);
        Assert.Equal(["Satır 2:", "Satır 3:", "Satır 5:", "Satır 6:"],
            result.Errors.Select(error => error[..(error.IndexOf(':', StringComparison.Ordinal) + 1)]));
    }

    [Fact]
    public void A_formula_cell_a_broken_package_or_a_shared_string_out_of_range_refuses_the_file()
    {
        var formula = Workbook(
            Row(1, Text("A", CodeHeader), Text("B", PiecesHeader)),
            Row(2, Text("A", "SNT-001"), "<c r=\"B2\"><f>2*3</f><v>6</v></c>"));
        Assert.Single(ProductPackageStandardWorkbook.Read(formula).Errors);
        Assert.Empty(ProductPackageStandardWorkbook.Read(formula).Rows);

        var outOfRange = Workbook(
            Row(1, Text("A", CodeHeader), Text("B", PiecesHeader)),
            Row(2, Shared("A", 7), Number("B", "6")));
        Assert.Single(ProductPackageStandardWorkbook.Read(outOfRange).Errors);

        Assert.Single(ProductPackageStandardWorkbook.Read(Encoding.UTF8.GetBytes("not a zip")).Errors);
        Assert.Single(ProductPackageStandardWorkbook.Read([]).Errors);
    }

    [Fact]
    public void A_workbook_with_too_many_rows_is_refused_naming_the_bound()
    {
        var rows = new List<string> { Row(1, Text("A", CodeHeader), Text("B", PiecesHeader)) };
        for (var index = 0; index <= ProductPackageStandardInputLimitsV2.MaximumRows; index++)
        {
            var number = index + 2;
            rows.Add(Row(number, Text("A", "SNT-" + number.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Number("B", "3")));
        }

        var result = ProductPackageStandardWorkbook.Read(Workbook([.. rows]));

        Assert.Empty(result.Rows);
        var error = Assert.Single(result.Errors);
        Assert.Contains(ProductPackageStandardInputLimitsV2.MaximumRows.ToString(System.Globalization.CultureInfo.InvariantCulture),
            error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1", true, 1)]
    [InlineData("24", true, 24)]
    [InlineData("2147483647", true, int.MaxValue)]
    [InlineData("2147483648", false, 0)]
    [InlineData("0", false, 0)]
    [InlineData("07", false, 0)]
    [InlineData("1,5", false, 0)]
    [InlineData(" 3", false, 0)]
    [InlineData("٣", false, 0)]
    public void Pieces_are_a_positive_whole_number_without_sign_mark_or_leading_zero(string text, bool valid, int expected)
    {
        Assert.Equal(valid, ProductPackageStandardWorkbook.TryParsePieces(text, out var pieces));
        Assert.Equal(expected, valid ? pieces : 0);
    }

    [Fact]
    public void The_tables_are_company_scoped_insert_only_and_mapped_with_the_decided_checks()
    {
        using var context = Context();
        var design = context.GetService<IDesignTimeModel>().Model;

        var header = design.FindEntityType(typeof(ProductPackageStandardVersionRow))!;
        Assert.Equal(("product", "ProductPackageStandardVersions"), (header.GetSchema(), header.GetTableName()));
        Assert.Equal(["CompanyId", "Version"], header.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(header.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "VersionId"]));
        Assert.Equal(ProductPackageStandardInputLimitsV2.SourceLabelMaximumLength,
            header.FindProperty(nameof(ProductPackageStandardVersionRow.SourceLabel))!.GetMaxLength());
        Assert.Equal(ProductPackageStandardInputLimitsV2.ReasonMaximumLength,
            header.FindProperty(nameof(ProductPackageStandardVersionRow.Reason))!.GetMaxLength());
        Assert.Equal(ProductPackageStandardInputLimitsV2.FileNameMaximumLength,
            header.FindProperty(nameof(ProductPackageStandardVersionRow.FileName))!.GetMaxLength());
        var source = Assert.Single(header.GetCheckConstraints(), check => check.Name == "CK_ProductPackageStandardVersions_Source");
        Assert.Contains("[SourceKind] = 'Import'", source.Sql, StringComparison.Ordinal);
        Assert.Contains("[SourceKind] = 'Edit'", source.Sql, StringComparison.Ordinal);
        Assert.Contains(header.GetCheckConstraints(), check => check.Name == "CK_ProductPackageStandardVersions_Identity");
        Assert.Single(context.Model.FindEntityType(typeof(ProductPackageStandardVersionRow))!.GetDeclaredQueryFilters());

        var line = design.FindEntityType(typeof(ProductPackageStandardLineRow))!;
        Assert.Equal(("product", "ProductPackageStandardLines"), (line.GetSchema(), line.GetTableName()));
        Assert.Equal(["CompanyId", "Version", "ProductId"], line.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var values = Assert.Single(line.GetCheckConstraints(), check => check.Name == "CK_ProductPackageStandardLines_Values");
        Assert.Contains("[PiecesPerPackage] > 0", values.Sql, StringComparison.Ordinal);
        Assert.Contains(line.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(ProductPackageStandardVersionRow)
            && key.DeleteBehavior == DeleteBehavior.Restrict);
        Assert.Single(context.Model.FindEntityType(typeof(ProductPackageStandardLineRow))!.GetDeclaredQueryFilters());

        var attempts = design.FindEntityType(typeof(ProductPackageStandardAttemptRow))!;
        Assert.Equal(("product", "ProductPackageStandardAttempts"), (attempts.GetSchema(), attempts.GetTableName()));
        Assert.Equal(["CompanyId", "CommandName", "IdempotencyKeyHash"],
            attempts.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Single(context.Model.FindEntityType(typeof(ProductPackageStandardAttemptRow))!.GetDeclaredQueryFilters());
    }

    [Fact]
    public void The_package_standard_migration_precedes_the_section_families_and_its_Down_refuses_first_with_51492()
    {
        using var context = Context();
        Assert.Equal(["AddProductPackageStandards", "AddProductSectionFamilies", "AddProductPackageStandardApprovalAndFiles"],
            context.Database.GetMigrations().TakeLast(3).Select(name => name.Split('_', 2)[1]));
        Assert.False(context.Database.HasPendingModelChanges());

        var migration = new AddProductPackageStandards();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 51492", guard, StringComparison.Ordinal);
        Assert.Contains("[product].[ProductPackageStandardVersions]", guard, StringComparison.Ordinal);
        Assert.Contains("[product].[ProductPackageStandardAttempts]", guard, StringComparison.Ordinal);
        Assert.Contains("is not rolled back (forward fix)", guard, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
        Assert.Equal(
            ["ProductPackageStandardAttempts", "ProductPackageStandardLines", "ProductPackageStandardVersions"],
            migration.UpOperations.OfType<CreateTableOperation>().Select(operation => operation.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_approve_permission_is_registered_once_company_wide_not_critical_and_seeded_to_nobody()
    {
        Assert.Equal("product.package-standards.approve", ProductPackageStandardPermissionsV3.Approve);
        Assert.Equal(ErpPermissions.ProductPackageStandardsApprove, ProductPackageStandardPermissionsV3.Approve);
        Assert.True(ErpPermissions.IsKnown(ProductPackageStandardPermissionsV3.Approve));
        Assert.Single(ErpPermissions.All, name => name == ProductPackageStandardPermissionsV3.Approve);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(ProductPackageStandardPermissionsV3.Approve));
        Assert.False(ErpPermissions.IsCritical(ProductPackageStandardPermissionsV3.Approve));
        var seed = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "backend", "GoymenERP.Api", "Seed", "identity-seed.json"));
        Assert.DoesNotContain(ProductPackageStandardPermissionsV3.Approve, seed, StringComparison.Ordinal);
        Assert.False(ProductPackageStandardApprovalOptionsV3.Closed.PreparerMayDecide);
    }

    [Fact]
    public void The_override_decision_and_kept_file_tables_are_company_scoped_insert_only_and_mapped_with_their_checks()
    {
        using var context = Context();
        var design = context.GetService<IDesignTimeModel>().Model;

        var header = design.FindEntityType(typeof(ProductPackageStandardVersionRow))!;
        Assert.True(header.FindProperty(nameof(ProductPackageStandardVersionRow.EditedCustomerId))!.IsNullable);
        Assert.Contains(header.GetCheckConstraints(), check => check.Name == "CK_ProductPackageStandardVersions_Approval");

        var customerLine = design.FindEntityType(typeof(ProductPackageStandardCustomerLineRow))!;
        Assert.Equal(("product", "ProductPackageStandardCustomerLines"), (customerLine.GetSchema(), customerLine.GetTableName()));
        Assert.Equal(["CompanyId", "Version", "CustomerId", "ProductId"],
            customerLine.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var values = Assert.Single(customerLine.GetCheckConstraints(),
            check => check.Name == "CK_ProductPackageStandardCustomerLines_Values");
        Assert.Contains("[PiecesPerPackage] > 0", values.Sql, StringComparison.Ordinal);
        Assert.Contains(customerLine.GetForeignKeys(), key =>
            key.PrincipalEntityType.ClrType == typeof(ProductPackageStandardVersionRow) && key.DeleteBehavior == DeleteBehavior.Restrict);
        Assert.Single(context.Model.FindEntityType(typeof(ProductPackageStandardCustomerLineRow))!.GetDeclaredQueryFilters());

        var decision = design.FindEntityType(typeof(ProductPackageStandardDecisionRow))!;
        Assert.Equal(("product", "ProductPackageStandardDecisions"), (decision.GetSchema(), decision.GetTableName()));
        Assert.Equal(["CompanyId", "Version"], decision.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var decided = Assert.Single(decision.GetCheckConstraints(), check => check.Name == "CK_ProductPackageStandardDecisions_Decision");
        Assert.Contains("'Approved'", decided.Sql, StringComparison.Ordinal);
        Assert.Contains("'Rejected'", decided.Sql, StringComparison.Ordinal);
        Assert.Contains(decision.GetForeignKeys(), key =>
            key.PrincipalEntityType.ClrType == typeof(ProductPackageStandardVersionRow) && key.DeleteBehavior == DeleteBehavior.Restrict);
        Assert.Single(context.Model.FindEntityType(typeof(ProductPackageStandardDecisionRow))!.GetDeclaredQueryFilters());

        var file = design.FindEntityType(typeof(ProductPackageStandardImportFileRow))!;
        Assert.Equal(("product", "ProductPackageStandardImportFiles"), (file.GetSchema(), file.GetTableName()));
        Assert.Equal(["CompanyId", "ImportId"], file.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var outcome = Assert.Single(file.GetCheckConstraints(), check => check.Name == "CK_ProductPackageStandardImportFiles_Outcome");
        Assert.Contains("'Accepted'", outcome.Sql, StringComparison.Ordinal);
        Assert.Contains("'Refused'", outcome.Sql, StringComparison.Ordinal);
        Assert.Contains(file.GetCheckConstraints(), check => check.Name == "CK_ProductPackageStandardImportFiles_File");
        Assert.Single(context.Model.FindEntityType(typeof(ProductPackageStandardImportFileRow))!.GetDeclaredQueryFilters());
    }

    [Fact]
    public void The_approval_migration_is_the_last_one_and_keeps_files_with_a_trigger_and_its_Down_refuses_first_with_51872()
    {
        using var context = Context();
        Assert.Equal("AddProductPackageStandardApprovalAndFiles",
            context.Database.GetMigrations().Last().Split('_', 2)[1]);

        var migration = new AddProductPackageStandardApprovalAndFiles();
        var trigger = Assert.Single(migration.UpOperations.OfType<SqlOperation>()).Sql;
        Assert.Contains("TR_ProductPackageStandardImportFiles_Guard", trigger, StringComparison.Ordinal);
        Assert.Contains("AFTER UPDATE, DELETE", trigger, StringComparison.Ordinal);
        Assert.Contains("THROW 51871", trigger, StringComparison.Ordinal);
        Assert.Equal(
            ["ProductPackageStandardCustomerLines", "ProductPackageStandardDecisions", "ProductPackageStandardImportFiles"],
            migration.UpOperations.OfType<CreateTableOperation>().Select(operation => operation.Name).Order(StringComparer.Ordinal));

        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 51872", guard, StringComparison.Ordinal);
        Assert.Contains("[product].[ProductPackageStandardImportFiles]", guard, StringComparison.Ordinal);
        Assert.Contains("[product].[ProductPackageStandardDecisions]", guard, StringComparison.Ordinal);
        Assert.Contains("[product].[ProductPackageStandardCustomerLines]", guard, StringComparison.Ordinal);
        Assert.Contains("is not rolled back (forward fix)", guard, StringComparison.Ordinal);
        Assert.Equal(2, migration.DownOperations.OfType<SqlOperation>().Count());
    }

    [Fact]
    public async Task The_approval_surface_answers_its_fences_and_malformed_input_before_anything_is_read()
    {
        var anonymous = Service(new FakeUser(null), new FakeAuthority(granted: true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => anonymous.GetApprovalStateAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => anonymous.ListImportsAsync());

        // The approver's read and decisions measure the approve name; the preparer's surface measures the manage name.
        var denied = new FakeAuthority(granted: false);
        var ungranted = Service(new FakeUser("user-1"), denied);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ungranted.GetApprovalStateForApproverAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            ungranted.ApproveAsync(2, new ApproveProductPackageStandardVersionRequestV3("2026-11-16", "SENTETIK"), Metadata()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            ungranted.RejectAsync(2, new RejectProductPackageStandardVersionRequestV3("SENTETIK"), Metadata()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ungranted.GetImportReportAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ungranted.SetCustomerAsync(Guid.NewGuid(), Guid.NewGuid(),
            new SetProductPackageStandardRequestV2("0", 6, "SENTETIK"), Metadata()));
        Assert.Equal(
            [
                ProductPackageStandardPermissionsV3.Approve, ProductPackageStandardPermissionsV3.Approve,
                ProductPackageStandardPermissionsV3.Approve, ProductPackageStandardPermissionsV2.Manage,
                ProductPackageStandardPermissionsV2.Manage,
            ],
            denied.Asked);

        var service = Service(new FakeUser("user-1"), new FakeAuthority(granted: true));
        await AssertDecisionUnstored(() => service.ApproveAsync(0,
            new ApproveProductPackageStandardVersionRequestV3("2026-11-16", "SENTETIK"), Metadata()),
            ProductPackageStandardProblemCodesV2.RequestInvalid);
        await AssertDecisionUnstored(() => service.ApproveAsync(2,
            new ApproveProductPackageStandardVersionRequestV3(null, "SENTETIK"), Metadata()),
            ProductPackageStandardProblemCodesV3.EffectiveDateInvalid);
        await AssertDecisionUnstored(() => service.ApproveAsync(2,
            new ApproveProductPackageStandardVersionRequestV3("16.11.2026", "SENTETIK"), Metadata()),
            ProductPackageStandardProblemCodesV3.EffectiveDateInvalid);
        await AssertDecisionUnstored(() => service.ApproveAsync(2,
            new ApproveProductPackageStandardVersionRequestV3("2026-11-16", " "), Metadata()),
            ProductPackageStandardProblemCodesV2.ReasonRequired);
        await AssertDecisionUnstored(() => service.RejectAsync(-1,
            new RejectProductPackageStandardVersionRequestV3("SENTETIK"), Metadata()),
            ProductPackageStandardProblemCodesV2.RequestInvalid);
        await AssertDecisionUnstored(() => service.RejectAsync(2,
            new RejectProductPackageStandardVersionRequestV3(null), Metadata()),
            ProductPackageStandardProblemCodesV2.ReasonRequired);
        await AssertDecisionUnstored(() => service.SetCustomerAsync(Guid.Empty, Guid.NewGuid(),
            new SetProductPackageStandardRequestV2("0", 6, "SENTETIK"), Metadata()),
            ProductPackageStandardProblemCodesV2.RequestInvalid);
        await AssertDecisionUnstored(() => service.SetCustomerAsync(Guid.NewGuid(), Guid.NewGuid(),
            new SetProductPackageStandardRequestV2("0", 0, "SENTETIK"), Metadata()),
            ProductPackageStandardProblemCodesV2.PiecesInvalid);

        // The empty import identity is answered as absent without a read.
        Assert.Null(await service.GetImportReportAsync(Guid.Empty));
    }

    [Fact]
    public void A_V3_read_without_a_value_carries_no_version_source_scope_or_date()
    {
        foreach (var outcome in Enum.GetValues<ProductPackageStandardReadOutcomeV2>()
                     .Where(outcome => outcome != ProductPackageStandardReadOutcomeV2.Found))
        {
            var read = ProductPackageStandardReadV3.Without(outcome);
            Assert.Equal((outcome, (int?)null, (long?)null, (Guid?)null, (string?)null, (string?)null, (string?)null,
                    (DateOnly?)null),
                (read.Outcome, read.PiecesPerPackage, read.StandardVersion, read.StandardVersionId, read.SourceKind,
                    read.SourceLabel, read.EntryScope, read.EffectiveFrom));
        }
    }

    [Fact]
    public async Task The_write_guard_keeps_versions_entries_and_answers_insert_only_and_in_the_acting_company()
    {
        await using (var context = Context())
        {
            var header = Version(CompanyA);
            context.Attach(header);
            context.Entry(header).Property(x => x.Reason).IsModified = true;
            header.Reason = "SENTETIK BAŞKA";
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await using (var context = Context())
        {
            context.Attach(Line(CompanyA));
            context.Remove(context.ChangeTracker.Entries<ProductPackageStandardLineRow>().Single().Entity);
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await using (var context = Context())
        {
            var line = Line(CompanyA);
            context.Attach(line);
            context.Entry(line).Property(x => x.PiecesPerPackage).IsModified = true;
            line.PiecesPerPackage = 9;
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await using (var context = Context())
        {
            context.Add(Version(CompanyB));
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await using (var context = Context())
        {
            var file = new ProductPackageStandardImportFileRow
            {
                CompanyId = CompanyA, ImportId = Guid.NewGuid(), FileName = "paket.xlsx", FileSha256 = new string('a', 64),
                FileSizeBytes = 1, Content = [1], Outcome = ProductPackageStandardImportOutcomesV3.Refused, ReportJson = "{}",
                SourceLabel = "SENTETIK", Reason = "SENTETIK", CreatedAtUtc = DateTimeOffset.UnixEpoch, CreatedBy = "user-1",
                CorrelationId = "corr-1",
            };
            context.Attach(file);
            context.Remove(file);
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await using (var context = Context())
        {
            var decision = new ProductPackageStandardDecisionRow
            {
                CompanyId = CompanyA, Version = 2, DecisionId = Guid.NewGuid(), Decision = ProductPackageStandardDecisionsV3.Approved,
                EffectiveFrom = new DateOnly(2026, 11, 16), Reason = "SENTETIK", DecidedAtUtc = DateTimeOffset.UnixEpoch,
                DecidedBy = "user-2", CorrelationId = "corr-1",
            };
            context.Attach(decision);
            context.Entry(decision).Property(x => x.Reason).IsModified = true;
            decision.Reason = "SENTETIK BAŞKA";
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task The_fences_and_malformed_input_answer_before_anything_is_read_and_store_nothing()
    {
        var anonymous = Service(new FakeUser(null), new FakeAuthority(granted: true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => anonymous.GetCurrentAsync());

        var denied = new FakeAuthority(granted: false);
        var ungranted = Service(new FakeUser("user-1"), denied);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ungranted.ImportAsync(Request(), Metadata()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            ungranted.SetAsync(Guid.NewGuid(), new SetProductPackageStandardRequestV2("0", 6, "SENTETIK"), Metadata()));
        Assert.Equal([ProductPackageStandardPermissionsV2.Manage, ProductPackageStandardPermissionsV2.Manage], denied.Asked);

        var branch = Service(new FakeUser("user-1"), new FakeAuthority(granted: true, companyWide: false));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => branch.GetCurrentAsync());

        var otherCompany = Service(new FakeUser("user-1", CompanyB), new FakeAuthority(granted: true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => otherCompany.GetCurrentAsync());

        var service = Service(new FakeUser("user-1"), new FakeAuthority(granted: true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(Request(), Metadata() with { ActorId = "user-2" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(Request(), Metadata() with { CompanyId = CompanyB }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(Request(), Metadata() with { IdempotencyKey = " " }));

        await AssertUnstored(service, Request() with { ExpectedVersion = "01" }, ProductPackageStandardProblemCodesV2.RequestInvalid);
        await AssertUnstored(service, Request() with { ExpectedVersion = "-1" }, ProductPackageStandardProblemCodesV2.RequestInvalid);
        await AssertUnstored(service, Request() with { ExpectedVersion = null }, ProductPackageStandardProblemCodesV2.RequestInvalid);
        await AssertUnstored(service, Request() with { SourceLabel = "  " }, ProductPackageStandardProblemCodesV2.SourceLabelRequired);
        await AssertUnstored(service, Request() with
        {
            SourceLabel = new string('k', ProductPackageStandardInputLimitsV2.SourceLabelMaximumLength + 1),
        }, ProductPackageStandardProblemCodesV2.SourceLabelTooLong);
        await AssertUnstored(service, Request() with { Reason = null }, ProductPackageStandardProblemCodesV2.ReasonRequired);
        await AssertUnstored(service, Request() with
        {
            Reason = new string('x', ProductPackageStandardInputLimitsV2.ReasonMaximumLength + 1),
        }, ProductPackageStandardProblemCodesV2.ReasonTooLong);
        await AssertUnstored(service, Request() with { FileName = "paket.csv" }, ProductPackageStandardProblemCodesV2.FileInvalid);
        await AssertUnstored(service, Request() with { FileName = "../paket.xlsx" }, ProductPackageStandardProblemCodesV2.FileInvalid);
        await AssertUnstored(service, Request() with { FileName = "C:paket.xlsx" }, ProductPackageStandardProblemCodesV2.FileInvalid);
        await AssertUnstored(service, Request() with { FileName = ".xlsx" }, ProductPackageStandardProblemCodesV2.FileInvalid);
        await AssertUnstored(service, Request() with { ContentBase64 = "" }, ProductPackageStandardProblemCodesV2.FileInvalid);
        await AssertUnstored(service, Request() with { ContentBase64 = "bm90IGJhc2U2NA=!" },
            ProductPackageStandardProblemCodesV2.FileInvalid);
        await AssertUnstored(service, Request() with
        {
            ContentBase64 = Convert.ToBase64String(new byte[ProductPackageStandardInputLimitsV2.MaximumFileBytes + 1]),
        }, ProductPackageStandardProblemCodesV2.FileInvalid);

        // SAT-D383: a file that reaches the row check (a row error included) is kept with its report, so it needs the
        // database; the SQL acceptance tests prove that refusal and its kept file.
        await Assert.ThrowsAnyAsync<Exception>(() => service.ImportAsync(Request() with
        {
            ContentBase64 = Convert.ToBase64String(Workbook(
                Row(1, Text("A", CodeHeader), Text("B", PiecesHeader)),
                Row(2, Text("A", "SNT-001"), Number("B", "0")))),
        }, Metadata()));

        await AssertSetUnstored(service, Guid.Empty, new SetProductPackageStandardRequestV2("0", 6, "SENTETIK"),
            ProductPackageStandardProblemCodesV2.RequestInvalid);
        await AssertSetUnstored(service, Guid.NewGuid(), new SetProductPackageStandardRequestV2("1.0", 6, "SENTETIK"),
            ProductPackageStandardProblemCodesV2.RequestInvalid);
        await AssertSetUnstored(service, Guid.NewGuid(), new SetProductPackageStandardRequestV2("0", 0, "SENTETIK"),
            ProductPackageStandardProblemCodesV2.PiecesInvalid);
        await AssertSetUnstored(service, Guid.NewGuid(), new SetProductPackageStandardRequestV2("0", -3, "SENTETIK"),
            ProductPackageStandardProblemCodesV2.PiecesInvalid);
        await AssertSetUnstored(service, Guid.NewGuid(), new SetProductPackageStandardRequestV2("0", null, " "),
            ProductPackageStandardProblemCodesV2.ReasonRequired);
    }

    [Fact]
    public async Task The_read_port_answers_denied_without_an_actor_and_not_found_for_an_empty_product_without_reading()
    {
        IProductPackageStandardReaderV2 anonymous = Service(new FakeUser(null), new FakeAuthority(granted: true));
        Assert.Equal(ProductPackageStandardReadV2.Without(ProductPackageStandardReadOutcomeV2.Denied),
            await anonymous.GetAsync(Guid.NewGuid()));

        IProductPackageStandardReaderV2 otherCompany = Service(new FakeUser("user-1", CompanyB), new FakeAuthority(granted: true));
        Assert.Equal(ProductPackageStandardReadOutcomeV2.Denied, (await otherCompany.GetAsync(Guid.NewGuid())).Outcome);

        // The read port measures no permission: an actor without the manage grant still reads (reference data).
        var denied = new FakeAuthority(granted: false);
        IProductPackageStandardReaderV2 member = Service(new FakeUser("user-1"), denied);
        Assert.Equal(ProductPackageStandardReadV2.Without(ProductPackageStandardReadOutcomeV2.NotFound),
            await member.GetAsync(Guid.Empty));
        Assert.Empty(denied.Asked);

        // A storage failure is Unavailable, never an exception (the fake server is unreachable).
        Assert.Equal(ProductPackageStandardReadOutcomeV2.Unavailable, (await member.GetAsync(Guid.NewGuid())).Outcome);
    }

    [Fact]
    public void A_read_without_a_value_carries_no_version_or_source()
    {
        foreach (var outcome in Enum.GetValues<ProductPackageStandardReadOutcomeV2>()
                     .Where(outcome => outcome != ProductPackageStandardReadOutcomeV2.Found))
        {
            var read = ProductPackageStandardReadV2.Without(outcome);
            Assert.Equal((outcome, (int?)null, (long?)null, (Guid?)null, (string?)null, (string?)null),
                (read.Outcome, read.PiecesPerPackage, read.StandardVersion, read.StandardVersionId, read.SourceKind,
                    read.SourceLabel));
        }

        Assert.Equal(3, (int)ProductPackageStandardReadOutcomeV2.NoSource);
    }

    private static async Task AssertUnstored(ProductPackageStandardService service, ImportProductPackageStandardsRequestV2 request,
        string code)
    {
        // The fake database has no reachable server: an answer here proves nothing was read or stored first.
        var result = await service.ImportAsync(request, Metadata());
        Assert.Equal((400, code, false, "corr-1"),
            (result.Answer.StatusCode, result.Answer.Code, result.IsReplay, result.OriginalCorrelationId));
    }

    private static async Task AssertDecisionUnstored(Func<Task<ProductPackageStandardResultV2>> command, string code)
    {
        var result = await command();
        Assert.Equal((400, code, false, "corr-1"),
            (result.Answer.StatusCode, result.Answer.Code, result.IsReplay, result.OriginalCorrelationId));
    }

    private static async Task AssertSetUnstored(ProductPackageStandardService service, Guid productId,
        SetProductPackageStandardRequestV2 request, string code)
    {
        var result = await service.SetAsync(productId, request, Metadata());
        Assert.Equal((400, code, false, "corr-1"),
            (result.Answer.StatusCode, result.Answer.Code, result.IsReplay, result.OriginalCorrelationId));
    }

    private static ImportProductPackageStandardsRequestV2 Request() =>
        new("0", "SENTETIK PAKET LİSTESİ", "SENTETIK gerekçe", "paket.xlsx", Convert.ToBase64String(Workbook(
            Row(1, Text("A", CodeHeader), Text("B", PiecesHeader)),
            Row(2, Text("A", "SNT-001"), Number("B", "12")))));

    private static ProductPackageStandardCommandMetadataV2 Metadata() =>
        new(CompanyA, "user-1", "corr-1", null, "Web", null, null, "key-1");

    private static ProductPackageStandardVersionRow Version(Guid company) => new()
    {
        CompanyId = company, Version = 1, VersionId = Guid.NewGuid(), SourceKind = ProductPackageStandardSourceKindsV2.Import,
        SourceLabel = "SENTETIK PAKET LİSTESİ", Reason = "SENTETIK", FileName = "paket.xlsx", FileSha256 = new string('a', 64),
        FileSizeBytes = 10, EntryCount = 1, CreatedAtUtc = DateTimeOffset.UnixEpoch, CreatedBy = "user-1", CorrelationId = "corr-1",
    };

    private static ProductPackageStandardLineRow Line(Guid company) => new()
    {
        CompanyId = company, Version = 1, ProductId = Guid.NewGuid(), PiecesPerPackage = 12,
    };

    // A minimal .xlsx package: workbook, its relationships, one worksheet and a shared string table holding "SNT-001".
    private static byte[] Workbook(params string[] rows)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Entry(archive, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                + "<Default Extension=\"xml\" ContentType=\"application/xml\"/></Types>");
            Entry(archive, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" "
                + "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>"
                + "<sheet name=\"Paket\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"Diğer\" sheetId=\"2\" r:id=\"rId2\"/></sheets></workbook>");
            Entry(archive, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/>"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>"
                + "</Relationships>");
            Entry(archive, "xl/sharedStrings.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                + "<si><t>SNT-001</t></si></sst>");
            Entry(archive, "xl/worksheets/sheet1.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                + "<sheetData>" + string.Concat(rows) + "</sheetData></worksheet>");
            // The second sheet is never read.
            Entry(archive, "xl/worksheets/sheet2.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                + "<sheetData>" + Row(1, Text("A", "Başka")) + "</sheetData></worksheet>");
        }

        return stream.ToArray();
    }

    private static void Entry(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string Row(int number, params string[] cells) => cells.Length == 0
        ? $"<row r=\"{number}\"/>"
        : $"<row r=\"{number}\">" + string.Concat(cells.Select(cell => cell.Replace("{row}", number.ToString(
            System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))) + "</row>";

    private static string Text(string column, string value) =>
        $"<c r=\"{column}{{row}}\" t=\"inlineStr\"><is><t>{SecurityElement.Escape(value)}</t></is></c>";

    private static string Number(string column, string value) => $"<c r=\"{column}{{row}}\"><v>{SecurityElement.Escape(value)}</v></c>";

    private static string Shared(string column, int index) => $"<c r=\"{column}{{row}}\" t=\"s\"><v>{index}</v></c>";

    private static ProductPackageStandardService Service(ICurrentUser user, IErpAuthorityRevalidationService authority)
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        var context = new ProductPriceListDbContext(new DbContextOptionsBuilder<ProductPriceListDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
        return new ProductPackageStandardService(context, company, user, authority, new NoAudit(), TimeProvider.System);
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
}
