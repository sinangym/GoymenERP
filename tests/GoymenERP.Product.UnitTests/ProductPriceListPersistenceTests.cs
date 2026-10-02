using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GoymenERP.Product.UnitTests;

/// <summary>
/// AT-018 persistence without a database: schema, tenant filters, the ADR-011 identities, the one-draft and published-start
/// indexes, the write guard (refuses before any connection), the snapshot = model and the rollback refusal; and the service's
/// fences, which answer before any read. What SQL Server enforces is proven by
/// <c>ProductPriceListSqlServerRelationalAcceptanceTests</c>. SENTETIK / NOT_ENV.
/// </summary>
public sealed class ProductPriceListPersistenceTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("55555555-5555-4555-8555-555555555555");

    [Fact]
    public void Every_price_list_table_is_in_the_product_schema_and_filtered_by_the_active_company()
    {
        using var context = Context();
        foreach (var type in new[] { typeof(PriceListRow), typeof(PriceListVersionRow), typeof(PriceListLineRow), typeof(PriceListAttemptRow) })
        {
            var entity = context.Model.FindEntityType(type)!;
            Assert.Equal("product", entity.GetSchema());
            Assert.Single(entity.GetDeclaredQueryFilters());
        }

        var audit = context.Model.FindEntityType(typeof(AuditEvent))!;
        Assert.Equal(("audit", "AuditEvents"), (audit.GetSchema(), audit.GetTableName()));
        var design = context.GetService<IDesignTimeModel>().Model;
        Assert.True(design.FindEntityType(typeof(PriceListProductReference))!.IsTableExcludedFromMigrations());
        Assert.True(design.FindEntityType(typeof(PriceListCompanyReference))!.IsTableExcludedFromMigrations());
    }

    [Fact]
    public void The_identities_indexes_and_checks_hold_the_rules_in_the_database_as_well()
    {
        using var context = Context();
        var design = context.GetService<IDesignTimeModel>().Model;
        var attempts = design.FindEntityType(typeof(PriceListAttemptRow))!;
        Assert.Equal(["CompanyId", "CommandName", "IdempotencyKeyHash"], attempts.FindPrimaryKey()!.Properties.Select(property => property.Name));
        var create = attempts.GetIndexes().Single(index => index.GetDatabaseName() == "UX_PriceListAttempts_PriceListId");
        Assert.True(create.IsUnique);
        Assert.Equal("[PriceListId] IS NOT NULL AND [CommandName] = N'product.price-list.create.v1'", create.GetFilter());

        var versions = design.FindEntityType(typeof(PriceListVersionRow))!;
        var oneDraft = versions.GetIndexes().Single(index => index.GetDatabaseName() == "UX_PriceListVersions_OneDraft");
        Assert.True(oneDraft.IsUnique);
        Assert.Equal("[State] = 1", oneDraft.GetFilter());
        var starts = versions.GetIndexes().Single(index => index.GetDatabaseName() == "UX_PriceListVersions_PublishedValidFrom");
        Assert.Equal("[State] = 2", starts.GetFilter());
        // Hand-written unique indexes over NOT NULL columns state "no filter" explicitly.
        Assert.Null(versions.GetIndexes().Single(index => index.GetDatabaseName() == "UX_PriceListVersions_VersionId").GetFilter());
        var lists = design.FindEntityType(typeof(PriceListRow))!;
        var name = lists.GetIndexes().Single(index => index.GetDatabaseName() == "UX_PriceLists_CompanyId_Name");
        Assert.True(name.IsUnique);
        Assert.Null(name.GetFilter());
        Assert.Equal("Turkish_100_CI_AS", lists.FindProperty(nameof(PriceListRow.Name))!.GetCollation());
        Assert.Contains(lists.GetCheckConstraints(), check => check.Name == "CK_PriceLists_Channel" && check.Sql == "[Channel] = 1");
        Assert.Contains(lists.GetCheckConstraints(), check => check.Name == "CK_PriceLists_VatBasis" && check.Sql == "[VatBasis] = 1");

        var lines = design.FindEntityType(typeof(PriceListLineRow))!;
        Assert.Equal(["CompanyId", "PriceListId", "VersionNo", "LineId"], lines.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal((19, 4), (lines.FindProperty(nameof(PriceListLineRow.UnitPrice))!.GetPrecision(),
            lines.FindProperty(nameof(PriceListLineRow.UnitPrice))!.GetScale()));
        Assert.Equal((18, 3), (lines.FindProperty(nameof(PriceListLineRow.LowerQuantity))!.GetPrecision(),
            lines.FindProperty(nameof(PriceListLineRow.LowerQuantity))!.GetScale()));
        Assert.Equal((18, 3), (lines.FindProperty(nameof(PriceListLineRow.UpperQuantity))!.GetPrecision(),
            lines.FindProperty(nameof(PriceListLineRow.UpperQuantity))!.GetScale()));
        Assert.Contains(lines.GetCheckConstraints(), check => check.Name == "CK_PriceListLines_Price"
            && check.Sql.Contains("[FloorUnitPrice] <= [UnitPrice]", StringComparison.Ordinal));
        Assert.Contains(lines.GetCheckConstraints(), check => check.Name == "CK_PriceListLines_QuantityTier"
            && check.Sql.Contains("[UpperQuantity] > [LowerQuantity]", StringComparison.Ordinal));
        Assert.Contains(lines.GetIndexes(), index => index.GetDatabaseName() == "IX_PriceListLines_QuantityTier");
        Assert.Contains(lines.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(PriceListProductReference));
    }

    [Fact]
    public void The_snapshot_matches_the_model_and_the_rollback_refuses_while_price_lists_exist()
    {
        using var context = Context();
        Assert.False(context.Database.HasPendingModelChanges());

        var migration = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "backend", "Modules", "Product",
            "GoymenERP.Modules.Product.Infrastructure", "PriceLists", "Migrations", "20260930113416_AddProductPriceLists.cs"));
        var down = migration[migration.IndexOf("protected override void Down", StringComparison.Ordinal)..];
        var refusal = down.IndexOf("THROW 51063", StringComparison.Ordinal);
        Assert.True(refusal > 0);
        Assert.True(refusal < down.IndexOf("DropTable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_write_guard_refuses_another_company_a_published_line_change_and_any_product_write()
    {
        await using (var context = Context())
        {
            context.Add(new PriceListRow { Id = Guid.NewGuid(), CompanyId = CompanyB, Name = "X", CurrencyCode = "TRY", Channel = 1, VatBasis = 1,
                Version = 1, CreatedBy = "u", CorrelationId = "c" });
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await using (var context = Context())
        {
            var version = new PriceListVersionRow { CompanyId = CompanyA, PriceListId = Guid.NewGuid(), VersionNo = 1, VersionId = Guid.NewGuid(),
                ValidFrom = new DateOnly(2026, 10, 1), State = 2, CreatedBy = "u", PublishedBy = "p", PublishedAtUtc = DateTimeOffset.UtcNow };
            var line = new PriceListLineRow { CompanyId = CompanyA, PriceListId = version.PriceListId, VersionNo = 1, LineNo = 1, LineId = Guid.NewGuid(),
                ProductId = Guid.NewGuid(), PriceUnitId = Guid.NewGuid(), UnitPrice = 1m };
            context.Attach(version);
            context.Attach(line);
            context.Remove(line);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Contains("taslak", exception.Message, StringComparison.Ordinal);
        }

        await using (var context = Context())
        {
            var version = new PriceListVersionRow { CompanyId = CompanyA, PriceListId = Guid.NewGuid(), VersionNo = 1, VersionId = Guid.NewGuid(),
                ValidFrom = new DateOnly(2026, 10, 1), State = 2, CreatedBy = "u", PublishedBy = "p", PublishedAtUtc = DateTimeOffset.UtcNow };
            context.Attach(version);
            version.ValidFrom = new DateOnly(2026, 10, 2);
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await using (var context = Context())
        {
            context.Add(new PriceListProductReference { Id = Guid.NewGuid(), Code = "X-1", Name = "x", ReferencesJson = "{}" });
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task The_fences_answer_before_anything_is_read()
    {
        var anonymous = Service(new FakeUser(null), new FakeAuthority(granted: true), known: true);
        Assert.Equal(401, (await Assert.ThrowsAsync<ProductPriceListException>(() => anonymous.ListAsync(null, null))).Status);

        var unregistered = Service(new FakeUser("user-1"), new FakeAuthority(granted: true), known: false);
        var setup = await Assert.ThrowsAsync<ProductPriceListException>(() => unregistered.GetAsync(Guid.NewGuid()));
        Assert.Equal((503, ProductPriceListCodesV1.PermissionUnregistered), (setup.Status, setup.Code));

        var denied = new FakeAuthority(granted: false);
        var ungranted = Service(new FakeUser("user-1"), denied, known: true);
        Assert.Equal(403, (await Assert.ThrowsAsync<ProductPriceListException>(() => ungranted.PublishAsync(Guid.NewGuid(),
            new PublishPriceListVersionRequestV1("1", 1), Metadata()))).Status);
        Assert.Equal([ProductPriceListPermissionsV1.Publish], denied.Asked);

        var otherCompany = Service(new FakeUser("user-1", CompanyB), new FakeAuthority(granted: true), known: true);
        Assert.Equal(403, (await Assert.ThrowsAsync<ProductPriceListException>(() => otherCompany.ListAsync(null, null))).Status);
    }

    [Fact]
    public async Task Malformed_input_is_refused_after_the_fence_and_before_any_read()
    {
        var authority = new FakeAuthority(granted: true);
        var service = Service(new FakeUser("user-1"), authority, known: true);
        var request = new CreatePriceListRequestV1(Guid.NewGuid(), Guid.NewGuid(), "Liste", "TRY", "Sales", "ExcludingVat", "2026-13-01", null,
            [new PriceListLineRequestV1(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "12,5")]);

        var exception = await Assert.ThrowsAsync<ProductPriceListException>(() => service.CreateAsync(request, Metadata()));

        Assert.Equal((400, ProductPriceListCodesV1.ValidationFailed), (exception.Status, exception.Code));
        Assert.Equal([new ProductPriceListFieldErrorV1("validFrom", ProductPriceListCodesV1.InvalidDate)], exception.Errors);
        Assert.Equal([ProductPriceListPermissionsV1.Write], authority.Asked);
        Assert.Equal(400, (await Assert.ThrowsAsync<ProductPriceListException>(() => service.ListAsync(101, null))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<ProductPriceListException>(() => service.ReviseDraftAsync(Guid.NewGuid(),
            new RevisePriceListDraftRequestV1("01", "L", "2026-10-01", null, []), Metadata()))).Status);
    }

    private static ProductPriceListCommandMetadata Metadata() =>
        new("user-1", ProductPriceListPermissionsV1.Write, [], "corr-1", null, "key-1", "Web");

    private static ProductPriceListService Service(ICurrentUser user, IErpAuthorityRevalidationService authority, bool known)
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        var context = new ProductPriceListDbContext(new DbContextOptionsBuilder<ProductPriceListDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
        var store = new ProductPriceListStore(context, company, new NoAudit(), TimeProvider.System);
        return new ProductPriceListService(company, user, authority, store, TimeProvider.System, null, _ => known);
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

        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Asked.Add(permission);
            return Task.FromResult(granted
                ? new PermissionScope(true, true, new HashSet<string> { "ROLE" }, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
        }
    }

    private sealed class NoAudit : IAuditEventCollector
    {
        public void Enqueue(AuditEventDraft auditEvent) => throw new InvalidOperationException("No audit is expected in this test.");
    }
}
