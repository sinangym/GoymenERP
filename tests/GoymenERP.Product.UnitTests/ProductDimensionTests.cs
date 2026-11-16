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
/// BS-9 "Ebat/kalınlık" without a database: the permission name is registered (not granted, not Critical), the shared-card row
/// and its command answers are mapped as decided (product schema, no company filter on the shared row, a filter on the answers,
/// the shape and measure checks equal to the context constants, millimetre / kg-per-metre precision), the forward-fix rollback
/// refuses first, the write guard refuses before any connection, and the service answers its fences and malformed input before
/// any read. What SQL Server enforces is proven by <c>ProductPriceListSqlServerRelationalAcceptanceTests</c>. SENTETIK / NOT_ENV.
/// </summary>
public sealed class ProductDimensionTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("55555555-5555-4555-8555-555555555555");

    [Fact]
    public void The_dimension_permission_is_registered_once_company_wide_not_critical_and_seeded_to_nobody()
    {
        Assert.Equal("product.dimension.manage", ProductDimensionPermissionsV1.Manage);
        Assert.Equal(ErpPermissions.ProductDimensionManage, ProductDimensionPermissionsV1.Manage);
        Assert.True(ErpPermissions.IsKnown(ProductDimensionPermissionsV1.Manage));
        Assert.Single(ErpPermissions.All, name => name == ProductDimensionPermissionsV1.Manage);
        Assert.True(ErpPermissions.RequiresCompanyWideScope(ProductDimensionPermissionsV1.Manage));
        Assert.False(ErpPermissions.IsCritical(ProductDimensionPermissionsV1.Manage));
        var seed = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "backend", "GoymenERP.Api", "Seed", "identity-seed.json"));
        Assert.DoesNotContain(ProductDimensionPermissionsV1.Manage, seed, StringComparison.Ordinal);
    }

    [Fact]
    public void The_shared_row_and_its_answers_are_mapped_with_the_decided_checks_precision_and_filters()
    {
        using var context = Context();
        var design = context.GetService<IDesignTimeModel>().Model;
        var row = design.FindEntityType(typeof(ProductCardDimensionRow))!;
        Assert.Equal(("product", "ProductCardDimensions"), (row.GetSchema(), row.GetTableName()));
        Assert.Equal(["ProductId"], row.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Empty(context.Model.FindEntityType(typeof(ProductCardDimensionRow))!.GetDeclaredQueryFilters());
        Assert.Contains(row.GetCheckConstraints(), check => check.Name == "CK_ProductCardDimensions_Shape"
            && check.Sql == ProductPriceListDbContext.DimensionShapeCheck);
        Assert.Contains(row.GetCheckConstraints(), check => check.Name == "CK_ProductCardDimensions_Measures"
            && check.Sql == ProductPriceListDbContext.DimensionMeasureCheck);
        foreach (var millimetre in new[]
                 {
                     nameof(ProductCardDimensionRow.ThicknessMm), nameof(ProductCardDimensionRow.WidthMm),
                     nameof(ProductCardDimensionRow.LengthMm), nameof(ProductCardDimensionRow.HeightMm),
                     nameof(ProductCardDimensionRow.OuterDiameterMm), nameof(ProductCardDimensionRow.WallThicknessMm),
                 })
        {
            var property = row.FindProperty(millimetre)!;
            Assert.True(property.IsNullable);
            Assert.Equal((18, ProductDimensionInputLimitsV1.MillimetreDecimals), (property.GetPrecision(), property.GetScale()));
        }

        var kg = row.FindProperty(nameof(ProductCardDimensionRow.KgPerMetre))!;
        Assert.Equal((18, ProductDimensionInputLimitsV1.KgPerMetreDecimals), (kg.GetPrecision(), kg.GetScale()));
        Assert.True(row.FindProperty(nameof(ProductCardDimensionRow.Version))!.IsConcurrencyToken);
        Assert.Contains(row.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(PriceListProductReference));
        Assert.Contains(row.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(PriceListCompanyReference));

        var attempts = design.FindEntityType(typeof(ProductDimensionAttemptRow))!;
        Assert.Equal(("product", "ProductDimensionAttempts"), (attempts.GetSchema(), attempts.GetTableName()));
        Assert.Equal(["CompanyId", "CommandName", "IdempotencyKeyHash"],
            attempts.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Single(context.Model.FindEntityType(typeof(ProductDimensionAttemptRow))!.GetDeclaredQueryFilters());
    }

    [Fact]
    public void The_dimension_migration_precedes_the_kg_per_metre_table_and_its_Down_refuses_first_with_51290()
    {
        using var context = Context();
        Assert.Equal(["AddProductCardDimensions", "AddProductProfileWeightImports", "AddProductPackageStandards"],
            context.Database.GetMigrations().TakeLast(3).Select(name => name.Split('_', 2)[1]));
        Assert.False(context.Database.HasPendingModelChanges());

        var migration = new AddProductCardDimensions();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation);
        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]).Sql;
        Assert.Contains("THROW 51290", guard, StringComparison.Ordinal);
        Assert.Contains("[product].[ProductCardDimensions]", guard, StringComparison.Ordinal);
        Assert.Contains("[product].[ProductDimensionAttempts]", guard, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations, operation => operation is SqlOperation);
        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>(),
            operation => operation.Name == "ProductCardDimensions");
        Assert.Contains(table.CheckConstraints, check => check.Name == "CK_ProductCardDimensions_Shape");
    }

    [Fact]
    public async Task The_write_guard_refuses_a_delete_and_a_write_in_the_name_of_another_company()
    {
        await using (var context = Context())
        {
            context.Attach(Row(CompanyA));
            context.Remove(context.ChangeTracker.Entries<ProductCardDimensionRow>().Single().Entity);
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await using (var context = Context())
        {
            context.Add(Row(CompanyB));
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await using (var context = Context())
        {
            var row = Row(CompanyA);
            context.Attach(row);
            context.Entry(row).Property(x => x.UpdatedInCompanyId).IsModified = true;
            row.UpdatedInCompanyId = CompanyB;
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }
    }

    [Theory]
    [InlineData("Sheet", "2", "1250", "2500", null, null, null, true)]
    [InlineData("Coil", "0.5", "1000", null, null, null, null, true)]
    [InlineData("RoundTube", null, null, null, null, "48.3", "2.6", true)]
    [InlineData("RoundTube", null, null, null, null, "10", "5", true)]
    // URN-D16: a profile's kg/m is never on the request; its shape alone is accepted here and the table decides the weight.
    [InlineData("Profile", null, "40", null, "20", null, "2", true)]
    [InlineData("Profile", null, "40", null, "40", null, "20", true)]
    // Unknown kind, wrong case, missing / extra measures, zero, negative, over-scale, walls that do not fit.
    [InlineData("sheet", "2", "1250", "2500", null, null, null, false)]
    [InlineData("Plate", "2", "1250", "2500", null, null, null, false)]
    [InlineData(null, "2", "1250", "2500", null, null, null, false)]
    [InlineData("Sheet", "2", "1250", null, null, null, null, false)]
    [InlineData("Coil", "0.5", "1000", "2000", null, null, null, false)]
    [InlineData("Coil", "0", "1000", null, null, null, null, false)]
    [InlineData("Coil", "-1", "1000", null, null, null, null, false)]
    [InlineData("Coil", "0.5005", "1000", null, null, null, null, false)]
    [InlineData("RoundTube", null, null, null, null, "10", "5.001", false)]
    [InlineData("RoundTube", null, null, null, null, "48.3", null, false)]
    [InlineData("Profile", null, "40", null, "3", null, "2", false)]
    [InlineData("Profile", "2", "40", null, "20", null, "2", false)]
    [InlineData("Profile", null, "40", null, null, null, "2", false)]
    public void The_shape_accepts_exactly_the_measures_of_the_section_kind_and_never_rounds(string? kind, string? thickness,
        string? width, string? length, string? height, string? diameter, string? wall, bool accepted)
    {
        var request = new SetProductDimensionRequestV1("0", kind, D(thickness), D(width), D(length), D(height), D(diameter), D(wall),
            "SENTETIK gerekçe");

        var refusal = ProductDimensionShape.Refusal(request, out var measures);

        Assert.Equal(accepted, refusal is null);
        Assert.Equal(accepted, measures is not null);
    }

    [Fact]
    public void Four_section_kinds_are_offered_and_the_request_carries_no_kg_per_metre()
    {
        Assert.Equal(["Sheet", "Coil", "RoundTube", "Profile"], ProductDimensionSectionKindsV1.All);
        Assert.DoesNotContain(typeof(SetProductDimensionRequestV1).GetProperties(),
            property => property.Name.Contains("Kg", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_canonical_measures_do_not_depend_on_trailing_zeros()
    {
        var typed = new SetProductDimensionRequestV1("0", ProductDimensionSectionKindsV1.Coil, 1.500m, 1000.000m, null, null, null,
            null, "r");
        var plain = typed with { ThicknessMm = 1.5m, WidthMm = 1000m };

        Assert.Null(ProductDimensionShape.Refusal(typed, out var first));
        Assert.Null(ProductDimensionShape.Refusal(plain, out var second));
        Assert.Equal(first!.ThicknessMm.ToString(), second!.ThicknessMm.ToString());
        Assert.Equal(first.WidthMm.ToString(), second.WidthMm.ToString());
    }

    [Fact]
    public async Task The_fences_and_malformed_input_answer_before_anything_is_read()
    {
        var anonymous = Service(new FakeUser(null), new FakeAuthority(granted: true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => anonymous.GetAsync(Guid.NewGuid()));

        var denied = new FakeAuthority(granted: false);
        var ungranted = Service(new FakeUser("user-1"), denied);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ungranted.SetAsync(Guid.NewGuid(), Coil("0"), Metadata()));
        Assert.Equal([ProductDimensionPermissionsV1.Manage], denied.Asked);

        var otherCompany = Service(new FakeUser("user-1", CompanyB), new FakeAuthority(granted: true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => otherCompany.GetAsync(Guid.NewGuid()));

        var granted = new FakeAuthority(granted: true);
        var service = Service(new FakeUser("user-1"), granted);
        Assert.Equal(400, (await Assert.ThrowsAsync<ProductDimensionException>(() => service.GetAsync(Guid.Empty))).StatusCode);
        Assert.Equal(ProductDimensionProblemCodesV1.RequestInvalid,
            (await service.SetAsync(Guid.NewGuid(), Coil("01"), Metadata())).Answer.Code);
        Assert.Equal(ProductDimensionProblemCodesV1.RequestInvalid,
            (await service.SetAsync(Guid.Empty, Coil("0"), Metadata())).Answer.Code);
        var shape = await service.SetAsync(Guid.NewGuid(), Coil("0") with { LengthMm = 2000m }, Metadata());
        Assert.Equal((400, ProductDimensionProblemCodesV1.MeasureInvalid, false), (shape.Answer.StatusCode, shape.Answer.Code,
            shape.IsReplay));
        Assert.Equal(ProductDimensionProblemCodesV1.ReasonRequired,
            (await service.SetAsync(Guid.NewGuid(), Coil("0") with { Reason = "  " }, Metadata())).Answer.Code);
        Assert.Equal(ProductDimensionProblemCodesV1.ReasonTooLong, (await service.SetAsync(Guid.NewGuid(),
            Coil("0") with { Reason = new string('x', ProductDimensionInputLimitsV1.ReasonMaximumLength + 1) }, Metadata())).Answer.Code);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetAsync(Guid.NewGuid(), Coil("0"),
            Metadata() with { ActorId = "user-2" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetAsync(Guid.NewGuid(), Coil("0"),
            Metadata() with { CompanyId = CompanyB }));
    }

    [Fact]
    public async Task The_read_port_denies_an_actor_outside_the_company_without_reading()
    {
        IProductDimensionReaderV1 reader = Service(new FakeUser("user-1", CompanyB), new FakeAuthority(granted: true));
        Assert.Equal(ProductDimensionOutcomeV1.Denied, (await reader.GetAsync(Guid.NewGuid())).Outcome);

        IProductDimensionReaderV1 anonymous = Service(new FakeUser(null), new FakeAuthority(granted: true));
        var answer = await anonymous.GetAsync(Guid.NewGuid());
        Assert.Equal(ProductDimensionReadV1.Without(ProductDimensionOutcomeV1.Denied), answer);
    }

    private static decimal? D(string? text) =>
        text is null ? null : decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    private static SetProductDimensionRequestV1 Coil(string version) =>
        new(version, ProductDimensionSectionKindsV1.Coil, 0.5m, 1000m, null, null, null, null, "SENTETIK gerekçe");

    private static ProductCardDimensionRow Row(Guid company) => new()
    {
        ProductId = Guid.NewGuid(), SectionKind = ProductDimensionSectionKindsV1.Coil, ThicknessMm = 0.5m, WidthMm = 1000m,
        Version = 1, UpdatedAtUtc = DateTimeOffset.UnixEpoch, UpdatedBy = "user-1", UpdatedInCompanyId = company,
    };

    private static ProductDimensionCommandMetadataV1 Metadata() =>
        new(CompanyA, "user-1", "corr-1", null, "Web", null, null, "key-1");

    private static ProductDimensionService Service(ICurrentUser user, IErpAuthorityRevalidationService authority)
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        var context = new ProductPriceListDbContext(new DbContextOptionsBuilder<ProductPriceListDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
        return new ProductDimensionService(context, company, user, authority, new NoAudit(), TimeProvider.System);
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

    private sealed class FakeAuthority(bool granted) : IErpAuthorityRevalidationService
    {
        public List<string> Asked { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Asked.Add(permission);
            return Task.FromResult(granted
                ? new PermissionScope(true, true, new HashSet<string> { "ROLE" }, new HashSet<Guid>(), new HashSet<Guid>(),
                    new HashSet<Guid>())
                : PermissionScope.Denied);
        }
    }

    private sealed class NoAudit : IAuditEventCollector
    {
        public void Enqueue(AuditEventDraft auditEvent) => throw new InvalidOperationException("No audit is expected in this test.");
    }
}
