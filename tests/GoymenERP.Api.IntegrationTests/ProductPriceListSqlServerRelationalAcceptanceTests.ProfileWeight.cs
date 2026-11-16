using System.Text;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;
using GoymenERP.Modules.Product.Infrastructure.PriceLists.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// URN-D13…D16 (STK-S37R) on a REAL SQL Server: the company's kg/m table is imported from a source-labelled file only while the
/// vault keeps it (fail-closed otherwise, nothing stored and the same key retryable), as an insert-only version with its rows,
/// one attempt and one audit event; a replay changes nothing and a stale version is refused. A profile card takes its kg/m from
/// that table (source label and version in the view) and a profile the table does not hold is refused. The database holds the
/// row shapes and one row per measure pair, and the forward-fix rollback of <see cref="AddProductProfileWeightImports"/> passes on
/// an empty store and refuses (51483) while an import or a profile card without a stored kg/m exists. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class ProductPriceListSqlServerRelationalAcceptanceTests
{
    private const int ProfileWeightRollbackRefusal = 51483;
    private const int UniqueIndexViolation = 2601;
    private const string ProfileWeightFile =
        ProductProfileWeightFileFormatV1.Header + "\nProfile;40;20;;2;1.72\nRoundTube;;;48.3;2.6;2.93\n";

    [SqlServerFact]
    public async Task The_profile_weight_migrations_Down_passes_on_an_empty_store_and_refuses_while_a_profile_card_or_an_import_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddProductProfileWeightImports().DownOperations[0]).Sql;
        var company = TestCompanyIds.CompanyA;

        await using (var scope = run.Request())
        {
            await scope.Context.Database.ExecuteSqlRawAsync(guard);

            // URN-D16: the card's profile branch no longer needs a stored kg/m, which the old check could not hold after a rollback.
            await scope.Context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO product.ProductCardDimensions (ProductId, SectionKind, ThicknessMm, WidthMm, LengthMm, HeightMm,
                    OuterDiameterMm, WallThicknessMm, KgPerMetre, Version, UpdatedAtUtc, UpdatedBy, UpdatedInCompanyId)
                VALUES ({ProductId}, N'Profile', NULL, 40.0, NULL, 20.0, NULL, 2.0, NULL, 1, SYSDATETIMEOFFSET(), N'sql', {company})
                """);

            var refusal = await Assert.ThrowsAsync<SqlException>(() => scope.Context.Database.ExecuteSqlRawAsync(guard));
            Assert.Equal(ProfileWeightRollbackRefusal, refusal.Number);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM product.ProductCardDimensions WHERE ProductId = @id AND KgPerMetre IS NULL", ProductId));
    }

    [SqlServerFact]
    public async Task The_kg_per_metre_table_is_imported_only_through_the_vault_and_a_profile_card_takes_its_weight_from_it()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddProductProfileWeightImports().DownOperations[0]).Sql;
        var vault = new SwitchableVault();

        // Fail-closed: no vault answer, no import, no stored answer; the same key is retried once the vault answers.
        await using (var scope = run.Request())
        {
            var unavailable = await WeightService(scope, run, vault).ImportAsync(ImportRequest("0"), WeightMetadata(run, "import-1"));
            Assert.Equal((503, ProductProfileWeightProblemCodesV1.FileVaultUnavailable),
                (unavailable.Answer.StatusCode, unavailable.Answer.Code));
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductProfileWeightImports"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductProfileWeightAttempts"));

        vault.Outcome = ProductProfileWeightFileStoreOutcomeV1.Stored;
        await using (var scope = run.Request())
        {
            var service = WeightService(scope, run, vault);
            var imported = await service.ImportAsync(ImportRequest("0"), WeightMetadata(run, "import-1"));
            Assert.Equal((204, false), (imported.Answer.StatusCode, imported.IsReplay));
            var replay = await service.ImportAsync(ImportRequest("0"), WeightMetadata(run, "import-1") with { CorrelationId = "retry" });
            Assert.True(replay.IsReplay);
            Assert.Equal("corr-import-1", replay.OriginalCorrelationId);
            var stale = await service.ImportAsync(ImportRequest("0"), WeightMetadata(run, "import-2"));
            Assert.Equal((409, ProductProfileWeightProblemCodesV1.VersionConflict), (stale.Answer.StatusCode, stale.Answer.Code));

            var table = await service.GetCurrentAsync();
            Assert.Equal(("1", "SENTETIK KATALOG", "katalog.csv", 2), (table.Version, table.SourceLabel, table.FileName, table.Rows.Count));
            Assert.Equal(vault.DocumentId, table.DocumentId);
            Assert.Equal(new ProductProfileWeightRowV1("Profile", 40m, 20m, null, 2m, 1.72m), table.Rows[0]);
        }

        Assert.Single(vault.Stored);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductProfileWeightImports"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductProfileWeightLines"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Product.ProfileWeightTable' AND EntityId = @id",
            vault.Stored[0].ImportId.ToString("D")));

        // The database holds the row shape and one row per measure pair of a version.
        await using (var scope = run.Request())
        {
            var solid = await Assert.ThrowsAsync<SqlException>(() => scope.Context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO product.ProductProfileWeightLines (CompanyId, ImportVersion, LineNo, SectionKind, WidthMm, HeightMm,
                    OuterDiameterMm, WallThicknessMm, KgPerMetre)
                VALUES ({TestCompanyIds.CompanyA}, 1, 3, 'Profile', 40.0, 3.0, NULL, 2.0, 1.0)
                """));
            Assert.Equal(CheckConstraintViolation, solid.Number);
            var twice = await Assert.ThrowsAsync<SqlException>(() => scope.Context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO product.ProductProfileWeightLines (CompanyId, ImportVersion, LineNo, SectionKind, WidthMm, HeightMm,
                    OuterDiameterMm, WallThicknessMm, KgPerMetre)
                VALUES ({TestCompanyIds.CompanyA}, 1, 3, 'RoundTube', NULL, NULL, 48.3, 2.6, 3.0)
                """));
            Assert.Equal(UniqueIndexViolation, twice.Number);
        }

        // The card: the table's kg/m with its source and version; a profile the table does not hold is refused.
        await using (var scope = run.Request())
        {
            var dimensions = DimensionService(scope, run);
            var missing = await dimensions.SetAsync(ProductId, ProfileRequest(30m, 20m, 2m), DimensionMetadata(run, "dimension-1"));
            Assert.Equal((409, ProductDimensionProblemCodesV1.WeightSourceMissing), (missing.Answer.StatusCode, missing.Answer.Code));

            var set = await dimensions.SetAsync(ProductId, ProfileRequest(40m, 20m, 2m), DimensionMetadata(run, "dimension-2"));
            Assert.Equal(204, set.Answer.StatusCode);
            var view = await dimensions.GetAsync(ProductId);
            Assert.Equal((1.72m, "SENTETIK KATALOG", "1"), (view.KgPerMetre, view.KgPerMetreSourceLabel, view.KgPerMetreTableVersion));
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM product.ProductCardDimensions WHERE ProductId = @id AND KgPerMetre IS NULL", ProductId));

        await using (var scope = run.Request())
        {
            var refusal = await Assert.ThrowsAsync<SqlException>(() => scope.Context.Database.ExecuteSqlRawAsync(guard));
            Assert.Equal(ProfileWeightRollbackRefusal, refusal.Number);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM product.ProductProfileWeightImports"));
    }

    [SqlServerFact]
    public async Task A_profile_finds_its_kg_per_metre_with_width_and_height_swapped_and_a_two_direction_table_conflict_is_refused()
    {
        await using var run = await Run.StartAsync();
        var vault = new SwitchableVault { Outcome = ProductProfileWeightFileStoreOutcomeV1.Stored };

        // URN-D19: 40×20 stands once; 60×40 and 40×60 (same wall) stand with different kg/m — a table conflict.
        var file = ProductProfileWeightFileFormatV1.Header
            + "\nProfile;40;20;;2;1.72\nProfile;60;40;;3;2.5\nProfile;40;60;;3;2.6\n";
        await using (var scope = run.Request())
        {
            var imported = await WeightService(scope, run, vault).ImportAsync(ImportRequest("0", file),
                WeightMetadata(run, "import-swap"));
            Assert.Equal(204, imported.Answer.StatusCode);
        }

        await using (var scope = run.Request())
        {
            var dimensions = DimensionService(scope, run);
            var swapped = await dimensions.SetAsync(ProductId, ProfileRequest(20m, 40m, 2m), DimensionMetadata(run, "swap-1"));
            Assert.Equal(204, swapped.Answer.StatusCode);
            var view = await dimensions.GetAsync(ProductId);
            Assert.Equal((20m, 40m, 1.72m, "SENTETIK KATALOG", "1"),
                (view.WidthMm, view.HeightMm, view.KgPerMetre, view.KgPerMetreSourceLabel, view.KgPerMetreTableVersion));

            var conflict = await dimensions.SetAsync(ProductId, ProfileRequest(40m, 60m, 3m) with { ExpectedVersion = "1" },
                DimensionMetadata(run, "swap-2"));
            Assert.Equal((409, ProductDimensionProblemCodesV1.WeightSourceConflict),
                (conflict.Answer.StatusCode, conflict.Answer.Code));
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM product.ProductCardDimensions WHERE ProductId = @id AND WidthMm = 20.0 AND Version = 1",
            ProductId));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Product.Dimension' AND EntityId = @id",
            ProductId.ToString("D")));

        // A stored card whose measures fall into the conflict: the view shows no kg/m, the read port names the conflict.
        await using (var scope = run.Request())
        {
            await scope.Context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE product.ProductCardDimensions SET WidthMm = 40.0, HeightMm = 60.0, WallThicknessMm = 3.0 WHERE ProductId = {ProductId}");
        }

        await using (var scope = run.Request())
        {
            var dimensions = DimensionService(scope, run);
            var view = await dimensions.GetAsync(ProductId);
            Assert.Equal(((decimal?)null, (string?)null), (view.KgPerMetre, view.KgPerMetreSourceLabel));

            var read = await ((IProductDimensionReaderV1)dimensions).GetAsync(ProductId);
            Assert.Equal((ProductDimensionOutcomeV1.Found, (decimal?)null, true, "SENTETIK KATALOG", (long?)1),
                (read.Outcome, read.KgPerMetre, read.KgPerMetreConflict, read.KgPerMetreSourceLabel, read.KgPerMetreTableVersion));
        }
    }

    private static ProductProfileWeightTableService WeightService(RequestScope scope, Run run, IProductProfileWeightFileVaultV1 vault) =>
        new(scope.Context, scope.CompanyContext, new User(run.CreatorActorId), new Granted(), ScopeAudit(scope), vault,
            TimeProvider.System);

    private static ProductDimensionService DimensionService(RequestScope scope, Run run) =>
        new(scope.Context, scope.CompanyContext, new User(run.CreatorActorId), new Granted(), ScopeAudit(scope), TimeProvider.System);

    private static IAuditEventCollector ScopeAudit(RequestScope scope) => scope.Services.GetRequiredService<IAuditEventCollector>();

    private static ImportProductProfileWeightTableRequestV1 ImportRequest(string expectedVersion) =>
        ImportRequest(expectedVersion, ProfileWeightFile);

    private static ImportProductProfileWeightTableRequestV1 ImportRequest(string expectedVersion, string file) =>
        new(expectedVersion, " SENTETIK KATALOG ", "SENTETIK gerekçe", "katalog.csv",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(file)));

    private static ProductProfileWeightCommandMetadataV1 WeightMetadata(Run run, string key) =>
        new(TestCompanyIds.CompanyA, run.CreatorActorId.ToString("D"), "corr-" + key, null, "Web", null, null, key);

    private static SetProductDimensionRequestV1 ProfileRequest(decimal width, decimal height, decimal wall) =>
        new("0", ProductDimensionSectionKindsV1.Profile, null, width, null, height, null, wall, "SENTETIK gerekçe");

    private static ProductDimensionCommandMetadataV1 DimensionMetadata(Run run, string key) =>
        new(TestCompanyIds.CompanyA, run.CreatorActorId.ToString("D"), "corr-" + key, null, "Web", null, null, key);

    private sealed class SwitchableVault : IProductProfileWeightFileVaultV1
    {
        public ProductProfileWeightFileStoreOutcomeV1 Outcome { get; set; } = ProductProfileWeightFileStoreOutcomeV1.Unavailable;

        public Guid DocumentId { get; } = Guid.NewGuid();

        public List<ProductProfileWeightFileV1> Stored { get; } = [];

        public Task<ProductProfileWeightFileStoreResultV1> StoreAsync(ProductProfileWeightFileV1 file,
            CancellationToken cancellationToken = default)
        {
            if (Outcome != ProductProfileWeightFileStoreOutcomeV1.Stored)
            {
                return Task.FromResult(new ProductProfileWeightFileStoreResultV1(Outcome, null));
            }

            Stored.Add(file);
            return Task.FromResult(new ProductProfileWeightFileStoreResultV1(Outcome, DocumentId));
        }
    }
}
